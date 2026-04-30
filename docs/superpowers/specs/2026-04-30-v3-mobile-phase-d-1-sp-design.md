# V3 Mobile — Phase D-1 (SalesPerson)

**Spec date:** 2026-04-30
**Branch (planned):** `feat/v3-mobile-phase-d-1-sp` (off `master` @ `73b7a06`)
**Predecessor:** V3 cutover 2026-04-30 (PRs #29–43, tag `v3-cutover-2026-04-30`)
**Parent decomposition:** `bom-mobile` rebuild against V3 backend, decomposed by role
**This phase:** SalesPerson screens only

---

## 1. Summary

`bom-mobile` (`mobile-shipped-vc1`, EAS preview-channel APK) shipped on V2.3 backend behavior. Backend went V3 on 2026-04-30 cutover. Mobile codebase has zero drift since vc1 → effectively orphaned. No live mobile users yet, so we have time to design properly rather than patch.

Phase D-1 rebuilds the SalesPerson surface against V3: list, detail, the new combined sales+BOM creation screen, and the new customer-confirm step. V2.3 SP screens are deleted (`V3-only purge`); V2.3 Approved historical reqs become invisible on mobile (still readable via web — primary access path anyway).

Phase D-2 (Accountant) and D-3 (MD + signature pad) are separate spec/plan/ship cycles in future sessions.

---

## 2. Goal

Bring SP mobile flows up to the V3 backend contract with mobile-native UX patterns (bottom-sheet pickers, per-FG drawers for focused editing, inline modals for confirmation actions). Ship as an EAS OTA update over `mobile-shipped-vc1` — no native deps change in D-1, so no APK rebuild needed.

**Secondary goals:**
- Delete V2.3 SP code rather than maintain dual-shape branches
- Reuse existing V2.3 components that are still V3-compatible (`OwnedByBadge`, `StatusChipRow`, `BranchSwapSheet`, `SearchablePicker`, theme tokens, AuthGuard, signalR)
- Keep API hook reuse surgical: rewrite only the V3-affected modules (`requisitions`, `customers`, `approvals`); leave V3-compatible ones (`auth`, `branches`, `groups`, `lookups`, `notifications`, `pdf`, `client`) untouched
- Lock the picker pattern (sheet → full-screen create modal) for re-use across Customer, FG, RM — and across future phases

---

## 3. Out of scope

- **Phase D-2 (Accountant):** the V3 cost-input flow (`CostingEntryV3Page` mobile equivalent) is its own brainstorm + spec
- **Phase D-3 (MD):** margin → final-sign + the signature-pad component (the single biggest unknown) is its own brainstorm + spec
- **V2.3 historical Approved reqs on mobile:** explicitly dropped. SP can still view them on web. PDFs still downloadable via web.
- **Admin role on mobile:** existing splash screen ("use web for admin tasks") stays as-is
- **Notifications screen:** new V3 notif types (`PricesOverridden`, `CustomerDeleted`) appear automatically; no UI work required in D-1
- **Login / profile / role-router:** unchanged (JWT + role-based redirect already V3-compatible)
- **Group-peer pool (V23b):** already implemented and V3-compatible — no changes

---

## 4. Locked design decisions

| # | Decision | Choice |
|---|---|---|
| D1 | Decomposition | Role-based — D-1 = SP only |
| D2 | V2.3 disposition | V3-only purge — delete V2.3 SP code; no legacy fallback on mobile |
| D3 | SP screen scope | List + Detail + Combined create + Customer-confirm modal |
| D4 | Customer-confirm UX | Inline modal on detail page (not a separate route) |
| D5 | Edit-draft flow | Supported — "Edit" button on Draft detail navigates to combined-create in `mode=edit` |
| D6 | Rejected status | Terminal, read-only detail (no resubmit) |
| D7 | List filter | Grouped tabs: **Active** / **Done** / **Closed** |
| D8 | Combined create layout | **Hybrid** — main FG list + per-FG bottom-sheet drawer for editing |
| D9 | Picker pattern (Customer / FG / RM) | **Pattern A** — bottom-sheet picker → "+ Create new" opens full-screen create modal |
| D10 | Detail FG layout | Expand-in-place FG cards (read-only); drawer reserved for edit context |

---

## 5. Screen specs

### 5.1 List page (`(sales)/index.tsx`)

**Layout:**
- Top: 3-tab segmented control — **Active** / **Done** / **Closed**
- Each tab: scrolling list of req cards
- Pull-to-refresh
- Floating action button (`+`) bottom-right → navigates to `(sales)/new`
- Empty state per tab with CTA where appropriate ("No drafts yet — create your first quotation")

**Tab membership:**
- **Active** = `Draft` ∪ `Costing` ∪ `MdPricing` ∪ `CustomerConfirm` ∪ `MdFinalSign`
- **Done** = `Signed`
- **Closed** = `Rejected` ∪ `Cancelled`

**Req card content:**
- Top row: status chip + REF-NNNN
- Customer name (one line, truncate)
- Date + currency badge
- Group-peer-owned reqs show `OwnedByBadge` per V23b

### 5.2 Combined create-with-BOM screen (`(sales)/new.tsx` + `(sales)/edit/[id].tsx`)

The biggest screen of D-1. Hybrid layout (D8). Used for both new and edit-draft.

**Main screen (always visible):**
- Header: customer card (tap → `CustomerPickerSheet`) + currency badge
- Reference + notes (collapsible — tap "Details" in nav-bar)
- FG list section: each FG rendered as card showing FG code + description + ExpectedQty + line count
- "+ Add FG" button → `FgPickerSheet`
- Sticky bottom: "Submit to Costing" (disabled until validation passes — at least 1 FG with ≥1 BOM line each)

**Per-FG drawer (`FgEditDrawer`):**
- Slides up from bottom when FG card tapped
- Header: FG code + description + ExpectedQty editor
- BOM lines list: each line shows process + RM + qty/kg + wastage%
- "+ Add line" → opens picker rows in-drawer (process, RM, qty/kg, wastage%)
- "Remove this FG" destructive action at bottom of drawer
- Drawer dismiss: tap-outside, swipe-down, or "Done" button

**Validation rules (mirrored from web):**
- Customer required
- ≥ 1 FG required
- Each FG needs ExpectedQty > 0 and ≥ 1 BOM line
- Each BOM line: ProcessId valid, RmItemId valid + active, QtyPerKg > 0, WastagePct ≥ 0
- Currency required (defaults to AED)

**Modes:**
- `mode=new` (route `(sales)/new`): blank state, `POST /api/requisitions` on submit
- `mode=edit` (route `(sales)/edit/[id]`): hydrate from `GET /api/requisitions/{id}` (V3 nested shape), `PUT /api/requisitions/{id}` on save (or specific edit endpoint per backend convention)

### 5.3 Detail page (`(sales)/[id].tsx`)

Read-only by default. Status-driven CTA at bottom.

**Layout:**
- Header: status chip + REF-NNNN + customer name + last-updated timestamp
- Customer section (collapsible): name + code + contact info
- FG cards (expand-in-place per D10): tap card → BOM lines render right under
- Notes section (if any)
- Final price card (Signed status only) — hero-styled, the price SP quotes to customer
- Cancellation context (Cancelled status only) — `cancelReason` + `cancelledAt` + `cancelledByUserId`
- Sticky bottom CTA — see status table below

**Status-driven CTA matrix:**

| Status | Bottom CTA | Top-right action |
|---|---|---|
| `Draft` | Submit to Costing | Edit (→ combined-create edit mode) |
| `Costing` | None — "Waiting on Accountant" subtle text | — |
| `MdPricing` | None — "Waiting on MD pricing" | — |
| `CustomerConfirm` | Customer accepted • Customer rejected | — |
| `MdFinalSign` | None — "Waiting on MD final sign" | — |
| `Signed` | Download PDF | — |
| `Rejected` | None — shows rejection reason | — |
| `Cancelled` | None — shows cancellation context | — |

### 5.4 Customer-confirm modal (`CustomerConfirmModal`)

Inline modal on detail page when status = `CustomerConfirm`. Not a separate route per D4.

**Two CTAs:**
- **Customer accepted** → `POST /api/approvals/{id}/accept-customer` → status → `MdFinalSign` → modal closes, detail refreshes
- **Customer rejected** → opens reason input → `POST /api/approvals/{id}/reject-customer` with `{ reason }` → status → `MdPricing` (back to MD for re-pricing) → modal closes

**Modal contents:**
- Title: "Customer response on REF-NNNN"
- Quote summary: customer name + total + currency + final-margin if shown to SP
- Two big buttons (Accept = green, Reject = amber)
- Reject path: in-modal reason field (required, ≥ 5 chars) before submit

### 5.5 Picker sheets + create modals (Pattern A — D9)

Three sheets, three modals — uniform pattern:

**Sheet structure (`CustomerPickerSheet`, `FgPickerSheet`, `RmPickerSheet`):**
- Search bar at top (debounced 300ms)
- Scrolling list of matching items
- Tap any item → returns to caller with selected ID, sheet closes
- "+ Create new" button at top of sheet → opens full-screen create modal

**Modal structure (`CustomerCreateModal`, `FgCreateModal`, `RmCreateModal`):**
- Full-screen modal with native nav bar (Cancel / Save)
- Form fields per entity
- Save → `POST /api/{customers|items}` → returns to picker sheet → caller auto-receives the new entity

**Customer create modal fields:** Name, Email (optional), Phone (optional), Address (optional). Code auto-generated server-side per V3.

**FG create modal fields:** Description, BranchId (auto = current req's branch), Type=`FinishedGood`, LastPurchasePrice (optional). Code auto-generated.

**RM create modal fields:** Description, BranchId (auto = current req's branch), Type=`RawMaterial`, LastPurchasePrice (optional).

---

## 6. Code architecture

### 6.1 Purge list

```
DELETE bom-mobile/app/(sales)/index.tsx        (V2.3 list)
DELETE bom-mobile/app/(sales)/new.tsx          (V2.3 create)
DELETE bom-mobile/app/(sales)/[id].tsx         (V2.3 detail)
DELETE bom-mobile/src/api/bom.ts               (V3 has no separate BOM endpoints)
KEEP   bom-mobile/app/(sales)/_layout.tsx      (route layout — minor edits for new route)
```

### 6.2 New routes

```
app/(sales)/index.tsx          → list page (3-tab segmented)
app/(sales)/new.tsx            → combined create (mode=new)
app/(sales)/edit/[id].tsx      → combined create (mode=edit)
app/(sales)/[id].tsx           → detail (with status-driven CTA + customer-confirm modal)
```

### 6.3 New components

```
bom-mobile/src/features/sales/
  list/
    SalesListScreen.tsx          (root for app/(sales)/index.tsx)
    StatusTabs.tsx               (Active / Done / Closed segmented control)
    ReqCard.tsx                  (single req row)

  create/
    CombinedCreateScreen.tsx     (root for new + edit routes)
    HeaderSection.tsx            (customer + currency + reference + notes)
    FgListMain.tsx               (FG cards + Add FG button)
    FgEditDrawer.tsx             (per-FG bottom-sheet BOM editor)
    BomLineRow.tsx               (single line in drawer)
    SubmitFooter.tsx             (sticky bottom CTA + validation)

  detail/
    SalesDetailScreen.tsx        (root for app/(sales)/[id].tsx)
    DetailHeader.tsx             (status chip + ref + customer + date)
    FgReadCard.tsx               (expand-in-place per D10)
    StatusFooterCta.tsx          (status-driven bottom button)
    CustomerConfirmModal.tsx     (D4 inline modal)
    FinalPriceCard.tsx           (Signed status only, hero-styled)

  pickers/
    CustomerPickerSheet.tsx      (Pattern A — sheet)
    CustomerCreateModal.tsx      (Pattern A — modal)
    FgPickerSheet.tsx
    FgCreateModal.tsx
    RmPickerSheet.tsx
    RmCreateModal.tsx
```

### 6.4 API hook strategy — surgical

| File | Action |
|---|---|
| `src/api/auth.ts` | Keep — V3 unchanged |
| `src/api/branches.ts` | Keep — V3 unchanged |
| `src/api/groups.ts` | Keep — V3 unchanged |
| `src/api/lookups.ts` | Keep — V3 unchanged (process list, currency list) |
| `src/api/notifications.ts` | Keep — same shape, new types appear automatically |
| `src/api/pdf.ts` | Keep — `GET /api/approvals/{id}/pdf` unchanged |
| `src/api/client.ts` | Keep — axios interceptors unchanged |
| `src/api/requisitions.ts` | **Rewrite** — V3 nested shape (`finishedGoods[].bomLines/costs`, `customer`, `salesPerson`); 8 statuses; cancellation context (`cancelReason`/`cancelledAt`/`cancelledByUserId`) |
| `src/api/approvals.ts` | **Rewrite** — D-1 needs only `accept-customer` + `reject-customer` endpoints; defer margin + final-sign to D-3 |
| `src/api/customers.ts` | **Rewrite** — V23c P2 `IsDeleted` filter; inline-create returns auto-generated `Code` |
| `src/api/bom.ts` | **Delete** — V3 has no separate BOM endpoints (combined into requisition) |
| `src/api/costing.ts` | **Defer to D-2** — Accountant scope |
| `src/api/stats.ts` | **Defer to D-2** — accountant dashboard scope |

### 6.5 Reused components (no changes)

`OwnedByBadge`, `StatusChipRow`, `BranchSwapSheet`, `SearchablePicker`, theme tokens, `AuthGuard`, signalR hub client, login screen, profile screen, notifications screen.

`StatusChipRow` will receive new V3 status colors (D11 below) but its API and DOM stay the same — single-prop addition.

---

## 7. State machine + status mapping

V3 enum from `RequisitionStatus.cs`:

```
Draft(0) → Costing(8) → MdPricing(9) → CustomerConfirm(10) → MdFinalSign(11) → Signed(12)
                                ↘ Rejected(7)              (terminal)
                                ↘ Cancelled(13) (set by admin C1, any non-terminal)
```

V2.3 ints `1` to `5` (`BomPending`, `BomInProgress`, `CostingPending`, `CostingInProgress`, `MdReview`) are deprecated and never produced by V3 — but they may appear in legacy data the mobile reads. SP mobile **does not** support legacy V2.3 statuses (D2 — V3-only purge); a V2.3-status req on mobile renders an error card "This requisition is in a legacy state — please view on web."

### Status-to-tab mapping (D7)

| Tab | Statuses |
|---|---|
| Active | `Draft`, `Costing`, `MdPricing`, `CustomerConfirm`, `MdFinalSign` |
| Done | `Signed` |
| Closed | `Rejected`, `Cancelled` |

### Status chip palette

| Status | Color | Hex |
|---|---|---|
| Draft | gray | `#6b7280` |
| Costing | amber | `#f59e0b` |
| MdPricing | blue | `#3b82f6` |
| CustomerConfirm | indigo | `#6366f1` |
| MdFinalSign | purple | `#8b5cf6` |
| Signed | green | `#10b981` |
| Rejected | red | `#ef4444` |
| Cancelled | slate | `#475569` |

---

## 8. Deploy strategy

### 8.1 EAS — OTA only

Phase D-1 introduces no native deps (no signature pad — that's D-3). Same APK (`mobile-shipped-vc1`, versionCode=1) consumes the new JS bundle.

```bash
npx eas-cli update --branch preview --message "v3-mobile-d1-sp-<sha>"
```

**No `git tag mobile-shipped-vc<N+1>`** — tag bumps only on fresh APK rebuilds.

### 8.2 Drift check before D-1 ship

```bash
git log mobile-shipped-vc1..HEAD --oneline -- bom-mobile/
```

Should show only D-1's commits (no native-affecting changes). If `bom-mobile/package.json` adds a native dep, `bom-mobile/app.config.ts` changes, or `bom-mobile/eas.json` changes — abort OTA and rebuild.

---

## 9. Testing strategy

V2 mobile shipped via on-device smoke (per memory `project_mobile_eas_deploy.md`, `project_mobile_v22_bom_drilldown.md`). Phase D-1 follows the same convention:

1. `npx tsc --noEmit` clean
2. `expo start` against local API (`adb reverse tcp:7300 tcp:7300`) — exercise create + detail + customer-confirm + edit-draft on Android emulator
3. EAS OTA push to `preview` channel
4. On-device smoke on physical Android device:
   - Login as SP (Ali)
   - Create new req with 2 FGs, 3 BOM lines per FG
   - Verify request appears in list
   - Tap req → detail loads with V3 nested shape
   - Submit to Costing → status transitions, list updates
   - (skip mid-flow steps until Accountant + MD test — those are D-2/D-3)
   - For an existing CustomerConfirm-status req (seeded by Sara on dev backend), test accept + reject paths

No Jest unit tests / Detox E2E in D-1 — out of scope per V2 mobile precedent.

---

## 10. Acceptance criteria

- [ ] All 4 V2.3 SP files purged from `bom-mobile/`
- [ ] List page renders V3 statuses with correct grouped-tab membership; pull-to-refresh works
- [ ] Combined create screen submits a valid V3 requisition (≥1 FG + ≥1 BOM line each); validation errors render inline
- [ ] Edit-draft route hydrates from V3 nested shape and saves correctly
- [ ] Detail page renders the 8 V3 statuses with correct CTA matrix; expand-in-place FG cards work; cancellation context displays for Cancelled
- [ ] Customer-confirm modal: accept transitions to MdFinalSign; reject (with reason ≥5 chars) transitions to MdPricing
- [ ] Picker → create modal pattern works for Customer + FG + RM
- [ ] Group-peer ownership renders via `OwnedByBadge`
- [ ] `tsc --noEmit` clean
- [ ] On-device smoke checklist passes
- [ ] EAS OTA published to `preview` channel; live APK consumes the new bundle

---

## 11. Open questions / risks

- **Edit-draft endpoint shape:** is V3 backend's edit endpoint `PUT /api/requisitions/{id}` or a more granular shape? Verify during implementation. Fallback: re-create approach (delete + new) acceptable since drafts are short-lived.
- **Inline raw material picker drawer-within-drawer:** when adding a BOM line inside `FgEditDrawer`, the RM picker is itself a sheet. Two-level sheet stacking on Android may need testing. Fallback: replace inner sheet with an inline expand-in-place RM search row.
- **OTA bundle size limit:** large new screens may push the JS bundle past EAS Update's optimal size. If an OTA push is rejected for size, rebuild as `mobile-shipped-vc2`.
- **Group-peer edit permissions on Draft:** SP can edit own drafts; can they also edit group-peer drafts? Confirm against `SalesAuthorization.VisibleSalesPersonIds` — V23b says peers can edit each other's reqs.
- **Notification deep-link from CustomerConfirm push:** when SP gets a "ready for customer" push, tapping should navigate to the req detail with the customer-confirm modal pre-opened. Implement basic deep-link in D-1 or defer to D-2/D-3? Recommend defer (D-1 lands; deep-link added when MD-flow ships in D-3).

---

## 12. Implementation phasing within D-1

Suggested phasing for the implementation plan (which `writing-plans` will refine):

1. **D-1.0 — Setup & API hook rewrites:** rewrite `requisitions.ts`, `approvals.ts` (accept/reject only), `customers.ts`. Delete `bom.ts`. tsc clean.
2. **D-1.1 — List page:** new route + components + status tabs + status chips palette update.
3. **D-1.2 — Detail page:** read path + expand-in-place FGs + status-driven CTA matrix (placeholders for action handlers).
4. **D-1.3 — Picker pattern (foundational):** `CustomerPickerSheet` + `CustomerCreateModal` + `FgPickerSheet` + `FgCreateModal` + `RmPickerSheet` + `RmCreateModal`.
5. **D-1.4 — Combined create (Hybrid):** main screen + `FgEditDrawer` + validation + new-mode submit.
6. **D-1.5 — Edit-draft:** route + hydration + save path.
7. **D-1.6 — Customer-confirm modal:** in-modal accept/reject + reason field.
8. **D-1.7 — V2.3 purge + smoke:** delete V2.3 files, clean up imports, on-device smoke checklist.
9. **D-1.8 — OTA push:** `eas-cli update --branch preview` + verify on physical device.

Estimated session count: 4–6 sessions of focused work (~8–14 hours).

---

## 13. Maintenance

This spec captures the design as agreed on 2026-04-30. Any change during implementation that contradicts a locked decision (D1–D10) should be flagged in chat and either spec-amended (with a re-confirmation) or rolled back. Implementation drift that does NOT change locked decisions is acceptable and gets captured in the implementation plan and commit messages.
