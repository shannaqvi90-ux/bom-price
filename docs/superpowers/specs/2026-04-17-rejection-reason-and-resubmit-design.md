# Rejection Reason Display & SalesPerson Resubmit Flow — Design

**Date:** 2026-04-17
**Scope:** Close the workflow gap where rejected requisitions are a dead end — surface the MD's rejection notes to the SalesPerson, and give them a way to edit the requisition and resubmit it for BOM.

## Problem

1. `GET /api/requisitions/{id}` returns `ApprovalSummary { isApproved: bool }` — the `Notes` column on `QuotationApproval` (populated by `ApprovalsController.Reject`) is never surfaced.
2. The frontend detail page has no UI to display rejection reasons.
3. `RequisitionDetailPage.actionButtonFor` returns `null` for `Rejected` status — a rejected req has no next action. There is no resubmit endpoint or edit path.

## Decisions (from brainstorm)

| # | Decision | Rationale |
|---|---|---|
| D1 | Soft-delete approvals via `IsSuperseded` + `SupersededAt` columns (no separate history table) | Smallest footprint; history queryable from same table; no extra migration complexity |
| D2 | Atomic `POST /resubmit` endpoint, items-only payload | No half-edit state; customer/currency locked (a re-work, not a new quote); reuses Create validation |
| D3 | Dedicated `/requisitions/:id/edit` page mirroring `NewRequisitionPage` | Item editor is non-trivial; shareable URL; matches Create pattern |

## Data model

**File:** `BomPriceApproval.API/Domain/Entities/QuotationApproval.cs`

Add two columns:

```csharp
public bool IsSuperseded { get; set; }          // default false
public DateTime? SupersededAt { get; set; }
```

**Relationship change:** `QuotationRequest.Approval` (1:1) → `QuotationRequest.Approvals` (1:N). The "current approval" is computed as `Approvals.FirstOrDefault(a => !a.IsSuperseded)`.

**EF configuration:**
- Filtered index on `QuotationApproval(QuotationRequestId) WHERE IsSuperseded = false` to keep current-approval lookup fast.
- Update any existing `HasOne(... ).WithOne(...)` config to `HasMany(... ).WithOne(...)`.

**Migration:** `AddSupersededFieldsToQuotationApproval`
- Add `IsSuperseded bool NOT NULL DEFAULT false`
- Add `SupersededAt timestamp with time zone NULL`
- Add the filtered index
- No data backfill (existing rows default to not-superseded)

## Backend — API

### 1. `GET /api/requisitions/{id}` — surface rejection reason

**File:** `BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs`

```csharp
public record ApprovalSummary(bool IsApproved, string? Notes, DateTime ApprovedAt);
```

**File:** `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` — `Get` action

- Replace `Include(r => r.Approval)` with `Include(r => r.Approvals)`.
- Compute current approval: `var current = q.Approvals.FirstOrDefault(a => !a.IsSuperseded);`
- Map to `ApprovalSummary(current.IsApproved, current.Notes, current.ApprovedAt)`; null when no current approval.

Notes are returned for both approved and rejected — the MD can leave notes on approvals too.

### 2. `POST /api/requisitions/{id}/resubmit` — new endpoint

**File:** `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs`

```csharp
[HttpPost("{id}/resubmit")]
[Authorize(Roles = "SalesPerson")]
public async Task<IActionResult> Resubmit(int id, ResubmitRequisitionRequest req)
```

**Request DTO** (`RequisitionDtos.cs`):

```csharp
public record ResubmitRequisitionRequest(List<RequisitionItemInput> Items);
```

**Authorization:**
- `[Authorize(Roles = "SalesPerson")]` attribute
- Runtime: `q.SalesPersonId == CurrentUserId` → else `Forbid()`

**Validation** (reuses Create validation rules):

| Rule | Error |
|---|---|
| Requisition exists | 404 |
| Status == `Rejected` | 400 ValidationProblemDetails, field `Status` |
| At least one item | 400, field `Items` |
| No duplicate item IDs | 400, field `Items` |
| All `ExpectedQty > 0` | 400, field `Items[i].ExpectedQty` |
| All items exist and are active | 400, field `Items[i].ItemId` |

**Transaction** (wrapped in `db.Database.BeginTransactionAsync()`):

1. Mark current approval `IsSuperseded = true`, `SupersededAt = DateTime.UtcNow`.
2. Delete existing `RequisitionItem` rows for this requisition.
3. Insert new `RequisitionItem` rows from payload (preserving `SortOrder` 1..N).
4. Refresh `ExchangeRateSnapshot` — re-fetch latest active rate for the locked `CurrencyCode` (matches Create).
5. Set `Status = RequisitionStatus.BomPending`; `UpdatedAt = DateTime.UtcNow`.
6. `SaveChangesAsync`, commit.
7. Notify BomCreators of the branch (same pattern as `Create`).

**Cascade verification task:** Before implementation, confirm FK cascade rules for entities referencing `RequisitionItemId`:
- `BomHeader.RequisitionItemId`
- `CostingHeader.RequisitionItemId` (if applicable)
- Any other child tables

If any are `Restrict`/`NoAction`, add explicit cleanup inside the transaction. If they're `Cascade`, the single `Remove` call on `RequisitionItem` is sufficient. This decision is made during implementation after reading the DbContext `OnModelCreating`.

**Response:** `200 OK` with `{ id, refNo, status }`.

### 3. `ApprovalsController.Reject` — no change

Already writes `Notes`, `IsApproved = false`, `Status = Rejected`. Query-side filtering of the "current" approval in `Get` is the only behavioral change.

## Frontend

### 1. Types — `bom-web/src/types/api.ts`

```ts
export interface ApprovalSummary {
  isApproved: boolean;
  notes: string | null;
  approvedAt: string;
}
```

### 2. Detail page — rejection reason display

**File:** `bom-web/src/features/requisitions/RequisitionDetailPage.tsx` (Approval card, ~line 150)

Replace the current single `LabeledValue` with:

```tsx
{r.approval ? (
  <>
    <LabeledValue
      label={r.approval.isApproved ? "Approved" : "Rejected"}
      value={formatRelative(r.approval.approvedAt)} />
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
```

### 3. Detail page — action button for Rejected

**File:** same, `actionButtonFor` (line 21)

Add one branch:

```ts
if (role === "SalesPerson" && status === "Rejected")
  return { label: "Edit & Resubmit", path: "edit" };
```

### 4. Edit page — new route

**Route:** `/requisitions/:id/edit` (register in `bom-web/src/App.tsx` or wherever routes live).

**File:** `bom-web/src/features/requisitions/EditRequisitionPage.tsx` (new).

Structure:
- Fetches current requisition via `useRequisition(id)`.
- Renders rejection reason at top (`text-destructive` banner with `approval.notes`).
- Pre-fills items/qty from `r.items`.
- Customer + currency read-only (locked per D2).
- Submit button: "Resubmit for BOM".
- On success: toast + navigate to `/requisitions/:id`.

**Guards:**
- If loaded req's status ≠ `Rejected` → show "Cannot edit — status is X" with back link.
- If `user.id !== r.salesPersonId` → show "Only the owning sales person can edit" with back link.

### 5. Shared items editor

If `NewRequisitionPage` currently has the item editor inline, extract into `bom-web/src/features/requisitions/components/RequisitionItemsEditor.tsx` and use from both pages. Targeted extraction only — no broader refactor. If already extracted, reuse as-is.

### 6. API hook — `bom-web/src/features/requisitions/requisitionsApi.ts`

Add:

```ts
export function useResubmitRequisition(id: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: { items: { itemId: number; expectedQty: number }[] }) =>
      api.post(`/api/requisitions/${id}/resubmit`, payload).then(r => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["requisition", id] });
      qc.invalidateQueries({ queryKey: ["requisitions"] });
    },
  });
}
```

## Tests

### Backend (`BomPriceApproval.Tests/Requisitions/ResubmitTests.cs` — new)

Integration tests using `WebApplicationFactory<Program>` with testcontainers (no mocks):

1. `Resubmit_RejectedRequisition_TransitionsToBomPending_AndSupersedesApproval`
2. `Resubmit_NonOwner_SameBranch_Forbidden`
3. `Resubmit_StatusNotRejected_ReturnsValidationProblemDetails` (field: `Status`)
4. `Resubmit_EmptyItems_ReturnsValidationProblemDetails` (field: `Items`)
5. `Resubmit_DuplicateItems_ReturnsValidationProblemDetails` (field: `Items`)
6. `Resubmit_InvalidQty_ReturnsValidationProblemDetails` (field: `Items[i].ExpectedQty`)
7. `Resubmit_NotifiesBomCreators` — verify NotificationService received one call per active BomCreator in branch
8. `GetDetail_OnRejectedRequisition_ReturnsNotes` — assert `approval.notes`, `approval.isApproved == false`
9. `GetDetail_AfterResubmit_ReturnsNullApproval` — superseded approval is filtered out

**Regression coverage (must stay green):** `RequisitionWorkflowTests`, `ApprovalsController.Reject` tests.

### Frontend (Vitest + RTL, matches existing T8.x error-path pattern)

10. `RequisitionDetailPage_RejectedWithNotes_RendersRejectionBlock` — `text-destructive` block with notes
11. `RequisitionDetailPage_Rejected_ShowsEditAndResubmitButton_ForOwningSalesPerson`
12. `RequisitionDetailPage_Rejected_NoButton_ForNonSalesPerson` (BomCreator, Accountant, MD)
13. `EditRequisitionPage_NonRejectedStatus_ShowsCannotEditMessage`
14. `EditRequisitionPage_SubmitSuccess_NavigatesToDetail`

### Out of scope

- Load/perf tests
- E2E browser tests
- DTO serialization unit tests (integration tests cover)

## Commit message

```
feat(api+web): rejection reason display + SalesPerson resubmit flow
```

## Follow-ups (not in scope)

- Showing the full approval history timeline (the superseded rows are there but not surfaced).
- Email notification to the original SalesPerson on rejection (currently only in-app via SignalR).
