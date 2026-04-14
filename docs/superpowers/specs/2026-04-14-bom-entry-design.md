# BOM Entry Design

**Date:** 2026-04-14
**Status:** Approved
**Feature:** BOM Entry Page — `/requisitions/:id/bom`

---

## Goal

Give BomCreators a dedicated page to build a Bill of Materials for a requisition. Materials are grouped by manufacturing process. Lines auto-save after each add/remove. A live Net Qty/kg warning fires when the overall total deviates from 1.0 kg, but never blocks submission.

---

## Architecture

### Backend — 1 new endpoint

**`PUT /api/bom/{requisitionId}/lines`** (BomCreator only)
- Replaces all BOM lines without changing requisition status
- Body: `{ lines: [{ processId, rawMaterialItemId, qtyPerKg, wastagePct }] }`
- Returns `204 NoContent`
- Requires status `BomInProgress`; returns 400 otherwise

All other BOM endpoints already exist:
- `GET /api/bom/{requisitionId}` — fetch BOM + lines
- `POST /api/bom/{requisitionId}/start` — BomPending → BomInProgress, creates BomHeader
- `POST /api/bom/{requisitionId}/submit` — replace lines + BomInProgress → CostingPending

### Frontend — new files

```
bom-web/src/
  features/bom/
    BomEntryPage.tsx          — route /requisitions/:id/bom
    bomApi.ts                 — useBom, useStartBom, useSaveBomLines, useSubmitBom
    BomEntryPage.test.tsx
  api/
    lookups.ts                — add useProcesses() hook (GET /api/processes)
  types/
    api.ts                    — add BomLine, BomDetail types
```

### Frontend — modified files

```
App.tsx                       — add /requisitions/:id/bom route (BomCreator only)
RequisitionDetailPage.tsx     — wire "Start BOM" / "Continue BOM" button to navigate
BomController.cs              — add PUT /lines endpoint
BomDtos.cs                    — add SaveBomLinesRequest DTO
```

---

## Data Flow

### Page load

1. Read `requisitionId` from URL params
2. Call `GET /api/bom/{id}` to fetch existing BOM + lines
3. If response is 404 and requisition status is `BomPending` → auto-call `POST /start`, then re-fetch BOM
4. Hydrate local `lines: BomLine[]` state from fetched BOM lines
5. Prefetch (via TanStack Query):
   - `GET /api/processes` → process list for "+ Add Process" dropdown
   - `GET /api/items` → filter client-side to `type === "RawMaterial"` for material search

### State management

Lines are held in local `useState` on `BomEntryPage`. The array is flat:

```ts
interface LocalLine {
  processId: number;
  processName: string;
  rawMaterialItemId: number;
  rawMaterialDescription: string;
  qtyPerKg: number;
  wastagePct: number;
}
```

Processes are derived (not stored separately): `const processes = [...new Set(lines.map(l => l.processId))]`

### Auto-save

Triggered on every add or remove action:
1. Update `lines` state immediately
2. Fire `PUT /api/bom/{id}/lines` mutation with full `lines` array
3. Show subtle "Saving…" label in page header during pending, "Saved ✓" on success
4. On error: revert `lines` to previous state + show error toast

---

## Screens & Interactions

### Page load states

| Status | Behaviour |
|---|---|
| `BomPending` | Show "Starting BOM…" spinner → auto-call `/start` → render empty form |
| `BomInProgress` | Load existing lines → render form ready to edit |
| `BomInProgress` + no lines | Render empty form (no process sections yet) |
| Any other status | Read-only view of submitted lines, no edit controls |

### Process management

- **"+ Add Process"** → `SearchableSelect` dropdown from `GET /api/processes` (active only, ordered by `displayOrder`)
- Processes already present on the page are excluded from the dropdown
- **"Remove process ✕"** on the process header → removes section and all its lines → auto-save

### Process header — live totals

Each process section header shows a 3-cell stats bar:

| Cell | Formula |
|---|---|
| Total Qty | `sum(line.qtyPerKg)` for lines in this process |
| Total Waste | `sum(line.qtyPerKg × line.wastagePct / 100)` |
| Net Qty | `Total Qty − Total Waste` |

### Adding a raw material line

1. Click **"+ add raw material"** under a process → append inline editable row
2. Row fields: `SearchableSelect` for raw material (RawMaterial items only), `QtyPerKg` number input, `WastagePct` number input, ✓ confirm / ✕ cancel
3. On ✓: validate (material required, qty > 0) → add to `lines` state → auto-save
4. On ✕: discard row, no save

**Duplicate check:** if the same `rawMaterialItemId` already exists anywhere in the BOM → show inline amber warning "Already added under [ProcessName]. Add anyway?" with **Yes / Cancel** — does not block adding.

### Removing a raw material line

- ✕ button on each row → remove from `lines` → auto-save

### Net Qty warning

- **Threshold:** `|overallNetQty − 1.0| > 0.01`
- **Where shown:**
  - Amber banner at top of page: "Net Qty/kg is X.XXXX — expected ~1.0000. You can still submit — double-check quantities."
  - Overall summary bar: Net Qty cell shown in amber with ⚠ icon
- Warning is purely informational — Submit button remains enabled

### Submit BOM

- Button label: "Submit BOM ↗"
- Disabled if `lines.length === 0`
- Calls `POST /api/bom/{id}/submit` with current `lines`
- On success → navigate to `/requisitions/:id` (detail page shows `CostingPending` status)
- On error → show error toast, stay on page

### Detail page changes

- Remove `disabled` prop from the action button for BomCreator
- `BomPending` status → button label "Start BOM", `onClick` → `navigate(`/requisitions/${id}/bom`)`
- `BomInProgress` status → button label "Continue BOM", same navigate

---

## Types to add (`api.ts`)

```ts
export interface BomLine {
  id: number;
  processId: number;
  processName: string;
  rawMaterialItemId: number;
  rawMaterialDescription: string;
  qtyPerKg: number;
  wastagePct: number;
}

export interface BomDetail {
  id: number;
  quotationRequestId: number;
  refNo: string;
  itemDescription: string;
  lines: BomLine[];
  totalCostPerKg: number;
  submittedAt: string | null;
}

export interface Process {
  id: number;
  name: string;
  displayOrder: number;
  isActive: boolean;
}

export interface SaveBomLinesRequest {
  lines: {
    processId: number;
    rawMaterialItemId: number;
    qtyPerKg: number;
    wastagePct: number;
  }[];
}
```

---

## TanStack Query hooks (`bomApi.ts`)

```ts
export const bomKeys = {
  detail: (reqId: number) => ["bom", reqId] as const,
};

export function useBom(requisitionId: number) { /* GET /api/bom/{id} */ }
export function useStartBom() { /* POST /api/bom/{id}/start */ }
export function useSaveBomLines() { /* PUT /api/bom/{id}/lines */ }
export function useSubmitBom() { /* POST /api/bom/{id}/submit — invalidates ["requisition", id] */ }
```

`useProcesses()` added to `api/lookups.ts`:
```ts
export function useProcesses() {
  return useQuery({
    queryKey: ["processes"],
    queryFn: () => api.get<Process[]>("/processes").then(r => r.data),
    staleTime: FIVE_MINUTES,
  });
}
```

---

## Error Handling

| Scenario | Behaviour |
|---|---|
| `/start` fails on page load | Show error card: "Failed to start BOM. Retry." |
| Auto-save fails | Revert local state + error toast "Failed to save. Changes reverted." |
| Submit fails | Error toast, stay on page |
| BOM fetch fails | Error card with Retry button |
| User navigates to `/bom` without BomCreator role | `ProtectedRoute` redirects to `/dashboard` |

---

## Testing (`BomEntryPage.test.tsx`)

1. Non-BomCreator role → redirected (covered by ProtectedRoute, tested at router level)
2. BomPending status → `/start` is called on load, form renders empty
3. BomInProgress + existing lines → lines render correctly, no `/start` call
4. Add a line → auto-save fires with correct payload
5. Remove a line → auto-save fires
6. Net Qty warning shows when overall net deviates > 0.01 from 1.0
7. Net Qty warning absent when overall net is within 0.01 of 1.0
8. Submit disabled when no lines; enabled when lines exist
9. Submit success → navigation to `/requisitions/:id`

---

## Out of Scope

- BomCreator editing a BOM after it has been submitted (status CostingPending+) — read-only only
- Creating new processes from within BOM entry — Admin manages processes separately
- Mobile layout — app targets desktop
