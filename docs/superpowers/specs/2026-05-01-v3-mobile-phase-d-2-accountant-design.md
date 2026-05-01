# V3 Mobile — Phase D-2 (Accountant)

**Spec date:** 2026-05-01
**Branch (planned):** `feat/v3-mobile-phase-d-2-accountant` (off `master` @ `bae1a67`)
**Predecessor:** D-1 SP shipped 2026-04-30 night via PR #46 (master @ `bae1a67`)
**Parent decomposition:** `bom-mobile` rebuild against V3 backend, decomposed by role
**This phase:** Accountant screens only

---

## 1. Summary

`bom-mobile` D-1 rebuilt the SalesPerson surface against V3. The accountant route group `(accountant)/` still runs V2.3 logic — per-item start→draft→submit cycle, V2.3-shape dashboard endpoint, `(accountant)/item/` per-item drilldown folder. V3 bulk-upsert backend has no per-item state — all FGs are costed in one shot via `PUT /api/costing/{id}/cost-data`.

Phase D-2 rebuilds the accountant surface: V3 dashboard with refreshed KPI cards, 4-tab list page, V3 cost-input flow using D-1's drawer pattern (hybrid), and detail page with customer-swap retained / branch-swap dropped. V2.3 accountant code is purged (V3-only), matching D-1's discipline.

Phase D-3 (MD + signature pad) is a separate spec/plan/ship cycle in a future session.

---

## 2. Goal

Bring accountant mobile flows up to the V3 backend contract while reusing every D-1 pattern (drawer + sheet + create modal + status palette + V3 nested shape). Ship as an EAS OTA update over `mobile-shipped-vc1` if no native deps land in D-2.

**Secondary goals:**

- Delete V2.3 accountant code rather than maintain dual-shape branches
- Reuse D-1 components: `OwnedByBadge`, `StatusChipRow` (V3 palette already complete), `SearchablePicker` (currency picker), `ScreenHeader`, `LoadingView`, `ErrorBanner`, `Skeleton`, `ItemCardShell`, `StatusPill`, `NotificationBell`, `MotiView` animation patterns, theme tokens
- Promote D-1's `ReqCard` to a shared component (`bom-mobile/src/components/ReqCard.tsx`) and reuse for accountant list
- Keep API hook reuse surgical: rewrite only the V3-affected modules (`costing`, `stats`); leave V3-compatible ones (`auth`, `branches`, `groups`, `lookups`, `notifications`, `pdf`, `client`, `requisitions`, `customers`, `approvals`) untouched
- Lock the cost-input drawer pattern for re-use in D-3 (MD margin pricing) and any future per-FG nested editors

---

## 3. Out of scope

- **Phase D-3 (MD):** margin pricing → final-sign + the signature-pad component is its own brainstorm + spec
- **V2.3 historical reqs on mobile:** explicitly dropped — accountant uses web for legacy data; web admin has full V2.3 read access
- **Web Push subscription mgmt UI on mobile:** still reuses existing notification screen; no D-2 changes
- **BomCreator role:** no-op — deactivated post-V3 cutover; never produces new reqs
- **Stats refactor for SP / MD dashboards:** out of scope — D-2 only changes the accountant stats endpoint
- **Login / profile / role-router:** unchanged (JWT + role-based redirect already V3-compatible)
- **Group-peer pool (V23b):** accountants don't have `GroupId`; scoping handled by `UserBranches` — no D-2 work

---

## 4. Locked design decisions

| # | Decision | Choice |
|---|---|---|
| **D1** | Cost-input layout | **Hybrid** — main FG list with per-FG status pills + bottom-sheet drawer for editing; cross-FG readiness summary on main |
| **D2** | Save semantics | Save-on-close per drawer (commits drawer edits → parent state → bulk PUT `/cost-data`); main-screen "Submit to MD" button calls `POST /submit` after a final save |
| **D3** | FG completion criteria | Strict client-side: every RM `costPerKg > 0` + currency set + (hasPrinting → printing fields filled) + FOH/Transport/Commission **non-empty (0 allowed)**. FG card states: ⚪ not started / 🟡 in progress / 🟢 ready. Submit-all enabled only when all 🟢 |
| **D4** | Navigation model | **Dashboard-first** — `(accountant)/index.tsx` = KPI dashboard with drill-down; `/list` route for full browse |
| **D5** | Dashboard KPI set | Hero: Costing-to-complete; rows: Awaiting MD, Awaiting customer, Submitted-this-month; plus Notifications + User cards. Backend payload: `{costing, awaitingMd, awaitingCustomer, submittedThisMonth}` |
| **D6** | List filter pattern | **4-tab** segmented control: **Queue** (Costing) / **In Flight** (MdPricing + CustomerConfirm + MdFinalSign) / **Done** (Signed) / **Closed** (Rejected + Cancelled) |
| **D7** | Edit-after-submit lock | Lock at `Costing → MdPricing`. Mobile read-only on `MdPricing+`. Admin C5 unlock-costing via web only |
| **D8** | Currency picker UX | Inline `SearchablePicker` (D-1 reuse). Hardcoded 7 currencies (`AED, USD, EUR, GBP, PKR, INR, CNY`) — same as web |
| **D9** | BOM display in drawer | Read-only: RM description + code, Qty/KG, Micron. Editable: Cost/KG, Currency. ProcessId + WastagePct hidden (not relevant to costing) |
| **D10** | Detail page swap actions | **Drop branch swap** (Alain-only branch); **keep customer swap** (still mutable per V3) |
| **D11** | Cancellation policy | No accountant cancel on mobile — admin C1 hard-delete via web only |
| **D12** | Status chip palette | Reuse D-1's V3 palette (Costing=amber, MdPricing=blue, CustomerConfirm=indigo, MdFinalSign=purple, Signed=green, Rejected=red, Cancelled=slate, Draft=gray) |

---

## 5. State machine + status mapping

V3 enum from `RequisitionStatus.cs` (unchanged from D-1):

```
Draft(0) → Costing(8) → MdPricing(9) → CustomerConfirm(10) → MdFinalSign(11) → Signed(12)
                                ↘ Rejected(7)                (terminal)
                                ↘ Cancelled(13) (admin C1, any non-terminal)
```

### Status visibility on accountant mobile

| Status | Accountant action | Mobile visibility |
|---|---|---|
| **Draft** | None — SP-only (req not yet submitted to costing) | **Hidden** |
| **Costing** | ✏️ Editable — opens cost-input drawer; Submit-all transitions to MdPricing | List + Detail (editable) |
| **MdPricing** | None — waiting on MD margin pricing | List + Detail (read-only) |
| **CustomerConfirm** | None — waiting on SP customer-confirm | List + Detail (read-only) |
| **MdFinalSign** | None — waiting on MD final sign | List + Detail (read-only) |
| **Signed** | None — terminal (PDF dispatched) | List + Detail (read-only); shows final price card |
| **Rejected** | None — terminal | List + Detail (read-only); shows rejection reason |
| **Cancelled** | None — admin set | List + Detail (read-only); shows cancellation context |

V2.3 enum values (`BomPending`/`BomInProgress`/`CostingPending`/`CostingInProgress`/`MdReview` — ints 1–5) are deprecated; never produced by V3 backend. Mobile renders any req with V2.3 status as an error card "This requisition is in a legacy state — please view on web." (mirrors D-1 fallback).

### Status-to-tab mapping (D6)

| Tab | Statuses |
|---|---|
| **Queue** | `Costing` |
| **In Flight** | `MdPricing` + `CustomerConfirm` + `MdFinalSign` |
| **Done** | `Signed` |
| **Closed** | `Rejected` + `Cancelled` |

### Dashboard KPI → drilldown route

| KPI card | Drilldown route |
|---|---|
| Costing-to-complete (hero) | `/list?tab=queue` |
| Awaiting MD | `/list?tab=in-flight&filter=md` (sub-filter on MdPricing + MdFinalSign) |
| Awaiting customer | `/list?tab=in-flight&filter=customer` (sub-filter on CustomerConfirm) |
| Submitted-this-month | `/list?tab=in-flight&from=YYYY-MM-01` (date filter, all in-flight statuses) |

In-flight tab adds 3 secondary chips (All / MD / Customer) for the sub-filter — visible only when on In Flight tab.

---

## 6. Screen specs

### 6.1 Dashboard (`(accountant)/index.tsx`)

**Layout (top to bottom):**

- `ScreenHeader` with greeting + role label "ACCOUNTANT" + `NotificationBell` + Logout button (V2.3 pattern preserved)
- **Hero card** — "COSTING TO COMPLETE" with big number; tap → `/list?tab=queue` with haptic feedback
- KPI rows (compact cards):
  - "AWAITING MD" → `/list?tab=in-flight&filter=md`
  - "AWAITING CUSTOMER" → `/list?tab=in-flight&filter=customer`
  - "MD-BOUND THIS MONTH" → `/list?tab=in-flight&from=<utc-month-start>`
- Notifications card (unread count) → `/notifications`
- User card (signed-in identity)
- Pull-to-refresh

Animations: `MotiView` spring-in cascade (delay 100/180/260/340/420ms) — V2.3 pattern preserved.

**Empty state per KPI:** show "0" not skeleton when data resolved zero.

### 6.2 List page (`(accountant)/list.tsx`)

**Layout:**

- `ScreenHeader` title "Requisitions" with back arrow
- **4-tab segmented control** (D6): Queue / In Flight / Done / Closed
- Sub-filter chip row (visible only on In Flight tab): All / MD / Customer
- Optional date-from filter (visible when `?from=` query param present, e.g. from "MD-bound this month" drill-down) — clear button to remove
- Scrolling list of `ReqCard` (promoted from D-1 SP, no functional changes)
- Pull-to-refresh
- Empty state per tab + sub-filter

**Req card content (D-1 reuse):** status chip + REF-NNNN + customer name + date + currency badge + `OwnedByBadge` (group-peer SP attribution still relevant when accountant views reqs from peer-pooled SPs).

**Tap card:** routes to `(accountant)/[id]` (detail).

### 6.3 Detail page (`(accountant)/[id].tsx`)

Status-driven CTA. Two distinct render paths: **active costing** (Costing status) vs **read-only post-submit** (everything else).

**Active path — `r.status === "Costing"`:**

- `ScreenHeader` with REF-NNNN + back
- Customer card with **"Change customer"** button (D10 — kept) → opens `CustomerSwapSheet` (existing component)
  - Customer-change-history pill if `historyCount > 0` (V2.3 component preserved)
- **No "Change branch" UI** (D10 — dropped)
- **FG list** — each FG card shows: FG description + ExpectedQty + readiness pill (⚪/🟡/🟢)
- Tap FG card → opens `CostInputDrawer`
- Sticky bottom **"Submit to MD"** button (D3 — disabled until all FGs 🟢, shows "X of N FGs ready" when partial)

**Read-only path — `r.status !== "Costing"`:**

- Reuses D-1's read-only detail layout (FG cards expand-in-place, status chip header, etc.)
- Per-status footer text:
  - `MdPricing` → "Waiting on MD margin pricing"
  - `CustomerConfirm` → "Waiting on SP customer-confirm"
  - `MdFinalSign` → "Waiting on MD final sign"
  - `Signed` → final price card (hero-styled) + Download PDF button
  - `Rejected` → rejection reason card
  - `Cancelled` → cancellation context (`cancelReason` + `cancelledAt` + `cancelledByUserId`)

`HistoricalRequisitionScreen` (existing V2.3 fallback) is **deleted** — V3 detail handles all post-Costing statuses inline. No "smart route to historical".

### 6.4 Cost-input drawer (`CostInputDrawer`)

The biggest screen of D-2.

**Trigger:** tap FG card on detail page when status is `Costing`.

**Drawer contents (top to bottom):**

- Header: FG description + code + ExpectedQty (display only) + close button
- Readiness pill for this FG (live-updating as fields change)
- **Raw materials section** — table-like list of BOM lines, one row per RM:
  - Read-only: RM description + code, Qty/KG, Micron (or "—")
  - Editable: Cost/KG (numeric input), Currency (`SearchablePicker` inline with 7-currency list)
- **Printing cost section** (visible only when `fg.hasPrinting === true`):
  - Editable: Printing cost/KG, Printing currency
- **Other cost components** (always visible, all in AED):
  - FOH/KG, Transport/KG, Commission/KG (3 numeric inputs)
- Sticky drawer footer: **Cancel** (discards drawer edits) + **Save & Close** (commits to parent state → triggers PUT `/cost-data` with full FG array → drawer closes → main screen refreshes readiness)

**Drawer dismissal:**

- Tap-outside / swipe-down → equivalent to Cancel (discards edits with confirmation if dirty by ≥ 3 fields)
- "Save & Close" button → commit + close
- "Cancel" button → discard + close (confirm if dirty)

**Validation feedback:**

- Live-update FG readiness pill at top of drawer as user types
- Each input shows red border + helper text when value violates the strict rule (D3)
- "Save & Close" always enabled (server accepts partial saves) — readiness gate only blocks main-screen Submit-all

### 6.5 Customer swap (D10 — retained)

`CustomerSwapSheet` (existing V2.3 component) — no changes; already V3-compatible (uses `PATCH /api/requisitions/{id}/customer`).

`CustomerChangeHistorySheet` — same.

---

## 7. Code architecture

### 7.1 Purge list

```
DELETE bom-mobile/app/(accountant)/index.tsx       (V2.3 dashboard — V2.3 KPI shape)
DELETE bom-mobile/app/(accountant)/list.tsx        (V2.3 list with chip filters)
DELETE bom-mobile/app/(accountant)/[id].tsx        (V2.3 per-item drilldown detail)
DELETE bom-mobile/app/(accountant)/item/           (entire folder — per-item costing screens)
KEEP   bom-mobile/app/(accountant)/_layout.tsx     (route layout — minor edits if route names change)
```

API hook purge:

```
REWRITE bom-mobile/src/api/costing.ts  (V2.3 per-item cycle → V3 bulk-upsert + submit pair)
REWRITE bom-mobile/src/api/stats.ts    (V2.3 dashboard shape → V3 shape)
```

Component purge:

```
DELETE bom-mobile/src/components/HistoricalRequisitionScreen.tsx
        (V2.3 read-only fallback — V3 detail handles all post-Costing statuses inline)
DELETE bom-mobile/src/components/BranchSwapSheet.tsx
        (D10 — branch swap dropped; Alain-only post-V3)
DELETE bom-mobile/src/components/BranchChangeHistorySheet.tsx
        (companion to BranchSwap — also dropped)
```

Verify with `grep` no remaining usages before deletion.

### 7.2 New routes

```
app/(accountant)/index.tsx          → Dashboard (V3 KPIs)
app/(accountant)/list.tsx           → 4-tab list page
app/(accountant)/[id].tsx           → Detail (active path or read-only path based on status)
```

No new sub-folders — drawer is a component used inline on detail page (not a route).

### 7.3 New / rewritten components

```
bom-mobile/src/features/accountant/
  dashboard/
    AccountantDashboard.tsx           (root for app/(accountant)/index.tsx)
    KpiHeroCard.tsx                   (Costing-to-complete hero — big number)
    KpiRow.tsx                        (compact cards: Awaiting MD, Awaiting customer, This month)

  list/
    AccountantListScreen.tsx          (root for app/(accountant)/list.tsx)
    AccountantTabs.tsx                (Queue / In Flight / Done / Closed segmented control)
    InFlightSubFilterChips.tsx        (All / MD / Customer — visible only on In Flight tab)

  detail/
    AccountantDetailScreen.tsx        (root for app/(accountant)/[id].tsx — branches on status)
    ActiveCostingView.tsx             (Costing status — FG list + Submit-all)
    ReadonlyDetailView.tsx            (post-Costing — reuses D-1 read-only layout primitives)
    FgCostingCard.tsx                 (single FG card on active view — readiness pill + tap-to-open-drawer)
    SubmitAllFooter.tsx               (sticky bottom — disabled until all FGs ready)

  drawer/
    CostInputDrawer.tsx               (the big drawer — sections below)
    RmCostRow.tsx                     (single BOM line row in drawer — Cost/KG + Currency picker)
    PrintingCostSection.tsx           (visible iff hasPrinting)
    OtherCostsSection.tsx             (FOH + Transport + Commission)
    DrawerFooter.tsx                  (Cancel + Save & Close)

  state/
    useCostingDraftState.ts           (parent-level state hook — owns full FG array, exposes setFg(idx, partial), readiness checks)
    fgReadiness.ts                    (pure function — applies D3 rules, returns "not_started" | "in_progress" | "ready")
```

`useCostingDraftState` is the heart of D-2 — single source of truth for the form. Drawer reads/writes via this hook. Submit-all reads from this hook to build the PUT `/cost-data` payload.

Plus shared component promotion:

```
PROMOTE bom-mobile/src/features/sales/list/ReqCard.tsx
     →  bom-mobile/src/components/ReqCard.tsx
   (shared between SP list and accountant list — same shape, same visual)
```

### 7.4 API hook strategy

| File | Action | Notes |
|---|---|---|
| `src/api/auth.ts` | Keep | V3 unchanged |
| `src/api/branches.ts` | Keep | V3 unchanged (used by user profile) |
| `src/api/groups.ts` | Keep | V3 unchanged |
| `src/api/lookups.ts` | Keep | V3 unchanged |
| `src/api/notifications.ts` | Keep | New V3 notif types appear automatically |
| `src/api/pdf.ts` | Keep | `GET /api/approvals/{id}/pdf` for Signed status |
| `src/api/client.ts` | Keep | Axios interceptors unchanged |
| `src/api/requisitions.ts` | Keep (D-1 already V3) | Detail endpoint returns `finishedGoods[].costs` shape — accountant hydrates from this |
| `src/api/customers.ts` | Keep (D-1 already V3) | Customer swap reuses |
| `src/api/approvals.ts` | Keep (D-1 already V3) | Read-only on accountant view (final price card on Signed) |
| `src/api/costing.ts` | **Rewrite** | New: `useV3CostingReview(requisitionId)` (hydrates from req detail, no separate endpoint), `useSaveV3CostData(requisitionId)`, `useSubmitV3Costing(requisitionId)` |
| `src/api/stats.ts` | **Rewrite** | `useAccountantDashboardV3()` returning `{costing, awaitingMd, awaitingCustomer, submittedThisMonth}` |

`costing.ts` rewrite is a near-1:1 port of `bom-web/src/features/costing/costingApi.ts` V3 hooks.

### 7.5 Reused components (no changes)

`OwnedByBadge`, `StatusChipRow` (D-1's V3 palette — already complete), `SearchablePicker` (D8 — currency picker reuse), `ScreenHeader`, `LoadingView`, `ErrorBanner`, `Skeleton`, `ItemCardShell`, `StatusPill`, `NotificationBell`, theme tokens, `AuthGuard`, signalR hub client, `MotiView` animations, `CustomerSwapSheet`, `CustomerChangeHistorySheet` (D10 retain).

---

## 8. Backend prerequisites

D-2 has 3 backend dependencies that must land **before** D-2 mobile ship. None are heavy lifts.

| # | Dependency | Action |
|---|---|---|
| **B1** | `GET /api/stats/accountant-dashboard` V3 reshape | Current returns V2.3 shape (`pendingCosting`, `inProgress`, `submittedThisMonth`, `awaitingMd`). Reshape to V3 payload `{costing, awaitingMd, awaitingCustomer, submittedThisMonth}`. Web doesn't consume this endpoint — no web breakage. |
| **B2** | Verify `POST /api/costing/{id}/submit` V3 behavior | Endpoint exists (web uses it via `useSubmitV3Costing`). Confirm: (a) requires `Status === Costing`; (b) validates every FG has a `BomCost` row before transitioning; (c) returns 400 with field-level error path on missing data. If gaps exist, fix in same PR. |
| **B3** | Customer swap endpoint allowed-status list | Per V2.3-A `PATCH /api/requisitions/{id}/customer` was allowed for `BomPending`/`BomInProgress`/`CostingPending`. After V3 cutover, equivalent allowed list should be `Draft`/`Costing`. Verify + adjust if needed. |

These ship as a small backend-only PR before D-2 mobile work begins. Estimate: 2-3 hours, ~50 LOC + tests. Tag it as a sub-task of D-2.

D-1 deferred T24 (`PUT /api/requisitions/{id}` edit-draft for SP) is unrelated to D-2 and stays deferred.

---

## 9. Deploy strategy

### 9.1 EAS — OTA vs rebuild

Phase D-2 introduces no native deps (signature pad still D-3). Same APK (`mobile-shipped-vc1`, versionCode=1) consumes the new JS bundle.

```bash
npx eas-cli update --branch preview --message "v3-mobile-d2-accountant-<sha>"
```

**No `git tag mobile-shipped-vc<N+1>`** — tag bumps only on fresh APK rebuilds.

D-1 itself shipped without rebuild + on-device smoke skipped (per memory `project_v3_mobile_d1_brainstorm.md`). D-2 likely needs same rebuild on SDK 54 alignment when next physical-device QA happens. If D-2 ships before D-1's APK rebuild, both phases consume the same OTA bundle once the rebuild lands.

### 9.2 Drift check before D-2 ship

```bash
git log mobile-shipped-vc1..HEAD --oneline -- bom-mobile/
```

If output includes any `bom-mobile/package.json` (native dep), `bom-mobile/app.config.ts`, or `bom-mobile/eas.json` change → abort OTA, schedule rebuild.

---

## 10. Testing strategy

D-1 precedent: tsc clean → emulator smoke → EAS OTA → physical-device smoke. D-2 follows same:

1. `npx tsc --noEmit` clean. D-2's purge of V2.3 accountant files should also clear lingering V2.3 cross-phase residuals — D-1 left 26 tsc errors that are V2.3 cross-phase.
2. `expo start` against local API (`adb reverse tcp:7300 tcp:7300`) — exercise dashboard, list tabs, costing drawer, save-and-close, submit-all on Android emulator.
3. EAS OTA push to `preview` channel.
4. On-device smoke checklist (physical Android, accountant role — Sara seed user):
   - Dashboard renders all 4 KPI cards with correct counts
   - Tap "Costing to complete" → list opens on Queue tab
   - Tap In Flight tab + sub-filter chips → counts match dashboard
   - Tap a Costing-status req → detail page in active mode
   - Tap FG card → drawer opens; fill RM cost + currency + FOH/Transport/Commission; "Save & Close" → drawer closes, FG card pill = 🟢
   - Repeat for all FGs → "Submit to MD" enables → tap → status flips to MdPricing → list updates
   - Open a non-Costing req (e.g. MdPricing seeded by previous round) → detail renders read-only with "Waiting on MD" footer
   - Open a Signed req → final price card + Download PDF works
   - Customer-swap on Costing req works; branch-swap UI absent
5. Web parity check: same req's detail in web admin matches mobile values.

No Jest unit tests / Detox E2E in D-2 (V2 mobile precedent).

---

## 11. Acceptance criteria

- [ ] All V2.3 accountant files purged (`index.tsx`, `list.tsx`, `[id].tsx`, `item/` folder, `HistoricalRequisitionScreen.tsx`, `BranchSwapSheet.tsx`, `BranchChangeHistorySheet.tsx`)
- [ ] V2.3 API hooks rewritten: `costing.ts` (V3 bulk), `stats.ts` (V3 dashboard shape)
- [ ] Backend B1+B2+B3 merged before D-2 mobile work begins
- [ ] Dashboard renders 4 KPI cards with correct V3 counts
- [ ] List page 4-tab control with In Flight sub-filter chips works
- [ ] Detail page renders correct active/read-only path per status
- [ ] Cost-input drawer: BOM rows show RM/Qty/Micron read-only + editable Cost/Currency; printing section conditional on `hasPrinting`; FOH/Transport/Commission inputs work
- [ ] Save-on-close drawer commits to parent state + triggers PUT `/cost-data`
- [ ] FG readiness pill (⚪🟡🟢) live-updates per D3 rules
- [ ] "Submit to MD" disabled until all FGs 🟢; on click → POST `/submit` → status `Costing → MdPricing`
- [ ] Customer swap retained; branch swap UI absent everywhere
- [ ] Read-only paths render Signed (final price card + PDF), Rejected (reason), Cancelled (cancel context)
- [ ] V2.3-status req renders error card "view on web" (D-1 fallback consistency)
- [ ] `tsc --noEmit` clean (or no-worse than D-1's residuals — ideally improved by purging V2.3 accountant code)
- [ ] On-device smoke checklist passes
- [ ] EAS OTA published to `preview` channel

---

## 12. Open questions / risks

- **R1: Submit-all error semantics.** If `POST /submit` returns 400 because backend per-FG validation is stricter than D3 rules (e.g. backend rejects `Commission = 0`), accountant sees toast but can't see which FG is wrong. **Mitigation:** parse response body for field-level error path → highlight failing FG card. **Fallback:** generic toast.
- **R2: Drawer-close discard confirmation.** Tap-outside / swipe-down should confirm if dirty (lest accountant lose 5 min of typing by accident). Confirmation dialog adds friction. **Decision in implementation:** confirm only if drawer state diverges from saved state by ≥ 3 fields.
- **R3: Two-level keyboard issue on small phones.** Drawer has many inputs; keyboard may cover the bottom Save button. Need `KeyboardAvoidingView` + `ScrollView` inside drawer. Verified working in D-1 for `FgEditDrawer`.
- **R4: Stats endpoint cache invalidation on submit.** When accountant submits, dashboard `costing` count should drop by 1 immediately. Submit mutation should invalidate `stats` query key.
- **R5: Backend B1 web breakage.** If web has any consumer of the V2.3 stats shape, reshape would break it. Quick grep before B1 PR. (Expected to be zero — web has its own dashboard logic.)
- **R6: Group-peer accountant scoping.** V23b group-peer pool is SP-only. Accountants don't have `GroupId`. Accountant sees all reqs scoped by `UserBranches`. No D-2-specific work — V3 backend already handles scoping.

---

## 13. Implementation phasing within D-2

Suggested phasing for the writing-plans phase:

1. **D-2.0 — Backend prereqs**: B1 + B2 + B3 PR first. Lands on master before any mobile work.
2. **D-2.1 — Purge + API hook rewrites**: delete V2.3 accountant files; rewrite `costing.ts` + `stats.ts`. tsc clean baseline.
3. **D-2.2 — Dashboard**: `AccountantDashboard.tsx` + KPI components. Smoke individually.
4. **D-2.3 — List page**: `AccountantListScreen` + tabs + sub-filter chips.
5. **D-2.4 — Detail page (read-only path)**: render Signed/Rejected/Cancelled/MdPricing/CustomerConfirm/MdFinalSign — easier subset, no drawer involved.
6. **D-2.5 — Detail page (active path) + drawer**: the big one — `ActiveCostingView` + `CostInputDrawer` + `useCostingDraftState` + readiness logic.
7. **D-2.6 — Submit-all + customer swap retention**: wire submit endpoint + reuse customer swap sheet.
8. **D-2.7 — Smoke + OTA push**: emulator → EAS → physical-device.

Estimated session count: 4-6 sessions of focused work (~10-14 hours). Roughly equivalent to D-1.

---

## 14. Maintenance

This spec captures the design as agreed on 2026-05-01. Any change during implementation that contradicts a locked decision (D1–D12) should be flagged in chat and either spec-amended (with a re-confirmation) or rolled back. Implementation drift that does NOT change locked decisions is acceptable and gets captured in the implementation plan and commit messages.
