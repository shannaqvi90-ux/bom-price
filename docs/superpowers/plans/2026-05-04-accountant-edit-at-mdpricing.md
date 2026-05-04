# Accountant Edit Window Extended Through MdPricing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow accountants to edit BOM line quantities and cost data on a requisition that is already in MdPricing status, without recalling it back to Costing. Edits stay locked once MD approves margin.

**Architecture:** Relax status guards on `PUT /api/costing/{id}/bom` and `PUT /api/costing/{id}/cost-data` from `Costing` only to `Costing | MdPricing`. Audit every save while in MdPricing via existing `AdminAuditLogger`. Notify all active MDs once per "edit session" using a new `bool MdPricingNotifiedAfterEdit` flag on `QuotationRequest` (reset on entry to + leave from MdPricing).

**Tech Stack:** ASP.NET Core 8 + EF Core 8 + Npgsql + xUnit + FluentAssertions (backend); React 19 + Vite + TanStack Query + vitest + RTL (web).

**Spec:** [docs/superpowers/specs/2026-05-04-accountant-edit-at-mdpricing-design.md](../specs/2026-05-04-accountant-edit-at-mdpricing-design.md)

**Branch:** `feat/accountant-edit-at-mdpricing` (off `master @ c4f6526` after PR #88 merge). Create at start of execution: `git checkout -b feat/accountant-edit-at-mdpricing master`.

**Operational notes:**
- Build/test from worktree directory; use `--configuration Release` if local API process holds Debug DLL locks.
- Tests use Guid-isolated throwaway entities (don't mutate seeded users `admin@test.com`/`sara@test.com`).
- Use `using var scope = factory.Services.CreateScope();` for per-test DbContext access.
- **Web hook URL convention:** axios `baseURL` is `/api`, so paths in hooks DO NOT prefix `/api`. (Bug from PR #88.)

---

## File Structure

### Backend — `BomPriceApproval.API/`

**Created:**
- `Infrastructure/Data/Migrations/<TIMESTAMP>_AddMdPricingNotifiedAfterEditFlag.cs` — adds the new bool column

**Modified:**
- `Domain/Entities/QuotationRequest.cs` — add `bool MdPricingNotifiedAfterEdit` property
- `Domain/Enums/AdminActionType.cs` — append `AccountantEditAfterSubmit`
- `Domain/Enums/NotificationType.cs` — append `CostingEditedAfterSubmit` (decorative; existing `Notification` entity has no Type column, but we keep the enum in lockstep with documented semantics)
- `Features/Costing/CostingController.cs` — relax status guards in `UpdateBom` + `SaveV3CostData`; add `AdminAuditLogger audit` constructor param; add audit + notify-once logic; reset flag in `Submit()`
- `Features/Approvals/ApprovalsController.cs` — reset flag on `SetMargin`, `Reject`, `RejectCustomer`
- `Features/Admin/AdminRequisitionsController.cs` — reset flag on `RollbackToCosting` / `UnlockCosting` paths

### Backend tests — `BomPriceApproval.Tests/`

**Created:**
- `Costing/EditAfterSubmitTests.cs` — 4 integration tests (notify-once, audit, customerConfirm-locks, notif-resets-after-rollback)

### Web — `bom-web/`

**Modified:**
- `src/features/costing/CostingEntryV3Page.tsx` — extend `editable` predicate to include `MdPricing`; add amber banner
- `src/features/requisitions/RequisitionDetailPage.tsx` — extend "Edit costing" button visibility predicate
- `src/api/admin.ts` — add `"AccountantEditAfterSubmit"` to `AdminActionType` union
- `src/features/admin/audit-log/AuditLogPage.tsx` — add label to `ACTION_TYPE_LABELS` + entry to `ACTION_TYPES`
- `src/features/costing/CostingEntryV3Page.test.tsx` — extend with one test for amber banner + enabled save at MdPricing

### Docs

**Modified:**
- `CLAUDE.md` — short paragraph under V3 Workflow describing the extended edit window

---

## Task 1: Add `MdPricingNotifiedAfterEdit` flag + enum values + EF migration

**Files:**
- Modify: `BomPriceApproval.API/Domain/Entities/QuotationRequest.cs`
- Modify: `BomPriceApproval.API/Domain/Enums/AdminActionType.cs`
- Modify: `BomPriceApproval.API/Domain/Enums/NotificationType.cs`
- Create (auto-generated): `BomPriceApproval.API/Infrastructure/Data/Migrations/<TIMESTAMP>_AddMdPricingNotifiedAfterEditFlag.cs`

- [ ] **Step 1: Add the bool property to `QuotationRequest`**

Edit `BomPriceApproval.API/Domain/Entities/QuotationRequest.cs`. After the `public DateTime UpdatedAt { ... }` line and before `public Branch Branch { ... }`, insert:

```csharp
    public bool MdPricingNotifiedAfterEdit { get; set; } = false;
```

- [ ] **Step 2: Append `AccountantEditAfterSubmit` to `AdminActionType`**

Edit `BomPriceApproval.API/Domain/Enums/AdminActionType.cs`. Append after the existing `UpdateCompanySettings` entry (do NOT reorder existing values; enum is stored as string for forensic readability):

```csharp
namespace BomPriceApproval.API.Domain.Enums;

public enum AdminActionType
{
    DeleteRequisition,
    RollbackStatus,
    ReassignSp,
    UnlockBom,
    UnlockCosting,
    ResetPassword,
    OverridePrices,
    HardDeleteCustomer,

    // V3 NEW values
    RollbackToCosting,          // C5 renamed (was UnlockCosting)
    V3CutoverMigration,         // logged once during Phase C cutover SQL

    UpdateCompanySettings,      // post-V3 PDF-redesign feature (admin company settings)
    AccountantEditAfterSubmit   // accountant edits BOM/cost while req is in MdPricing
}
```

- [ ] **Step 3: Append `CostingEditedAfterSubmit` to `NotificationType`**

Edit `BomPriceApproval.API/Domain/Enums/NotificationType.cs`. Append after `RequisitionCancelled`:

```csharp
namespace BomPriceApproval.API.Domain.Enums;

public enum NotificationType
{
    RequisitionDeleted,
    StatusRolledBack,
    SalesPersonReassigned,
    BomUnlocked,
    CostingUnlocked,
    PricesOverridden,
    CustomerDeleted,

    // V3 NEW values
    MarginSet,                  // Stage 1 done — sent to sales + accountant
    CustomerConfirmRequested,   // sent to sales
    CustomerAccepted,           // sent to MD + accountant
    CustomerRejected,           // sent to MD + accountant
    SignedNotif,                // sent to sales + accountant (Signed name suffix to avoid clash with status enum)
    RequisitionCancelled,       // sent to sales

    CostingEditedAfterSubmit    // sent to MDs once when accountant edits in MdPricing
}
```

- [ ] **Step 4: Build to verify entity compiles**

Run from worktree: `dotnet build --nologo -v q`
Expected: Build succeeds, 0 errors.

- [ ] **Step 5: Generate the EF migration**

Run from worktree: `dotnet ef migrations add AddMdPricingNotifiedAfterEditFlag --project BomPriceApproval.API --output-dir Infrastructure/Data/Migrations`

Expected: outputs `Build started... Build succeeded... Done.`; creates a new `<TIMESTAMP>_AddMdPricingNotifiedAfterEditFlag.cs` + `.Designer.cs` and updates `AppDbContextModelSnapshot.cs`.

- [ ] **Step 6: Inspect the generated migration**

Open the new `<TIMESTAMP>_AddMdPricingNotifiedAfterEditFlag.cs` and verify:
- `Up()` adds a `MdPricingNotifiedAfterEdit` column to `QuotationRequests` with `nullable: false` and `defaultValue: false`.
- `Down()` drops that column.
- No other unrelated migrations / changes.

If anything looks off (e.g. nullable, no default), revert with `dotnet ef migrations remove --project BomPriceApproval.API` and revisit Step 1.

- [ ] **Step 7: Apply the migration to local DB**

Run from worktree: `dotnet ef database update --project BomPriceApproval.API`
Expected: `Applying migration '<TIMESTAMP>_AddMdPricingNotifiedAfterEditFlag'. Done.`

If the API is currently running it may hold a DLL lock. If it fails with a file-lock error, stop the API first.

- [ ] **Step 8: Commit**

```bash
git add BomPriceApproval.API/Domain/Entities/QuotationRequest.cs \
        BomPriceApproval.API/Domain/Enums/AdminActionType.cs \
        BomPriceApproval.API/Domain/Enums/NotificationType.cs \
        BomPriceApproval.API/Infrastructure/Data/Migrations/
git commit -m "feat(api): add MdPricingNotifiedAfterEdit flag + AccountantEditAfterSubmit audit type"
```

---

## Task 2: Reset flag on every status transition that touches MdPricing

**Files:**
- Modify: `BomPriceApproval.API/Features/Costing/CostingController.cs`
- Modify: `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs`
- Modify: `BomPriceApproval.API/Features/Admin/AdminRequisitionsController.cs`

The flag must be `false` at every entry to MdPricing, and it doesn't matter outside MdPricing — but defensively reset to `false` on every leave too, so the flag never carries stale `true` into a future MdPricing window.

- [ ] **Step 1: Reset on `Submit()` (Costing → MdPricing)**

Open `BomPriceApproval.API/Features/Costing/CostingController.cs`. Find the `Submit` method's status assignment (around line 524). Replace:

```csharp
        req.Status = RequisitionStatus.MdPricing;
        req.UpdatedAt = DateTime.UtcNow;
```

With:

```csharp
        req.Status = RequisitionStatus.MdPricing;
        req.MdPricingNotifiedAfterEdit = false;  // fresh edit-notification window
        req.UpdatedAt = DateTime.UtcNow;
```

- [ ] **Step 2: Reset on `SetMargin()` (MdPricing → CustomerConfirm)**

Open `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs`. Find the `SetMargin` method's status assignment (around line 410). Replace:

```csharp
        req.Status = RequisitionStatus.CustomerConfirm;
        req.UpdatedAt = nowUtc;
```

With:

```csharp
        req.Status = RequisitionStatus.CustomerConfirm;
        req.MdPricingNotifiedAfterEdit = false;  // leaving MdPricing
        req.UpdatedAt = nowUtc;
```

- [ ] **Step 3: Reset on `Reject()` (MdPricing → Rejected)**

In the same file, find the `Reject` method's status assignment (around line 240). Replace:

```csharp
        req.Status = RequisitionStatus.Rejected;
```

With:

```csharp
        req.Status = RequisitionStatus.Rejected;
        req.MdPricingNotifiedAfterEdit = false;  // leaving MdPricing
```

- [ ] **Step 4: Reset on `RejectCustomer()` (CustomerConfirm → MdPricing)**

In the same file, find `RejectCustomer` method's status assignment (around line 537). Replace:

```csharp
        req.Status = RequisitionStatus.MdPricing;
```

With:

```csharp
        req.Status = RequisitionStatus.MdPricing;
        req.MdPricingNotifiedAfterEdit = false;  // fresh edit-notification window after customer reject bounce-back
```

- [ ] **Step 5: Reset on admin rollback to Costing**

Open `BomPriceApproval.API/Features/Admin/AdminRequisitionsController.cs`. Find the `RollbackToCosting` method's status assignment (around line 460). Replace:

```csharp
        req.Status = RequisitionStatus.Costing;
```

With:

```csharp
        req.Status = RequisitionStatus.Costing;
        req.MdPricingNotifiedAfterEdit = false;  // leaving MdPricing
```

- [ ] **Step 6: Build to verify**

Run from worktree: `dotnet build --nologo -v q`
Expected: Build succeeds, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add BomPriceApproval.API/Features/Costing/CostingController.cs \
        BomPriceApproval.API/Features/Approvals/ApprovalsController.cs \
        BomPriceApproval.API/Features/Admin/AdminRequisitionsController.cs
git commit -m "feat(api): reset MdPricingNotifiedAfterEdit flag on all status transitions touching MdPricing"
```

---

## Task 3: Relax status guard + add audit + notify-once on `UpdateBom`

**Files:**
- Modify: `BomPriceApproval.API/Features/Costing/CostingController.cs`

- [ ] **Step 1: Add `AdminAuditLogger` to constructor injection**

Open `BomPriceApproval.API/Features/Costing/CostingController.cs`. Find the constructor (around line 18-21):

```csharp
public class CostingController(
    AppDbContext db,
    NotificationService notificationService,
    ILogger<CostingController> logger) : ControllerBase
```

Replace with:

```csharp
public class CostingController(
    AppDbContext db,
    NotificationService notificationService,
    AdminAuditLogger audit,
    ILogger<CostingController> logger) : ControllerBase
```

- [ ] **Step 2: Relax status guard in `UpdateBom`**

Find line 146 in the same file:

```csharp
        if (req.Status != RequisitionStatus.Costing)
            return BadRequest(new { error = $"BOM editable only in Costing status (current: {req.Status})" });
```

Replace with:

```csharp
        if (req.Status != RequisitionStatus.Costing && req.Status != RequisitionStatus.MdPricing)
            return BadRequest(new { error = $"BOM editable only in Costing or MdPricing status (current: {req.Status})" });
```

- [ ] **Step 3: Capture pre-mutation BOM snapshot in `UpdateBom`**

Find the line right after the FG-not-found / no-BOM checks but BEFORE the `var updateItemIds = ...` validation block in `UpdateBom` (around line 156). Insert a `before` snapshot:

```csharp
        // Snapshot pre-edit BOM lines for audit (used only when status==MdPricing).
        var beforeAuditSnapshot = req.Status == RequisitionStatus.MdPricing
            ? bom.Lines.Select(l => new
            {
                l.Id, l.QtyPerKg, l.Micron, l.WastagePct,
                ItemId = l.RawMaterialItemId
            }).ToList()
            : null;
```

(Place this immediately AFTER `if (bom is null) return BadRequest(...)` line and BEFORE `var updateItemIds = ...`.)

- [ ] **Step 4: Add audit + notify-once logic right BEFORE the existing `await db.SaveChangesAsync()` in `UpdateBom`**

In `UpdateBom`, find the existing tail (around line 225-229):

```csharp
        if (mutated)
        {
            req.UpdatedAt = now;
            await db.SaveChangesAsync();
        }

        return Ok(new { ok = true, finishedGoodId = body.FinishedGoodId });
```

Replace with:

```csharp
        if (mutated)
        {
            req.UpdatedAt = now;

            // Edit-after-submit: audit + notify-once when status is MdPricing.
            if (req.Status == RequisitionStatus.MdPricing)
            {
                var afterAuditSnapshot = bom.Lines.Select(l => new
                {
                    l.Id, l.QtyPerKg, l.Micron, l.WastagePct,
                    ItemId = l.RawMaterialItemId
                }).ToList();

                audit.Log(
                    CurrentUserId,
                    AdminActionType.AccountantEditAfterSubmit,
                    "QuotationRequest",
                    req.Id,
                    "Accountant edit during MdPricing window",
                    beforeAuditSnapshot!,
                    afterAuditSnapshot);

                if (!req.MdPricingNotifiedAfterEdit)
                {
                    var mdIds = await db.Users
                        .Where(u => u.Role == UserRole.ManagingDirector && u.IsActive)
                        .Select(u => u.Id)
                        .ToListAsync();
                    await notificationService.SendToUsersAsync(
                        mdIds,
                        $"{req.RefNo} — costing edited, please refresh before approving",
                        req.Id,
                        "QuotationRequest");
                    req.MdPricingNotifiedAfterEdit = true;
                }
            }

            await db.SaveChangesAsync();
        }

        return Ok(new { ok = true, finishedGoodId = body.FinishedGoodId });
```

NOTE: `AdminAuditLogger.Log` uses generic `<TBefore, TAfter>` constraints; both anonymous types must be class types — anonymous type-of-list-of-anonymous already satisfies that. The `beforeAuditSnapshot!` non-null assertion is safe because we only enter this block when status==MdPricing, which means we entered the snapshot capture branch above.

- [ ] **Step 5: Build to verify**

Run from worktree: `dotnet build --nologo -v q`
Expected: Build succeeds, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Features/Costing/CostingController.cs
git commit -m "feat(api): allow accountant BOM edits at MdPricing, audit + notify-once"
```

---

## Task 4: Relax status guard + add audit + notify-once on `SaveV3CostData`

**Files:**
- Modify: `BomPriceApproval.API/Features/Costing/CostingController.cs`

- [ ] **Step 1: Relax status guard in `SaveV3CostData`**

Find line 252 in the same file:

```csharp
        if (req.Status != RequisitionStatus.Costing)
            return Validation
                .Detail($"Cost data can only be saved when status is Costing (current: {req.Status})")
                .Field("Status", "Must be Costing.")
                .Return();
```

Replace with:

```csharp
        if (req.Status != RequisitionStatus.Costing && req.Status != RequisitionStatus.MdPricing)
            return Validation
                .Detail($"Cost data can only be saved when status is Costing or MdPricing (current: {req.Status})")
                .Field("Status", "Must be Costing or MdPricing.")
                .Return();
```

- [ ] **Step 2: Capture pre-mutation cost snapshot in `SaveV3CostData`**

In `SaveV3CostData`, find the spot right after the status guard returned above and BEFORE any cost-data manipulation begins. Look for the first action after the status guard (likely loading BomCost / BomCostLine entities or beginning the upsert loop).

Insert this snapshot capture immediately AFTER the status guard (the new condition you wrote in Step 1):

```csharp
        // Snapshot pre-edit cost data for audit (used only when status==MdPricing).
        List<object>? beforeAuditSnapshot = null;
        if (req.Status == RequisitionStatus.MdPricing)
        {
            var preFgIds = req.Items.Select(ri => ri.Id).ToList();
            var preBomHeaderIds = await db.BomHeaders
                .Where(b => preFgIds.Contains(b.RequisitionItemId))
                .Select(b => b.Id)
                .ToListAsync();
            var preCosts = await db.BomCosts
                .Where(c => preBomHeaderIds.Contains(c.BomHeaderId))
                .Select(c => new
                {
                    c.BomHeaderId, c.PrintingCostPerKg, c.PrintingCostCurrency,
                    c.FohPerKg, c.TransportPerKg, c.CommissionPerKg
                })
                .ToListAsync();
            var preLines = await db.BomCostLines
                .Where(cl => preBomHeaderIds.Contains(cl.BomHeaderId))
                .Select(cl => new { cl.BomHeaderId, cl.BomLineId, cl.CostPerKg, cl.CurrencyCode })
                .ToListAsync();
            beforeAuditSnapshot = new List<object>
            {
                new { type = "BomCost", values = preCosts },
                new { type = "BomCostLine", values = preLines }
            };
        }
```

If `req.Items` isn't loaded at that point (it's loaded via `FromSqlInterpolated` which doesn't include navs), use `await db.RequisitionItems.Where(ri => ri.QuotationRequestId == requisitionId).Select(ri => ri.Id).ToListAsync()` for `preFgIds` instead. Verify the surrounding code's load pattern before pasting and adjust if needed.

- [ ] **Step 3: Add audit + notify-once logic right BEFORE the existing transaction commit in `SaveV3CostData`**

In `SaveV3CostData`, find the line right BEFORE `await tx.CommitAsync()` (or before the final `await db.SaveChangesAsync()` if there's no explicit commit — this method uses `BeginTransactionAsync` so look for `tx.CommitAsync`). Insert:

```csharp
        // Edit-after-submit: audit + notify-once when status is MdPricing.
        if (req.Status == RequisitionStatus.MdPricing)
        {
            var postFgIds = req.Items.Count > 0
                ? req.Items.Select(ri => ri.Id).ToList()
                : await db.RequisitionItems.Where(ri => ri.QuotationRequestId == requisitionId).Select(ri => ri.Id).ToListAsync();
            var postBomHeaderIds = await db.BomHeaders
                .Where(b => postFgIds.Contains(b.RequisitionItemId))
                .Select(b => b.Id)
                .ToListAsync();
            var postCosts = await db.BomCosts
                .Where(c => postBomHeaderIds.Contains(c.BomHeaderId))
                .Select(c => new
                {
                    c.BomHeaderId, c.PrintingCostPerKg, c.PrintingCostCurrency,
                    c.FohPerKg, c.TransportPerKg, c.CommissionPerKg
                })
                .ToListAsync();
            var postLines = await db.BomCostLines
                .Where(cl => postBomHeaderIds.Contains(cl.BomHeaderId))
                .Select(cl => new { cl.BomHeaderId, cl.BomLineId, cl.CostPerKg, cl.CurrencyCode })
                .ToListAsync();
            var afterAuditSnapshot = new List<object>
            {
                new { type = "BomCost", values = postCosts },
                new { type = "BomCostLine", values = postLines }
            };

            audit.Log(
                CurrentUserId,
                AdminActionType.AccountantEditAfterSubmit,
                "QuotationRequest",
                req.Id,
                "Accountant edit during MdPricing window",
                beforeAuditSnapshot!,
                afterAuditSnapshot);

            if (!req.MdPricingNotifiedAfterEdit)
            {
                var mdIds = await db.Users
                    .Where(u => u.Role == UserRole.ManagingDirector && u.IsActive)
                    .Select(u => u.Id)
                    .ToListAsync();
                await notificationService.SendToUsersAsync(
                    mdIds,
                    $"{req.RefNo} — costing edited, please refresh before approving",
                    req.Id,
                    "QuotationRequest");
                req.MdPricingNotifiedAfterEdit = true;
                await db.SaveChangesAsync();  // persist flag flip inside the transaction
            }
        }
```

The trailing `await db.SaveChangesAsync()` for the flag flip is needed because `audit.Log()` only enqueues the audit row; the flag flip must be persisted before the transaction commits so subsequent saves in the same edit session see the updated flag.

(`SendToUsersAsync` already calls SaveChangesAsync internally for the notification rows.)

- [ ] **Step 4: Build to verify**

Run from worktree: `dotnet build --nologo -v q`
Expected: Build succeeds, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Costing/CostingController.cs
git commit -m "feat(api): allow accountant cost-data edits at MdPricing, audit + notify-once"
```

---

## Task 5: Backend integration tests

**Files:**
- Create: `BomPriceApproval.Tests/Costing/EditAfterSubmitTests.cs`

This test class is large because it exercises the full happy path AND three regression checks. Follow the existing `BomPriceApproval.Tests/Admin/CompanySettingsTests.cs` style.

- [ ] **Step 1: Verify backend is running**

Run from worktree: `curl -s http://localhost:7300/swagger/index.html >/dev/null && echo OK || echo "Backend not running"`
If not running: `dotnet run --project BomPriceApproval.API` (separate terminal) and wait for `Now listening on...`.

- [ ] **Step 2: Create the test file**

Create `BomPriceApproval.Tests/Costing/EditAfterSubmitTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.Tests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Costing;

/// <summary>
/// Verifies the V3 extended edit window: accountant can edit BOM lines and
/// cost data while the requisition is in MdPricing status; each save audits
/// to AdminAuditLog and the FIRST save per MdPricing window notifies all
/// active MDs (subsequent saves in the same window stay silent).
/// </summary>
public class EditAfterSubmitTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> TokenAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = password });
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    private void AuthAs(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    /// <summary>
    /// Creates a throwaway requisition seeded into MdPricing status with one
    /// FG, one BOM line, and one BomCost row. Returns the req id.
    /// </summary>
    private async Task<int> SeedReqInMdPricingAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Seed a customer (use Sara as creator + sales-person hint)
        var sales = await db.Users.FirstAsync(u => u.Email == "ali@test.com");  // SalesPerson seeded
        var accountant = await db.Users.FirstAsync(u => u.Email == "sara@test.com");
        var customer = new Customer
        {
            Code = $"CUST-T-{Guid.NewGuid():N}".Substring(0, 12),
            Name = $"Test Customer {Guid.NewGuid():N}".Substring(0, 30),
            BranchId = sales.BranchId ?? 1,
            SalesPersonId = sales.Id,
            CreatedByUserId = sales.Id
        };
        db.Customers.Add(customer);

        // Pick or seed a finished-good item + a raw-material item
        var fgItem = await db.Items.FirstAsync(i => i.Type == ItemType.FinishedGood && i.IsActive);
        var rmItem = await db.Items.FirstAsync(i => i.Type == ItemType.RawMaterial && i.IsActive);
        await db.SaveChangesAsync();

        // Build req with one FG + BOM + cost
        var req = new QuotationRequest
        {
            BranchId = sales.BranchId ?? 1,
            SalesPersonId = sales.Id,
            CustomerId = customer.Id,
            CurrencyCode = "AED",
            Status = RequisitionStatus.MdPricing,
            MdPricingNotifiedAfterEdit = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.QuotationRequests.Add(req);
        await db.SaveChangesAsync();

        var ri = new RequisitionItem
        {
            QuotationRequestId = req.Id,
            ItemId = fgItem.Id,
            ExpectedQty = 1000,
            SortOrder = 1,
            HasPrinting = false
        };
        db.RequisitionItems.Add(ri);
        await db.SaveChangesAsync();

        var bomHeader = new BomHeader { RequisitionItemId = ri.Id };
        db.BomHeaders.Add(bomHeader);
        await db.SaveChangesAsync();

        var process = await db.Processes.FirstAsync();
        var bomLine = new BomLine
        {
            BomHeaderId = bomHeader.Id,
            ProcessId = process.Id,
            RawMaterialItemId = rmItem.Id,
            QtyPerKg = 1.0m,
            Micron = 50,
            WastagePct = 5
        };
        db.BomLines.Add(bomLine);
        await db.SaveChangesAsync();

        var bomCost = new BomCost
        {
            BomHeaderId = bomHeader.Id,
            PrintingCostPerKg = null,
            PrintingCostCurrency = null,
            FohPerKg = 0.5m,
            TransportPerKg = 0.2m,
            CommissionPerKg = 0.1m
        };
        db.BomCosts.Add(bomCost);

        var bomCostLine = new BomCostLine
        {
            BomHeaderId = bomHeader.Id,
            BomLineId = bomLine.Id,
            CostPerKg = 5.0m,
            CurrencyCode = "AED"
        };
        db.BomCostLines.Add(bomCostLine);
        await db.SaveChangesAsync();

        return req.Id;
    }

    private async Task CleanupReqAsync(int reqId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var audit = await db.AdminAuditLogs
            .Where(a => a.EntityType == "QuotationRequest" && a.EntityId == reqId)
            .ToListAsync();
        if (audit.Count > 0) db.AdminAuditLogs.RemoveRange(audit);

        var notifs = await db.Notifications
            .Where(n => n.ReferenceType == "QuotationRequest" && n.ReferenceId == reqId)
            .ToListAsync();
        if (notifs.Count > 0) db.Notifications.RemoveRange(notifs);

        var req = await db.QuotationRequests
            .Include(r => r.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(b => b!.Lines)
            .FirstOrDefaultAsync(r => r.Id == reqId);
        if (req is null) { await db.SaveChangesAsync(); return; }

        var customerId = req.CustomerId;
        foreach (var ri in req.Items)
        {
            if (ri.BomHeader is not null)
            {
                var costLines = await db.BomCostLines.Where(cl => cl.BomHeaderId == ri.BomHeader.Id).ToListAsync();
                if (costLines.Count > 0) db.BomCostLines.RemoveRange(costLines);
                var cost = await db.BomCosts.FirstOrDefaultAsync(c => c.BomHeaderId == ri.BomHeader.Id);
                if (cost is not null) db.BomCosts.Remove(cost);
                db.BomLines.RemoveRange(ri.BomHeader.Lines);
                db.BomHeaders.Remove(ri.BomHeader);
            }
        }
        db.RequisitionItems.RemoveRange(req.Items);
        db.QuotationRequests.Remove(req);
        var customer = await db.Customers.FindAsync(customerId);
        if (customer is not null) db.Customers.Remove(customer);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateBom_AtMdPricing_AccountantSucceeds_AndAudits_AndNotifiesMdsOnce()
    {
        var reqId = await SeedReqInMdPricingAsync();
        try
        {
            var token = await TokenAsync("sara@test.com", "Test@1234");
            AuthAs(token);

            // Find the FG id + BOM line id we seeded
            int fgId, lineId, currentItemId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ri = await db.RequisitionItems.FirstAsync(x => x.QuotationRequestId == reqId);
                fgId = ri.Id;
                var bom = await db.BomHeaders.FirstAsync(b => b.RequisitionItemId == fgId);
                var line = await db.BomLines.FirstAsync(l => l.BomHeaderId == bom.Id);
                lineId = line.Id;
                currentItemId = line.RawMaterialItemId;
            }

            // First edit
            var resp1 = await _client.PutAsJsonAsync($"/api/costing/{reqId}/bom", new
            {
                FinishedGoodId = fgId,
                Lines = new[]
                {
                    new { Id = (int?)lineId, ItemId = currentItemId, QtyPerKg = 1.5m, Micron = (int?)60, Delete = false }
                }
            });
            resp1.StatusCode.Should().Be(HttpStatusCode.OK);

            // Second edit (same session)
            var resp2 = await _client.PutAsJsonAsync($"/api/costing/{reqId}/bom", new
            {
                FinishedGoodId = fgId,
                Lines = new[]
                {
                    new { Id = (int?)lineId, ItemId = currentItemId, QtyPerKg = 2.0m, Micron = (int?)70, Delete = false }
                }
            });
            resp2.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope2 = factory.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

            // Two audit rows expected (one per save)
            var audit = await db2.AdminAuditLogs
                .Where(a => a.EntityType == "QuotationRequest"
                         && a.EntityId == reqId
                         && a.ActionType == AdminActionType.AccountantEditAfterSubmit)
                .ToListAsync();
            audit.Should().HaveCount(2);

            // Notification: ONE notif per active MD (only the first save fires).
            var mdCount = await db2.Users
                .CountAsync(u => u.Role == UserRole.ManagingDirector && u.IsActive);
            var notifs = await db2.Notifications
                .Where(n => n.ReferenceType == "QuotationRequest" && n.ReferenceId == reqId)
                .ToListAsync();
            notifs.Should().HaveCount(mdCount, "exactly one notif per MD across the whole session");
            notifs.Should().AllSatisfy(n => n.Message.Should().Contain("costing edited"));

            // Flag must be true
            var req = await db2.QuotationRequests.AsNoTracking().FirstAsync(r => r.Id == reqId);
            req.MdPricingNotifiedAfterEdit.Should().BeTrue();
        }
        finally
        {
            await CleanupReqAsync(reqId);
        }
    }

    [Fact]
    public async Task SaveCostData_AtMdPricing_AccountantSucceeds_AndAudits_AndDoesNotDoubleNotify()
    {
        var reqId = await SeedReqInMdPricingAsync();
        try
        {
            var token = await TokenAsync("sara@test.com", "Test@1234");
            AuthAs(token);

            int fgId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ri = await db.RequisitionItems.FirstAsync(x => x.QuotationRequestId == reqId);
                fgId = ri.Id;
            }

            // First save: change FOH to 0.6
            var resp1 = await _client.PutAsJsonAsync($"/api/costing/{reqId}/cost-data", new
            {
                FinishedGoods = new[]
                {
                    new
                    {
                        FinishedGoodId = fgId,
                        FohPerKg = 0.6m,
                        TransportPerKg = 0.2m,
                        CommissionPerKg = 0.1m,
                        PrintingCostPerKg = (decimal?)null,
                        PrintingCostCurrency = (string?)null,
                        Lines = Array.Empty<object>()
                    }
                }
            });
            resp1.StatusCode.Should().Be(HttpStatusCode.OK);

            // Second save: change FOH to 0.7
            var resp2 = await _client.PutAsJsonAsync($"/api/costing/{reqId}/cost-data", new
            {
                FinishedGoods = new[]
                {
                    new
                    {
                        FinishedGoodId = fgId,
                        FohPerKg = 0.7m,
                        TransportPerKg = 0.2m,
                        CommissionPerKg = 0.1m,
                        PrintingCostPerKg = (decimal?)null,
                        PrintingCostCurrency = (string?)null,
                        Lines = Array.Empty<object>()
                    }
                }
            });
            resp2.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope2 = factory.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

            var audit = await db2.AdminAuditLogs
                .Where(a => a.EntityType == "QuotationRequest"
                         && a.EntityId == reqId
                         && a.ActionType == AdminActionType.AccountantEditAfterSubmit)
                .ToListAsync();
            audit.Should().HaveCount(2);

            var mdCount = await db2.Users
                .CountAsync(u => u.Role == UserRole.ManagingDirector && u.IsActive);
            var notifs = await db2.Notifications
                .Where(n => n.ReferenceType == "QuotationRequest" && n.ReferenceId == reqId)
                .ToListAsync();
            notifs.Should().HaveCount(mdCount);
        }
        finally
        {
            await CleanupReqAsync(reqId);
        }
    }

    [Fact]
    public async Task UpdateBom_AtCustomerConfirm_Returns400()
    {
        var reqId = await SeedReqInMdPricingAsync();
        try
        {
            // Bump status to CustomerConfirm directly (skipping the SetMargin flow for test simplicity)
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var req = await db.QuotationRequests.FirstAsync(r => r.Id == reqId);
                req.Status = RequisitionStatus.CustomerConfirm;
                req.MdPricingNotifiedAfterEdit = false;
                await db.SaveChangesAsync();
            }

            var token = await TokenAsync("sara@test.com", "Test@1234");
            AuthAs(token);

            int fgId, lineId, currentItemId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ri = await db.RequisitionItems.FirstAsync(x => x.QuotationRequestId == reqId);
                fgId = ri.Id;
                var bom = await db.BomHeaders.FirstAsync(b => b.RequisitionItemId == fgId);
                var line = await db.BomLines.FirstAsync(l => l.BomHeaderId == bom.Id);
                lineId = line.Id;
                currentItemId = line.RawMaterialItemId;
            }

            var resp = await _client.PutAsJsonAsync($"/api/costing/{reqId}/bom", new
            {
                FinishedGoodId = fgId,
                Lines = new[]
                {
                    new { Id = (int?)lineId, ItemId = currentItemId, QtyPerKg = 1.5m, Micron = (int?)60, Delete = false }
                }
            });
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            await CleanupReqAsync(reqId);
        }
    }

    [Fact]
    public async Task UpdateBom_NotifResetsAfterAdminUnlockCosting()
    {
        var reqId = await SeedReqInMdPricingAsync();
        try
        {
            var token = await TokenAsync("sara@test.com", "Test@1234");
            AuthAs(token);

            int fgId, lineId, currentItemId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ri = await db.RequisitionItems.FirstAsync(x => x.QuotationRequestId == reqId);
                fgId = ri.Id;
                var bom = await db.BomHeaders.FirstAsync(b => b.RequisitionItemId == fgId);
                var line = await db.BomLines.FirstAsync(l => l.BomHeaderId == bom.Id);
                lineId = line.Id;
                currentItemId = line.RawMaterialItemId;
            }

            // First edit at MdPricing -> notifies
            (await _client.PutAsJsonAsync($"/api/costing/{reqId}/bom", new
            {
                FinishedGoodId = fgId,
                Lines = new[]
                {
                    new { Id = (int?)lineId, ItemId = currentItemId, QtyPerKg = 1.5m, Micron = (int?)60, Delete = false }
                }
            })).StatusCode.Should().Be(HttpStatusCode.OK);

            int notifCountAfterFirstEdit;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                notifCountAfterFirstEdit = await db.Notifications
                    .CountAsync(n => n.ReferenceType == "QuotationRequest" && n.ReferenceId == reqId);
            }

            // Simulate admin rollback via direct DB mutation (mirrors RollbackToCosting effect:
            // Status -> Costing, flag reset to false). Skips going through the admin API.
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var req = await db.QuotationRequests.FirstAsync(r => r.Id == reqId);
                req.Status = RequisitionStatus.Costing;
                req.MdPricingNotifiedAfterEdit = false;
                await db.SaveChangesAsync();
            }

            // Re-submit to MdPricing via the public endpoint
            var subResp = await _client.PostAsync($"/api/costing/{reqId}/submit", null);
            subResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // Edit again at MdPricing -> should notify AGAIN
            (await _client.PutAsJsonAsync($"/api/costing/{reqId}/bom", new
            {
                FinishedGoodId = fgId,
                Lines = new[]
                {
                    new { Id = (int?)lineId, ItemId = currentItemId, QtyPerKg = 2.5m, Micron = (int?)80, Delete = false }
                }
            })).StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope2 = factory.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var totalNotifs = await db2.Notifications
                .CountAsync(n => n.ReferenceType == "QuotationRequest" && n.ReferenceId == reqId);

            // Expectation: notifs from both sessions present. The second edit emits another batch
            // of "costing edited" notifs (one per active MD), so total should be ~2x first count.
            totalNotifs.Should().BeGreaterThan(notifCountAfterFirstEdit,
                "second MdPricing session must trigger a fresh notification batch after rollback");
        }
        finally
        {
            await CleanupReqAsync(reqId);
        }
    }
}
```

NOTE on the seed: the assumption is that the local DB already has at least one active `FinishedGood` item, one active `RawMaterial` item, and one `Process` row. If the seed in `Program.cs` doesn't seed those, the test will throw on `FirstAsync()` — fall back to creating them inside `SeedReqInMdPricingAsync` if needed (verify with a quick `dotnet ef migrations list` check + a scan of `Program.cs`).

The "submit re-enters MdPricing" path in test #4 requires the `Submit` endpoint's preconditions to be met (BOM + cost data exist for all FGs). The seed satisfies that. If that fails, double-check the seed.

- [ ] **Step 3: Run the new tests**

Run from worktree: `dotnet test --filter "FullyQualifiedName~EditAfterSubmitTests" --nologo -v q`
Expected: 4 tests pass.

- [ ] **Step 4: Run the full backend suite to ensure no regressions**

Run from worktree: `dotnet test --nologo -v q`
Expected: All previously passing tests still pass; new tests added.

If a previously passing test now fails because it stuffs `Status = MdPricing` and expected the BOM endpoint to reject — that's the EXPECTED behaviour change. Update the test if it's checking for the old "must be Costing" 400 error to now allow 200.

Common candidates for unexpected breakage: `BomTests`, `CostingTests`, `BomSaveLinesTests`. Read failure carefully and fix the assertion (not the production code) if the test is asserting the OLD behaviour.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.Tests/Costing/EditAfterSubmitTests.cs
git commit -m "test(costing): integration tests for accountant edit at MdPricing"
```

---

## Task 6: Web — extend `CostingEntryV3Page` editable predicate + amber banner

**Files:**
- Modify: `bom-web/src/features/costing/CostingEntryV3Page.tsx`

- [ ] **Step 1: Read the current page file to find the editable gate**

Open `bom-web/src/features/costing/CostingEntryV3Page.tsx` in the worktree. Search for `status` references. Likely patterns:

```ts
const editable = req.status === "Costing";
// or
const isReadOnly = req?.status !== "Costing";
// or various other forms
```

Record the exact current expression and its location (line numbers).

- [ ] **Step 2: Replace the predicate to include MdPricing**

Wherever `req.status === "Costing"` is the editable gate, change it to:

```ts
const editable = req.status === "Costing" || req.status === "MdPricing";
```

If there are multiple references (e.g. one for the form, one for the save button, one for the FG-level controls), update each to the same predicate. They should all match.

- [ ] **Step 3: Add an amber banner above the form when status is MdPricing**

Find the top-of-page render area (after the page heading, before the form). Insert:

```tsx
{req.status === "MdPricing" && (
  <div className="mb-4 rounded border border-amber-300 dark:border-amber-700 bg-amber-50 dark:bg-amber-900/30 px-3 py-2 text-sm text-amber-900 dark:text-amber-200">
    MD pricing pending. Saving here will refresh the data MDs see; the first edit notifies all MDs.
  </div>
)}
```

If the page uses `<Card>`/`<CardContent>` wrappers, place the banner inside the same wrapper but above the form's first heading.

- [ ] **Step 4: Verify TypeScript compiles**

Run from worktree: `cd bom-web && npx tsc -b --noEmit`
Expected: No errors.

- [ ] **Step 5: Run the existing component tests**

Run from worktree: `cd bom-web && npx vitest run src/features/costing/`
Expected: All existing tests pass (no regressions).

- [ ] **Step 6: Commit**

```bash
git add bom-web/src/features/costing/CostingEntryV3Page.tsx
git commit -m "feat(web): allow costing edits at MdPricing; show amber banner"
```

---

## Task 7: Web — extend "Edit costing" button visibility on `RequisitionDetailPage`

**Files:**
- Modify: `bom-web/src/features/requisitions/RequisitionDetailPage.tsx`

- [ ] **Step 1: Locate the existing "Edit costing" button**

Open `bom-web/src/features/requisitions/RequisitionDetailPage.tsx`. Search for "Edit costing" or "Start costing" or "costing" references. There should be a conditional render gating the button by status + role.

Likely current pattern:

```ts
{user?.role === "Accountant" && req.status === "Costing" && (
  <Link to={`/requisitions/${req.id}/costing`}>...</Link>
)}
```

Record the exact expression and line.

- [ ] **Step 2: Extend the predicate**

Replace the predicate to allow MdPricing as well:

```tsx
{user?.role === "Accountant" && (req.status === "Costing" || req.status === "MdPricing") && (
  <Link to={`/requisitions/${req.id}/costing`}>
    Edit costing
  </Link>
)}
```

Use button text "Edit costing" (more accurate than "Start costing" when status is MdPricing). If the existing label was conditional ("Start" vs "Edit"), use a single "Edit costing" label going forward.

- [ ] **Step 3: Verify TypeScript compiles**

Run from worktree: `cd bom-web && npx tsc -b --noEmit`
Expected: No errors.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/features/requisitions/RequisitionDetailPage.tsx
git commit -m "feat(web): show Edit costing button for accountant at MdPricing"
```

---

## Task 8: Web — extend `AdminActionType` union + audit-log labels

**Files:**
- Modify: `bom-web/src/api/admin.ts`
- Modify: `bom-web/src/features/admin/audit-log/AuditLogPage.tsx`

- [ ] **Step 1: Append `AccountantEditAfterSubmit` to the `AdminActionType` union**

Open `bom-web/src/api/admin.ts`. Find the `AdminActionType` type alias. Append `| "AccountantEditAfterSubmit"` after `"UpdateCompanySettings"`:

```typescript
export type AdminActionType =
  | "DeleteRequisition"
  | "RollbackStatus"
  | "ReassignSp"
  | "UnlockBom"
  | "UnlockCosting"
  | "ResetPassword"
  | "OverridePrices"
  | "HardDeleteCustomer"
  // V3 additions:
  | "RollbackToCosting"
  | "V3CutoverMigration"
  | "UpdateCompanySettings"
  | "AccountantEditAfterSubmit";
```

- [ ] **Step 2: Add label + array entry in `AuditLogPage`**

Open `bom-web/src/features/admin/audit-log/AuditLogPage.tsx`. Find `ACTION_TYPE_LABELS` and append:

```typescript
  AccountantEditAfterSubmit: "Accountant Edit After Submit",
```

Find the `ACTION_TYPES` array and append:

```typescript
  "AccountantEditAfterSubmit",
```

(Both placements should be at the END, after `UpdateCompanySettings`.)

- [ ] **Step 3: Verify TypeScript compiles**

Run from worktree: `cd bom-web && npx tsc -b --noEmit`
Expected: No errors.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/api/admin.ts \
        bom-web/src/features/admin/audit-log/AuditLogPage.tsx
git commit -m "feat(web): expose AccountantEditAfterSubmit in audit log dropdown"
```

---

## Task 9: Web component test for `CostingEntryV3Page` MdPricing rendering

**Files:**
- Modify: `bom-web/src/features/costing/CostingEntryV3Page.test.tsx`

- [ ] **Step 1: Read existing test scaffolding**

Open `bom-web/src/features/costing/CostingEntryV3Page.test.tsx`. Note the mock pattern (likely `vi.mock("@/api/axios")` for the data hooks) and the existing `renderPage()` helper.

- [ ] **Step 2: Add a new test case for MdPricing status**

Append a new `it()` block inside the existing `describe`:

```tsx
it("shows amber banner and keeps form editable when status is MdPricing", async () => {
  // Mock the GET to return a req with status MdPricing.
  // (Adjust the mocked endpoint + shape to match what the existing tests do.)
  const fixture = {
    /* same shape as existing test fixtures, but with status: "MdPricing" */
  };
  (api.get as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ data: fixture });

  renderPage();

  await waitFor(() => {
    expect(screen.getByText(/MD pricing pending/i)).toBeInTheDocument();
  });

  // Save button should be enabled (not disabled by status gate)
  const saveBtn = screen.getByRole("button", { name: /save/i });
  expect(saveBtn).toBeEnabled();
});
```

If the existing tests use a different fixture/render pattern, mirror it exactly. The key assertions are: amber banner text visible, save button enabled. If the existing tests don't have a direct `screen.getByRole("button", { name: /save/i })` selector (e.g. multiple Save buttons per FG), use `getAllByRole` and assert at least one is enabled.

- [ ] **Step 3: Run the new test**

Run from worktree: `cd bom-web && npx vitest run src/features/costing/`
Expected: existing tests + 1 new test all pass.

- [ ] **Step 4: Run the full vitest suite to ensure no regressions**

Run from worktree: `cd bom-web && npx vitest run`
Expected: All tests pass (294 + 1 new = 295+).

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/features/costing/CostingEntryV3Page.test.tsx
git commit -m "test(web): CostingEntryV3Page MdPricing banner + enabled save"
```

---

## Task 10: Manual web smoke

**Files:** none (smoke verification only)

- [ ] **Step 1: Verify backend is running with the new code**

Run from worktree: `curl -s http://localhost:7300/swagger/index.html >/dev/null && echo OK`. If not running: `dotnet run --project BomPriceApproval.API` from worktree (separate terminal).

- [ ] **Step 2: Start the web dev server using preview tools**

Use `preview_start` with the configured `web-feat` (or equivalent) entry pointing at the worktree's `bom-web`. Do NOT use `npm run dev` directly via Bash.

If a launch.json entry doesn't exist for the worktree's web folder, copy the existing one but with absolute `cwd` to the worktree path.

- [ ] **Step 3: Smoke flow**

In a real browser via the preview server:

1. Log in as Sara (`sara@test.com` / `Test@1234`).
2. Find an existing requisition in `Costing` status and submit costing to flip it to `MdPricing` (you may need to seed one fresh from a SalesPerson login first, or use a test req from the In Flight tab if one already in MdPricing exists).
3. Navigate to `/requisitions/<id>` in MdPricing — verify "Edit costing" button is visible.
4. Click it — should land on `/requisitions/<id>/costing` with the amber banner visible.
5. Edit a value (e.g. FOH per kg from 0.5 → 0.6) and save.
6. Verify the toast "Saved" (or whatever the existing success toast says).
7. As MD (login `md@test.com` / `Test@1234`) — verify a new bell-badge notification appears with the message "<RefNo> — costing edited, please refresh before approving".
8. Back as Sara — edit again. The toast should still show "Saved" and the MD should NOT see a second notification.
9. As MD — call `/api/approvals/<id>/set-margin` with a margin value (or use the UI). Status flips to `CustomerConfirm`.
10. Back as Sara — refresh the requisition detail page. Verify the "Edit costing" button is GONE.

If any step fails, diagnose and fix in the relevant task before proceeding.

- [ ] **Step 4: Take a screenshot of the amber banner for the user**

Use `preview_screenshot` to capture the costing page with the amber banner visible. Surface the saved file path in the chat for the user to see.

- [ ] **Step 5: Stop the preview**

Use `preview_stop` to terminate the dev server.

- [ ] **Step 6: No commit (smoke only) — proceed to next task.**

---

## Task 11: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Locate the V3 Workflow section**

Open `CLAUDE.md`. Find the heading `### V3 Workflow (CURRENT — post-2026-04-30 cutover)`.

- [ ] **Step 2: Add a sentence about the extended edit window**

Inside the V3 Workflow section, after the table of statuses and before the "V3 endpoints:" subsection, add:

```markdown
**Accountant edit window (post-2026-05-04, PR #TBD):** the costing-edit endpoints `PUT /api/costing/{id}/bom` and `PUT /api/costing/{id}/cost-data` accept BOTH `Costing` and `MdPricing` statuses. Accountant can self-correct after submit, until MD approves margin (status flips to `CustomerConfirm`). Each save while in `MdPricing` writes an `AdminAuditLog` row with `ActionType=AccountantEditAfterSubmit`; the FIRST save per MdPricing window notifies all active MDs once via `NotificationService` (subsequent saves audit silently). Tracked by `QuotationRequest.MdPricingNotifiedAfterEdit` flag, reset on every status transition that touches MdPricing.
```

(Replace `#TBD` with the actual PR number once opened in Task 13. If you want, leave as `#TBD` here and update in a follow-up commit to avoid blocking.)

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: document accountant edit window at MdPricing in CLAUDE.md"
```

---

## Task 12: Push branch + open PR

**Files:** none (git/gh operations only)

- [ ] **Step 1: Final test sweep**

Run from worktree:

```bash
dotnet test --nologo -v q
```

Expected: all tests pass (309 baseline + 4 new = 313 expected; ±1 if any flaky timing test).

```bash
cd bom-web && npx vitest run
```

Expected: all tests pass (294 baseline + 1 new = 295 expected).

```bash
cd bom-web && npx tsc -b --noEmit && npm run lint
```

Expected: 0 type errors; lint clean (the project's existing 2 `react-hooks/incompatible-library` warnings are acceptable as they predate this change).

- [ ] **Step 2: Verify clean working tree**

Run from worktree: `git status`
Expected: "nothing to commit, working tree clean" (after all Task commits) — or only ignored files visible.

- [ ] **Step 3: Push the feature branch**

Run from worktree: `git push -u origin feat/accountant-edit-at-mdpricing`
Expected: branch pushed, tracking set up.

- [ ] **Step 4: Open the PR**

Run:

```bash
gh pr create --title "feat: allow accountant to edit BOM/cost at MdPricing" --body "$(cat <<'EOF'
## Summary

- Relaxes status guards on `PUT /api/costing/{id}/bom` and `PUT /api/costing/{id}/cost-data` from `Costing` only to `Costing | MdPricing`.
- Each save while status=MdPricing writes an `AdminAuditLog` row (new `AdminActionType.AccountantEditAfterSubmit`).
- First save per MdPricing window notifies all active MDs once; tracked via new `QuotationRequest.MdPricingNotifiedAfterEdit` bool flag (reset on every status transition touching MdPricing).
- Web: `CostingEntryV3Page` editable + amber banner when status=MdPricing; `RequisitionDetailPage` "Edit costing" button visible to Accountant when status=MdPricing.
- One small EF migration adds the bool column.

## Spec / plan

- Spec: `docs/superpowers/specs/2026-05-04-accountant-edit-at-mdpricing-design.md`
- Plan: `docs/superpowers/plans/2026-05-04-accountant-edit-at-mdpricing.md`

## Test plan

- [x] Backend: `dotnet test --filter "FullyQualifiedName~EditAfterSubmitTests"` — 4/4 pass
- [x] Backend: full suite green
- [x] Web: full vitest suite green
- [x] Web: tsc clean + lint clean (modulo pre-existing warnings)
- [x] Manual: dev-server smoke walked through Costing → submit → MdPricing → edit → MD notified once → edit again → no second notif → MD approves → button vanishes
- [ ] **Post-merge:** apply Neon migration → Fly deploy → smoke against prod

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Surface the returned PR URL.

- [ ] **Step 5: Wait for CI green + auto-merge**

Watch CI via `gh pr checks <PR>`. Per CLAUDE.md auto-merge rule: when CI green + base is master + no `hold` label/comment, merge with squash via `gh pr merge <PR> --squash --delete-branch`. Report the new master SHA.

If lint fails (per the past pattern), read the failure, fix in a new commit on the branch, push, re-await.

- [ ] **Step 6: Apply Neon migration after merge**

Ask the user for the Neon production URI (it's a Fly secret; user has it locally — won't paste in chat unless explicitly provided). When provided:

```bash
dotnet ef database update --project BomPriceApproval.API \
  --connection "$NEON_PROD_URI" --no-build
```

Verify with `dotnet ef migrations list --connection "$NEON_PROD_URI"` — the new migration should appear without `(Pending)`.

After applying: REMIND the user to rotate the Neon password since they pasted the URI in chat (mirrors PR #88 lesson).

- [ ] **Step 7: Fly deploy**

```bash
flyctl deploy --remote-only --config fly.toml
```

Wait for deploy to finish. Verify:

```bash
curl -s -o /dev/null -w "health: %{http_code}\n" https://bom-fpf-api.fly.dev/health
# expected: 200
```

Pick a Signed quote, log in as admin on https://bom-fpf.pages.dev, navigate to `/admin/audit-log`, verify the new `Accountant Edit After Submit` entry appears in the action-type dropdown.

- [ ] **Step 8: Worktree cleanup**

```bash
git worktree remove .claude/worktrees/<worktree-name>
git worktree prune
git worktree list
```

- [ ] **Step 9: Final report**

Summarize for the user: PR URL, merge SHA, Neon migration status, Fly deploy result, prod smoke verdict, worktree cleanup.

---

## Self-review checklist (executed before publishing this plan)

✅ **Spec coverage:**
- Status gate relaxation (BOM + cost) → Tasks 3, 4
- Audit on every save while MdPricing → Tasks 3, 4
- Notify-once per session via flag → Task 1 (flag) + Tasks 3, 4 (logic)
- Flag reset on every status transition touching MdPricing → Task 2
- Web editable + amber banner → Task 6
- "Edit costing" button visibility → Task 7
- Audit log dropdown label → Task 8
- Backend integration tests → Task 5
- Web component test → Task 9
- Manual smoke → Task 10
- CLAUDE.md update → Task 11
- PR + Neon + Fly + cleanup → Task 12

✅ **Placeholder scan:** No "TBD" except in the CLAUDE.md PR-number placeholder (intentional — to avoid blocking on PR creation order). All step-by-step code blocks are full implementations.

✅ **Type consistency:**
- `MdPricingNotifiedAfterEdit` (bool) — same name in entity (Task 1), in resets (Task 2), in `if (!req.MdPricingNotifiedAfterEdit)` checks (Tasks 3, 4), and in tests (Task 5).
- `AdminActionType.AccountantEditAfterSubmit` — same name in enum (Task 1), audit calls (Tasks 3, 4), tests (Task 5), web admin.ts (Task 8), audit-log labels (Task 8).
- `NotificationType.CostingEditedAfterSubmit` — added to enum (Task 1) but never written to DB (existing Notification entity has no Type column); decorative only, kept for documentary parity.
- Notification entity uses `ReferenceId`/`ReferenceType` (NOT `RelatedEntityId`/`RelatedEntityType`) — tests and audit use the correct names.
- `SendToUsersAsync(IEnumerable<int>, string, int, string, CancellationToken)` — matches existing signature (NO NotificationType param).
