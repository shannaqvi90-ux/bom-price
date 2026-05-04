# Accountant Edit Window Extended Through MdPricing

**Date:** 2026-05-04
**Author:** Shan + Claude (Opus 4.7)
**Status:** Design — pending writing-plans

## Goal

Allow accountants to edit BOM line quantities and cost data on a requisition that is already submitted for pricing (status = `MdPricing`), without recalling it back to `Costing`. Edits remain blocked once MD approves margin (status moves to `CustomerConfirm` and beyond).

## Why

Real-world flow: accountant submits costing for MD pricing, then realises a line qty or rate is off. Today the only path is admin override (`/api/admin/requisitions/{id}/unlock-costing`) — extra friction that requires Shan in the loop. Accountants should be able to self-correct as long as MD hasn't priced yet.

## Scope

**In scope (web only — Q4):**

- Backend: relax status guards on `PUT /api/costing/{id}/bom` and `PUT /api/costing/{id}/cost-data` from `Costing` only to `Costing | MdPricing`.
- Backend: every save while status=`MdPricing` writes an audit row to existing `AdminAuditLog` (new `AdminActionType.AccountantEditAfterSubmit`).
- Backend: first save while status=`MdPricing` notifies all active MDs via `NotificationService` (new `NotificationType.CostingEditedAfterSubmit`); subsequent saves stay silent until req leaves and re-enters `MdPricing`.
- Frontend: `CostingEntryV3Page` editable when `status in (Costing, MdPricing)`; amber info banner at top when `MdPricing`.
- Frontend: `RequisitionDetailPage` shows "Edit costing" button for Accountant when `status=MdPricing`.

**Out of scope:**

- Mobile UI (defer to next mobile rebuild — D-3 already queued).
- Adding new BOM lines via this path (existing "Phase A gap" — `UpdateBom` rejects `Id == null && !Delete` with "Creating new BOM lines via this endpoint is not yet supported"). Stays.
- Optimistic concurrency tokens. The existing `FOR UPDATE` row-lock in `SaveV3CostData` plus the status check at write time is sufficient. If MD approves margin in the same window, the second writer gets a status-mismatch 400 and a clear message.
- Cancelling MD's in-progress margin draft. V3 MD margin entry is all-at-once — no draft state — so no cleanup needed.
- Recall workflow (Q1: rejected in favour of inline edit).

## Approach

### Status gate

Single change in two endpoints. Today:

```csharp
if (req.Status != RequisitionStatus.Costing)
    return BadRequest(new { error = $"... only in Costing status (current: {req.Status})" });
```

After:

```csharp
if (req.Status != RequisitionStatus.Costing && req.Status != RequisitionStatus.MdPricing)
    return BadRequest(new { error = $"... only in Costing or MdPricing status (current: {req.Status})" });
```

Same predicate in both `UpdateBom` (line 146) and `SaveV3CostData` (line 252). No change to `Submit` or any other transition.

### Audit on edit-after-submit

After successful mutation, before `SaveChangesAsync`:

```csharp
if (req.Status == RequisitionStatus.MdPricing)
{
    var before = /* snapshot of pre-edit BOM lines or cost lines */;
    var after  = /* snapshot of post-edit state */;
    audit.Log(
        CurrentUserId,
        AdminActionType.AccountantEditAfterSubmit,
        entityType: "QuotationRequest",
        entityId: req.Id,
        reason: "Accountant edit during MdPricing window",
        before, after);
}
```

`AdminAuditLogger` is already DI-registered. Each endpoint takes one new constructor param (`AdminAuditLogger audit`). Snapshots are anonymous types — same pattern as `AdminUsersController.ResetPassword`.

### One-notification-per-edit-session

A naive anchor on `req.UpdatedAt` does not work, because the edit itself bumps `UpdatedAt` and the next save would see no prior notification within the new window → spurious re-notify.

Instead, add a single boolean column to `QuotationRequest`:

```csharp
public bool MdPricingNotifiedAfterEdit { get; set; } = false;
```

Lifecycle:

- `Submit()` (Costing → MdPricing): set to `false`. (Fresh window.)
- Any transition that LEAVES `MdPricing` (admin C5 rollback, MD approve, MD reject): set to `false`. Defensive — the column is irrelevant outside MdPricing, but keeping it false avoids stale state if status returns to MdPricing later.
- First accountant edit while status=`MdPricing` AND flag=`false`: notify all active MDs, set flag to `true`.
- Subsequent edits in the same MdPricing window: audit only, no notification (flag is already `true`).

```csharp
if (req.Status == RequisitionStatus.MdPricing && !req.MdPricingNotifiedAfterEdit)
{
    var mdIds = await db.Users
        .Where(u => u.Role == UserRole.ManagingDirector && u.IsActive)
        .Select(u => u.Id).ToListAsync();
    await notificationService.SendToUsersAsync(
        mdIds,
        $"{req.RefNo} — costing edited, please refresh before approving",
        req.Id,
        "QuotationRequest",
        NotificationType.CostingEditedAfterSubmit);
    req.MdPricingNotifiedAfterEdit = true;
}
```

`SendToUsersAsync` already exists with this exact signature; just need to add `NotificationType.CostingEditedAfterSubmit` to the enum.

One small EF migration: `AddMdPricingNotifiedAfterEditFlag` adds the bool column (default `false`, NOT NULL). Existing rows: backfilled to `false` (no in-flight requisitions are mid-edit at deploy time on a tiny dev/Shan-only prod).

### Frontend — `CostingEntryV3Page`

The page already gates save by status. Extend the `editable` predicate:

```ts
const editable = req.status === "Costing" || req.status === "MdPricing";
```

Add an amber banner above the form when `req.status === "MdPricing"`:

> "MD pricing pending. Saving here will refresh the data MDs see; first edit notifies all MDs."

Existing form, save button, validation — unchanged.

### Frontend — `RequisitionDetailPage`

The page surfaces an "Edit costing" / "Start costing" button conditional on `status === "Costing"` + role. Extend the predicate to include `MdPricing`:

```ts
const showEditCosting =
  user.role === "Accountant" &&
  (req.status === "Costing" || req.status === "MdPricing");
```

Button text stays "Edit costing" (more accurate than "Start costing" in both cases).

### Frontend — `AccountantListPage`

No change. The "In Flight" tab already includes `MdPricing` reqs in its filter (`TAB_STATUSES["in-flight"] = ["MdPricing", "CustomerConfirm", "MdFinalSign"]`). Accountant sees the req → clicks → lands on `RequisitionDetailPage` → uses the new button.

### Notification + UI for the MD

No new MD UI. Existing real-time `NotificationService` pipeline delivers the notification (toast + bell badge increment + DB row). MD page already subscribes to SignalR refreshes; if they have the margin page open and the notification fires, they see the new bell icon and can manually refresh.

## Components

| Component | Change |
|---|---|
| `Domain/Entities/QuotationRequest.cs` | Add `bool MdPricingNotifiedAfterEdit` property (default false) |
| `Domain/Enums/AdminActionType.cs` | Append `AccountantEditAfterSubmit` |
| `Domain/Enums/NotificationType.cs` | Append `CostingEditedAfterSubmit` |
| `Infrastructure/Data/Migrations/<TIMESTAMP>_AddMdPricingNotifiedAfterEditFlag.cs` | Adds the new bool column (NOT NULL, default false) |
| `Features/Costing/CostingController.cs` | Relax status guards in `UpdateBom` + `SaveV3CostData`; add `AdminAuditLogger audit` constructor param; add audit + notify-once logic after each successful mutation when `Status == MdPricing`. Also: in `Submit()` (and any other status-leave-MdPricing path) reset `MdPricingNotifiedAfterEdit = false`. |
| `Features/Approvals/ApprovalsController.cs` | On any transition that LEAVES MdPricing (margin approve, reject, customer-confirm flow start), reset `MdPricingNotifiedAfterEdit = false` for cleanliness. |
| `Features/Admin/AdminRequisitionsController.cs` | C5 rollback (MdPricing → Costing): reset `MdPricingNotifiedAfterEdit = false`. |
| `bom-web/src/features/costing/CostingEntryV3Page.tsx` | Extend `editable` predicate; add amber banner |
| `bom-web/src/features/requisitions/RequisitionDetailPage.tsx` | Extend "Edit costing" button visibility predicate |
| `bom-web/src/api/admin.ts` | Add `"AccountantEditAfterSubmit"` to `AdminActionType` union |
| `bom-web/src/features/admin/audit-log/AuditLogPage.tsx` | Add label to `ACTION_TYPE_LABELS` + entry to `ACTION_TYPES` |
| `BomPriceApproval.Tests/Costing/EditAfterSubmitTests.cs` | New file: 4 integration tests |
| `bom-web/src/features/costing/CostingEntryV3Page.test.tsx` | Extend with one test for the amber banner + enabled save at MdPricing |
| `CLAUDE.md` | Short paragraph under V3 Workflow describing the extended edit window |

One small EF migration adds the new bool column on `QuotationRequest`. The existing `AdminAuditLog` and `Notifications` tables already hold the new logical values (enum stored as string for `AdminActionType`; `NotificationType` is int-based — verify in implementation that appending a value at the end is safe for existing DB rows).

## Tests

### Backend (`BomPriceApproval.Tests/Costing/EditAfterSubmitTests.cs`)

Pattern mirrors `BomPriceApproval.Tests/Admin/CompanySettingsTests.cs`:

1. `UpdateBom_AtMdPricing_AccountantSucceeds_AndAudits_AndNotifiesMdsOnce` — seed a req in MdPricing, accountant PUTs `/bom`, expect 200; verify `AdminAuditLog` has one row with `ActionType=AccountantEditAfterSubmit`; verify one new `Notifications` row of type `CostingEditedAfterSubmit` for each active MD; second PUT in the same session should still audit but not duplicate the notification.
2. `SaveCostData_AtMdPricing_AccountantSucceeds_AndAudits_AndDoesNotDoubleNotify` — same idea on the cost-data endpoint.
3. `UpdateBom_AtCustomerConfirm_Returns400` — verify edits remain locked once MD has approved margin.
4. `UpdateBom_NotifResetsAfterAdminUnlockCosting` — req in MdPricing → accountant edits (notif fires) → admin calls `/admin/requisitions/{id}/rollback-status` MdPricing→Costing → accountant edits → re-submits → accountant edits again at MdPricing → expect a SECOND notification (because `req.UpdatedAt` reset by `Submit()`).

### Web (`bom-web/src/features/costing/CostingEntryV3Page.test.tsx`)

Extend with one test case: render the page with `req.status === "MdPricing"`, expect the amber banner to be visible AND the save button to be enabled. Test mocks `useReq` (or whichever hook provides the req) the same way existing test cases do.

## Risks

| Risk | Mitigation |
|---|---|
| MD clicks Approve at the same instant accountant clicks Save | Status check is at write time, inside the `FOR UPDATE` row lock for cost-data (already there). Whichever loses gets a status-mismatch 400. Acceptable — small window, clear error. |
| MD has the margin page open with stale cost numbers | Q2 notification covers — MD sees a bell badge + toast and can refresh. PdfService recomputes from DB at print time, so the eventual price is always consistent. |
| Notification noise if accountant tweaks repeatedly | Q2 design: only the first edit per MdPricing session notifies. Subsequent saves audit silently. |
| Audit log fills up faster | Each save adds one row. `AdminAuditLog` is paginated and filterable; not a real concern at this scale. |
| Double-PUT of the same payload yields two audit rows for no real change | Acceptable — no value-comparison check. The audit log is "what happened", not "what changed". Extra rows are visible but harmless. |

## Definition of done

- Backend integration tests green.
- Web component test green.
- Manual smoke on dev: accountant submits a req → status MdPricing → opens costing page → amber banner shows + save enabled → edits a value → MD receives one notification → accountant edits again → no second notification → MD clicks Approve → status flips → accountant's "Edit costing" button vanishes.
- CI green; PR opened, auto-merged per CLAUDE.md gating.
- Single EF migration applied to Neon (`AddMdPricingNotifiedAfterEditFlag`). Fly redeployed. `/health` 200, login DB-roundtrip 401.
- CLAUDE.md updated.
