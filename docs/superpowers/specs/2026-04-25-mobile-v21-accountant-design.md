# Mobile V2.1 — Accountant Mobile Stack Design

**Date:** 2026-04-25
**Scope:** Accountant role on mobile (parity with web `CostingEntryPage`)
**Parent work:** V1 (Sales mobile), V2.0 + V2.2 (MD mobile)
**Status:** Spec, awaiting plan

---

## 1. Problem

The mobile app currently supports SalesPerson (V1) and ManagingDirector (V2.0 + V2.2). The Accountant role — Sara — has no mobile presence. She must use the desktop web app (`CostingEntryPage`) to:

- Review pending requisitions awaiting costing.
- Enter per-item cost data (BOM-line raw material costs + landed cost + FOH).
- Submit costed items so the requisition can advance to MD review.
- Optionally change the customer mid-flow (allowed in `CostingInProgress`) and review change history.

This blocks her from unblocking quotations while away from her desk. The desktop costing flow is the most time-sensitive write path in the system — every requisition passes through it, and delays here cascade.

## 2. Goals

Bring the Accountant flow to mobile with full parity to web, in two explicit phases shipped from a single spec:

### Phase 1 — MVP

1. Role-guarded `(accountant)/` route group.
2. **Pending list** — `CostingPending` + `CostingInProgress` requisitions for the current branch.
3. **Requisition detail** — items list with per-item cost-status badges; auto-start the first `NotStarted` item on entry (web parity).
4. **Per-item costing form** — drill-in screen with:
   - BOM-line cost entry (read-only structure, editable cost + currency per line).
   - Last-cost reference per line with stale flag (`> 10 days`).
   - Landed cost (`Percentage` or `Fixed AED`).
   - FOH per kg.
   - Hybrid auto-save: debounce ~2s + on input blur + on screen exit.
   - Submit (validation-gated).
5. Notification bell + push-to-screen reuse (already wired via `SignalRProvider`); accountant lands on `(accountant)/index.tsx` from a notification.

### Phase 2 — Polish (same spec, separate plan/PR)

1. **All-list** — search + 6 status chips (parity with V2.0 sales/MD all-lists).
2. **Customer change mid-flow** — same backend rules as web (`CostingInProgress` allowed).
3. **Customer change history** modal screen.
4. **Notification deep-link** upgrade — currently routes to home; upgrade to land on the specific `item/[reqId]/[itemId]` screen.

### Non-goals

- BomCreator role on mobile (separate future plan; V2.3+).
- Costing analytics / dashboards.
- Offline mode (no service worker, no local DB cache).
- Bulk submit (per-item only, matching web).
- Editing of BOM structure (BOM lines are read-only on this screen — Accountant only fills costs).

## 3. Scope

### In scope

- New mobile route group `bom-mobile/app/(accountant)/`.
- Two list screens (pending in P1, all in P2), one requisition-detail screen, one costing-form screen, one customer-history screen (P2).
- Six new mobile components (cost-line card, landed section, FOH section, save-status badge, stale-cost badge, currency picker sheet).
- New mobile API hook file `src/api/costing.ts` (1 query + 3 mutations).
- New TypeScript types in `src/types/api.ts` mirroring backend DTOs.
- One new backend integration test guarding the `Submit last item → MdReview` auto-transition.
- Manual on-device smoke checklist (no `jest-expo` setup yet).

### Out of scope

- Backend route or DTO changes (the existing `CostingController` endpoints are reused as-is).
- Web `CostingEntryPage` changes.
- Offline persistence layer.
- Component snapshot tests (no `jest-expo` yet — defer with the rest of mobile testing).
- EAS build / deploy (still deferred per V2.0 close note; Plan 3b).

## 4. User flow

### Phase 1

1. Sara logs in (Accountant role) → root `index.tsx` redirect → `(accountant)/index.tsx`.
2. Pending list shows requisitions in `CostingPending` or `CostingInProgress`, scoped to her branch. Each row: ref no · customer · item count · created date · status pill.
3. Sara taps a requisition → `(accountant)/[id].tsx` (req detail).
4. Detail screen header: `ScreenHeader` with ref no + customer; status pill on the right. Body: stacked `ItemCardShell` rows, one per requisition item, each with item description, expected qty, and a per-item cost-status badge (NotStarted / InProgress / Submitted). On first render, the first `NotStarted` item is auto-started (`POST /costing/{reqId}/items/{itemId}/start`) and a refetch updates its badge.
5. Sara taps an item card → `(accountant)/item/[reqId]/[itemId].tsx` (the costing form).
6. Costing form (long single scroll, layout option A from brainstorm):
   - **Sticky header:** item description, `100 kg` label, status pill, save-status badge.
   - **BOM lines section:** one `CostLineCard` per BOM line, ordered as returned by backend (insertion order; matches web).
   - **Landed Cost section:** segmented `Percentage / Fixed AED` toggle + value input.
   - **FOH section:** AED amount input.
   - **Sticky bottom:** `Submit` button (disabled until form valid).
7. As Sara edits, the hybrid auto-save fires: ~2s debounce on each change, plus on every input blur, plus on screen-exit (`useEffect` cleanup before unmount). The save-status badge transitions `idle → saving → saved → idle` (or `→ error` on failure).
8. Sara taps Submit → confirmation dialog → `POST submit` → server may return field errors (inline) or success.
9. On success, screen pops back to `(accountant)/[id].tsx`; the just-submitted item card now shows `Submitted` badge. If it was the last item, the req status flips to `MdReview` server-side; the detail screen displays a "All items submitted" banner and auto-pops back to the pending list after 2 seconds.

### Phase 2

10. Sara opens `(accountant)/all.tsx` (via tab/link from home) → debounced search + 6 status chips (BomPending, BomInProgress, CostingPending, CostingInProgress, MdReview, Approved/Rejected). Same chip-to-statuses contract as `StatusChipRow` used by sales/MD V2.0.
11. From the req-detail screen, a "Change customer" action opens a modal: select new customer + reason → `PATCH /api/requisitions/{id}/customer`.
12. From the same screen, "View change history" → `(accountant)/customer-history/[id].tsx` reuses the existing `useCustomerChangeHistory` hook with read-only rows.
13. A push notification for `CostingPending` deep-links to `(accountant)/[id]` (or to `item/[reqId]/[itemId]` for `CostingInProgress` items the accountant has already started).

## 5. Data sources — existing backend (no changes)

All endpoints below already exist, are role/branch isolated, and are used by web today.

| Endpoint | Auth | Purpose | Already used on mobile? |
|---|---|---|---|
| `GET /api/requisitions?status=CostingPending&status=CostingInProgress` | Authenticated, branch-isolated | Pending list (P1) and all-list (P2 with chips) | Yes — `useRequisitions` |
| `GET /api/requisitions/{id}` | Authenticated, branch-isolated | Detail header + items | Yes — `useRequisitionDetail` |
| `GET /api/costing/{requisitionId}` | Accountant only | Per-item cost review (BOM lines + draft if any) | **No — new hook** |
| `POST /api/costing/{requisitionId}/items/{requisitionItemId}/start` | Accountant only | Transition `NotStarted → InProgress` | **No — new hook** |
| `PUT /api/costing/{requisitionId}/items/{requisitionItemId}/draft` | Accountant only | Save costing draft (lines + landed + FOH) | **No — new hook** |
| `POST /api/costing/{requisitionId}/items/{requisitionItemId}/submit` | Accountant only | Transition `InProgress → Submitted`; auto-flip req → `MdReview` if last | **No — new hook** |
| `GET /api/exchange-rates` | Authenticated | Currency list for picker | Yes — `useActiveExchangeRates` |
| `PATCH /api/requisitions/{id}/customer` *(P2)* | Sales/Accountant per backend rules | Customer change mid-flow | No — new mutation |
| `GET /api/requisitions/{id}/customer-history` *(P2)* | Authenticated | Read change history | No — new hook |

Verification: `CostingController` and `RequisitionsController.PatchCustomer` carry no `ApiExplorerSettings(IgnoreApi = true)` and rely on standard `[Authorize(Roles = "...")]` attributes. If the integration test reveals an unexpected status gate, fix in place.

### Data flow for the costing form screen

```
useCostingReview(reqId)            → items[], per-item bomLines[] + draft (if any)
useActiveExchangeRates()           → currency picker options
local useReducer (per item screen) → working copy of lines + landed + FOH + saveStatus
useStartCostingItem (mutation)     → NotStarted → InProgress (auto on detail mount)
useSaveCostingItemDraft (mutation) → fires on debounce / blur / screen exit
useSubmitCostingItem (mutation)    → InProgress → Submitted; refetch parent on success
```

## 6. Architecture & file layout

### New files

```
bom-mobile/
├── app/
│   └── (accountant)/
│       ├── _layout.tsx                  # NEW: Stack + role guard
│       ├── index.tsx                    # NEW: pending list (P1)
│       ├── all.tsx                      # NEW (P2): search + chips
│       ├── [id].tsx                     # NEW: req detail with item cards
│       ├── item/
│       │   └── [reqId]/
│       │       └── [itemId].tsx         # NEW: costing form (long scroll, layout A)
│       └── customer-history/
│           └── [id].tsx                 # NEW (P2): change history view
├── src/
│   ├── api/
│   │   └── costing.ts                   # NEW: 1 query + 3 mutations
│   ├── components/
│   │   ├── CostLineCard.tsx             # NEW: layout A — compact card per BOM line
│   │   ├── LandedCostSection.tsx        # NEW: Pct/AED segmented + input
│   │   ├── FohSection.tsx               # NEW: AED input
│   │   ├── SaveStatusBadge.tsx          # NEW: idle/saving/saved/error pill
│   │   ├── StaleCostBadge.tsx           # NEW: ⚠ X days inline tag
│   │   └── CurrencyPickerSheet.tsx      # NEW: thin wrapper around SearchablePicker
│   ├── utils/
│   │   └── apiError.ts                  # NEW: port of bom-web/src/lib/apiError.ts
│   │                                    #      (extractFieldErrors + extractApiError)
│   └── types/
│       └── api.ts                       # EXTEND: CostingReview, CostingItem,
│                                        #         CostingDraft, LandedCostType,
│                                        #         CostingBomLine, CostingLineDraft
```

### Modified files

```
bom-mobile/
├── app/
│   └── index.tsx                        # MODIFIED: add Accountant role redirect
└── src/
    └── components/                       # No modifications — Phase 1 only consumes
                                          # existing reusable components
```

### Type additions (`src/types/api.ts`)

Mirror backend DTOs one-to-one. Names taken from `Features/Costing/CostingDtos.cs`:

```ts
export type LandedCostType = "Percentage" | "Fixed";

export type LastCostReference = {
  costPerKg: number;
  currencyCode: string;
  updatedAt: string;
};

export type CostingBomLine = {
  bomLineId: number;
  processId: number;
  processName: string;
  rawMaterialItemId: number;
  rawMaterialDescription: string;
  qtyPerKg: number;
  wastagePct: number;
  lastCost: LastCostReference | null;
};

export type CostingLineDraft = {
  bomLineId: number;
  costPerKg: number;
  currencyCode: string;
};

export type CostingDraft = {
  lines: CostingLineDraft[];
  landedCostType: LandedCostType;
  landedCostValue: number;
  fohAmount: number;
};

export type CostingItemResponse = {
  requisitionItemId: number;
  itemDescription: string;
  expectedQty: number;
  costStatus: "NotStarted" | "InProgress" | "Submitted";
  bomLines: CostingBomLine[];
  draft: CostingDraft | null;
};

export type CostingReviewResponse = {
  requisitionId: number;
  refNo: string;
  status: string;
  currencyCode: string;
  items: CostingItemResponse[];
};
```

### Component contracts

- **`CostLineCard`** — props: `{ line: CostingBomLine; quoteCurrency: string; value: { costPerKg: number; currencyCode: string }; onChange: (v: { costPerKg: number; currencyCode: string }) => void; onBlur: () => void }`. Renders layout A: process header, RM description, qty/wastage subline, cost input + currency picker on its own row, optional `StaleCostBadge` below.
- **`LandedCostSection`** — props: `{ type: LandedCostType; value: number; onChange: (v: { type: LandedCostType; value: number }) => void; onBlur: () => void }`. Segmented toggle (Pct / Fixed AED) + numeric input.
- **`FohSection`** — props: `{ amount: number; onChange: (v: number) => void; onBlur: () => void }`. Single AED amount input.
- **`SaveStatusBadge`** — props: `{ status: "idle" | "saving" | "saved" | "error"; onRetry?: () => void }`. Top-right pill in screen header. Tap on `error` calls `onRetry`.
- **`StaleCostBadge`** — props: `{ daysAgo: number; costPerKg: number; currencyCode: string }`. Renders inline only when `daysAgo > 10`.
- **`CurrencyPickerSheet`** — props: `{ value: string; options: string[]; onChange: (code: string) => void }`. Thin wrapper around the existing `SearchablePicker`; opens as a bottom sheet.

All styling inline (consistent with project convention — NativeWind avoided on RN 0.81.5 due to known Pressable-render issues; preserved across V1, V2.0, V2.2).

## 7. Workflow / state machine details

### Auto-start on detail entry (web parity)

```
On (accountant)/[id].tsx mount:
  if requisition.status === "CostingPending"
     && first item's costStatus === "NotStarted"
     && !hasAutoStarted ref:
        set hasAutoStarted = true
        useStartCostingItem.mutate({ reqId, requisitionItemId: first.id })
        on success → refetch req detail
```

This mirrors `CostingEntryPage:121-140` exactly. Uses a `useRef` guard so React StrictMode doesn't double-fire.

### Hybrid auto-save (per item screen)

A `saveStatus` reducer with these transitions:

```
idle ──user edit──→ debouncing (timer 2000ms) ──timer fire──→ saving
                                                              │
debouncing ──input blur──→ saving                             │
debouncing ──screen unmount──→ saving (await before unmount)  │
                                                              │
saving ──ok──→ saved ──5000ms──→ idle                         │
saving ──err──→ error                                         │
error ──tap badge / next edit──→ debouncing                   │
```

- Debounce timer cancelled on every new edit; restarted with the new payload.
- On blur of any input, the debounce timer is cleared and a save fires immediately.
- Screen-exit save: in the unmount `useEffect` cleanup, if `saveStatus === "debouncing"`, fire the save synchronously (best-effort; React Native does not guarantee completion).
- Save retries are explicit (user taps the error badge); no automatic retry to avoid thrash on flaky connections.

### Submit transition

```
On Submit tap → confirm dialog (ConfirmDialog component)
  → useSubmitCostingItem.mutate
    → on success:
        - refetch parent (req detail) so the just-submitted item card updates
        - router.back() to (accountant)/[id]
        - if the new requisition.status === "MdReview" (last item submitted):
            show toast "All items costed. Sent to MD review."
            after 2000ms: router.replace("/(accountant)/")
    → on field-error response:
        - extract errors via existing `extractFieldErrors` helper
        - render inline next to the offending input
        - submit button stays disabled until any field changes
    → on 409 (race / stale BOM):
        - read `reason` from response
        - "Item already submitted by X" → toast + refetch + back
        - "BOM changed" → modal to drop draft + refetch
```

## 8. Errors / edge cases

| Case | Handling |
|---|---|
| Network drop during save | `SaveStatusBadge` shows `error`. Last good payload retained in local state. Tap badge → retry the same payload. |
| Submit fails validation server-side | Inline errors via existing `extractFieldErrors` + visual highlight on offending input. Submit button disabled until any edit occurs. |
| Item already submitted by another accountant (race) | `409 Conflict` → toast "Already submitted by {name} at {time}" → auto-refetch detail → pop back. |
| Requisition status changed by MD mid-edit (sent back to BOM) | On screen `useFocusEffect` and on save failure, refetch. If status no longer `CostingPending`/`CostingInProgress` → read-only banner + disable inputs. |
| BOM lines changed after costing draft saved | Server returns `409 { reason: "BomChanged" }` on save or submit → modal: "BOM updated; reload?" → confirm = drop draft, refetch. |
| App backgrounded mid-edit | On focus (`useFocusEffect`), refetch the costing review. Server-side draft is the source of truth. If the local working copy diverges from the server payload, the server wins (last-write-wins, matches web). |
| Cost line count mismatch (BOM line removed server-side) | Same as "BOM changed" above. |
| User navigates back without saving | The screen-exit save fires synchronously. If it errors, the badge state is preserved on next visit (because the badge state lives in the form, which is unmounted — so on next visit, server is source of truth and the unsaved edit is lost). This is consistent with web's "best effort" semantics. |
| Stale BOM line cost reference (> 10 days old) | `StaleCostBadge` rendered inline; non-blocking. Sara may use it as-is or override. |
| Empty cost (left at 0) | Allowed at draft time. Submit-side validation enforces `costPerKg > 0` per line — server returns field error if violated. |

## 9. Testing

### Backend (`BomPriceApproval.Tests`)

One new integration test:

- `Costing_Submit_LastItem_TransitionsRequisitionToMdReview` — guards the auto-transition on last-item submit, which the mobile flow depends on for its "all items submitted" UX.

Existing tests already cover start / draft / submit per-item happy paths.

### Mobile (manual smoke per V2.2 precedent)

Run on Expo Go with the LAN backend setup documented in memory `project_mobile_plan1_status.md`. Checklist:

| # | Case | Expected |
|---|---|---|
| M1 | Pending list shows only `CostingPending` + `CostingInProgress` for current branch | ✓ list filtered |
| M2 | Tap req → detail screen → first NotStarted item auto-starts (badge flips InProgress) | ✓ no extra tap |
| M3 | Drill into item → form renders BOM lines (layout A) + landed + FOH | ✓ layout matches mockup |
| M4 | Edit cost on a line → wait 2s → SaveStatusBadge transitions saving → saved | debounce trigger |
| M5 | Edit + immediately blur input → save fires immediately | blur trigger |
| M6 | Edit + immediately back-tap → save fires before unmount | screen-exit trigger |
| M7 | Submit → confirmation → back to detail → item badge = Submitted | success path |
| M8 | Submit with 0 cost on a line → server returns field error → inline shown | validation path |
| M9 | Stale BOM-line lastCost (manually tweaked DB) → ⚠ badge shows | stale UI |
| M10 | Submit last item → req moves to MdReview → toast + auto-pop to pending list | last-item transition |

### Component / unit tests

Deferred until `jest-expo` is introduced. Same posture as V2.2 (no mobile component test infrastructure exists; spec-level testability via manual smoke is the contract).

## 10. Implementation ordering

1. Backend integration test for last-item transition (red → green confirms current behaviour).
2. Port `extractFieldErrors` / `extractApiError` from `bom-web/src/lib/apiError.ts` to `bom-mobile/src/utils/apiError.ts`.
3. Mobile types additions (`src/types/api.ts`).
4. Mobile API hooks (`src/api/costing.ts`).
5. Leaf components in dependency order: `StaleCostBadge`, `CurrencyPickerSheet`, `SaveStatusBadge`, `CostLineCard`, `LandedCostSection`, `FohSection`.
6. Screens: `(accountant)/_layout`, `(accountant)/index`, `(accountant)/[id]`, `(accountant)/item/[reqId]/[itemId]`.
7. Wire root `app/index.tsx` redirect for Accountant role.
8. Manual smoke verification per §9.
9. Phase 2 work begins in a new plan: `(accountant)/all.tsx`, customer-change modal + history screen, notification deep-link upgrade.
10. Commit per logical unit; no squash. Conventional Commits format per `CLAUDE.md`.

## 11. Phase 1 cut-line (explicit)

✅ **In Phase 1 (this implementation plan):**
- All Phase 1 routes, components, hooks, and types from §6.
- Hybrid auto-save full implementation per §7.
- 1 backend integration test from §9.
- Manual smoke checklist M1–M10.

❌ **Deferred to Phase 2 (separate plan, same spec):**
- `(accountant)/all.tsx` (search + chips, parity with V2.0 sales/MD).
- Customer change modal + `(accountant)/customer-history/[id].tsx` screen.
- Notification deep-link upgrade (currently lands on home; upgrade to land on the specific item screen).
- Backend customer-change permission test gaps already noted in `memory/project_mobile_plan1_status.md`.

## 12. Open questions

None at spec time. Any ambiguity surfaced during the plan phase will loop back here.

## 13. References

- Project memory: `memory/project_mobile_plan1_status.md`, `memory/project_mobile_v22_bom_drilldown.md`
- Prior V2.2 spec (BOM drill-down — pattern reference): `docs/superpowers/specs/2026-04-25-mobile-v22-md-bom-drilldown-design.md`
- Web counterpart: `bom-web/src/features/costing/CostingEntryPage.tsx`
- Backend: `BomPriceApproval.API/Features/Costing/CostingController.cs`, `BomPriceApproval.API/Features/Costing/CostingDtos.cs`
- Mobile patterns: `bom-mobile/app/(md)/historical/[id].tsx`, `bom-mobile/app/(md)/item/[reqId]/[itemId].tsx`, `bom-mobile/src/components/SearchablePicker.tsx`
- Brainstorm visual companion mockups: `.superpowers/brainstorm/*/content/cost-line-layout.html`, `screen-structure.html`
