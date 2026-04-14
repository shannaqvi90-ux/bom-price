# MD Review Page — Design Spec

**Date:** 2026-04-14  
**Feature:** Managing Director approval/rejection of requisitions at `MdReview` status  
**Route:** `/requisitions/:id/approval` (ManagingDirector only)

---

## Overview

The MD Review page is the final human gate in the requisition workflow. The Managing Director sees the full cost breakdown assembled by the accountant, sets a sales price, and either approves (generating a PDF quotation) or rejects (notifying the sales person with a mandatory reason).

Workflow position: `CostingInProgress → MdReview → Approved | Rejected`

---

## Architecture

### New Files

| File | Purpose |
|---|---|
| `bom-web/src/features/approvals/approvalsApi.ts` | TanStack Query hooks for approvals API |
| `bom-web/src/features/approvals/MdReviewPage.tsx` | Main review/decision page |
| `bom-web/src/features/approvals/MdReviewPage.test.tsx` | Vitest unit tests |

### Modified Files

| File | Change |
|---|---|
| `bom-web/src/App.tsx` | Add `/requisitions/:id/approval` route, ManagingDirector-only |
| `bom-web/src/features/requisitions/RequisitionDetailPage.tsx` | Enable the "Review & Approve" button (remove disabled + "Coming soon" for `approval` path) |
| `bom-web/src/types/api.ts` | Add `MdReviewDetail` type |

---

## API Hooks (`approvalsApi.ts`)

Three hooks, following the same pattern as `bomApi.ts` and `costingApi.ts`:

```typescript
useMdReview(requisitionId: number)
// GET /api/approvals/{id}
// Returns MdReviewDetail

useApproveRequisition()
// POST /api/approvals/{id}/approve
// Body: { salesPricePerKgAed: number; notes?: string }
// Response: { message: "Approved"; refNo: string }

useRejectRequisition()
// POST /api/approvals/{id}/reject
// Body: { notes: string }   ← required
// Response: { message: "Rejected" }
```

Query key namespace: `["approval", "review", requisitionId]`

---

## Type: `MdReviewDetail`

Add to `bom-web/src/types/api.ts`:

```typescript
interface MdReviewDetail {
  refNo: string;
  itemDescription: string;
  customerName: string;
  expectedQty: number;
  currencyCode: string;
  exchangeRate: number | null;
  rawMaterialCostPerKg: number;
  landedCostPerKg: number;
  fohPerKg: number;
  totalCostPerKg: number;
  materialCostPct: number;
  landedCostPct: number;
  fohPct: number;
}
```

Matches backend `MdReviewDetail` record in `BomPriceApproval.API/Features/Approvals/ApprovalDtos.cs`.

---

## Page Design (`MdReviewPage.tsx`)

### Page States

The page manages a local `pageState` variable:

- `"reviewing"` — default; shows the two-column decision form
- `"approved"` — after a successful approve mutation; shows success card with PDF download

### Layout: Two-Column Split

**Left column — Cost Breakdown (read-only)**
- Header: `refNo`, StatusBadge, back link to `/requisitions/:id`
- Labeled rows: Raw Material / Landed Cost / FOH — each as `AED/kg`
- Highlighted total row: Total Cost/kg (bold, visually distinct)
- Footer metadata: expected qty, currency code, exchange rate snapshot

**Right column — Decision Form** (visible when `pageState === "reviewing"`)
- `Sales Price (AED/kg)` — number input, step `0.0001`, 4 decimal places
- Live **Profit Margin %** pill — recalculates on every keystroke:
  ```
  margin = ((salesPrice - totalCostPerKg) / salesPrice) * 100
  ```
  Displayed green when positive, red when negative or sales price ≤ cost
- `Notes` textarea — always visible; labelled "optional" context for approve, "required" for reject
- Two action buttons at bottom:
  - Green **Approve** — disabled if no sales price entered
  - Red **Reject** — disabled if notes is empty (client-side validation)
- Inline error message below buttons on mutation failure (same pattern as `CostingEntryPage`)

**Right column — Success Card** (visible when `pageState === "approved"`)
- "Approved ✓" heading in green
- Summary: sales price entered, profit margin %
- Green **Download PDF** button — `GET /api/approvals/{id}/pdf` (opens in new tab)

### Reject Flow

1. MD clicks Reject
2. Client validates notes is non-empty; shows inline error if blank
3. On valid notes: fires `useRejectRequisition` mutation
4. On success: `navigate(`/requisitions/${id}`)` (back to detail page)

### Loading & Error States

- While `useMdReview` loads: spinner replacing the left column
- On fetch error: full-page error card with back link (same pattern as `RequisitionDetailPage`)
- If the backend returns 403 (not MD role) or 404 (requisition not found): same error cards as `RequisitionDetailPage`
- If the MD navigates directly to the URL when status is already `Approved` or `Rejected`: the backend GET still returns cost data; the page renders normally but any approve/reject mutation will fail with a backend error, shown inline. Normal navigation (via the detail page button) only reaches this page when status is `MdReview`.

---

## Routing (`App.tsx`)

```tsx
<Route
  path="/requisitions/:id/approval"
  element={
    <ProtectedRoute allow={["ManagingDirector"]}>
      <MdReviewPage />
    </ProtectedRoute>
  }
/>
```

---

## `RequisitionDetailPage.tsx` Update

The existing `actionButtonFor` already returns `{ label: "Review & Approve", path: "approval" }` for `ManagingDirector + MdReview`. The button's `disabled` condition currently disables any path that is not `"bom"` or `"costing"`. Change it to also allow `"approval"`:

```tsx
// Before
disabled={action.path !== "bom" && action.path !== "costing"}

// After
disabled={action.path !== "bom" && action.path !== "costing" && action.path !== "approval"}
```

Remove the `title="Coming soon"` for the `approval` path at the same time.

---

## Tests (`MdReviewPage.test.tsx`)

Seven test cases, using `vi.mock("@/api/axios")` + `QueryClientProvider` + `MemoryRouter` (same pattern as `RequisitionDetailPage.test.tsx`):

| # | Test |
|---|---|
| 1 | Renders cost breakdown rows from API response |
| 2 | Live profit margin updates as sales price is typed |
| 3 | Approve fires mutation with correct payload; page flips to approved state showing Download PDF |
| 4 | Reject with empty notes shows validation error and does not fire mutation |
| 5 | Reject with notes fires mutation and navigates to `/requisitions/:id` |
| 6 | Shows loading state while data is fetching |
| 7 | Shows error card on API failure |

---

## Out of Scope

- PDF preview inline (PDF opens in new tab via browser)
- Editing the sales price after approval (status becomes `Approved`, page is read-only if revisited)
- Mobile-specific layout adjustments
