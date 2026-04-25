# Mobile V2.1 Phase 2 — Accountant Polish + Customer Change Parity

**Date:** 2026-04-25
**Scope:** Accountant mobile polish (dashboard, all-list, notification deep-link fix) + Feature X (customer change mobile parity for Accountant/Admin)
**Parent work:** [2026-04-25 Mobile V2.1 (Accountant) — Phase 1 shipped](2026-04-25-mobile-v21-accountant-design.md)
**Status:** Spec, awaiting plan

---

## 1. Problem

V2.1 Phase 1 shipped the Accountant pending list + per-item costing form. On-device smoke passed. Four gaps remain before Accountant mobile reaches feature-parity with Sales/MD mobile and the desktop web:

1. **No landing/dashboard.** Accountant logs in and lands directly on the pending list. There is no at-a-glance view of workload, momentum, or downstream bottlenecks (Sales has the new-button FAB; MD has a hero card landing — Accountant has nothing).
2. **No all-list.** The list is hard-coded to `CostingPending + CostingInProgress`. Accountant cannot see her own historical work, items currently in MD review, or already-approved/rejected items. Web has all six status chips; mobile does not.
3. **Notification deep-link is broken for Accountant.** `pathForNotification` only handles `ManagingDirector` and `SalesPerson`. When Sara taps a notification, nothing happens.
4. **No mobile customer-change.** The desktop web supports customer swap during `CostingPending`/`CostingInProgress` (Accountant/Admin only) including a change-history audit. Mobile cannot perform or view this — Sara must switch to desktop.

This spec covers all four gaps in a single Phase 2 release. **Feature Y (customer profile modal showing past orders/insights) is explicitly deferred** to a later spec — it has not been validated as a real user need.

---

## 2. Goals

1. **Dashboard** at `(accountant)/index.tsx` mirroring MD's pattern — Hero card for primary action + 3 stacked KPI rows + notifications + user card. All 4 KPIs tappable to a filtered list view.
2. **All-list** at `(accountant)/list.tsx` with `StatusChipRow` (default chip = "Costing"). Smart `(accountant)/[id]` route renders the costing form for `CostingPending`/`CostingInProgress` and a read-only historical view for any other status.
3. **Notification deep-link fix** — `pathForNotification` adds the `Accountant` role.
4. **Feature X — Customer change mobile parity** — Accountant or Admin can change customer on a requisition in `CostingPending`/`CostingInProgress` and view the change-history audit log, using already-shipped backend endpoints.

### Non-goals

- Feature Y (customer profile modal showing past requisitions and stats for a customer).
- BomCreator role on mobile (no `(bom)/` route group exists yet).
- Item-level notification deep-linking (DB schema change deferred to V2.3+).
- Charts, activity feed, or any analytics in the dashboard.
- Customer swap outside `CostingPending`/`CostingInProgress`.
- Web parity for any other web-only feature.

---

## 3. Scope

### In scope

- **Backend:**
  - 1 new endpoint: `GET /api/stats/accountant-dashboard` (KPI counts).
  - 1 list extension: optional `?from=YYYY-MM-DD&to=YYYY-MM-DD` on `GET /api/requisitions`.
  - Reuse already-shipped: `PATCH /api/requisitions/{id}/customer`, `GET /api/requisitions/{id}/customer-history`.

- **Mobile:**
  - 1 route replacement: `(accountant)/index.tsx` becomes the dashboard (was: pending list).
  - 1 new route: `(accountant)/list.tsx` (all-list with chips).
  - 1 route modified: `(accountant)/[id].tsx` becomes a smart route (form vs. read-only).
  - 1 component extraction: `(md)/historical/[id].tsx` body → shared `HistoricalRequisitionScreen.tsx` (mounted by both MD and Accountant).
  - 2 new components: `CustomerSwapSheet.tsx`, `CustomerChangeHistorySheet.tsx`.
  - 1 hook addition: `useAccountantDashboardStats()`.
  - 2 hook additions for Feature X: `useChangeCustomer()`, `useCustomerChangeHistory()`.
  - 1 deep-link fix in `app/notifications.tsx`.

### Out of scope (see Non-goals)

---

## 4. Architecture

### 4.1 Backend additions

#### 4.1.1 `GET /api/stats/accountant-dashboard`

```http
GET /api/stats/accountant-dashboard
Authorization: Bearer <token>  // role: Accountant or Admin
```

**Response:**

```json
{
  "pendingCosting": 5,
  "inProgress": 2,
  "submittedThisMonth": 23,
  "awaitingMd": 7
}
```

**Computation:**

- `pendingCosting` = count of `QuotationRequest` with `Status = CostingPending`
- `inProgress` = count where `Status = CostingInProgress`
- `submittedThisMonth` = count where `Status = MdReview` AND `SubmittedAt >= start-of-current-month-utc`
  - Note: `SubmittedAt` is the timestamp when the requisition transitioned into `MdReview`. If no such field exists, use `UpdatedAt` and document the proxy choice in the implementation plan.
- `awaitingMd` = count where `Status = MdReview`

**Branch isolation:** Accountant has `null` BranchId per `CLAUDE.md` (sees all branches), so no branch filter applied.

**Filing:** Extend `Features/Stats/StatsController.cs`. If a `[Authorize(Roles="...")]` style is in use, restrict to `Accountant,Admin`.

#### 4.1.2 List endpoint extension

`GET /api/requisitions` already supports `?status=` (multi-value) and `?search=`. Add:

- `?from=YYYY-MM-DD` — filter `SubmittedAt >= from` (UTC)
- `?to=YYYY-MM-DD` — filter `SubmittedAt < to + 1 day` (UTC, exclusive)

Both optional, backwards-compatible. If `SubmittedAt` is unavailable, use `UpdatedAt` as the proxy and document.

**Filing:** Extend the existing requisitions list query in `Features/Requisitions/RequisitionsController.cs`.

#### 4.1.3 Reused endpoints (Feature X)

No backend work for Feature X. Both endpoints already shipped:

- `PATCH /api/requisitions/{id}/customer` (RequisitionsController.cs:441) — auth: Accountant or Admin; status guard: `CostingPending` or `CostingInProgress`; branch-scoped for Accountant; transactional; writes to `CustomerChangeHistories`; notifies Salesperson + all MDs.
- `GET /api/requisitions/{id}/customer-history` (RequisitionsController.cs:516) — read-only audit log of change entries.

### 4.2 Frontend route map

```
bom-mobile/app/
  (accountant)/
    _layout.tsx                       (unchanged)
    index.tsx                         REPLACE  → Dashboard (was: pending list)
    list.tsx                          NEW      → All-list with StatusChipRow
    [id].tsx                          MODIFY   → Smart route (form OR <HistoricalRequisitionScreen>)
    item/[reqId]/[itemId].tsx         (unchanged)
  (md)/
    historical/[id].tsx               EXTRACT body → shared HistoricalRequisitionScreen.tsx
  notifications.tsx                   MODIFY   → pathForNotification adds Accountant
```

### 4.3 New / modified components

| Component | Path | Purpose |
|---|---|---|
| `HistoricalRequisitionScreen.tsx` | `src/components/` | Extracted from `(md)/historical/[id].tsx`. Mounted by both MD and Accountant routes. |
| `CustomerSwapSheet.tsx` | `src/components/` | Bottom-sheet modal. Visible from `(accountant)/[id].tsx` (costing form) when role = Accountant + status ∈ {CostingPending, CostingInProgress}. Wraps `SearchablePicker` + optional `Reason` text input. Calls `useChangeCustomer()`. |
| `CustomerChangeHistorySheet.tsx` | `src/components/` | Bottom-sheet read-only audit log. Triggered by "Customer changed (N)" badge in costing form and in MD review/historical screens. Renders `CustomerChangeHistoryEntry[]` with old → new, by whom, when, reason. |

### 4.4 API hooks

```ts
// src/api/stats.ts
export function useAccountantDashboardStats() { /* useQuery → /api/stats/accountant-dashboard */ }

// src/api/requisitions.ts
export function useChangeCustomer(requisitionId: number) { /* useMutation → PATCH /api/requisitions/:id/customer */ }
export function useCustomerChangeHistory(requisitionId: number, enabled = true) { /* useQuery → GET /api/requisitions/:id/customer-history */ }
```

The list query (`useInfiniteQuery` in `list.tsx`) accepts `from` and `to` from URL params and includes them in the request URL.

---

## 5. Detailed designs

### 5.1 Dashboard

**Route:** `(accountant)/index.tsx`

> **Post-smoke deviation (2026-04-26, commit `14cb05e`):** the originally-spec'd 4 KPIs were reduced to 3 at the user's request — "Pending Costing" + "In Progress" merged into a single hero "COSTING TO COMPLETE" because the Accountant treats both as the same workflow goal ("kaam jo complete karna hai"). Backend stats endpoint still returns all 4 fields (`pendingCosting`, `inProgress`, `submittedThisMonth`, `awaitingMd`); mobile sums the first two on the client. Layout + tap table below reflect the as-shipped behavior.

**Layout:**

```
┌──────────────────────────────────────┐
│ ACCOUNTANT          🔔  Log out      │  ScreenHeader
│ Good morning, Sara 👋                │
├──────────────────────────────────────┤
│ ╔══════════════════════════════════╗ │
│ ║ COSTING TO COMPLETE              ║ │  Hero card (#1e40af blue)
│ ║ 7                  to review     ║ │  → /(accountant)/list?chip=Costing
│ ║ Tap to open list →               ║ │  count = pendingCosting + inProgress
│ ╚══════════════════════════════════╝ │
│ ┌──────────────────────────────────┐ │
│ │ MD-BOUND THIS MONTH         23   │ │  → /(accountant)/list?chip=MD%20review&from=YYYY-MM-01
│ └──────────────────────────────────┘ │
│ ┌──────────────────────────────────┐ │
│ │ AWAITING MD                  7   │ │  → /(accountant)/list?chip=MD%20review
│ └──────────────────────────────────┘ │
│                                      │
│ ┌──────────────────────────────────┐ │
│ │ NOTIFICATIONS    3 unread    🔔  │ │  → /notifications
│ └──────────────────────────────────┘ │
│ ┌──────────────────────────────────┐ │
│ │ SIGNED IN AS                     │ │
│ │ Sara Khan                        │ │
│ │ Accountant                       │ │
│ └──────────────────────────────────┘ │
└──────────────────────────────────────┘
```

**KPI tap behavior:**

| KPI | Tap target |
|---|---|
| Costing to complete | `(accountant)/list?chip=Costing` |
| MD-bound this month | `(accountant)/list?chip=MD%20review&from=<start-of-current-month>` |
| Awaiting MD | `(accountant)/list?chip=MD%20review` |

`onlyStatus` is a list-screen query param that overrides the chip's status set with a single status. After the dashboard merge above, no in-app tap target spawns `?onlyStatus=`; the parameter is reserved for future deep-link use cases (e.g. notification → exact-status filter).

**Loading:** `<Skeleton>` for each count (matches existing MD pattern in `(md)/index.tsx`).

**Animation:** Same MotiView spring stagger as MD dashboard (delays 100/180/260/...).

### 5.2 All-list with chips

**Route:** `(accountant)/list.tsx` (NEW; replaces former pending-only behavior of `index.tsx`)

**Layout:** Mirror `(md)/pending.tsx` and `(sales)/index.tsx` exactly:

- `ScreenHeader` (label "ACCOUNTANT", title "Requisitions", count, NotificationBell + Log out)
- Search input (debounced 300ms, same pattern)
- `StatusChipRow` (existing component) — default chip = `"Costing"`
- `FlatList` with `RequisitionCard` + MotiView stagger + pull-to-refresh + paginated infinite scroll

**URL params:**

- `?chip=All|BOM|Costing|MD review|Approved|Rejected` — sets the chip group (overrides default)
- `?onlyStatus=BomPending|BomInProgress|CostingPending|CostingInProgress|MdReview|Approved|Rejected` — overrides chip's status array with a single status (set by dashboard KPI taps; clears when the user picks a different chip)
- `?from=YYYY-MM-DD&to=YYYY-MM-DD` — date filter (passed through to backend)
- `?search=...` — pre-fills search input (optional)

**Status filter resolution:**

```
if onlyStatus is present → statuses = [onlyStatus]
else → statuses = CHIP_TO_STATUSES[activeChip]
```

**Detail tap routing:** Always `/(accountant)/${item.id}` (routing logic lives inside the smart `[id]` route, not here).

### 5.3 Smart `[id]` route

**Route:** `(accountant)/[id].tsx` (modified)

```ts
// Pseudocode
const status = requisition.status;
const isCostingActive = status === "CostingPending" || status === "CostingInProgress";

if (isCostingActive) {
  return <CostingForm requisitionId={id} />;     // existing behavior
}
return <HistoricalRequisitionScreen requisitionId={id} />;  // shared with MD
```

**`HistoricalRequisitionScreen` extraction:**

The body of `(md)/historical/[id].tsx` becomes `src/components/HistoricalRequisitionScreen.tsx`, exporting a single component that takes `{ requisitionId }`. Both `(md)/historical/[id].tsx` and `(accountant)/[id].tsx` (when status is non-active) mount this component.

**Role-based field visibility** in the historical screen continues to follow the existing rule from memory `feedback_role_visibility.md`:

- Salesperson: hide margin and cost lines
- BomCreator: hide sales price
- Accountant + MD: full visibility

The shared component reads role from `useAuth()` and applies the same conditionals already present in MD's screen.

### 5.4 Notification deep-link fix

**File:** `app/notifications.tsx`, function `pathForNotification`.

**Current:**

```ts
function pathForNotification(n, role) {
  if (n.referenceType !== "QuotationRequest") return null;
  if (role === "ManagingDirector") return `/(md)/${n.referenceId}`;
  if (role === "SalesPerson")      return `/(sales)/${n.referenceId}`;
  return null;
}
```

**After:**

```ts
function pathForNotification(n, role) {
  if (n.referenceType !== "QuotationRequest") return null;
  if (role === "ManagingDirector") return `/(md)/${n.referenceId}`;
  if (role === "SalesPerson")      return `/(sales)/${n.referenceId}`;
  if (role === "Accountant")       return `/(accountant)/${n.referenceId}`;  // new
  // BomCreator: no (bom) route group exists yet — defer
  return null;
}
```

The Accountant route hits the smart `[id]` route, which auto-routes to form or historical based on status. No additional logic needed.

### 5.5 Feature X — Customer change mobile parity

**Trigger from costing form:** In `(accountant)/[id].tsx` (when costing form is rendered, i.e. status ∈ {CostingPending, CostingInProgress}):

- Render a "Change customer" button near the customer name/header.
- On tap → open `CustomerSwapSheet`.
- After successful swap → invalidate the requisition query and show a success toast.

**Trigger from change-history badge:** In both `(accountant)/[id].tsx` (active form) AND `<HistoricalRequisitionScreen>` (read-only path used by Accountant non-active and MD historical):

- If `useCustomerChangeHistory(id)` returns ≥ 1 entry, show a badge: `Customer changed (N)`.
- Tap the badge → open `CustomerChangeHistorySheet`.

**`CustomerSwapSheet.tsx` props:**

```ts
{
  requisitionId: number;
  currentCustomerId: number;
  currentCustomerName: string;
  open: boolean;
  onClose: () => void;
}
```

Renders:
- Title: "Change customer"
- Read-only: current customer
- `SearchablePicker` for new customer (excludes the current customer)
- Optional `<TextInput>` for `Reason` (placeholder: "Reason for change (optional)")
- "Cancel" / "Save" buttons (Save disabled until a new customer is picked)
- On save: call `useChangeCustomer(requisitionId)` mutation with `{ customerId, reason }`, then close + toast on success.

**`CustomerChangeHistorySheet.tsx` props:**

```ts
{
  requisitionId: number;
  open: boolean;
  onClose: () => void;
}
```

Renders a vertical list of `CustomerChangeHistoryEntry`:

- "Old customer → New customer"
- Changed by: `<userName>`, `<formatShortDate(changedAt)>`
- Reason (if any)

Read-only. Same role-based visibility rules apply: any role can view audit; the entries themselves don't expose price data.

---

## 6. Authorization matrix

| Action | Mobile permission check | Backend enforces |
|---|---|---|
| Dashboard view | role = Accountant or Admin | `[Authorize(Roles="Accountant,Admin")]` on stats endpoint |
| All-list view | role = Accountant or Admin | existing list endpoint role/branch checks |
| Smart `[id]` form path | role = Accountant or Admin AND status ∈ {CostingPending, CostingInProgress} | existing costing endpoints |
| Smart `[id]` historical path | role = Accountant or Admin (any status) | existing read endpoints |
| Customer swap (Feature X) | role ∈ {Accountant, Admin} AND status ∈ {CostingPending, CostingInProgress} | `PATCH /api/requisitions/:id/customer` already enforces |
| Customer change history view | any logged-in role | `GET /api/requisitions/:id/customer-history` enforces `CanAccess` |
| Notification deep-link | any role | navigation only — destination route enforces its own auth |

UI gating is **defense in depth** — the source of truth is server enforcement.

---

## 7. Migration & risk

| Concern | Mitigation |
|---|---|
| `(accountant)/index.tsx` replacement | Old pending-list query logic is hoisted into `list.tsx` with `chip="Costing"` default. Net no functionality lost; user lands one tap deeper but with a richer landing. |
| Smart `[id]` route mixes two screens | Single status-based branch at the top of the component. Either branch delegates to a focused component (existing form or extracted historical screen). File stays small. |
| `HistoricalRequisitionScreen` extraction | Mechanical refactor. Cover with on-device smoke for both MD and Accountant entry points. |
| Date filter on requisitions list | Backwards-compatible (optional query params). Existing callers unaffected. |
| Customer swap is a write op | Backend already shipped + tested. Mobile is purely a new caller. |
| Customer swap sheet visibility leak | UI-side check on role + status before rendering the button. Server enforces regardless. |
| BomCreator notification deep-link still null | Out of scope. Document in plan; revisit when `(bom)` route group exists. |
| Notification dispatch on customer swap | Already implemented backend-side — Salesperson and all MDs get notified. Mobile relies on existing SignalR + bell. |

---

## 8. Testing strategy

### 8.1 Backend (xUnit + Testcontainers per CLAUDE.md)

**`StatsControllerTests.AccountantDashboard_*`:**

- Returns correct counts under seeded data covering all four buckets.
- "Submitted This Month" only counts items in `MdReview` with `SubmittedAt >= start-of-month`.
- 401 without auth, 403 for non-Accountant/non-Admin role.
- Empty database → all four counts = 0.

**`RequisitionsControllerTests.List_DateFilter_*`:**

- `?from=` filter excludes items submitted before that date.
- `?to=` filter excludes items submitted on/after that date.
- Both together → narrow window.
- Backwards compat: omitting both behaves identically to existing tests.

### 8.2 Mobile (manual on-device smoke — same pattern as Phase 1 and V2.2)

Smoke checklist (also reproduced in the implementation plan):

**Dashboard:** *(updated 2026-04-26 to match the post-merge 3-KPI layout in §5.1)*

1. Login as Accountant → land on dashboard. ScreenHeader shows correct greeting + role label. NotificationBell + Log out visible.
2. Hero card "COSTING TO COMPLETE" shows correct count = `pendingCosting + inProgress` from `useAccountantDashboardStats()`. Tap → list filtered to `chip=Costing` (combined Pending + InProgress).
3. Each of the 2 stacked KPIs (MD-bound this month, Awaiting MD) taps to the right list filter (verify URL params + visible items).
4. "MD-bound this month" → list shows MdReview items submitted on/after start of current month UTC only.
5. Pull-to-refresh on dashboard refetches all 4 backend stats fields + unread count.

**All-list:**

6. Default chip = "Costing", count matches Pending+InProgress total.
7. Each chip ("All", "BOM", "Costing", "MD review", "Approved", "Rejected") changes the result set correctly.
8. Search filters in combination with chip.
9. ~~`onlyStatus` URL param overrides the chip; switching chip clears it.~~ **N/A in current design (2026-04-26).** The dashboard merge (§5.1) eliminated the only in-app paths that emitted `?onlyStatus=`. The `onlyStatus` parser is retained in `list.tsx` for future deep-link callers (e.g. notifications), so this item only matters when a future feature reintroduces an `onlyStatus` URL.
10. Pagination: scrolling beyond 20 items triggers `fetchNextPage`.

**Smart `[id]`:**

11. Tap a CostingPending item → costing form renders (existing Phase 1 behavior).
12. Tap an Approved/Rejected/MdReview/BomPending/BomInProgress item → `<HistoricalRequisitionScreen>` renders (read-only).
13. From MD app, `/(md)/historical/<id>` still works (regression check).

**Notification deep-link:**

14. Receive a notification as Accountant → tap → land on the correct `(accountant)/<id>` (smart route).
15. ManagingDirector and SalesPerson deep-links continue to work (regression).

**Feature X — Customer change:**

16. Open a CostingPending requisition as Accountant → "Change customer" button visible.
17. Tap → `CustomerSwapSheet` opens, current customer shown read-only, `SearchablePicker` filters customers as typed.
18. Pick new customer + add reason → Save → toast success → form refetches with new customer name. Notification arrives at Salesperson + MDs.
19. Open same requisition as Salesperson → no "Change customer" button (UI gate works).
20. Open an Approved requisition as Accountant → no "Change customer" button (status gate works).
21. "Customer changed (1)" badge appears on the requisition. Tap → `CustomerChangeHistorySheet` shows the entry.
22. From MD app, both the badge and history sheet appear correctly on `(md)/[id]` and `(md)/historical/[id]`.

---

## 9. Implementation order (suggested for the plan)

1. Backend: add `GET /api/stats/accountant-dashboard` + tests.
2. Backend: extend list with `?from=&to=` + tests.
3. Mobile: extract `HistoricalRequisitionScreen` from MD's historical screen. Smoke MD path.
4. Mobile: smart `(accountant)/[id]` route. Smoke both branches.
5. Mobile: `(accountant)/list.tsx` — all-list with chips + URL params (`onlyStatus`, `from`, `to`).
6. Mobile: `(accountant)/index.tsx` replaced with dashboard. Smoke KPI taps.
7. Mobile: notification deep-link patch. Regression-smoke MD/Sales.
8. Mobile: hooks for Feature X (`useChangeCustomer`, `useCustomerChangeHistory`).
9. Mobile: `CustomerSwapSheet` + integration into smart `[id]` form branch.
10. Mobile: `CustomerChangeHistorySheet` + badge wiring on Accountant `[id]` and MD `[id]`/historical.
11. Full smoke pass against all 22 checklist items above.

Each step should compile (`dotnet build` for backend, `tsc --noEmit` for mobile if available) before moving on.

---

## 10. Open questions

- **`SubmittedAt` field availability.** If `QuotationRequest` does not expose a `SubmittedAt` (or equivalent) timestamp marking the `MdReview` transition, the implementation plan must decide between (a) using `UpdatedAt` as a proxy with documented imprecision, or (b) introducing a column. Default: (a), revisit if data quality complaints surface.
- **`(md)` historical reuse vs duplication.** Spec assumes extraction of MD's historical body into a shared component. If the MD historical screen has MD-only side effects (e.g. analytics events, badges), the plan must confirm those are gated or hoisted before extraction.
- **Accountant smart-route behavior on `BomPending` items.** Today, an Accountant who taps a `BomPending` item via the all-list will land on the read-only historical view. Confirm this is acceptable UX (vs. e.g. an empty-state "Costing not started" screen). Default: read-only is fine; the historical screen already handles all statuses.
