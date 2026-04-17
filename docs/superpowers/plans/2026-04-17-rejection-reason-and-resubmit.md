# Rejection Reason Display & SalesPerson Resubmit Flow — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface MD rejection notes on the requisition detail page, and give SalesPersons a way to edit the rejected requisition's items and resubmit it for BOM.

**Architecture:** Soft-delete approvals via `IsSuperseded`/`SupersededAt` columns; atomic `POST /api/requisitions/{id}/resubmit` endpoint (items-only payload) resets status to `BomPending`, supersedes the rejected approval, deletes stale child rows (cascade-driven), and re-notifies BomCreators; dedicated `/requisitions/:id/edit` page mirrors `NewRequisitionPage` with items pre-filled.

**Tech Stack:** ASP.NET Core 8 + EF Core 8 (Npgsql), xUnit + Testcontainers, React 19 + Vite + TanStack Query + react-hook-form + Zod, Vitest + React Testing Library.

**Spec:** [`docs/superpowers/specs/2026-04-17-rejection-reason-and-resubmit-design.md`](../specs/2026-04-17-rejection-reason-and-resubmit-design.md)

---

## File Structure

### Files to create
- `BomPriceApproval.API/Infrastructure/Data/Migrations/<timestamp>_AddSupersededFieldsToQuotationApproval.cs` — EF migration
- `BomPriceApproval.Tests/Requisitions/ResubmitTests.cs` — integration tests for resubmit + rejection-notes surfacing
- `bom-web/src/features/requisitions/components/RequisitionItemsEditor.tsx` — extracted shared component
- `bom-web/src/features/requisitions/EditRequisitionPage.tsx` — new page
- `bom-web/src/features/requisitions/EditRequisitionPage.test.tsx` — frontend tests

### Files to modify
- `BomPriceApproval.API/Domain/Entities/QuotationApproval.cs` — add `IsSuperseded`, `SupersededAt`
- `BomPriceApproval.API/Domain/Entities/QuotationRequest.cs` — change `Approval` to `Approvals` collection
- `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs` — 1:N relationship, filtered index
- `BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs` — `ApprovalSummary`, new `ResubmitRequisitionRequest`
- `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` — surface notes in `Get`, add `Resubmit` action
- `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs` — update `Approve` to use `Approvals` navigation
- `bom-web/src/types/api.ts` — extend `ApprovalSummary`
- `bom-web/src/features/requisitions/requisitionsApi.ts` — add `useResubmitRequisition`
- `bom-web/src/features/requisitions/RequisitionDetailPage.tsx` — rejection block + resubmit button
- `bom-web/src/features/requisitions/RequisitionDetailPage.test.tsx` — new test cases
- `bom-web/src/features/requisitions/NewRequisitionPage.tsx` — swap inline items editor for shared component
- `bom-web/src/App.tsx` — register `/requisitions/:id/edit` route

---

## Task 1: Add soft-delete columns to `QuotationApproval`

**Files:**
- Modify: `BomPriceApproval.API/Domain/Entities/QuotationApproval.cs`

- [ ] **Step 1: Edit the entity**

Replace the existing content of `BomPriceApproval.API/Domain/Entities/QuotationApproval.cs` with:

```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class QuotationApproval
{
    public int Id { get; set; }
    public int QuotationRequestId { get; set; }
    public int ApprovedByUserId { get; set; }
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public bool IsApproved { get; set; }
    public bool IsSuperseded { get; set; }
    public DateTime? SupersededAt { get; set; }
    public QuotationRequest QuotationRequest { get; set; } = null!;
    public User ApprovedBy { get; set; } = null!;
    public ICollection<ApprovalItem> Items { get; set; } = [];
}
```

- [ ] **Step 2: Verify build passes**

Run: `dotnet build`
Expected: Succeeds (only entity shape changed; navigation from `QuotationRequest.Approval` still compiles since it's an optional reference).

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.API/Domain/Entities/QuotationApproval.cs
git commit -m "feat(api): add IsSuperseded and SupersededAt to QuotationApproval"
```

---

## Task 2: Change `QuotationRequest` → `QuotationApproval` to 1:N

**Files:**
- Modify: `BomPriceApproval.API/Domain/Entities/QuotationRequest.cs`
- Modify: `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs:84-94`

- [ ] **Step 1: Change `QuotationRequest.Approval` to a collection**

In `BomPriceApproval.API/Domain/Entities/QuotationRequest.cs`, replace:
```csharp
public QuotationApproval? Approval { get; set; }
```
with:
```csharp
public ICollection<QuotationApproval> Approvals { get; set; } = [];
```

- [ ] **Step 2: Update EF model config in `AppDbContext.cs`**

In `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs`, replace the block (currently lines 84-88):
```csharp
        // QuotationApproval → QuotationRequest (1:1)
        mb.Entity<QuotationApproval>()
            .HasOne(a => a.QuotationRequest)
            .WithOne(q => q.Approval)
            .HasForeignKey<QuotationApproval>(a => a.QuotationRequestId);
```
with:
```csharp
        // QuotationApproval → QuotationRequest (many:1 — superseded rows are kept as history)
        mb.Entity<QuotationApproval>()
            .HasOne(a => a.QuotationRequest)
            .WithMany(q => q.Approvals)
            .HasForeignKey(a => a.QuotationRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        // Fast lookup of the current (non-superseded) approval per requisition
        mb.Entity<QuotationApproval>()
            .HasIndex(a => a.QuotationRequestId)
            .HasFilter("\"IsSuperseded\" = false")
            .HasDatabaseName("ix_quotation_approvals_current");
```

- [ ] **Step 3: Fix call sites that still reference `q.Approval`**

Run: `git grep -n '\.Approval\b' BomPriceApproval.API`
Expected matches (not in the controller Get — we rewrite it in Task 5; focus on approvals flow):
- `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs` — in `Approve`, after `db.QuotationApprovals.Add(approval)`, the code writes the newly-created approval into the DB via DbSet, not via `req.Approval`, so no change needed there. Verify by reading the method.
- `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs:54` — `Include(r => r.Approval)`. We rewrite this in Task 5, but to keep this task compilable, change it **now** to:
  ```csharp
  .Include(r => r.Approvals)
  ```
  And update the `q.Approval is null ? null : new ApprovalSummary(q.Approval.IsApproved)` expression (line 68) to:
  ```csharp
  q.Approvals.Where(a => !a.IsSuperseded).Select(a => new ApprovalSummary(a.IsApproved)).FirstOrDefault()
  ```
  (We will expand the `ApprovalSummary` in Tasks 4 and 5; for now this keeps build+tests green.)

- [ ] **Step 4: Verify build passes**

Run: `dotnet build`
Expected: Succeeds. No migration yet — database will be out of sync, but the code compiles.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Domain/Entities/QuotationRequest.cs \
        BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs \
        BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs
git commit -m "feat(api): change QuotationRequest → QuotationApproval to 1:N with filtered index"
```

---

## Task 3: Create and apply EF migration

**Files:**
- Create: `BomPriceApproval.API/Infrastructure/Data/Migrations/<timestamp>_AddSupersededFieldsToQuotationApproval.cs`

- [ ] **Step 1: Generate the migration**

Run: `dotnet ef migrations add AddSupersededFieldsToQuotationApproval --project BomPriceApproval.API`
Expected: A new migration file and Designer file are created under `BomPriceApproval.API/Infrastructure/Data/Migrations/`.

- [ ] **Step 2: Inspect the generated migration**

Read the new `*_AddSupersededFieldsToQuotationApproval.cs` file. Confirm the `Up` method contains:
- `AddColumn<bool>(name: "IsSuperseded", ...)` with default `false`
- `AddColumn<DateTime>(name: "SupersededAt", ..., nullable: true)`
- `CreateIndex(... "ix_quotation_approvals_current" ...)` with filter `"IsSuperseded" = false`
- A change converting the unique FK index to non-unique (because 1:1 → 1:N)

If any of these are missing, fix the EF model config in `AppDbContext.cs` and re-generate (delete the migration files, re-run `dotnet ef migrations add`).

- [ ] **Step 3: Apply the migration to the local DB**

Run: `dotnet ef database update --project BomPriceApproval.API`
Expected: Migration applies without errors.

- [ ] **Step 4: Verify build still passes**

Run: `dotnet build`
Expected: Succeeds.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Data/Migrations/
git commit -m "feat(api): migration AddSupersededFieldsToQuotationApproval"
```

---

## Task 4: Extend `ApprovalSummary` DTO and update `Get` mapping

**Files:**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs:26`
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs:60-68`
- Modify: `bom-web/src/types/api.ts:69-71`

- [ ] **Step 1: Write the failing backend test**

Create `BomPriceApproval.Tests/Requisitions/ResubmitTests.cs` with the following initial test (more added later):

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Requisitions;

public class ResubmitTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private record ApprovalSummaryDto(bool IsApproved, string? Notes, DateTime ApprovedAt);
    private record RequisitionDetailDto(
        int Id, string RefNo, string Status,
        List<object> Items, ApprovalSummaryDto? Approval);

    [Fact]
    public async Task GetDetail_OnRejectedRequisition_ReturnsNotes()
    {
        // Seed the test DB by running the shared harness: create req, drive it through
        // BOM + costing + MD reject. This uses only public HTTP endpoints.
        // ... (the helper below builds a rejected req with notes "Margin too low")
        var (spToken, requisitionId) = await CreateRejectedRequisitionAsync("Margin too low");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);
        var resp = await _client.GetAsync($"/api/requisitions/{requisitionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await resp.Content.ReadFromJsonAsync<RequisitionDetailDto>();
        detail!.Approval.Should().NotBeNull();
        detail.Approval!.IsApproved.Should().BeFalse();
        detail.Approval.Notes.Should().Be("Margin too low");
    }

    // Helper stub — will be implemented in Task 7 alongside other resubmit tests.
    // For now, this test will compile but fail at runtime until the helper exists.
    private Task<(string spToken, int requisitionId)> CreateRejectedRequisitionAsync(string notes)
        => throw new NotImplementedException("Implemented in Task 7");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ResubmitTests.GetDetail_OnRejectedRequisition_ReturnsNotes"`
Expected: FAIL with `NotImplementedException` (placeholder helper). This just confirms test discovery works.

- [ ] **Step 3: Expand `ApprovalSummary` DTO**

In `BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs`, replace line 26:
```csharp
public record ApprovalSummary(bool IsApproved);
```
with:
```csharp
public record ApprovalSummary(bool IsApproved, string? Notes, DateTime ApprovedAt);
```

- [ ] **Step 4: Update `Get` action mapping**

In `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs`, replace the `Get` action's return (currently around lines 60-68) with:

```csharp
        return Ok(new RequisitionDetail(
            q.Id, q.RefNo, q.Status.ToString(),
            q.CustomerId, q.Customer.Name, q.Customer.Email, q.Customer.PhoneNumber, q.Customer.Address,
            q.CurrencyCode, q.ExchangeRateSnapshot,
            q.BranchId, q.Branch.Name, q.SalesPersonId, q.SalesPerson.Name,
            q.CreatedAt, q.UpdatedAt,
            q.Items.OrderBy(ri => ri.SortOrder).Select(ri => new RequisitionItemDto(
                ri.Id, ri.ItemId, ri.Item.Description, ri.ExpectedQty, ri.SortOrder)).ToList(),
            q.Approvals
                .Where(a => !a.IsSuperseded)
                .OrderByDescending(a => a.ApprovedAt)
                .Select(a => new ApprovalSummary(a.IsApproved, a.Notes, a.ApprovedAt))
                .FirstOrDefault()));
```

- [ ] **Step 5: Update frontend types**

In `bom-web/src/types/api.ts`, replace lines 69-71:
```ts
export interface ApprovalSummary {
  isApproved: boolean;
}
```
with:
```ts
export interface ApprovalSummary {
  isApproved: boolean;
  notes: string | null;
  approvedAt: string;
}
```

- [ ] **Step 6: Verify build + existing tests**

Run: `dotnet build` and `dotnet test --filter "FullyQualifiedName~RequisitionWorkflowTests"`
Expected: Build succeeds; existing tests pass.

Run (from `bom-web/`): `npm run build`
Expected: TypeScript compile succeeds.

- [ ] **Step 7: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs \
        BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs \
        BomPriceApproval.Tests/Requisitions/ResubmitTests.cs \
        bom-web/src/types/api.ts
git commit -m "feat(api+web): surface rejection notes in RequisitionDetail ApprovalSummary"
```

---

## Task 5: Frontend — render rejection reason block on detail page

**Files:**
- Modify: `bom-web/src/features/requisitions/RequisitionDetailPage.tsx:149-158`
- Modify: `bom-web/src/features/requisitions/RequisitionDetailPage.test.tsx`

- [ ] **Step 1: Write the failing frontend test**

Append to `bom-web/src/features/requisitions/RequisitionDetailPage.test.tsx` (inside the existing `describe` block):

```tsx
it("renders rejection reason block when approval.isApproved is false", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "SalesPerson", userId: 10, name: "Ali", branchId: 1,
    });
    const rejected: RequisitionDetail = {
      ...sample,
      status: "Rejected",
      approval: {
        isApproved: false,
        notes: "Margin too low",
        approvedAt: "2026-04-15T12:00:00Z",
      },
    };
    vi.mocked(api.get).mockResolvedValueOnce({ data: rejected });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());

    expect(screen.getByText("Rejection reason")).toBeInTheDocument();
    expect(screen.getByText("Margin too low")).toBeInTheDocument();
    // Styled as destructive:
    const notesEl = screen.getByText("Margin too low").closest("div");
    expect(notesEl).toHaveClass("text-destructive");
});

it("renders notes block (non-destructive) when approval.isApproved is true", async () => {
    const approved: RequisitionDetail = {
      ...sample,
      status: "Approved",
      approval: {
        isApproved: true,
        notes: "Approved with conditions",
        approvedAt: "2026-04-15T12:00:00Z",
      },
    };
    vi.mocked(api.get).mockResolvedValueOnce({ data: approved });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());

    expect(screen.getByText("Notes")).toBeInTheDocument();
    expect(screen.getByText("Approved with conditions")).toBeInTheDocument();
});
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `bom-web/`): `npm test -- RequisitionDetailPage`
Expected: FAIL — "Rejection reason" text not found.

- [ ] **Step 3: Update the Approval card**

In `bom-web/src/features/requisitions/RequisitionDetailPage.tsx`, replace the current Approval card body (currently `{r.approval ? <LabeledValue ... /> : <p>Not yet...</p>}` around lines 149-158):

```tsx
          <Card>
            <CardHeader><CardTitle>Approval</CardTitle></CardHeader>
            <CardContent>
              {r.approval ? (
                <>
                  <LabeledValue
                    label={r.approval.isApproved ? "Approved" : "Rejected"}
                    value={formatRelative(r.approval.approvedAt)}
                  />
                  {r.approval.notes && (
                    <div className={`mt-2 text-sm ${r.approval.isApproved ? "" : "text-destructive"}`}>
                      <p className="font-medium">
                        {r.approval.isApproved ? "Notes" : "Rejection reason"}
                      </p>
                      <p className="mt-1 whitespace-pre-wrap">{r.approval.notes}</p>
                    </div>
                  )}
                </>
              ) : (
                <p className="text-sm text-muted-foreground">Not yet submitted for approval.</p>
              )}
            </CardContent>
          </Card>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- RequisitionDetailPage`
Expected: PASS — all existing tests still pass, new tests pass.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/features/requisitions/RequisitionDetailPage.tsx \
        bom-web/src/features/requisitions/RequisitionDetailPage.test.tsx
git commit -m "feat(web): render rejection reason block on RequisitionDetailPage"
```

---

## Task 6: Backend — new `POST /api/requisitions/{id}/resubmit` endpoint

**Files:**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs`
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs`

- [ ] **Step 1: Add request DTO**

Append to `BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs`:

```csharp
public record ResubmitRequisitionRequest(List<RequisitionItemInput> Items);
```

- [ ] **Step 2: Add `Resubmit` action to `RequisitionsController`**

Append inside the class in `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` (before the closing brace):

```csharp
    [HttpPost("{id}/resubmit")]
    [Authorize(Roles = "SalesPerson")]
    public async Task<IActionResult> Resubmit(int id, ResubmitRequisitionRequest req)
    {
        var q = await db.QuotationRequests
            .Include(r => r.Items)
            .Include(r => r.Approvals)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (q is null) return NotFound();
        if (q.SalesPersonId != CurrentUserId) return Forbid();

        if (q.Status != RequisitionStatus.Rejected)
            return Validation
                .Detail("Requisition is not in Rejected status.")
                .Field("Status", "Only rejected requisitions can be resubmitted.")
                .Return();

        if (req.Items.Count == 0)
            return Validation
                .Detail("At least one item is required.")
                .Field("Items", "At least one item is required.")
                .Return();

        if (req.Items.Any(i => i.ExpectedQty <= 0))
        {
            var builder = Validation.Detail("ExpectedQty must be greater than 0.");
            for (int i = 0; i < req.Items.Count; i++)
                if (req.Items[i].ExpectedQty <= 0)
                    builder.Field($"Items[{i}].ExpectedQty", "Must be greater than 0.");
            return builder.Return();
        }

        var distinctItemIds = req.Items.Select(i => i.ItemId).Distinct().ToList();
        if (distinctItemIds.Count != req.Items.Count)
            return Validation
                .Detail("Duplicate items in requisition are not allowed.")
                .Field("Items", "Duplicate items are not allowed.")
                .Return();

        var activeItemIds = await db.Items
            .Where(i => distinctItemIds.Contains(i.Id) && i.IsActive)
            .Select(i => i.Id)
            .ToListAsync();
        var missingItems = distinctItemIds.Except(activeItemIds).ToList();
        if (missingItems.Count > 0)
        {
            var builder = Validation.Detail($"Unknown or inactive items: {string.Join(", ", missingItems)}");
            for (int i = 0; i < req.Items.Count; i++)
                if (missingItems.Contains(req.Items[i].ItemId))
                    builder.Field($"Items[{i}].ItemId", "Unknown or inactive.");
            return builder.Return();
        }

        decimal? rateSnapshot = null;
        if (q.CurrencyCode != "AED")
        {
            var rate = await db.ExchangeRates
                .Where(e => e.CurrencyCode == q.CurrencyCode && e.IsActive)
                .OrderByDescending(e => e.EffectiveDate).FirstOrDefaultAsync();
            if (rate is null)
                return Validation
                    .Detail($"No active exchange rate for {q.CurrencyCode}")
                    .Field("CurrencyCode", "No active exchange rate.")
                    .Return();
            rateSnapshot = rate.RateToAed;
        }

        await using var tx = await db.Database.BeginTransactionAsync();

        var currentApproval = q.Approvals.FirstOrDefault(a => !a.IsSuperseded);
        if (currentApproval is not null)
        {
            currentApproval.IsSuperseded = true;
            currentApproval.SupersededAt = DateTime.UtcNow;
        }

        db.RequisitionItems.RemoveRange(q.Items);

        foreach (var (input, i) in req.Items.Select((x, idx) => (x, idx)))
        {
            db.RequisitionItems.Add(new RequisitionItem
            {
                QuotationRequestId = q.Id,
                ItemId = input.ItemId,
                ExpectedQty = input.ExpectedQty,
                SortOrder = i + 1
            });
        }

        q.ExchangeRateSnapshot = rateSnapshot;
        q.Status = RequisitionStatus.BomPending;
        q.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        var bomCreators = await db.Users
            .Where(u => u.Role == UserRole.BomCreator && (u.BranchId == q.BranchId || u.BranchId == null) && u.IsActive)
            .ToListAsync();

        foreach (var creator in bomCreators)
            await notificationService.SendAsync(creator.Id,
                $"Resubmitted BOM request: {q.RefNo}", q.Id, "QuotationRequest");

        return Ok(new { q.Id, q.RefNo, Status = q.Status.ToString() });
    }
```

- [ ] **Step 3: Verify build passes**

Run: `dotnet build`
Expected: Succeeds.

- [ ] **Step 4: Run existing tests to confirm no regression**

Run: `dotnet test --filter "FullyQualifiedName~Requisitions"`
Expected: All existing tests pass (the one new test from Task 4 still fails at `NotImplementedException`).

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs \
        BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs
git commit -m "feat(api): POST /api/requisitions/{id}/resubmit — items-only, atomic transition"
```

---

## Task 7: Backend — integration tests for resubmit flow

**Files:**
- Modify: `BomPriceApproval.Tests/Requisitions/ResubmitTests.cs`

**Context for the implementer:** The existing `BomPriceApproval.Tests/Bom/BomWithCostTests.cs` drives the full create → BOM → costing workflow via HTTP. Use it as the reference for endpoint paths and payloads. Key facts:

- Seed users (`BomPriceApproval.API/Program.cs:86-130`): `admin@test.com`/`Admin@1234`, `ali@test.com`/`Test@1234` (SalesPerson, Fujairah), `bob@test.com`/`Test@1234` (BomCreator, Fujairah), `sara@test.com`/`Test@1234` (Accountant, Fujairah), `md@test.com`/`Test@1234` (MD, branch-less).
- There is **no** second SalesPerson in the seed. The non-owner test creates one via `POST /api/users` (Admin).
- BOM: `POST /api/bom/{reqId}/items/{riId}/start`, `PUT /api/bom/{reqId}/items/{riId}/lines`, `POST /api/bom/{reqId}/submit`.
- Costing: `POST /api/costing/{reqId}/items/{riId}/start`, `POST /api/costing/{reqId}/items/{riId}/submit` with body `{ RawMaterialCosts: [...], LandedCostType, LandedCostValue, FohAmount }`.
- BomLineIds are obtained by re-fetching `GET /api/bom/{reqId}` **after** BOM submit.
- Reject: `POST /api/approvals/{reqId}/reject` body `{ Notes }`.

- [ ] **Step 1: Write the test file**

Replace the entire contents of `BomPriceApproval.Tests/Requisitions/ResubmitTests.cs` with:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Requisitions;

public class ResubmitTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private record AuthLoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record ItemMin(int Id, string Code, string Description, string Type);
    private record CustomerMin(int Id, string Name);
    private record ProcessMin(int Id, string Name, int DisplayOrder, bool IsActive);
    private record CreatedReq(int Id, string RefNo);
    private record RiMin(int Id, int ItemId);
    private record ReqDetailMin(int Id, string RefNo, string Status, List<RiMin> Items);
    private record BomLineMin(int Id);
    private record BomItemMin(int RequisitionItemId, List<BomLineMin> Lines);
    private record BomReviewMin(int RequisitionId, string RefNo, string RequisitionStatus, List<BomItemMin> Items);
    private record ApprovalSummaryMin(bool IsApproved, string? Notes, DateTime ApprovedAt);
    private record ReqDetailWithApproval(int Id, string RefNo, string Status, ApprovalSummaryMin? Approval);
    private record CreatedUser(int Id);

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<AuthLoginResponse>();
        return body!.AccessToken;
    }

    private void UseToken(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<int> CreateActiveFinishedGoodAsync()
    {
        var code = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var resp = await _client.PostAsJsonAsync("/api/items",
            new { Code = code, Description = "Test FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        resp.EnsureSuccessStatusCode();
        var item = await resp.Content.ReadFromJsonAsync<ItemMin>();
        return item!.Id;
    }

    private async Task<int> CreateActiveRawMaterialAsync()
    {
        var code = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var resp = await _client.PostAsJsonAsync("/api/items",
            new { Code = code, Description = "Test RM", Type = "RawMaterial", LastPurchasePrice = 5m });
        resp.EnsureSuccessStatusCode();
        var item = await resp.Content.ReadFromJsonAsync<ItemMin>();
        return item!.Id;
    }

    private async Task<int> EnsureProcessAsync()
    {
        var processes = await _client.GetFromJsonAsync<List<ProcessMin>>("/api/processes");
        if (processes is { Count: > 0 }) return processes[0].Id;
        var code = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var resp = await _client.PostAsJsonAsync("/api/processes",
            new { Name = code, DisplayOrder = 99 });
        resp.EnsureSuccessStatusCode();
        var p = await resp.Content.ReadFromJsonAsync<ProcessMin>();
        return p!.Id;
    }

    // Drive a requisition from create → BOM → costing → MD reject. Returns the id + the SalesPerson token.
    private async Task<(string spToken, int reqId)> CreateRejectedRequisitionAsync(string notes)
    {
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        var mdToken = await LoginAsync("md@test.com", "Test@1234");

        // SalesPerson creates an FG item and a new requisition.
        UseToken(spToken);
        var fgId = await CreateActiveFinishedGoodAsync();
        var customers = await _client.GetFromJsonAsync<List<CustomerMin>>("/api/customers");
        var createResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fgId, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<CreatedReq>();
        var reqId = created!.Id;

        var detail = await _client.GetFromJsonAsync<ReqDetailMin>($"/api/requisitions/{reqId}");
        var riId = detail!.Items[0].Id;

        // Admin creates a raw material + process (admin is the allowed role for both endpoints).
        UseToken(adminToken);
        var rmId = await CreateActiveRawMaterialAsync();
        var processId = await EnsureProcessAsync();

        // BomCreator: start, save lines, submit BOM.
        UseToken(bomToken);
        var startBom = await _client.PostAsync($"/api/bom/{reqId}/items/{riId}/start", null);
        startBom.IsSuccessStatusCode.Should().BeTrue();

        var putLines = await _client.PutAsJsonAsync($"/api/bom/{reqId}/items/{riId}/lines", new
        {
            Lines = new[]
            {
                new { ProcessId = processId, RawMaterialItemId = rmId, QtyPerKg = 1.0m, WastagePct = 0m }
            }
        });
        putLines.IsSuccessStatusCode.Should().BeTrue();

        var submitBom = await _client.PostAsync($"/api/bom/{reqId}/submit", null);
        submitBom.IsSuccessStatusCode.Should().BeTrue();

        // Accountant: start costing, then re-fetch BOM review to get BomLineId, then submit costing.
        UseToken(acctToken);
        var startCost = await _client.PostAsync($"/api/costing/{reqId}/items/{riId}/start", null);
        startCost.IsSuccessStatusCode.Should().BeTrue();

        var bomReview = await _client.GetFromJsonAsync<BomReviewMin>($"/api/bom/{reqId}");
        var bomLineId = bomReview!.Items.First(i => i.RequisitionItemId == riId).Lines[0].Id;

        var submitCost = await _client.PostAsJsonAsync(
            $"/api/costing/{reqId}/items/{riId}/submit", new
            {
                RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 5m, CurrencyCode = "AED" } },
                LandedCostType = "Percentage",
                LandedCostValue = 0m,
                FohAmount = 0m
            });
        submitCost.IsSuccessStatusCode.Should().BeTrue();

        // MD rejects with the supplied notes.
        UseToken(mdToken);
        var reject = await _client.PostAsJsonAsync($"/api/approvals/{reqId}/reject", new { Notes = notes });
        reject.IsSuccessStatusCode.Should().BeTrue();

        return (spToken, reqId);
    }

    // Create a second SalesPerson in the same branch via Admin API. Returns (token, userId).
    private async Task<(string token, int userId)> CreateSecondSalesPersonAsync()
    {
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        UseToken(adminToken);
        var email = $"sp{Guid.NewGuid():N}@t.com";
        var password = "Test@1234";
        var resp = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "Other Sales",
            Email = email,
            Password = password,
            Role = "SalesPerson",
            BranchId = 1
        });
        resp.EnsureSuccessStatusCode();
        var created = await resp.Content.ReadFromJsonAsync<CreatedUser>();
        var token = await LoginAsync(email, password);
        return (token, created!.Id);
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDetail_OnRejectedRequisition_ReturnsNotes()
    {
        var (spToken, reqId) = await CreateRejectedRequisitionAsync("Margin too low");
        UseToken(spToken);

        var resp = await _client.GetAsync($"/api/requisitions/{reqId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<ReqDetailWithApproval>();
        body!.Status.Should().Be("Rejected");
        body.Approval.Should().NotBeNull();
        body.Approval!.IsApproved.Should().BeFalse();
        body.Approval.Notes.Should().Be("Margin too low");
    }

    [Fact]
    public async Task Resubmit_RejectedRequisition_TransitionsToBomPending_AndSupersedesApproval()
    {
        var (spToken, reqId) = await CreateRejectedRequisitionAsync("Try again");
        UseToken(spToken);
        var newFgId = await CreateActiveFinishedGoodAsync();

        var resp = await _client.PostAsJsonAsync($"/api/requisitions/{reqId}/resubmit",
            new { Items = new[] { new { ItemId = newFgId, ExpectedQty = 250m } } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await _client.GetFromJsonAsync<ReqDetailWithApproval>($"/api/requisitions/{reqId}");
        detail!.Status.Should().Be("BomPending");
        detail.Approval.Should().BeNull("the superseded approval must be filtered out of the current-approval projection");
    }

    [Fact]
    public async Task Resubmit_StatusNotRejected_ReturnsValidationProblem_StatusField()
    {
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        UseToken(spToken);
        var itemId = await CreateActiveFinishedGoodAsync();
        var customers = await _client.GetFromJsonAsync<List<CustomerMin>>("/api/customers");

        var createResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = itemId, ExpectedQty = 1m } },
            CurrencyCode = "AED"
        });
        var created = await createResp.Content.ReadFromJsonAsync<CreatedReq>();

        var resp = await _client.PostAsJsonAsync($"/api/requisitions/{created!.Id}/resubmit",
            new { Items = new[] { new { ItemId = itemId, ExpectedQty = 2m } } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Errors.Should().ContainKey("Status");
    }

    [Fact]
    public async Task Resubmit_EmptyItems_ReturnsValidationProblem_ItemsField()
    {
        var (spToken, reqId) = await CreateRejectedRequisitionAsync("Reason");
        UseToken(spToken);

        var resp = await _client.PostAsJsonAsync($"/api/requisitions/{reqId}/resubmit",
            new { Items = Array.Empty<object>() });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Errors.Should().ContainKey("Items");
    }

    [Fact]
    public async Task Resubmit_NonOwnerSameBranch_Forbidden()
    {
        var (_, reqId) = await CreateRejectedRequisitionAsync("R1");

        // Admin-created second SalesPerson in branch 1, not the owner.
        var (otherSpToken, _) = await CreateSecondSalesPersonAsync();
        UseToken(otherSpToken);

        // Any valid FG id will do — we expect Forbid before validation.
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        UseToken(spToken);
        var anyFg = await CreateActiveFinishedGoodAsync();

        UseToken(otherSpToken);
        var resp = await _client.PostAsJsonAsync($"/api/requisitions/{reqId}/resubmit",
            new { Items = new[] { new { ItemId = anyFg, ExpectedQty = 1m } } });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

**If the `POST /api/users` payload above doesn't match the controller contract**, read `BomPriceApproval.API/Features/Users/UsersController.cs` + `UserDtos.cs` (or equivalent) and align the field names. Don't guess.

- [ ] **Step 2: Run the new tests**

Run: `dotnet test --filter "FullyQualifiedName~ResubmitTests"`
Expected: All 5 tests PASS. If a test fails, read the failure carefully — most failures here indicate an API-path or payload mismatch. Fix the helper to match the real endpoint (consult `BomWithCostTests.cs`) rather than adjusting the assertion.

- [ ] **Step 3: Run the full suite to confirm no regressions**

Run: `dotnet test`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.Tests/Requisitions/ResubmitTests.cs
git commit -m "test(api): integration tests for resubmit flow + rejection notes"
```

---

## Task 8: Frontend — `useResubmitRequisition` hook

**Files:**
- Modify: `bom-web/src/features/requisitions/requisitionsApi.ts`

- [ ] **Step 1: Add the hook**

Append to `bom-web/src/features/requisitions/requisitionsApi.ts`:

```ts
export interface ResubmitRequisitionRequest {
  items: { itemId: number; expectedQty: number }[];
}

export function useResubmitRequisition(id: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: ResubmitRequisitionRequest) =>
      api
        .post<{ id: number; refNo: string; status: string }>(
          `/requisitions/${id}/resubmit`,
          body,
        )
        .then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(id) });
      qc.invalidateQueries({ queryKey: requisitionKeys.list() });
    },
  });
}
```

- [ ] **Step 2: Verify TypeScript compiles**

Run (from `bom-web/`): `npm run build`
Expected: Succeeds.

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/features/requisitions/requisitionsApi.ts
git commit -m "feat(web): add useResubmitRequisition hook"
```

---

## Task 9: Frontend — extract shared `RequisitionItemsEditor`

**Files:**
- Create: `bom-web/src/features/requisitions/components/RequisitionItemsEditor.tsx`
- Modify: `bom-web/src/features/requisitions/NewRequisitionPage.tsx`

- [ ] **Step 1: Create the shared component**

Create `bom-web/src/features/requisitions/components/RequisitionItemsEditor.tsx` with the exact content below. This extracts the items-array editor from `NewRequisitionPage`, keeping behavior identical. The component is generic over the form shape via a constrained type.

```tsx
import { Controller, useFieldArray, useWatch } from "react-hook-form";
import type { Control, FieldErrors, UseFormRegister } from "react-hook-form";
import { Plus, Trash2 } from "lucide-react";

import { Button } from "@/components/ui/Button";
import { SearchableSelect } from "@/components/ui/SearchableSelect";
import type { Item } from "@/types/api";

export interface ItemsFormShape {
  items: {
    item: { id: number } | null;
    expectedQty: number;
  }[];
}

interface Props<T extends ItemsFormShape> {
  control: Control<T>;
  register: UseFormRegister<T>;
  errors: FieldErrors<T>;
  availableItems: Item[];
}

export function RequisitionItemsEditor<T extends ItemsFormShape>({
  control,
  register,
  errors,
  availableItems,
}: Props<T>) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const { fields, append, remove } = useFieldArray({ control: control as any, name: "items" as any });
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const watchedItems = useWatch({ control: control as any, name: "items" as any }) as ItemsFormShape["items"] | undefined;

  const availableFor = (rowIndex: number): Item[] => {
    const takenIds = new Set(
      (watchedItems ?? [])
        .map((row, i) => (i !== rowIndex ? row?.item?.id : undefined))
        .filter((v): v is number => typeof v === "number"),
    );
    return availableItems.filter((it) => !takenIds.has(it.id));
  };

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const itemsErrors = (errors as any).items;

  return (
    <div className="space-y-2">
      <label className="text-sm font-medium">Items</label>
      {fields.map((field, index) => (
        <div key={field.id} className="flex items-start gap-2">
          <div className="flex-1">
            <Controller
              // eslint-disable-next-line @typescript-eslint/no-explicit-any
              control={control as any}
              // eslint-disable-next-line @typescript-eslint/no-explicit-any
              name={`items.${index}.item` as any}
              render={({ field: f }) => (
                <SearchableSelect<Item>
                  id={`item-${index}`}
                  options={availableFor(index)}
                  value={f.value as Item | null}
                  onChange={f.onChange}
                  getLabel={(i) => i.description}
                  getValue={(i) => i.id}
                  placeholder="Search items…"
                />
              )}
            />
            {itemsErrors?.[index]?.item && (
              <p className="text-xs text-destructive">
                {itemsErrors[index].item?.message as string}
              </p>
            )}
          </div>
          <div className="w-32">
            <input
              type="number"
              step="0.0001"
              className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
              placeholder="Qty"
              // eslint-disable-next-line @typescript-eslint/no-explicit-any
              {...register(`items.${index}.expectedQty` as any, { valueAsNumber: true })}
            />
            {itemsErrors?.[index]?.expectedQty && (
              <p className="text-xs text-destructive">
                {itemsErrors[index].expectedQty?.message as string}
              </p>
            )}
          </div>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            disabled={fields.length <= 1}
            onClick={() => remove(index)}
          >
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
      ))}
      <Button
        type="button"
        variant="outline"
        size="sm"
        onClick={() =>
          append({
            item: null as unknown as { id: number },
            expectedQty: undefined as unknown as number,
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
          } as any)
        }
      >
        <Plus className="mr-1 h-4 w-4" /> Add Item
      </Button>
      {itemsErrors?.root && (
        <p className="text-xs text-destructive">{itemsErrors.root.message as string}</p>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Refactor `NewRequisitionPage` to use the shared component**

In `bom-web/src/features/requisitions/NewRequisitionPage.tsx`:

1. Add the import at the top:
   ```ts
   import { RequisitionItemsEditor } from "./components/RequisitionItemsEditor";
   ```

2. Remove these pieces (they now live in the shared component):
   - The `useFieldArray` destructure (currently `const { fields, append, remove } = useFieldArray(...)` around line 71).
   - The `useWatch` destructure (line 73).
   - The `availableItemsFor` helper (lines 75-83).

3. Replace the `<div className="space-y-2">…</div>` block that renders items (lines 149-213) with:
   ```tsx
   <RequisitionItemsEditor
     control={control}
     register={register}
     errors={errors}
     availableItems={itemsQ.data ?? []}
   />
   ```

- [ ] **Step 3: Run existing tests**

Run (from `bom-web/`): `npm test -- NewRequisitionPage`
Expected: All existing NewRequisitionPage tests PASS. If any fail, fix the shared component or the integration in `NewRequisitionPage` — do not change the tests.

- [ ] **Step 4: Verify TypeScript compiles**

Run: `npm run build`
Expected: Succeeds.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/features/requisitions/components/RequisitionItemsEditor.tsx \
        bom-web/src/features/requisitions/NewRequisitionPage.tsx
git commit -m "refactor(web): extract RequisitionItemsEditor for reuse by edit page"
```

---

## Task 10: Frontend — `EditRequisitionPage` + route + action button

**Files:**
- Create: `bom-web/src/features/requisitions/EditRequisitionPage.tsx`
- Modify: `bom-web/src/features/requisitions/RequisitionDetailPage.tsx:21-34`
- Modify: `bom-web/src/App.tsx`

- [ ] **Step 1: Create the edit page**

Create `bom-web/src/features/requisitions/EditRequisitionPage.tsx` with:

```tsx
import { useForm } from "react-hook-form";
import type { Path } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Link, useNavigate, useParams } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { useEffect } from "react";

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { useItems } from "@/api/lookups";
import { useRequisition, useResubmitRequisition } from "./requisitionsApi";
import { RequisitionItemsEditor } from "./components/RequisitionItemsEditor";
import { notify } from "@/lib/notify";
import { extractFieldErrors } from "@/lib/apiError";
import { useAuthStore } from "@/store/authStore";

const itemRowSchema = z.object({
  item: z
    .object({ id: z.number() })
    .nullable()
    .refine((v) => v !== null, { message: "Item is required" }),
  expectedQty: z
    .number({ invalid_type_error: "Qty is required" })
    .positive("Qty must be greater than zero"),
});

const schema = z.object({
  items: z
    .array(itemRowSchema)
    .min(1, "At least one item is required")
    .refine(
      (arr) => {
        const ids = arr
          .map((r) => r.item?.id)
          .filter((v): v is number => typeof v === "number");
        return new Set(ids).size === ids.length;
      },
      { message: "Duplicate items not allowed" },
    ),
});

type FormValues = z.infer<typeof schema>;

export default function EditRequisitionPage() {
  const { id } = useParams<{ id: string }>();
  const numericId = Number(id);
  const navigate = useNavigate();
  const detailQ = useRequisition(numericId);
  const itemsQ = useItems();
  const resubmit = useResubmitRequisition(numericId);
  const userId = useAuthStore((s) => s.user?.userId);

  const {
    control,
    handleSubmit,
    register,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { items: [] },
  });

  useEffect(() => {
    if (!detailQ.data) return;
    reset({
      items: detailQ.data.items.map((ri) => ({
        item: { id: ri.itemId },
        expectedQty: ri.expectedQty,
      })),
    });
  }, [detailQ.data, reset]);

  if (detailQ.isLoading || itemsQ.isLoading) {
    return <p className="text-sm text-muted-foreground">Loading…</p>;
  }

  if (detailQ.isError || itemsQ.isError) {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center text-destructive">
          Failed to load requisition.
        </CardContent>
      </Card>
    );
  }

  const r = detailQ.data!;

  if (r.status !== "Rejected") {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center">
          <p className="text-sm">
            Cannot edit — requisition status is <strong>{r.status}</strong>.
          </p>
          <Link to={`/requisitions/${id}`} className="mt-4 inline-block text-sm underline">
            Back to requisition
          </Link>
        </CardContent>
      </Card>
    );
  }

  if (userId !== r.salesPersonId) {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center">
          <p className="text-sm">Only the owning sales person can edit this requisition.</p>
          <Link to={`/requisitions/${id}`} className="mt-4 inline-block text-sm underline">
            Back to requisition
          </Link>
        </CardContent>
      </Card>
    );
  }

  const onSubmit = handleSubmit(async (values) => {
    try {
      await resubmit.mutateAsync({
        items: values.items.map((row) => ({
          itemId: row.item!.id,
          expectedQty: row.expectedQty,
        })),
      });
      notify.success("Requisition resubmitted");
      navigate(`/requisitions/${id}`, { replace: true });
    } catch (e) {
      const fields = extractFieldErrors(e);
      for (const [key, msg] of Object.entries(fields)) {
        setError(key as Path<FormValues>, { type: "server", message: msg });
      }
      notify.fromApiError(e, "Failed to resubmit requisition");
    }
  });

  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <Link
        to={`/requisitions/${id}`}
        className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-4 w-4" /> Back to requisition
      </Link>

      <Card>
        <CardHeader>
          <CardTitle>Edit &amp; Resubmit {r.refNo}</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          {r.approval?.notes && (
            <div className="rounded-md border border-destructive/40 bg-destructive/5 p-3 text-sm">
              <p className="font-medium text-destructive">Previous rejection reason</p>
              <p className="mt-1 whitespace-pre-wrap">{r.approval.notes}</p>
            </div>
          )}

          <div className="text-sm text-muted-foreground">
            Customer: <span className="text-foreground">{r.customerName}</span> • Currency:{" "}
            <span className="text-foreground">{r.currencyCode}</span>
          </div>

          <form onSubmit={onSubmit} className="space-y-4" noValidate>
            <RequisitionItemsEditor
              control={control}
              register={register}
              errors={errors}
              availableItems={itemsQ.data ?? []}
            />
            <Button type="submit" disabled={isSubmitting || resubmit.isPending}>
              {resubmit.isPending ? "Resubmitting…" : "Resubmit for BOM"}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
```

- [ ] **Step 2: Register the route in `App.tsx`**

In `bom-web/src/App.tsx`:

1. Add import near the other `features/requisitions` imports (around line 9):
   ```ts
   import EditRequisitionPage from "@/features/requisitions/EditRequisitionPage";
   ```

2. Add a new route inside the `children` array, just before `requisitions/:id/bom`:
   ```tsx
         {
           path: "requisitions/:id/edit",
           element: (
             <ProtectedRoute allow={["SalesPerson"]}>
               <EditRequisitionPage />
             </ProtectedRoute>
           ),
         },
   ```

- [ ] **Step 3: Add the "Edit & Resubmit" action button**

In `bom-web/src/features/requisitions/RequisitionDetailPage.tsx`, edit `actionButtonFor` (currently lines 21-34) by inserting the new branch just before the final `return null`:

```ts
  if (role === "SalesPerson" && status === "Rejected")
    return { label: "Edit & Resubmit", path: "edit" };
  return null;
```

- [ ] **Step 4: Verify TypeScript compiles**

Run (from `bom-web/`): `npm run build`
Expected: Succeeds.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/features/requisitions/EditRequisitionPage.tsx \
        bom-web/src/features/requisitions/RequisitionDetailPage.tsx \
        bom-web/src/App.tsx
git commit -m "feat(web): EditRequisitionPage + Edit & Resubmit action for SalesPerson"
```

---

## Task 11: Frontend — tests for detail-page button and edit page

**Files:**
- Modify: `bom-web/src/features/requisitions/RequisitionDetailPage.test.tsx`
- Create: `bom-web/src/features/requisitions/EditRequisitionPage.test.tsx`

- [ ] **Step 1: Add detail-page tests for the resubmit button**

Append to `bom-web/src/features/requisitions/RequisitionDetailPage.test.tsx` (inside the existing `describe` block):

```tsx
it('shows "Edit & Resubmit" button for the owning SalesPerson when status is Rejected', async () => {
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "SalesPerson", userId: 10, name: "Ali", branchId: 1, // userId matches sample.salesPersonId
    });
    vi.mocked(api.get).mockResolvedValueOnce({
      data: {
        ...sample,
        status: "Rejected",
        approval: { isApproved: false, notes: "try again", approvedAt: "2026-04-15T12:00:00Z" },
      },
    });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /edit & resubmit/i })).toBeInTheDocument();
});

it('does not show "Edit & Resubmit" for non-SalesPerson roles', async () => {
    for (const role of ["BomCreator", "Accountant", "ManagingDirector"] as const) {
      vi.mocked(api.get).mockResolvedValueOnce({
        data: {
          ...sample,
          status: "Rejected",
          approval: { isApproved: false, notes: "try again", approvedAt: "2026-04-15T12:00:00Z" },
        },
      });
      useAuthStore.getState().setSession({
        accessToken: "at", refreshToken: "rt",
        role, userId: 99, name: "X", branchId: 1,
      });
      const { unmount } = render(wrap(<RequisitionDetailPage />));
      await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
      expect(screen.queryByRole("button", { name: /edit & resubmit/i })).not.toBeInTheDocument();
      unmount();
    }
});
```

- [ ] **Step 2: Create edit-page tests**

Create `bom-web/src/features/requisitions/EditRequisitionPage.test.tsx` with:

```tsx
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import type { ReactNode } from "react";
import type { RequisitionDetail, Item } from "@/types/api";
import { useAuthStore } from "@/store/authStore";

vi.mock("@/api/axios", () => ({ api: { get: vi.fn(), post: vi.fn() } }));

import { api } from "@/api/axios";
import EditRequisitionPage from "./EditRequisitionPage";

function wrap(ui: ReactNode, path = "/requisitions/1/edit") {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/requisitions/:id/edit" element={ui} />
          <Route path="/requisitions/:id" element={<div>detail page</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

const sampleRejected: RequisitionDetail = {
  id: 1, refNo: "REQ-0001", status: "Rejected",
  customerId: 3, customerName: "ACME",
  customerEmail: "s@a.test", customerPhone: "+971", customerAddress: "FZ",
  currencyCode: "AED", exchangeRateSnapshot: null,
  branchId: 1, branchName: "Fujairah",
  salesPersonId: 10, salesPersonName: "Ali",
  createdAt: "2026-04-14T10:00:00Z", updatedAt: "2026-04-14T11:00:00Z",
  items: [
    { id: 1, itemId: 2, itemDescription: "HDPE Pipe 20mm", expectedQty: 100, sortOrder: 1 },
  ],
  approval: { isApproved: false, notes: "Margin too low", approvedAt: "2026-04-15T12:00:00Z" },
};

const sampleItems: Item[] = [
  { id: 2, code: "FG-1", description: "HDPE Pipe 20mm", type: "FinishedGood", branchId: 1, isActive: true, lastPurchasePrice: null },
  { id: 3, code: "FG-2", description: "HDPE Pipe 32mm", type: "FinishedGood", branchId: 1, isActive: true, lastPurchasePrice: null },
];

function mockLookups() {
  vi.mocked(api.get).mockImplementation((url: string) => {
    if (url.includes("/requisitions/1")) return Promise.resolve({ data: sampleRejected });
    if (url.includes("/items")) return Promise.resolve({ data: sampleItems });
    return Promise.reject(new Error(`unmocked: ${url}`));
  });
}

describe("EditRequisitionPage", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    vi.mocked(api.post).mockReset();
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "SalesPerson", userId: 10, name: "Ali", branchId: 1,
    });
  });

  afterEach(() => {
    useAuthStore.getState().logout();
  });

  it("renders the previous rejection reason banner", async () => {
    mockLookups();
    render(wrap(<EditRequisitionPage />));
    await waitFor(() => expect(screen.getByText(/Previous rejection reason/i)).toBeInTheDocument());
    expect(screen.getByText("Margin too low")).toBeInTheDocument();
  });

  it("shows a 'Cannot edit' message when status is not Rejected", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url.includes("/requisitions/1")) return Promise.resolve({ data: { ...sampleRejected, status: "BomPending" } });
      if (url.includes("/items")) return Promise.resolve({ data: sampleItems });
      return Promise.reject(new Error(`unmocked: ${url}`));
    });
    render(wrap(<EditRequisitionPage />));
    await waitFor(() => expect(screen.getByText(/Cannot edit/i)).toBeInTheDocument());
    expect(screen.getByText(/BomPending/)).toBeInTheDocument();
  });

  it("blocks non-owning SalesPerson", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "SalesPerson", userId: 99, name: "Other", branchId: 1, // NOT the owner
    });
    mockLookups();
    render(wrap(<EditRequisitionPage />));
    await waitFor(() =>
      expect(screen.getByText(/Only the owning sales person/i)).toBeInTheDocument(),
    );
  });

  it("submits resubmit and navigates to detail on success", async () => {
    mockLookups();
    vi.mocked(api.post).mockResolvedValueOnce({
      data: { id: 1, refNo: "REQ-0001", status: "BomPending" },
    });
    render(wrap(<EditRequisitionPage />));
    await waitFor(() => expect(screen.getByText("HDPE Pipe 20mm")).toBeInTheDocument());

    await userEvent.click(screen.getByRole("button", { name: /resubmit for bom/i }));

    await waitFor(() => expect(api.post).toHaveBeenCalledWith(
      "/requisitions/1/resubmit",
      expect.objectContaining({
        items: expect.arrayContaining([
          expect.objectContaining({ itemId: 2, expectedQty: 100 }),
        ]),
      }),
    ));
    await waitFor(() => expect(screen.getByText("detail page")).toBeInTheDocument());
  });
});
```

- [ ] **Step 3: Run frontend tests**

Run (from `bom-web/`): `npm test -- RequisitionDetailPage EditRequisitionPage`
Expected: All tests PASS.

- [ ] **Step 4: Run the full frontend suite to catch regressions**

Run: `npm test`
Expected: All suites pass.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/features/requisitions/RequisitionDetailPage.test.tsx \
        bom-web/src/features/requisitions/EditRequisitionPage.test.tsx
git commit -m "test(web): detail-page resubmit button + EditRequisitionPage tests"
```

---

## Task 12: End-to-end smoke check and final verification

- [ ] **Step 1: Run the full backend suite**

Run: `dotnet test`
Expected: All tests pass.

- [ ] **Step 2: Run the full frontend suite**

Run (from `bom-web/`): `npm test -- --run` and `npm run build`
Expected: All tests pass; production build succeeds.

- [ ] **Step 3: Manual smoke test**

Only if a local environment is available:

1. `dotnet run --project BomPriceApproval.API` + `cd bom-web && npm run dev`.
2. Log in as SalesPerson, create a requisition.
3. Log in as BomCreator, Accountant, then ManagingDirector — reject with notes "Test rejection — please fix margin".
4. Re-login as the original SalesPerson, open the requisition — the rejection reason should appear in `text-destructive` in the Approval card.
5. Click "Edit & Resubmit", change the quantity of one item, click "Resubmit for BOM".
6. Status should become `BomPending`; the Approval card should go back to "Not yet submitted for approval".

If any step fails, file the issue and revisit the relevant task. Do not paper over with spot fixes.

- [ ] **Step 4: Final commit (if anything changed during smoke test)**

If the smoke test required fixes, commit them as normal task-specific commits — do not lump them into one omnibus commit.

---

## Spec-to-Plan Coverage

| Spec Requirement | Task(s) |
|---|---|
| Add `IsSuperseded`/`SupersededAt` columns | 1 |
| 1:N relationship + filtered index | 2 |
| Migration | 3 |
| `ApprovalSummary` DTO surfaces notes | 4 |
| `Get` action maps current approval with notes | 4 |
| Frontend `ApprovalSummary` type | 4 |
| Detail page rejection block (destructive styling) | 5 |
| Detail page notes block for approved reqs | 5 |
| New `POST /resubmit` endpoint, items-only, atomic | 6 |
| Validation (Status, Items, ExpectedQty, duplicates, unknown/inactive) | 6, 7 |
| Re-fetch exchange rate on resubmit | 6 |
| Notify BomCreators on resubmit | 6 |
| Backend integration tests (notes, resubmit happy path, status/items validation, non-owner) | 7 |
| `useResubmitRequisition` hook | 8 |
| Shared `RequisitionItemsEditor` | 9 |
| `EditRequisitionPage` + route | 10 |
| "Edit & Resubmit" action button | 10 |
| Guards (non-Rejected status, non-owner) | 10, 11 |
| Frontend tests for detail button + edit page | 11 |
| Final verification | 12 |
