# Notification Dispatch Resilience

## Problem

Several controller methods dispatch SignalR + DB notifications **after** the transactional state change has been committed, with no exception handling around the dispatch. If notification dispatch fails (hub disposed, user disconnected mid-tx, background queue full, DB hiccup during `Notifications` insert, or the user query throws), the unhandled exception surfaces as a **500 response** even though the state change succeeded.

The client then retries the same operation. The retry hits the status guard (e.g., "Must be `CostingInProgress`") and returns a **400 Validation** error for an operation that actually succeeded, leaving the user with a confusing error on a successful state change.

This is a latent issue that pre-dates the concurrency fix landed at `docs/superpowers/plans/2026-04-17-costing-submit-concurrency-fix.md`; it was flagged during code review of that work (Task 2) and is being addressed separately.

## Goals

1. A failure inside notification dispatch must never cause a non-2xx HTTP response after the DB state change is committed.
2. Failures must be observable in application logs (not silently swallowed).
3. The fix must be localized and consistent across all affected controllers.

## Non-Goals

- Retrying failed notifications.
- Dead-letter / queued delivery.
- Decoupling notification dispatch onto a background channel.
- Refactoring `NotificationService` beyond the minimal change needed for test injection.

## Affected Call Sites

Six call sites across four controllers:

| # | File | Method | Current state |
|---|---|---|---|
| 1 | `BomPriceApproval.API/Features/Costing/CostingController.cs` | `SubmitItem` (around lines 346-352) | No try/catch; dispatches to all MDs when all items become costed. |
| 2 | `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` | `Create` (around lines 162-168) | No try/catch; dispatches to eligible BomCreators. |
| 3 | `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` | Resubmit endpoint (around lines 342-348) | No try/catch; dispatches to eligible BomCreators. |
| 4 | `BomPriceApproval.API/Features/Bom/BomController.cs` | `Submit` (around lines 210-215) | No try/catch; dispatches to eligible Accountants. |
| 5 | `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs` | `Approve` (around lines 158-174) | Already wrapped in a try/catch, but the catch is **silent** (no logger). |
| 6 | `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs` | `Reject` (around lines 205-212) | No try/catch; dispatches to SalesPerson and all Accountants. |

## Design

### Error-handling pattern

Every post-commit notification block becomes:

```csharp
try
{
    var recipients = await db.Users.Where(...).ToListAsync();
    foreach (var r in recipients)
        await notificationService.SendAsync(r.Id, message, req.Id, "QuotationRequest");
}
catch (Exception ex)
{
    logger.LogError(ex,
        "Notification dispatch failed after successful commit for {Entity} {Id}",
        "QuotationRequest", req.Id);
}
```

Rules:

- **Single try/catch wraps the entire dispatch block** (recipient query + foreach). A thrown `Users.Where(...).ToListAsync()` is just as fatal to the response as a thrown `SendAsync`.
- **Swallow the exception.** The state change is already committed; re-throwing breaks the API contract.
- **Log at `Error` level** with structured properties (`{Entity}`, `{Id}`) so log aggregation can filter on the entity type.
- **Always include `ex`** as the exception argument to `LogError` so the stack trace is preserved.
- For `ApprovalsController.Approve`, the existing silent catch is **replaced** with the logged pattern. The PDF generation + email send that share the same try block stay inside it (their failures are likewise non-fatal after approval commit), and a single `LogError` reports whatever failed.
- For `ApprovalsController.Reject`, both the SalesPerson dispatch and the Accountant-loop dispatch go inside **one** try/catch (they share the same fail-open policy).

### Logger injection

All four controllers use C# 12 primary constructors. Each primary constructor gains an `ILogger<T>` parameter. Usage is `logger.LogError(...)` referring to the primary-constructor parameter. No field declaration needed.

Example:

```csharp
public class CostingController(
    AppDbContext db,
    NotificationService notificationService,
    ILogger<CostingController> logger) : ControllerBase
```

ASP.NET Core's DI container provides `ILogger<T>` automatically — no registration change required.

### `NotificationService` — one-line change

`SendAsync` is made `virtual` so the test suite can inject a subclass that throws. No other changes to `NotificationService`.

```csharp
public virtual async Task SendAsync(int userId, string message, int referenceId, string referenceType)
```

## Testing

### Test infrastructure

Add to `BomPriceApproval.Tests/Shared/` (new file, e.g. `ThrowingNotificationFactory.cs`):

```csharp
public class ThrowingNotificationService(AppDbContext db, IHubContext<NotificationHub> hub)
    : NotificationService(db, hub)
{
    public override Task SendAsync(int userId, string message, int referenceId, string referenceType)
        => throw new InvalidOperationException("Simulated notification failure");
}

public class ThrowingNotificationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<NotificationService>();
            services.AddScoped<NotificationService, ThrowingNotificationService>();
        });
    }
}
```

`ThrowingNotificationFactory` is used as the `IClassFixture<T>` parameter for the new resilience test classes. It preserves all other DI registrations, including the Testcontainers PostgreSQL fixture used by the default factory.

### Integration tests — one per affected endpoint

Each test sets up the prerequisite workflow state (reusing helpers from the existing test files where possible), calls the target endpoint, and asserts:

1. HTTP response status is **2xx** (the exact expected success code for that endpoint — e.g., `204 NoContent`, `200 OK`, `201 Created`).
2. The persisted DB state change is visible through a subsequent read (e.g., `GET /api/requisitions/{id}` returns the expected new status).

Target endpoints:

| # | Test class location | Endpoint under test | Success code |
|---|---|---|---|
| 1 | `BomPriceApproval.Tests/Costing/` | `POST /api/costing/{reqId}/items/{itemId}/submit` (last item) | 204 |
| 2 | `BomPriceApproval.Tests/Requisitions/` | `POST /api/requisitions` | 201 |
| 3 | `BomPriceApproval.Tests/Requisitions/` | Resubmit endpoint | 200 |
| 4 | `BomPriceApproval.Tests/Bom/` | `POST /api/bom/{reqId}/submit` | 204 |
| 5 | `BomPriceApproval.Tests/Approvals/` | `POST /api/approvals/{reqId}/approve` | 200 |
| 6 | `BomPriceApproval.Tests/Approvals/` | `POST /api/approvals/{reqId}/reject` | 200 |

Grouping: tests may be bundled into one `NotificationResilienceTests.cs` per feature folder (so each feature folder gets one file), or added to existing test classes — the implementation plan will decide. Either organization satisfies the design.

### Existing tests

All existing tests continue to use the default `WebApplicationFactory<Program>` (real `NotificationService`). No changes expected; the `virtual` modifier on `SendAsync` is binary-compatible.

## Risks & Mitigations

- **Risk:** The recipient query (`db.Users.Where(...).ToListAsync()`) is inside the try block, so a transient DB failure there is also swallowed. — *Mitigation:* This is intentional and matches the goal: after commit, no post-commit side-effect failure should leak to the client. The error is logged for ops to act on.
- **Risk:** `ApprovalsController.Approve` currently swallows PDF + email failures silently. Adding a logger is a behavior change for observability but not for user-facing behavior. — *Mitigation:* Ops will start seeing these errors in logs; this is the desired behavior.
- **Risk:** `ThrowingNotificationFactory` replaces `NotificationService` process-wide for the fixture, so any background dispatch would also throw. — *Mitigation:* Notifications are only dispatched from controller actions, so there is no background path. All calls in a test using this fixture are expected to throw — this is the test's premise.

## Out of Scope

- Moving notification dispatch to an outbox / background worker.
- Retry policy for transient failures.
- Converting `NotificationService` to an interface (`INotificationService`). The `virtual` subclass is sufficient for the test hook and keeps the PR small.
- Changes to the SignalR hub itself.

## Acceptance Criteria

1. All six call sites listed above have the error-handling pattern applied.
2. All four affected controllers inject `ILogger<T>` and use it in their catch blocks.
3. `NotificationService.SendAsync` is `virtual`.
4. `ThrowingNotificationFactory` exists and is wired into six new integration tests.
5. Each new test asserts (a) 2xx response and (b) persisted state change.
6. `dotnet test` passes for the entire solution.
