# Costing Entry Design

**Date:** 2026-04-14
**Status:** Approved
**Feature:** Costing Entry Page — `/requisitions/:id/costing`

---

## Goal

Give Accountants a dedicated page to enter raw material costs for a Bill of Materials. Costs are entered per-line with currency selection, auto-saved as a draft, and converted to the requisition's quote currency on submit. Each item's last known cost pre-fills the form. A stale-price warning fires when a cost is 10+ days old. On submit, per-line cost history is stored permanently and the requisition moves to `MdReview`.

---

## Architecture

### Backend — new endpoints

**`PUT /api/costing/{requisitionId}/draft`** (Accountant only)
- Upserts `CostingDraft` record for this BOM
- Body: `{ lines: [{ bomLineId, costPerKg, currencyCode }], landedCostType, landedCostValue, fohAmount }`
- Returns `204 NoContent`
- Requires status `CostingInProgress`; returns `400` otherwise

### Backend — modified endpoints

**`GET /api/costing/{requisitionId}`** (Accountant only)
- Returns `404` only if `BomHeader` does not exist for this requisition
- Returns BOM lines + draft + last-cost pre-fills whenever `BomHeader` exists (even when `BomCost` has not been submitted yet — `BomCost` aggregate fields are `null` in that case)
- Extended to return:
  - BOM lines (processId, processName, rawMaterialItemId, rawMaterialDescription, qtyPerKg, wastagePct, bomLineId)
  - Per-line last-cost pre-fill from `ItemLastCost` (costPerKg, currencyCode, updatedAt)
  - Draft values from `CostingDraft` if one exists (overrides pre-fills on the frontend)
- Add branch isolation (currently missing)

**`POST /api/costing/{requisitionId}/submit`** (Accountant only)
- Extended body: add `currencyCode` per line in `RawMaterialCostInput`
- Converts each line's `CostPerKg` from its currency to the requisition's `QuotationRequest.CurrencyCode` using `ExchangeRate` table
- Returns `400` if any required exchange rate is missing
- Writes `BomCostLine` rows permanently (one per BOM line)
- Upserts `ItemLastCost` for each raw material (one record per item, last-submitted cost)
- Deletes `CostingDraft` for this BOM
- Calculates `RawMaterialCostTotal`, applies landed cost (percentage or fixed), adds FOH — all in quote currency
- Writes `BomCost` aggregate, sets `BomHeader.TotalCostPerKg`
- Transitions requisition: `CostingInProgress → MdReview`
- Notifies all active Managing Directors
- Returns `204 NoContent`
- Add branch isolation (currently missing)

**`POST /api/costing/{requisitionId}/start`** (Accountant only)
- Add branch isolation (currently missing)

### Backend — new database tables

**`CostingDraft`**
| Column | Type | Notes |
|---|---|---|
| `Id` | int | PK |
| `BomHeaderId` | int | FK, unique — one draft per BOM |
| `LinesJson` | string | JSON array of `{ bomLineId, costPerKg, currencyCode }` |
| `LandedCostType` | LandedCostType | Percentage or FixedValue |
| `LandedCostValue` | decimal | |
| `FohAmount` | decimal | |
| `UpdatedAt` | DateTime | |

**`BomCostLine`**
| Column | Type | Notes |
|---|---|---|
| `Id` | int | PK |
| `BomHeaderId` | int | FK |
| `BomLineId` | int | FK |
| `CostPerKg` | decimal | In original entry currency |
| `CurrencyCode` | string | Currency the cost was entered in |
| `CostPerKgInQuoteCurrency` | decimal | Converted value at time of submit |
| Written on submit, never modified or deleted |

**`ItemLastCost`**
| Column | Type | Notes |
|---|---|---|
| `Id` | int | PK |
| `ItemId` | int | FK, unique — one record per item |
| `CostPerKg` | decimal | Last submitted cost |
| `CurrencyCode` | string | Currency of last submitted cost |
| `UpdatedAt` | DateTime | When last submitted |
| `UpdatedByUserId` | int | FK to User |

### Backend — existing DTOs to modify

**`RawMaterialCostInput`** — add `CurrencyCode` field:
```csharp
public record RawMaterialCostInput(int BomLineId, decimal CostPerKg, string CurrencyCode);
```

**New `SaveCostingDraftRequest`**:
```csharp
public record CostingDraftLineInput(int BomLineId, decimal CostPerKg, string CurrencyCode);
public record SaveCostingDraftRequest(
    [Required] List<CostingDraftLineInput> Lines,
    LandedCostType LandedCostType,
    decimal LandedCostValue,
    decimal FohAmount
);
```

**Extended `CostingDetailResponse`** — add BOM lines + draft + last-cost pre-fills:
```csharp
public record LastCostInfo(decimal CostPerKg, string CurrencyCode, DateTime UpdatedAt);
public record CostingBomLineResponse(
    int BomLineId, int ProcessId, string ProcessName,
    int RawMaterialItemId, string RawMaterialDescription,
    decimal QtyPerKg, decimal WastagePct,
    LastCostInfo? LastCost
);
public record CostingDraftResponse(
    List<CostingDraftLineInput> Lines,
    LandedCostType LandedCostType,
    decimal LandedCostValue,
    decimal FohAmount
);
public record CostingDetailResponse(
    int Id,
    decimal RawMaterialCostTotal,
    string LandedCostType,
    decimal LandedCostValue,
    decimal FohAmount,
    decimal TotalCostPerKg,
    DateTime? SubmittedAt,
    List<CostingBomLineResponse> BomLines,   // new
    CostingDraftResponse? Draft              // new — null if no draft
);
```

### Frontend — new files

```
bom-web/src/
  features/costing/
    CostingEntryPage.tsx        — route /requisitions/:id/costing
    costingApi.ts               — useCosting, useStartCosting, useSaveCostingDraft, useSubmitCosting
    CostingEntryPage.test.tsx
```

### Frontend — modified files

```
App.tsx                         — add /requisitions/:id/costing route (Accountant only)
RequisitionDetailPage.tsx       — wire "Start Costing" / "Continue Costing" button for Accountant
```

---

## Data Flow

### Page load

1. Read `requisitionId` from URL
2. Call `GET /api/costing/{id}`
3. If `404` and requisition status is `CostingPending` → auto-call `POST /start`, then re-fetch
4. Hydrate local state:
   - If `Draft` in response → use draft values (override pre-fills)
   - Else → use `LastCost` per line for pre-fill
5. Group BOM lines by `processId` for display

### State management

Lines held in local `useState`:

```ts
interface LocalCostLine {
  bomLineId: number;
  processId: number;
  processName: string;
  rawMaterialItemId: number;
  rawMaterialDescription: string;
  qtyPerKg: number;
  wastagePct: number;
  costPerKg: number;
  currencyCode: string;
  lastCost: { costPerKg: number; currencyCode: string; updatedAt: string } | null;
}
```

Global fields: `landedCostType`, `landedCostValue`, `fohAmount` in local `useState`.

### Auto-save

Triggered on every field change (debounced 800ms):
1. Fire `PUT /api/costing/{id}/draft` with full form state
2. Show "Saving…" in header during pending, "Saved ✓" on success
3. On error: toast "Failed to save draft." — do not revert form

### Currency conversion (frontend — live preview)

For the summary bar, convert each line's cost to quote currency using exchange rates fetched from `GET /api/exchange-rates`. The backend re-does this authoritatively on submit.

---

## Screens & Interactions

### Page load states

| Status | Behaviour |
|---|---|
| `CostingPending` | Show "Starting costing…" spinner → auto-call `/start` → render form |
| `CostingInProgress` | Load BOM lines + draft/pre-fills → render editable form |
| Any other status | Read-only view of submitted `BomCostLine` records, no edit controls |

### Per-line display

Each row shows:
- **Read-only:** Raw Material name (item code), Qty/kg, Wastage%
- **Editable:** Cost/kg (number input), Currency (dropdown — populated from `GET /api/exchange-rates`, which includes all currencies including USD)
- **Last Price column:**
  - No history → grey "No previous price"
  - ≤ 10 days old → grey "AED 1.2500 · 3 days ago"
  - > 10 days old → amber "⚠ AED 1.2500 · 14 days ago — verify from ERP"

### Landed Cost & Overheads section

- **Landed Cost Type:** dropdown — `Percentage` or `Fixed Value`
- **Landed Cost Value:**
  - If Percentage → number input with `%` suffix label ("% of raw material total")
  - If Fixed Value → number input with quote currency suffix label ("AED per kg")
- **FOH (per kg):** number input with quote currency suffix

### Summary bar

Always visible at page bottom. Recalculates live as values change:
- Raw Material Total (in quote currency)
- Landed Cost amount (in quote currency)
- FOH (in quote currency)
- **Total Cost/kg** (in quote currency) — shown in green

### Submit

- Button label: "Submit Costing ↗"
- Disabled if any `costPerKg === 0`
- Calls `POST /api/costing/{id}/submit`
- On success → navigate to `/requisitions/:id`
- On error → toast "Failed to submit costing." — stay on page
- On missing exchange rate error (400) → inline message: "No exchange rate found for [currency]. Contact admin."

### Detail page changes (RequisitionDetailPage)

- `CostingPending` → button label "Start Costing", navigate to `/requisitions/${id}/costing`
- `CostingInProgress` → button label "Continue Costing", same navigate
- Button only shown for Accountant role

---

## Types to add (`api.ts`)

```ts
export interface LastCostInfo {
  costPerKg: number;
  currencyCode: string;
  updatedAt: string;
}

export interface CostingBomLine {
  bomLineId: number;
  processId: number;
  processName: string;
  rawMaterialItemId: number;
  rawMaterialDescription: string;
  qtyPerKg: number;
  wastagePct: number;
  lastCost: LastCostInfo | null;
}

export interface CostingDraft {
  lines: { bomLineId: number; costPerKg: number; currencyCode: string }[];
  landedCostType: "Percentage" | "FixedValue";
  landedCostValue: number;
  fohAmount: number;
}

export interface CostingDetail {
  id: number;
  rawMaterialCostTotal: number;
  landedCostType: string;
  landedCostValue: number;
  fohAmount: number;
  totalCostPerKg: number;
  submittedAt: string | null;
  bomLines: CostingBomLine[];
  draft: CostingDraft | null;
}
```

---

## TanStack Query hooks (`costingApi.ts`)

```ts
export const costingKeys = {
  detail: (reqId: number) => ["costing", reqId] as const,
};

export function useCosting(requisitionId: number) { /* GET /api/costing/{id}, retry: false */ }
export function useStartCosting() { /* POST /api/costing/{id}/start */ }
export function useSaveCostingDraft() { /* PUT /api/costing/{id}/draft */ }
export function useSubmitCosting() { /* POST /api/costing/{id}/submit — invalidates ["requisitions"] + costingKeys.detail */ }
```

---

## Error Handling

| Scenario | Behaviour |
|---|---|
| `/start` fails on page load | Error card: "Failed to start costing. Retry." |
| Draft auto-save fails | Toast: "Failed to save draft." — form stays editable, no revert |
| Submit fails | Toast: "Failed to submit costing." — stay on page |
| Submit — exchange rate missing | Inline error: "No exchange rate found for [currency]. Contact admin." |
| BOM/costing fetch fails | Error card with Retry button |
| Any Cost/kg is 0 on submit | Submit button disabled — tooltip: "Enter cost for all lines before submitting" |
| Non-Accountant navigates to `/costing` | `ProtectedRoute` redirects to `/dashboard` |

---

## Immutability Rule

`BomCostLine` rows written on submit are **never modified or deleted**. When a raw material's cost changes in a future requisition, only `ItemLastCost` is updated — old `BomCostLine` records remain intact. Previously approved BOM costs are unaffected.

---

## Testing (`CostingEntryPage.test.tsx`)

1. `CostingPending` → `/start` called on load, form renders empty (pre-fills only)
2. `CostingInProgress` + draft exists → draft values pre-fill form (not `ItemLastCost`)
3. `CostingInProgress` + no draft → `ItemLastCost` values pre-fill form
4. Stale price warning (⚠) shows when `lastCost.updatedAt` > 10 days ago
5. No stale warning when `lastCost.updatedAt` ≤ 10 days ago
6. Auto-save PUT fires on field change (debounced)
7. Submit disabled when any `costPerKg === 0`; enabled when all > 0
8. Submit success → navigate to `/requisitions/:id`
9. Exchange rate error → inline message shown

Backend tests (`CostingTests.cs`):
1. Start costing → `CostingPending → CostingInProgress`
2. Save draft → draft persisted, status unchanged
3. Submit → costs converted to quote currency, `BomCostLine` written permanently, `ItemLastCost` upserted, status → `MdReview`
4. Submit with missing exchange rate → `400`
5. Re-costing a new requisition does not modify `BomCostLine` records of a previously approved BOM

---

## Out of Scope

- Accountant editing a costing after submission (status `MdReview`+) — read-only only
- Per-item default currency on Item master — Accountant always selects currency manually
- Audit log of all draft saves — only the final submit is recorded in `BomCostLine`
- Mobile layout — app targets desktop
