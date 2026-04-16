# Multi-Item Validation Hardening — Design Spec

**Date:** 2026-04-16
**Status:** Approved (design); pending implementation plan
**Scope:** Sub-project A of the multi-item requisition follow-up
**Sub-projects out of scope:** B (workflow completeness), C (UX polish), D (PDF/email), E (clone), F (bug reports)

---

## Goal

Close validation gaps across the requisition flow so that invalid data cannot reach the database, cannot be submitted through any controller endpoint, and is caught in the UI before a request is ever sent.

## Non-Goals

- No workflow state-machine changes (no partial approval, no per-item rejection).
- No item-selector UX improvements beyond filtering already-added items from the add dropdown.
- No new libraries (no FluentValidation, no additional form library).
- No changes to Auth, Users, Branches, Customers, or any non-requisition feature.

---

## Design Principles

**Defense in depth — three layers, each assumes the others can fail:**

```
Frontend (Zod + react-hook-form / local state)
    Prevents invalid submits; inline per-field errors; best UX.
         │ HTTP (validated payloads)
         ▼
API controllers (inline guards, existing style)
    Authoritative policy; independent of frontend. Returns 400.
         │ EF Core
         ▼
PostgreSQL (EF migration: unique + CHECK constraints)
    Hard-stop bedrock. Concurrency-safe. Catches races & direct DB writes.
```

- Each layer enforces everything it can independently.
- DB layer is for data integrity, not UX. A DB-layer violation produces a generic toast, not a field-level error.
- Frontend Zod layer is the primary UX surface — it's where field-level errors originate.
- Controller is the safety net between them.

---

## Policy Decisions

| # | Decision | Choice |
|---|---|---|
| 1 | Scope | All 5 layers (Create, Add Item, BOM Submit, Costing Submit, MD Approve) |
| 2 | Duplicate items in a requisition | Hard block + DB unique constraint |
| 3 | MD approves with `SalesPrice < TotalCost` | Soft UI warning only — no block |
| 4 | Existing data | Pre-launch dev data; migration may fail loudly on violators |
| 5 | Error response shape | Unchanged: `{ message: string }` with HTTP 400. Fail-fast (first violation). |
| 6 | Field-level errors in UI | Come from Zod (frontend), not from backend responses |

---

## Layer 1 — Database

Single EF migration: `AddRequisitionValidationConstraints`.

| # | Entity | Constraint | Rationale |
|---|---|---|---|
| 1 | `RequisitionItem` | `UNIQUE (QuotationRequestId, ItemId)` | No duplicate items per requisition. Concurrency-safe. |
| 2 | `RequisitionItem` | `CHECK ExpectedQty > 0` | Zero/negative qty is never valid. |
| 3 | `BomLine` | `CHECK QtyPerKg > 0` | A BOM line with zero qty is a data bug. |
| 4 | `ApprovalItem` | `CHECK SalesPricePerKgAed > 0` | Cannot approve with a zero/negative price. |

**Deliberately NOT constrained** (to avoid over-restriction):
- `BomLine.WastagePct` — enforced at controller level (≥ 0). Keeps DB flexible if business later needs negative adjustments.
- `BomCostLine.CostPerKg` — 0 is legitimate (free / provided material).
- `BomCost.TotalCostPerKg` — derived value; edge cases possible.
- `ApprovalItem.SalesPricePerKgForeign` — derived; nullable.

**Implementation:**
- Configured in `AppDbContext.OnModelCreating` via `HasIndex(...).IsUnique()` and `entity.ToTable(t => t.HasCheckConstraint(name, sql))`.
- Migration generated with `dotnet ef migrations add AddRequisitionValidationConstraints --project BomPriceApproval.API`.

---

## Layer 2 — Backend Controllers

Inline guards, existing style. Nothing removed; only adds. Returns `BadRequest(new { message = "..." })` on first violation.

### `RequisitionsController`

**`Create(CreateRequisitionRequest req)`** — add after existing `items.Count == 0` check:
- Each `item.ExpectedQty > 0` → `"ExpectedQty must be greater than 0"`
- No duplicate `ItemId` in `req.Items` → `"Duplicate item: {id}"`
- All `ItemId` values exist and reference an `IsActive` Item (single `db.Items.Where(i => ids.Contains(i.Id) && i.IsActive)` lookup) → `"Unknown or inactive items: {ids}"`

**`AddItem(id, AddRequisitionItemRequest req)`** — add after existing status check:
- `ExpectedQty > 0`
- `req.ItemId` not already in requisition → `"Item already added"`
- Item exists and is active

**`RemoveItem`** — no new logic needed.

### `BomController`

**`SaveLines(requisitionId, requisitionItemId, request)`** — add:
- Each `line.QtyPerKg > 0` → `"QtyPerKg must be greater than 0"`
- Each `line.WastagePct >= 0` → `"WastagePct cannot be negative"`
- Each `ProcessId` and `RawMaterialItemId` exist (lookup) → `"Unknown process/material: {id}"`

**`Submit(requisitionId)`** — no new logic needed (already checks every item has ≥1 BOM line).

### `CostingController`

**`Submit(requisitionId, requisitionItemId, request)`** — add:
- Each `rc.CostPerKg >= 0` (≥ 0, not > 0 — free material is valid) → `"CostPerKg cannot be negative"`
- Each `rc.BomLineId` must match a real BOM line on this item. Currently silently skipped via `continue`; change to 400 → `"Unknown BOM line: {id}"`
- Every BOM line on this item must be costed (`request.RawMaterialCosts` covers all `bom.Lines`) → `"Missing cost for BOM line(s): {ids}"`

### `ApprovalsController`

**`Approve(requisitionId, ApproveRequest request)`** — add after existing status check. **Fixes a latent bug** where mismatched input items are silently skipped.
- `request.Items.Count > 0` → `"No items provided"`
- No duplicate `RequisitionItemId` in `request.Items` → `"Duplicate item in request: {id}"`
- Every `input.SalesPricePerKgAed > 0` → `"SalesPrice must be greater than 0"`
- `request.Items` covers **every** item in the requisition (no orphans) → `"Missing price for item(s): {ids}"`
- Every item has a costed BOM (`ri.BomHeader?.Cost` not null) → `"Item(s) not costed: {ids}"`

**No negative-margin block** (soft warning in UI only, per Decision 3).

**`Reject`** — no new logic needed.

### Error-response shape

Unchanged: `{ message: string }` with HTTP 400. Fail-fast (return on first violation encountered).

---

## Layer 3 — Frontend

Adapted per page. Only `NewRequisitionPage` currently uses `react-hook-form + zod`; BOM/Costing/MdReview use local `useState`. Each page follows its **existing** pattern — no library migration.

### `NewRequisitionPage` (react-hook-form + zod)

Extend the existing schema:

```ts
items: z.array(z.object({
  itemId: z.number().int().positive("Select an item"),
  expectedQty: z.coerce.number().positive("Qty must be > 0"),
})).min(1, "Add at least one item")
  .refine((arr) => new Set(arr.map(i => i.itemId)).size === arr.length,
          { message: "Duplicate items not allowed" }),
```

Item-picker dropdown: filter out items already selected in the form so duplicates cannot be added in the first place.

### `BomEntryPage` (state-based) — mostly already correct

Current code already has: `min="0"` on qty/wastage inputs, early-return in `confirmAddLine` when `qty <= 0`, Submit disabled until every item has lines. **The only change** is replacing inline error extraction on submit with the shared `extractApiError` helper.

### `CostingEntryPage` (state-based) — mostly already correct

Current code already has: `min="0"` on cost inputs, `canSubmit` requires `costPerKg > 0` on every BOM line, inline 400-message extraction on submit error. **The only change** is replacing inline error extraction with the shared `extractApiError` helper.

Note: the existing frontend requires `costPerKg > 0` (strict), while the backend allows `>= 0` (relaxed, for free/provided materials). This inconsistency is **out of scope** — flag for a future UX change if free materials become a real use case.

### `MdReviewPage` (state-based) — mostly already correct

Current code already has: `min="0"` on price inputs, `allPricesValid` guard that requires every price > 0, Approve button disabled on `!allPricesValid`, red background box for negative margins. **Changes:**
- Replace inline error extraction with the shared `extractApiError` helper.
- Add a small `⚠ Negative margin` badge adjacent to the existing red margin box, for clearer semantics (Decision 3: soft warning, never block).

### Not in scope — `RequisitionDetailPage`

The `useAddRequisitionItem` / `useRemoveRequisitionItem` hooks exist in `requisitionsApi.ts` but are **not wired to any UI**. This spec does not add that UI. When the add-item dialog is eventually built, it must include:
- Qty > 0 guard with inline helper text
- Item picker filtered to exclude items already in the requisition
- Confirm disabled when qty invalid

That work belongs to a separate UX-polish sub-project (sub-project C).

### Cross-cutting

- Backend 400 errors: surface via toast in existing axios interceptor / mutation `onError`. Show the `message` field. No field highlighting from backend responses.
- DB-layer violations (CHECK / UNIQUE failures) → generic toast (`"Something went wrong"`). DB is not a UX surface.

---

## Testing

### Backend (Testcontainers + `WebApplicationFactory<Program>`, matching existing style)

**New file `BomPriceApproval.Tests/Requisitions/ValidationTests.cs`:**
- `Create_ZeroQty_Returns400`
- `Create_NegativeQty_Returns400`
- `Create_DuplicateItemIds_Returns400`
- `Create_InactiveItem_Returns400`
- `Create_NonExistentItem_Returns400`
- `AddItem_DuplicateItem_Returns400`
- `AddItem_ZeroQty_Returns400`
- `DbLevel_DuplicateInsert_ThrowsUniqueViolation` (direct EF save — proves DB layer works)

**Extend `BomTests.cs` / `BomSaveLinesTests.cs`:**
- `SaveLines_ZeroQty_Returns400`
- `SaveLines_NegativeWastage_Returns400`

**Extend `CostingTests.cs`:**
- `Submit_NegativeCost_Returns400`
- `Submit_MissingLineCost_Returns400`
- `Submit_UnknownBomLineId_Returns400` (was silently skipped; now 400)

**New `BomPriceApproval.Tests/Approvals/ApprovalValidationTests.cs`:**
- `Approve_ZeroPrice_Returns400`
- `Approve_MissingItemInInput_Returns400` (the latent-bug fix)
- `Approve_DuplicateItemInInput_Returns400`
- `Approve_NegativeMargin_Succeeds` (documents the soft-warning policy — backend accepts)

### Frontend (Vitest + RTL, matching existing style)

**Extend `NewRequisitionPage.test.tsx`:**
- Zero qty → error shown, Submit disabled.
- Duplicate item prevented (picker excludes already-added).
- Valid submit → mutation called.

**Extend `BomEntryPage.test.tsx`, `CostingEntryPage.test.tsx`:**
- Zero/negative inputs → red helper, Submit disabled.

**Extend `MdReviewPage.test.tsx`:**
- Zero price → Submit disabled.
- Price < cost → negative-margin badge shown, Submit **still enabled**.

### Migration
No separate migration test. Testcontainers runs migrations on every test startup; a broken migration fails every test loudly. Pre-launch data means no cleanup script to verify.

---

## Files Changed (Summary)

### Backend
- `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs` (add unique index + 3 check constraints)
- `BomPriceApproval.API/Infrastructure/Data/Migrations/*_AddRequisitionValidationConstraints.cs` (generated)
- `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` (guards in Create + AddItem)
- `BomPriceApproval.API/Features/Bom/BomController.cs` (guards in SaveLines)
- `BomPriceApproval.API/Features/Costing/CostingController.cs` (guards in Submit + fix silent skip)
- `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs` (guards in Approve — fixes latent bug)

### Backend Tests
- `BomPriceApproval.Tests/Requisitions/ValidationTests.cs` (new)
- `BomPriceApproval.Tests/Approvals/ApprovalValidationTests.cs` (new)
- `BomPriceApproval.Tests/Bom/BomSaveLinesTests.cs` (extend)
- `BomPriceApproval.Tests/Costing/CostingTests.cs` (extend)

### Frontend
- `bom-web/src/lib/apiError.ts` (new, tiny `extractApiError(err, fallback): string` helper)
- `bom-web/src/features/requisitions/NewRequisitionPage.tsx` (extend zod schema with dedupe refinement + picker filter)
- `bom-web/src/features/bom/BomEntryPage.tsx` (use `extractApiError` on submit error)
- `bom-web/src/features/costing/CostingEntryPage.tsx` (use `extractApiError` on submit error)
- `bom-web/src/features/approvals/MdReviewPage.tsx` (use `extractApiError` + add negative-margin badge)

### Frontend Tests
- `bom-web/src/lib/apiError.test.ts` (new — unit tests for the helper)
- `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx` (extend)
- `bom-web/src/features/approvals/MdReviewPage.test.tsx` (extend — badge visibility)

Estimated net additions: ~300 lines backend + ~80 lines frontend + ~450 lines tests.
