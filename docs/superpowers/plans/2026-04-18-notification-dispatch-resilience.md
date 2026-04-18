# Notification Dispatch Resilience Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make post-commit notification dispatch failures non-fatal to the HTTP response across all six affected controller endpoints, and log them via `ILogger<T>` so they are observable.

**Architecture:** Wrap every post-commit notification block in a try/catch that logs `Error`-level via a per-controller `ILogger<T>` (injected via C# 12 primary constructor). Swallow the exception — the DB state change is already committed, and the response must remain 2xx. Verify each endpoint with an integration test that replaces `NotificationService` with a subclass that unconditionally throws, and asserts (a) the HTTP response is 2xx and (b) the persisted state change is visible.

**Tech Stack:** ASP.NET Core 8, EF Core, xUnit, FluentAssertions, Testcontainers (PostgreSQL), `WebApplicationFactory<Program>`.

**Spec:** [docs/superpowers/specs/2026-04-18-notification-dispatch-resilience-design.md](../specs/2026-04-18-notification-dispatch-resilience-design.md)

---

## File Structure

**Modified:**
- `BomPriceApproval.API/Infrastructure/Services/NotificationService.cs` — make `SendAsync` `virtual` (1 line)
- `BomPriceApproval.API/Features/Costing/CostingController.cs` — inject `ILogger<CostingController>`, wrap MD-notification block
- `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` — inject `ILogger<RequisitionsController>`, wrap two notification blocks (`Create`, `Resubmit`)
- `BomPriceApproval.API/Features/Bom/BomController.cs` — inject `ILogger<BomController>`, wrap accountant-notification block
- `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs` — inject `ILogger<ApprovalsController>`, replace silent catch in `Approve` with logged catch, wrap `Reject` dispatch blocks

**Created (tests):**
- `BomPriceApproval.Tests/Shared/ThrowingNotificationFactory.cs` — test fixture that replaces `NotificationService` with a throwing subclass
- `BomPriceApproval.Tests/Costing/NotificationResilienceTests.cs` — 1 test (SubmitItem last item)
- `BomPriceApproval.Tests/Requisitions/NotificationResilienceTests.cs` — 2 tests (Create, Resubmit)
- `BomPriceApproval.Tests/Bom/NotificationResilienceTests.cs` — 1 test (Submit)
- `BomPriceApproval.Tests/Approvals/NotificationResilienceTests.cs` — 2 tests (Approve, Reject)

Each resilience-test class uses `IClassFixture<ThrowingNotificationFactory>` — a separate fixture type from the default `WebApplicationFactory<Program>` so that existing tests continue to use the real `NotificationService`.

---

## Task 1: Make `NotificationService.SendAsync` virtual

**Files:**
- Modify: `BomPriceApproval.API/Infrastructure/Services/NotificationService.cs:10`

- [ ] **Step 1: Edit the method signature**

Open `BomPriceApproval.API/Infrastructure/Services/NotificationService.cs` and change line 10:

Before:
```csharp
    public async Task SendAsync(int userId, string message, int referenceId, string referenceType)
```

After:
```csharp
    public virtual async Task SendAsync(int userId, string message, int referenceId, string referenceType)
```

- [ ] **Step 2: Build to verify no breakage**

Run: `dotnet build`
Expected: Build succeeds with 0 errors and 0 warnings related to this file.

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Services/NotificationService.cs
git commit -m "refactor(api): make NotificationService.SendAsync virtual for test injection"
```

---

## Task 2: Add `ThrowingNotificationFactory` test fixture

**Files:**
- Create: `BomPriceApproval.Tests/Shared/ThrowingNotificationFactory.cs`

- [ ] **Step 1: Create the test fixture file**

Write this exact content to `BomPriceApproval.Tests/Shared/ThrowingNotificationFactory.cs`:

```csharp
using BomPriceApproval.API.Features.Notifications;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BomPriceApproval.Tests.Shared;

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

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeds. The new types are referenced only by themselves at this point.

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.Tests/Shared/ThrowingNotificationFactory.cs
git commit -m "test: add ThrowingNotificationFactory for resilience tests"
```

---

## Task 3: Fix `CostingController.SubmitItem`

**Files:**
- Modify: `BomPriceApproval.API/Features/Costing/CostingController.cs:17` (constructor), `:344-352` (notification block)
- Create: `BomPriceApproval.Tests/Costing/NotificationResilienceTests.cs`

- [ ] **Step 1: Write the failing test**

Write this exact content to `BomPriceApproval.Tests/Costing/NotificationResilienceTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Costing;

public class NotificationResilienceTests(ThrowingNotificationFactory factory)
    : IClassFixture<ThrowingNotificationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private record LoginResp(string AccessToken);
    private record ItemMin(int Id);
    private record CustomerMin(int Id);
    private record ProcessMin(int Id);
    private record CreatedReq(int Id, string RefNo);
    private record RiMin(int Id);
    private record ReqDetail(int Id, string Status, List<RiMin> Items);
    private record CostingBomLineMin(int BomLineId);
    private record CostingItemMin(int RequisitionItemId, List<CostingBomLineMin> BomLines);
    private record CostingReview(int RequisitionId, List<CostingItemMin> Items);

    private async Task<string> LoginAsync(string email, string password)
    {
        // Fresh client for login so the test's bearer token doesn't conflict.
        using var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResp>();
        return body!.AccessToken;
    }

    private void UseToken(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task SubmitItem_LastItem_ReturnsSuccess_EvenIfNotificationThrows()
    {
        // Arrange — walk the workflow to CostingInProgress on a single-item requisition.
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        UseToken(spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        fgResp.EnsureSuccessStatusCode();
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemMin>();
        var rmResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "RM", Type = "RawMaterial", LastPurchasePrice = 5m });
        rmResp.EnsureSuccessStatusCode();
        var rm = await rmResp.Content.ReadFromJsonAsync<ItemMin>();

        var customers = await _client.GetFromJsonAsync<List<CustomerMin>>("/api/customers");
        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fg!.Id, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });
        reqResp.EnsureSuccessStatusCode();
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedReq>();
        var reqId = created!.Id;

        var reqDetail = await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}");
        var riId = reqDetail!.Items[0].Id;

        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        UseToken(adminToken);
        var procCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await _client.PostAsJsonAsync("/api/processes", new { Name = procCode, DisplayOrder = 99 });
        procResp.EnsureSuccessStatusCode();
        var process = await procResp.Content.ReadFromJsonAsync<ProcessMin>();

        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        UseToken(bomToken);
        await _client.PostAsync($"/api/bom/{reqId}/items/{riId}/start", null);
        await _client.PutAsJsonAsync($"/api/bom/{reqId}/items/{riId}/lines", new
        {
            Lines = new[]
            {
                new { ProcessId = process!.Id, RawMaterialItemId = rm!.Id, QtyPerKg = 0.85m, WastagePct = 2.0m }
            }
        });
        await _client.PostAsync($"/api/bom/{reqId}/submit", null);

        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        UseToken(acctToken);
        await _client.PostAsync($"/api/costing/{reqId}/items/{riId}/start", null);
        var review = await _client.GetFromJsonAsync<CostingReview>($"/api/costing/{reqId}");
        var bomLineId = review!.Items[0].BomLines[0].BomLineId;

        // Act — submit the last (only) item's costing. With a throwing NotificationService,
        // this previously surfaced as a 500; after the fix, it must still return 204.
        var submitResp = await _client.PostAsJsonAsync($"/api/costing/{reqId}/items/{riId}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 1.00m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        // Assert — response is 2xx and the state change (→ MdReview) did commit.
        submitResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        UseToken(spToken);
        var req = await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}");
        req!.Status.Should().Be("MdReview");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~Costing.NotificationResilienceTests.SubmitItem_LastItem_ReturnsSuccess_EvenIfNotificationThrows"`
Expected: FAIL. The submit call returns 500 (InvalidOperationException: Simulated notification failure), so `submitResp.StatusCode.Should().Be(HttpStatusCode.NoContent)` fails.

- [ ] **Step 3: Inject `ILogger<CostingController>` into the primary constructor**

Open `BomPriceApproval.API/Features/Costing/CostingController.cs` and change line 17:

Before:
```csharp
public class CostingController(AppDbContext db, NotificationService notificationService) : ControllerBase
```

After:
```csharp
public class CostingController(
    AppDbContext db,
    NotificationService notificationService,
    ILogger<CostingController> logger) : ControllerBase
```

- [ ] **Step 4: Wrap the notification block in a logged try/catch**

Open `BomPriceApproval.API/Features/Costing/CostingController.cs` and replace the block at lines 344-352 (the comment `// Send notifications outside the transaction...` through `}`):

Before:
```csharp
        // Send notifications outside the transaction so a delivery failure
        // cannot roll back the status promotion.
        if (allSubmitted)
        {
            var mds = await db.Users.Where(u => u.Role == UserRole.ManagingDirector && u.IsActive).ToListAsync();
            foreach (var md in mds)
                await notificationService.SendAsync(md.Id,
                    $"Costing complete, ready for approval: {req.RefNo}", req.Id, "QuotationRequest");
        }
```

After:
```csharp
        // Send notifications outside the transaction so a delivery failure
        // cannot roll back the status promotion. Swallow and log dispatch
        // failures — the state change is already committed.
        if (allSubmitted)
        {
            try
            {
                var mds = await db.Users.Where(u => u.Role == UserRole.ManagingDirector && u.IsActive).ToListAsync();
                foreach (var md in mds)
                    await notificationService.SendAsync(md.Id,
                        $"Costing complete, ready for approval: {req.RefNo}", req.Id, "QuotationRequest");
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Notification dispatch failed after successful commit for {Entity} {Id}",
                    "QuotationRequest", req.Id);
            }
        }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~Costing.NotificationResilienceTests.SubmitItem_LastItem_ReturnsSuccess_EvenIfNotificationThrows"`
Expected: PASS. Response is 204, status transitions to `MdReview`.

- [ ] **Step 6: Run the full costing test suite to check for regressions**

Run: `dotnet test --filter "FullyQualifiedName~Costing"`
Expected: All Costing tests pass.

- [ ] **Step 7: Commit**

```bash
git add BomPriceApproval.API/Features/Costing/CostingController.cs BomPriceApproval.Tests/Costing/NotificationResilienceTests.cs
git commit -m "fix(api): swallow+log notification dispatch failures in CostingController.SubmitItem"
```

---

## Task 4: Fix `RequisitionsController.Create`

**Files:**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs:16` (constructor), `:162-168` (notification block)
- Create: `BomPriceApproval.Tests/Requisitions/NotificationResilienceTests.cs`

- [ ] **Step 1: Write the failing test**

Write this exact content to `BomPriceApproval.Tests/Requisitions/NotificationResilienceTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Requisitions;

public class NotificationResilienceTests(ThrowingNotificationFactory factory)
    : IClassFixture<ThrowingNotificationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private record LoginResp(string AccessToken);
    private record ItemMin(int Id);
    private record CustomerMin(int Id);
    private record CreatedReq(int Id, string RefNo);
    private record ReqDetail(int Id, string Status);

    private async Task<string> LoginAsync(string email, string password)
    {
        using var c = factory.CreateClient();
        var resp = await c.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResp>();
        return body!.AccessToken;
    }

    private void UseToken(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task Create_ReturnsCreated_EvenIfNotificationThrows()
    {
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        UseToken(spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        fgResp.EnsureSuccessStatusCode();
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemMin>();

        var customers = await _client.GetFromJsonAsync<List<CustomerMin>>("/api/customers");

        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fg!.Id, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });

        reqResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedReq>();

        var detail = await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{created!.Id}");
        detail!.Status.Should().Be("BomPending");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~Requisitions.NotificationResilienceTests.Create_ReturnsCreated_EvenIfNotificationThrows"`
Expected: FAIL. Create returns 500 due to the throwing notification.

- [ ] **Step 3: Inject `ILogger<RequisitionsController>` into the primary constructor**

Open `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` and change line 16:

Before:
```csharp
public class RequisitionsController(AppDbContext db, NotificationService notificationService) : ControllerBase
```

After:
```csharp
public class RequisitionsController(
    AppDbContext db,
    NotificationService notificationService,
    ILogger<RequisitionsController> logger) : ControllerBase
```

- [ ] **Step 4: Wrap the `Create` notification block**

Open `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` and replace the block at approximately lines 162-168:

Before:
```csharp
        var bomCreators = await db.Users
            .Where(u => u.Role == UserRole.BomCreator && (u.BranchId == requisition.BranchId || u.BranchId == null) && u.IsActive)
            .ToListAsync();

        foreach (var creator in bomCreators)
            await notificationService.SendAsync(creator.Id,
                $"New BOM request: {requisition.RefNo}", requisition.Id, "QuotationRequest");
```

After:
```csharp
        try
        {
            var bomCreators = await db.Users
                .Where(u => u.Role == UserRole.BomCreator && (u.BranchId == requisition.BranchId || u.BranchId == null) && u.IsActive)
                .ToListAsync();

            foreach (var creator in bomCreators)
                await notificationService.SendAsync(creator.Id,
                    $"New BOM request: {requisition.RefNo}", requisition.Id, "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful commit for {Entity} {Id}",
                "QuotationRequest", requisition.Id);
        }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~Requisitions.NotificationResilienceTests.Create_ReturnsCreated_EvenIfNotificationThrows"`
Expected: PASS. Response is 201 Created, requisition persists with status `BomPending`.

- [ ] **Step 6: Run existing Requisitions tests**

Run: `dotnet test --filter "FullyQualifiedName~Requisitions"`
Expected: All existing Requisitions tests still pass (the default fixture uses the real `NotificationService`).

- [ ] **Step 7: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs BomPriceApproval.Tests/Requisitions/NotificationResilienceTests.cs
git commit -m "fix(api): swallow+log notification dispatch failures in RequisitionsController.Create"
```

---

## Task 5: Fix `RequisitionsController` Resubmit endpoint

**Files:**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs:342-348` (notification block in `Resubmit`)
- Modify: `BomPriceApproval.Tests/Requisitions/NotificationResilienceTests.cs` (add test)

Logger was injected in Task 4; this task adds the try/catch to the second notification block.

- [ ] **Step 1: Write the failing test**

Append this test method to the `NotificationResilienceTests` class in `BomPriceApproval.Tests/Requisitions/NotificationResilienceTests.cs` (before the final closing `}`):

```csharp
    private record ProcessMin(int Id);
    private record RiMin(int Id);
    private record ReqDetailWithItems(int Id, string Status, List<RiMin> Items);
    private record CostingBomLineMin(int BomLineId);
    private record CostingItemMin(int RequisitionItemId, List<CostingBomLineMin> BomLines);
    private record CostingReview(int RequisitionId, List<CostingItemMin> Items);

    [Fact]
    public async Task Resubmit_ReturnsOk_EvenIfNotificationThrows()
    {
        // Arrange — drive a requisition all the way through to Rejected, then resubmit.
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        var mdToken = await LoginAsync("md@test.com", "Test@1234");

        UseToken(spToken);
        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        fgResp.EnsureSuccessStatusCode();
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemMin>();
        var rmResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "RM", Type = "RawMaterial", LastPurchasePrice = 5m });
        rmResp.EnsureSuccessStatusCode();
        var rm = await rmResp.Content.ReadFromJsonAsync<ItemMin>();

        var customers = await _client.GetFromJsonAsync<List<CustomerMin>>("/api/customers");
        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fg!.Id, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });
        reqResp.EnsureSuccessStatusCode();
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedReq>();
        var reqId = created!.Id;

        var reqDetail = await _client.GetFromJsonAsync<ReqDetailWithItems>($"/api/requisitions/{reqId}");
        var riId = reqDetail!.Items[0].Id;

        UseToken(adminToken);
        var procCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await _client.PostAsJsonAsync("/api/processes", new { Name = procCode, DisplayOrder = 99 });
        procResp.EnsureSuccessStatusCode();
        var process = await procResp.Content.ReadFromJsonAsync<ProcessMin>();

        UseToken(bomToken);
        await _client.PostAsync($"/api/bom/{reqId}/items/{riId}/start", null);
        await _client.PutAsJsonAsync($"/api/bom/{reqId}/items/{riId}/lines", new
        {
            Lines = new[]
            {
                new { ProcessId = process!.Id, RawMaterialItemId = rm!.Id, QtyPerKg = 0.85m, WastagePct = 2.0m }
            }
        });
        await _client.PostAsync($"/api/bom/{reqId}/submit", null);

        UseToken(acctToken);
        await _client.PostAsync($"/api/costing/{reqId}/items/{riId}/start", null);
        var review = await _client.GetFromJsonAsync<CostingReview>($"/api/costing/{reqId}");
        var bomLineId = review!.Items[0].BomLines[0].BomLineId;

        await _client.PostAsJsonAsync($"/api/costing/{reqId}/items/{riId}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 1.00m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        UseToken(mdToken);
        var rejectResp = await _client.PostAsJsonAsync($"/api/approvals/{reqId}/reject", new { Notes = "please revise" });
        rejectResp.EnsureSuccessStatusCode();

        // Act — resubmit with the same single item. This goes through the second notification block.
        UseToken(spToken);
        var resubmitResp = await _client.PostAsJsonAsync($"/api/requisitions/{reqId}/resubmit", new
        {
            CustomerId = customers.First().Id,
            Items = new[] { new { ItemId = fg.Id, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });

        // Assert — 200 OK and status back to BomPending.
        resubmitResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await _client.GetFromJsonAsync<ReqDetailWithItems>($"/api/requisitions/{reqId}");
        after!.Status.Should().Be("BomPending");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~Requisitions.NotificationResilienceTests.Resubmit_ReturnsOk_EvenIfNotificationThrows"`
Expected: FAIL. Resubmit returns 500 due to the throwing notification.

- [ ] **Step 3: Wrap the resubmit notification block**

Open `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` and replace the block at approximately lines 342-348 (after `await tx.CommitAsync();`):

Before:
```csharp
        var bomCreators = await db.Users
            .Where(u => u.Role == UserRole.BomCreator && (u.BranchId == q.BranchId || u.BranchId == null) && u.IsActive)
            .ToListAsync();

        foreach (var creator in bomCreators)
            await notificationService.SendAsync(creator.Id,
                $"Resubmitted BOM request: {q.RefNo}", q.Id, "QuotationRequest");
```

After:
```csharp
        try
        {
            var bomCreators = await db.Users
                .Where(u => u.Role == UserRole.BomCreator && (u.BranchId == q.BranchId || u.BranchId == null) && u.IsActive)
                .ToListAsync();

            foreach (var creator in bomCreators)
                await notificationService.SendAsync(creator.Id,
                    $"Resubmitted BOM request: {q.RefNo}", q.Id, "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful commit for {Entity} {Id}",
                "QuotationRequest", q.Id);
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~Requisitions.NotificationResilienceTests.Resubmit_ReturnsOk_EvenIfNotificationThrows"`
Expected: PASS. Response is 200 OK, status back to `BomPending`.

- [ ] **Step 5: Run existing Requisitions tests**

Run: `dotnet test --filter "FullyQualifiedName~Requisitions"`
Expected: All Requisitions tests pass, including the existing `ResubmitTests`.

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs BomPriceApproval.Tests/Requisitions/NotificationResilienceTests.cs
git commit -m "fix(api): swallow+log notification dispatch failures in RequisitionsController.Resubmit"
```

---

## Task 6: Fix `BomController.Submit`

**Files:**
- Modify: `BomPriceApproval.API/Features/Bom/BomController.cs:16` (constructor), `:210-215` (notification block)
- Create: `BomPriceApproval.Tests/Bom/NotificationResilienceTests.cs`

- [ ] **Step 1: Write the failing test**

Write this exact content to `BomPriceApproval.Tests/Bom/NotificationResilienceTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Bom;

public class NotificationResilienceTests(ThrowingNotificationFactory factory)
    : IClassFixture<ThrowingNotificationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private record LoginResp(string AccessToken);
    private record ItemMin(int Id);
    private record CustomerMin(int Id);
    private record ProcessMin(int Id);
    private record CreatedReq(int Id, string RefNo);
    private record RiMin(int Id);
    private record ReqDetail(int Id, string Status, List<RiMin> Items);

    private async Task<string> LoginAsync(string email, string password)
    {
        using var c = factory.CreateClient();
        var resp = await c.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResp>();
        return body!.AccessToken;
    }

    private void UseToken(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task Submit_ReturnsNoContent_EvenIfNotificationThrows()
    {
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");

        UseToken(spToken);
        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        fgResp.EnsureSuccessStatusCode();
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemMin>();
        var rmResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "RM", Type = "RawMaterial", LastPurchasePrice = 5m });
        rmResp.EnsureSuccessStatusCode();
        var rm = await rmResp.Content.ReadFromJsonAsync<ItemMin>();

        var customers = await _client.GetFromJsonAsync<List<CustomerMin>>("/api/customers");
        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fg!.Id, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });
        reqResp.EnsureSuccessStatusCode();
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedReq>();
        var reqId = created!.Id;

        var reqDetail = await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}");
        var riId = reqDetail!.Items[0].Id;

        UseToken(adminToken);
        var procCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await _client.PostAsJsonAsync("/api/processes", new { Name = procCode, DisplayOrder = 99 });
        procResp.EnsureSuccessStatusCode();
        var process = await procResp.Content.ReadFromJsonAsync<ProcessMin>();

        UseToken(bomToken);
        await _client.PostAsync($"/api/bom/{reqId}/items/{riId}/start", null);
        await _client.PutAsJsonAsync($"/api/bom/{reqId}/items/{riId}/lines", new
        {
            Lines = new[]
            {
                new { ProcessId = process!.Id, RawMaterialItemId = rm!.Id, QtyPerKg = 0.85m, WastagePct = 2.0m }
            }
        });

        // Act — submit the BOM. Previously surfaced as 500 due to accountant notification throw.
        var submitResp = await _client.PostAsync($"/api/bom/{reqId}/submit", null);

        // Assert — 204 and status advances to CostingPending.
        submitResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        UseToken(spToken);
        var after = await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}");
        after!.Status.Should().Be("CostingPending");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~Bom.NotificationResilienceTests.Submit_ReturnsNoContent_EvenIfNotificationThrows"`
Expected: FAIL. Submit returns 500.

- [ ] **Step 3: Inject `ILogger<BomController>` and wrap the notification block**

Open `BomPriceApproval.API/Features/Bom/BomController.cs` and change line 16:

Before:
```csharp
public class BomController(AppDbContext db, NotificationService notificationService) : ControllerBase
```

After:
```csharp
public class BomController(
    AppDbContext db,
    NotificationService notificationService,
    ILogger<BomController> logger) : ControllerBase
```

Then replace the block at approximately lines 210-215:

Before:
```csharp
        var accountants = await db.Users
            .Where(u => u.Role == UserRole.Accountant && (u.BranchId == req.BranchId || u.BranchId == null) && u.IsActive)
            .ToListAsync();
        foreach (var accountant in accountants)
            await notificationService.SendAsync(accountant.Id,
                $"BOM ready for costing: {req.RefNo}", req.Id, "QuotationRequest");
```

After:
```csharp
        try
        {
            var accountants = await db.Users
                .Where(u => u.Role == UserRole.Accountant && (u.BranchId == req.BranchId || u.BranchId == null) && u.IsActive)
                .ToListAsync();
            foreach (var accountant in accountants)
                await notificationService.SendAsync(accountant.Id,
                    $"BOM ready for costing: {req.RefNo}", req.Id, "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful commit for {Entity} {Id}",
                "QuotationRequest", req.Id);
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~Bom.NotificationResilienceTests.Submit_ReturnsNoContent_EvenIfNotificationThrows"`
Expected: PASS.

- [ ] **Step 5: Run existing Bom tests**

Run: `dotnet test --filter "FullyQualifiedName~Bom"`
Expected: All Bom tests pass.

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Features/Bom/BomController.cs BomPriceApproval.Tests/Bom/NotificationResilienceTests.cs
git commit -m "fix(api): swallow+log notification dispatch failures in BomController.Submit"
```

---

## Task 7: Fix `ApprovalsController.Approve` (replace silent catch with logged catch)

**Files:**
- Modify: `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs:16` (constructor), `:158-174` (existing try/catch)
- Create: `BomPriceApproval.Tests/Approvals/NotificationResilienceTests.cs`

Note: `Approve` already has a silent try/catch that swallows notification+PDF+email failures. The 2xx behavior is already correct — this task is a **regression-lock test** (ensures 2xx stays 2xx) and upgrades the silent catch to log with `ILogger<ApprovalsController>`.

- [ ] **Step 1: Write the regression-lock test**

Write this exact content to `BomPriceApproval.Tests/Approvals/NotificationResilienceTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Approvals;

public class NotificationResilienceTests(ThrowingNotificationFactory factory)
    : IClassFixture<ThrowingNotificationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private record LoginResp(string AccessToken);
    private record ItemMin(int Id);
    private record CustomerMin(int Id);
    private record ProcessMin(int Id);
    private record CreatedReq(int Id, string RefNo);
    private record RiMin(int Id);
    private record ReqDetail(int Id, string Status, List<RiMin> Items);
    private record CostingBomLineMin(int BomLineId);
    private record CostingItemMin(int RequisitionItemId, List<CostingBomLineMin> BomLines);
    private record CostingReview(int RequisitionId, List<CostingItemMin> Items);

    private async Task<string> LoginAsync(string email, string password)
    {
        using var c = factory.CreateClient();
        var resp = await c.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResp>();
        return body!.AccessToken;
    }

    private void UseToken(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<(int reqId, int riId)> WalkToMdReviewAsync()
    {
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");

        UseToken(spToken);
        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        fgResp.EnsureSuccessStatusCode();
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemMin>();
        var rmResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "RM", Type = "RawMaterial", LastPurchasePrice = 5m });
        rmResp.EnsureSuccessStatusCode();
        var rm = await rmResp.Content.ReadFromJsonAsync<ItemMin>();

        var customers = await _client.GetFromJsonAsync<List<CustomerMin>>("/api/customers");
        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fg!.Id, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });
        reqResp.EnsureSuccessStatusCode();
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedReq>();
        var reqId = created!.Id;
        var reqDetail = await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}");
        var riId = reqDetail!.Items[0].Id;

        UseToken(adminToken);
        var procCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await _client.PostAsJsonAsync("/api/processes", new { Name = procCode, DisplayOrder = 99 });
        procResp.EnsureSuccessStatusCode();
        var process = await procResp.Content.ReadFromJsonAsync<ProcessMin>();

        UseToken(bomToken);
        await _client.PostAsync($"/api/bom/{reqId}/items/{riId}/start", null);
        await _client.PutAsJsonAsync($"/api/bom/{reqId}/items/{riId}/lines", new
        {
            Lines = new[]
            {
                new { ProcessId = process!.Id, RawMaterialItemId = rm!.Id, QtyPerKg = 0.85m, WastagePct = 2.0m }
            }
        });
        await _client.PostAsync($"/api/bom/{reqId}/submit", null);

        UseToken(acctToken);
        await _client.PostAsync($"/api/costing/{reqId}/items/{riId}/start", null);
        var review = await _client.GetFromJsonAsync<CostingReview>($"/api/costing/{reqId}");
        var bomLineId = review!.Items[0].BomLines[0].BomLineId;

        await _client.PostAsJsonAsync($"/api/costing/{reqId}/items/{riId}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 1.00m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        return (reqId, riId);
    }

    [Fact]
    public async Task Approve_ReturnsOk_EvenIfNotificationThrows()
    {
        var (reqId, riId) = await WalkToMdReviewAsync();

        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        UseToken(mdToken);

        var approveResp = await _client.PostAsJsonAsync($"/api/approvals/{reqId}/approve", new
        {
            Items = new[] { new { RequisitionItemId = riId, SalesPricePerKgAed = 10m } }
        });

        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        UseToken(spToken);
        var after = await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}");
        after!.Status.Should().Be("Approved");
    }
}
```

- [ ] **Step 2: Run test to verify current behavior (should PASS even before the code change)**

Run: `dotnet test --filter "FullyQualifiedName~Approvals.NotificationResilienceTests.Approve_ReturnsOk_EvenIfNotificationThrows"`
Expected: PASS. `Approve` already swallows the exception inside an unlogged catch block.

This is a regression-lock test — it ensures the 2xx behavior of `Approve` under notification failure cannot silently regress when the catch block is edited.

- [ ] **Step 3: Inject `ILogger<ApprovalsController>` into the primary constructor**

Open `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs` and change line 16:

Before:
```csharp
public class ApprovalsController(AppDbContext db, NotificationService notificationSvc, EmailService emailSvc, PdfService pdfSvc) : ControllerBase
```

After:
```csharp
public class ApprovalsController(
    AppDbContext db,
    NotificationService notificationSvc,
    EmailService emailSvc,
    PdfService pdfSvc,
    ILogger<ApprovalsController> logger) : ControllerBase
```

- [ ] **Step 4: Replace the silent catch in `Approve` with a logged catch**

Open `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs` and replace the block at approximately lines 158-174:

Before:
```csharp
        try
        {
            await db.Entry(approval).Collection(a => a.Items).LoadAsync();
            var pdf = pdfSvc.GenerateQuotation(req, approval);

            await notificationSvc.SendAsync(req.SalesPersonId,
                $"Quotation approved! Download ready: {req.RefNo}", req.Id, "QuotationRequest");

            await emailSvc.SendAsync(req.SalesPerson.Email, req.SalesPerson.Name,
                $"Quotation Approved – {req.RefNo}",
                $"<p>Dear {req.SalesPerson.Name},</p><p>Your quotation <strong>{req.RefNo}</strong> has been approved. Please find the quotation PDF attached.</p><p>Regards,<br/>Fujairah Plastic Factory</p>",
                pdf, $"{req.RefNo}-Quotation.pdf");
        }
        catch
        {
            // Approval committed; notification/email failures are non-fatal
        }
```

After:
```csharp
        try
        {
            await db.Entry(approval).Collection(a => a.Items).LoadAsync();
            var pdf = pdfSvc.GenerateQuotation(req, approval);

            await notificationSvc.SendAsync(req.SalesPersonId,
                $"Quotation approved! Download ready: {req.RefNo}", req.Id, "QuotationRequest");

            await emailSvc.SendAsync(req.SalesPerson.Email, req.SalesPerson.Name,
                $"Quotation Approved – {req.RefNo}",
                $"<p>Dear {req.SalesPerson.Name},</p><p>Your quotation <strong>{req.RefNo}</strong> has been approved. Please find the quotation PDF attached.</p><p>Regards,<br/>Fujairah Plastic Factory</p>",
                pdf, $"{req.RefNo}-Quotation.pdf");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful commit for {Entity} {Id}",
                "QuotationRequest", req.Id);
        }
```

- [ ] **Step 5: Run test to verify it still passes**

Run: `dotnet test --filter "FullyQualifiedName~Approvals.NotificationResilienceTests.Approve_ReturnsOk_EvenIfNotificationThrows"`
Expected: PASS (as before).

- [ ] **Step 6: Run existing Approvals tests**

Run: `dotnet test --filter "FullyQualifiedName~Approvals"`
Expected: All Approvals tests pass.

- [ ] **Step 7: Commit**

```bash
git add BomPriceApproval.API/Features/Approvals/ApprovalsController.cs BomPriceApproval.Tests/Approvals/NotificationResilienceTests.cs
git commit -m "fix(api): log (no longer silently swallow) dispatch failures in ApprovalsController.Approve"
```

---

## Task 8: Fix `ApprovalsController.Reject`

**Files:**
- Modify: `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs:205-212` (notification blocks in `Reject`)
- Modify: `BomPriceApproval.Tests/Approvals/NotificationResilienceTests.cs` (add test)

Logger was injected in Task 7.

- [ ] **Step 1: Write the failing test**

Append this test method to the `NotificationResilienceTests` class in `BomPriceApproval.Tests/Approvals/NotificationResilienceTests.cs` (before the final closing `}`):

```csharp
    [Fact]
    public async Task Reject_ReturnsOk_EvenIfNotificationThrows()
    {
        var (reqId, _) = await WalkToMdReviewAsync();

        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        UseToken(mdToken);

        var rejectResp = await _client.PostAsJsonAsync($"/api/approvals/{reqId}/reject",
            new { Notes = "needs more detail" });

        rejectResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        UseToken(spToken);
        var after = await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}");
        after!.Status.Should().Be("Rejected");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~Approvals.NotificationResilienceTests.Reject_ReturnsOk_EvenIfNotificationThrows"`
Expected: FAIL. `Reject` returns 500 because the SalesPerson notification throw is uncaught.

- [ ] **Step 3: Wrap both `Reject` notification blocks in a single try/catch**

Open `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs` and replace the block at approximately lines 205-212:

Before:
```csharp
        await notificationSvc.SendAsync(req.SalesPersonId,
            $"Quotation rejected: {req.RefNo}. Reason: {request.Notes}", req.Id, "QuotationRequest");

        var accountants = await db.Users
            .Where(u => u.Role == UserRole.Accountant && u.IsActive).ToListAsync();
        foreach (var acct in accountants)
            await notificationSvc.SendAsync(acct.Id,
                $"Quotation rejected by MD: {req.RefNo}. Reason: {request.Notes}", req.Id, "QuotationRequest");
```

After:
```csharp
        try
        {
            await notificationSvc.SendAsync(req.SalesPersonId,
                $"Quotation rejected: {req.RefNo}. Reason: {request.Notes}", req.Id, "QuotationRequest");

            var accountants = await db.Users
                .Where(u => u.Role == UserRole.Accountant && u.IsActive).ToListAsync();
            foreach (var acct in accountants)
                await notificationSvc.SendAsync(acct.Id,
                    $"Quotation rejected by MD: {req.RefNo}. Reason: {request.Notes}", req.Id, "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful commit for {Entity} {Id}",
                "QuotationRequest", req.Id);
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~Approvals.NotificationResilienceTests.Reject_ReturnsOk_EvenIfNotificationThrows"`
Expected: PASS.

- [ ] **Step 5: Run existing Approvals tests**

Run: `dotnet test --filter "FullyQualifiedName~Approvals"`
Expected: All Approvals tests pass.

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Features/Approvals/ApprovalsController.cs BomPriceApproval.Tests/Approvals/NotificationResilienceTests.cs
git commit -m "fix(api): swallow+log notification dispatch failures in ApprovalsController.Reject"
```

---

## Task 9: Final verification

**Files:** none modified.

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test`
Expected: All tests pass, including:
- 6 new `NotificationResilienceTests` (one per endpoint)
- All pre-existing tests unchanged

If any test fails, do NOT proceed to close the plan. Diagnose and fix.

- [ ] **Step 2: Run a final build with warnings as errors check**

Run: `dotnet build /warnaserror`
Expected: Build succeeds with 0 errors and 0 warnings.

- [ ] **Step 3: Confirm nothing is uncommitted**

Run: `git status`
Expected: `nothing to commit, working tree clean`.

---

## Post-Plan Checklist

- Six call sites wrapped (Costing/SubmitItem, Requisitions/Create, Requisitions/Resubmit, Bom/Submit, Approvals/Approve, Approvals/Reject).
- Four controllers inject `ILogger<T>`.
- `NotificationService.SendAsync` is `virtual`.
- `ThrowingNotificationFactory` wired into six integration tests.
- Each new test asserts (a) 2xx response and (b) persisted state change.
- `dotnet test` passes end-to-end.
