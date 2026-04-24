# Mobile V2.2 — MD BOM / Costing Drill-Down Design

**Date:** 2026-04-25
**Scope:** Managing Director mobile app
**Parent work:** V2.0 (mobile MD historical list + approved prices)
**Status:** Spec, awaiting plan

---

## 1. Problem

After V2.0, MD on mobile can:
- Search and open any historical requisition from the all-list.
- See approved per-kg sales prices on approved requisitions.

MD **cannot** see:
- BOM breakdown (raw materials, qty/kg, wastage, contribution per line, process grouping).
- Cost breakdown (raw material / landed / FOH / total and their percentages).
- Margin (approved price vs total cost) for historical requisitions.

Web has this via `MdReviewPage` (inline cost card + "View BOM" dialog) for the live review flow, but mobile has no equivalent for approved or rejected requisitions.

## 2. Goals

Enable MD on mobile to drill into any **Approved** or **Rejected** requisition, per item, and inspect:
1. Margin (Approved only).
2. Full cost breakdown.
3. Full BOM lines grouped by process.

Non-goals:
- Drill-down for in-progress statuses (BomPending, CostingPending, MdReview). MD already has the active review flow for MdReview items.
- Editing BOM or cost data. Read-only.
- Exposing this data to other roles. MD-only.

## 3. Scope

### In scope

- New mobile route `(md)/item/[reqId]/[itemId].tsx` — one screen per item.
- Entry point: a per-item "View details ▸" CTA inside each `ItemCardShell` on `(md)/historical/[id].tsx`, shown only when `status === "Approved" || status === "Rejected"`.
- Stacked-scroll layout: margin hero (Approved only) → cost breakdown card → BOM process groups with per-line rows.
- Reuse existing backend endpoints — no controller or DTO changes.
- New mobile types mirroring backend DTOs.
- New mobile API hooks.
- Four new components (`MarginHeroCard`, `CostBreakdownCard`, `BomProcessGroup`, `BomLineRow`).
- Backend regression tests guarding against accidental status-gates on the two read endpoints.

### Out of scope

- Any UI for other roles (SalesPerson, BomCreator, Accountant).
- Backend changes to `BomController` or `ApprovalsController` beyond tests.
- Drill-down for statuses other than Approved / Rejected.
- Mobile component snapshot tests (no `jest-expo` setup yet).
- EAS build or deploy (still deferred per V2.0 close note).

## 4. User flow

1. MD opens any Approved or Rejected requisition from the historical list.
2. Each item card shows the existing content (description, quantity, stage badge, approved price for Approved) plus a new `View details ▸` CTA row at the bottom.
3. MD taps the CTA → navigates to `(md)/item/[reqId]/[itemId]`.
4. Screen loads BOM + approval review data in parallel (both already cached if MD previously visited MdReviewPage — but historical flow does not cache them, so expect first-time fetch).
5. Screen renders:
   - **Header:** `ScreenHeader` with item description (truncated) as title, `{expectedQty} kg` as label; status pill on the right.
   - **Margin hero card** (Approved only): green card showing `Margin XX.X%` with subline `Price P.PPPP · Cost C.CCCC AED/kg`.
   - **Cost breakdown card:** rows for Raw Material / Landed / FOH with `N.NNNN/kg (XX.X%)`, total row on bottom.
   - **BOM process groups:** one card-group per distinct process. Header is process name + line count; body lists `BomLineRow` entries.
6. MD reviews, taps back. No write actions on this screen.

## 5. Data sources — existing backend

All three endpoints already exist, are branch-isolated where relevant, and work for Approved + Rejected.

| Endpoint | Auth | Purpose | Already used on mobile? |
|---|---|---|---|
| `GET /api/requisitions/{id}` | Any authenticated (branch-isolated) | Header, items, approval.items[].pricePerKg | Yes — `useRequisitionDetail` |
| `GET /api/bom/{requisitionId}` | Any authenticated (branch-isolated) | BOM items, lines, per-item total cost/kg | **No — new hook** |
| `GET /api/approvals/{requisitionId}` | MD only | Per-item cost breakdown + percentages | **No — new hook** |

Verification: both `BomController.Get` and `ApprovalsController.GetReview` have no `status` filter in code. If integration tests reveal otherwise, we fix in place.

### Data flow for the drill-down screen

```
useRequisitionDetail(reqId)  →  approval.items[itemId].pricePerKg
useBomDetail(reqId)          →  item's lines + per-item total cost/kg
useMdReview(reqId)           →  item's cost breakdown + percentages
```

All three queried in parallel via TanStack Query. Margin is computed client-side:
`margin% = (pricePerKg - totalCostPerKg) / pricePerKg * 100`

## 6. Components

### New files

```
bom-mobile/src/api/bom.ts                            # new: useBomDetail
bom-mobile/src/api/approvals.ts                      # extend: add useMdReview
bom-mobile/src/types/api.ts                          # extend: add BOM + MdReview types
bom-mobile/src/components/MarginHeroCard.tsx
bom-mobile/src/components/CostBreakdownCard.tsx
bom-mobile/src/components/BomProcessGroup.tsx
bom-mobile/src/components/BomLineRow.tsx
bom-mobile/app/(md)/item/[reqId]/[itemId].tsx        # new screen
```

### Modified files

```
bom-mobile/app/(md)/historical/[id].tsx              # add CTA + navigation
bom-mobile/src/components/ItemCardShell.tsx          # only if we need CTA slot; likely just compose externally
```

### Type additions (`src/types/api.ts`)

Mirror backend DTOs one-to-one:

```ts
export type BomLineResponse = {
  id: number;
  processId: number;
  processName: string;
  rawMaterialItemId: number;
  rawMaterialDescription: string;
  qtyPerKg: number;
  wastagePct: number;
  costPerKg: number | null;
  currencyCode: string | null;
  costPerKgInAed: number | null;
  contributionAed: number | null;
};

export type BomItemResponse = {
  requisitionItemId: number;
  itemId: number;
  itemDescription: string;
  expectedQty: number;
  sortOrder: number;
  bomHeaderId: number | null;
  bomStatus: string;
  lines: BomLineResponse[];
  totalCostPerKg: number;
  submittedAt: string | null;
};

export type BomReviewResponse = {
  requisitionId: number;
  refNo: string;
  status: string;
  items: BomItemResponse[];
};

export type MdReviewItemCost = {
  rawMaterialCostPerKg: number;
  landedCostPerKg: number;
  fohPerKg: number;
  totalCostPerKg: number;
  materialCostPct: number;
  landedCostPct: number;
  fohPct: number;
};

export type MdReviewItemDetail = {
  requisitionItemId: number;
  itemDescription: string;
  expectedQty: number;
  bomStatus: string;
  cost: MdReviewItemCost | null;
};

export type MdReviewDetail = {
  refNo: string;
  customerName: string;
  currencyCode: string;
  exchangeRate: number | null;
  readyForReview: boolean;
  items: MdReviewItemDetail[];
};
```

### Component contracts

- **`MarginHeroCard`** — props: `{ pricePerKg: number; totalCostPerKg: number; currencyCode: string }`. Computes margin%, renders green/red card. Red only for negative margin.
- **`CostBreakdownCard`** — props: `{ cost: MdReviewItemCost | null }`. Null → "Costing not completed" placeholder.
- **`BomProcessGroup`** — props: `{ processName: string; lines: BomLineResponse[] }`. Renders an uppercase section label (e.g. `BOM — EXTRUSION`) followed by one `BomLineRow` card per line.
- **`BomLineRow`** — props: `{ line: BomLineResponse }`. Two-line row: description (wrap to 2 lines) + secondary "qty · waste · cost · contribution".

All styling inline (consistent with project convention — NativeWind is avoided on RN 0.81.5 due to known Pressable-render issues).

## 7. States — loading / error / empty

| State | Trigger | UI |
|---|---|---|
| Loading | Both queries `isPending` | `LoadingView` |
| 404 (bom or review) | Backend returned 404 | `ErrorBanner` with retry |
| 403 (review) | User not MD (session role drift) | `ErrorBanner "Access denied"` + back link |
| BOM missing for item | `bomHeaderId === null` in `BomItemResponse` | BomProcessGroup section replaced by "BOM not available for this item" callout |
| Cost missing for item | `cost === null` in `MdReviewItemDetail` | `CostBreakdownCard` renders "Costing not completed" |
| Rejected requisition | `r.status === "Rejected"` | `MarginHeroCard` not rendered. Rest renders as normal. |
| Item not in response | safety — item id from URL doesn't match any entry | `ErrorBanner "Item not found"` + back link |

## 8. Testing

### Backend (`BomPriceApproval.Tests`)

New integration tests to guard against accidental status-gate regressions:

- `MD_GetBom_ReturnsDataForApprovedStatus`
- `MD_GetBom_ReturnsDataForRejectedStatus`
- `MD_GetApprovalReview_ReturnsDataForApprovedStatus`
- `MD_GetApprovalReview_ReturnsDataForRejectedStatus`

Each test seeds a requisition through to the target status, then asserts the endpoint returns items with cost / BOM data.

### Mobile

Manual verification (no `jest-expo` setup yet — out of scope to introduce):

- Approved requisition, full data → margin hero + cost + BOM all render, numbers match backend.
- Rejected requisition with BOM + cost present → cost + BOM render, no margin hero.
- Rejected requisition with no cost (rejected in earlier stage) → "Costing not completed" placeholder.
- Back-navigation to historical detail works.
- Auth drift (expired role) → 403 shown cleanly.

## 9. Implementation ordering

1. Backend integration tests (red → green confirms endpoints remain open).
2. Mobile types additions.
3. Mobile API hooks (`useBomDetail`, `useMdReview`).
4. Leaf components (`BomLineRow`, `CostBreakdownCard`, `MarginHeroCard`, `BomProcessGroup`).
5. Drill-down screen (`(md)/item/[reqId]/[itemId].tsx`).
6. Historical detail modification (add CTA + navigation).
7. Manual smoke verification on device/emulator.
8. Commit per logical unit; no squash.

## 10. Open questions

None at spec time. Any ambiguity surfaced during the plan phase will loop back here.

## 11. References

- Project memory: `memory/project_mobile_v22_bom_drilldown.md`
- Prior V2.0 spec: `docs/superpowers/specs/2026-04-24-mobile-v2-md-list-prices-design.md`
- Web counterparts: `bom-web/src/features/approvals/MdReviewPage.tsx`, `bom-web/src/features/bom/BomEntryPage.tsx`
- Backend: `BomPriceApproval.API/Features/Bom/BomController.cs`, `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs`
