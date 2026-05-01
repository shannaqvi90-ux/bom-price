---
title: V3 Mobile — Phase D-3 (Managing Director) — Design Spec
date: 2026-05-02
status: approved
phase: D-3 (post D-1 SP, D-2 Accountant)
---

# V3 Mobile — Phase D-3 (Managing Director) — Design Spec

## 1. Summary

Phase D-3 rebuilds the **Managing Director (MD)** mobile experience for the V3 simplified workflow. It replaces the V2.3 MD code (BOM/costing drilldown screens, branch-swap UI, historical detail) with V3-native screens for the two MD-active states — `MdPricing` (margin entry) and `MdFinalSign` (sign + lock) — plus read-only views for non-active states.

Signature is captured as a **PNG image upload via Profile** (one-time per user), not via on-screen signature pad. Backend already supports this (`POST /api/profile/signature`, `User.SignatureImagePath`); D-3 only needs the mobile UI.

D-3 also closes Test 8 deferral from D-2 by adding a backend-computed `finalPrice` field to the `V3Requisition` response, used both for the MdFinalSign preview and the existing `<FinalPriceCard>` on Signed reqs.

## 2. Goal

After D-3 ships:

- Managing Director can complete the MdPricing → CustomerConfirm → MdFinalSign → Signed flow entirely on mobile.
- One-time signature PNG upload via Profile, blocking final-sign if absent.
- All V2.3 MD code purged from `app/(md)/` and `src/components/`.
- `tsc` clean (V2.3 MD residuals from D-2's 19-error baseline drop to zero).
- D-1 + D-2 + D-3 all riding the same fresh APK rebuild (`mobile-shipped-vc2`).

## 3. Out of scope

- Web parity. Web MD pages already exist (`MdMarginPage`, `MdFinalSignPage`); minor gaps (e.g. `useRejectRequisition` defined but unused) deferred to a separate cleanup.
- BOM/costing drilldown by MD. V2.2/V2.3 allowed MD to inspect BOM and costing detail; V3 MD trusts the accountant's costing and only sees per-FG `costPerKg` aggregate.
- Branch swap. V3 is Alain-only — branch UI is dead code.
- Customer swap. V3 customer-swap is SP-only on `Costing` reqs. MD does not perform customer swap.
- Per-quote signature override. Single signature per user; no override at sign time.
- Detox E2E. D-1/D-2 precedent — manual smoke + jest unit tests only.

## 4. Locked design decisions

| # | Decision | Choice |
|---|---|---|
| **D1** | D-3 scope | Mobile MD core + backend `finalPrice` (Q1 = C). No web parity in scope. |
| **D2** | Signature upload UX | One-time via Profile screen; final-sign blocked if missing (Q2 = A). |
| **D3** | MdPricing screen content | Cost-informed: per-FG cost + margin input + live sale/total preview + Reject CTA (Q3 = C). |
| **D4** | MdFinalSign screen content | Full quote preview + signature preview + SIGN-token gate + lock button (Q4 = C). |
| **D5** | `finalPrice` API shape | Embedded in `V3Requisition` response, populated when status ∈ {MdFinalSign, Signed} (Q5 = B). |
| **D6** | MD status-to-tab mapping | 4-tab (Queue/InFlight/Done/Closed) — same pattern as D-2 accountant. Queue = MdPricing + MdFinalSign. |
| **D7** | Image source for signature upload | Both gallery and camera via `expo-image-picker`. |
| **D8** | V2.3 purge timing | Part of D-3 PR (rewriting MD routes naturally replaces the old imports). |
| **D9** | Detail page pattern | Status-branch dispatcher: `ActiveMdPricingView`, `ActiveMdFinalSignView`, `ReadonlyMdView`. Mirrors D-2's `AccountantDetailScreen`. |
| **D10** | Reject UI placement | On `ActiveMdPricingView` only (MdPricing → Rejected). Not on MdFinalSign — at that stage SP has already customer-confirmed; rejection is no longer normal flow. |

## 5. State machine + status mapping

V3 status enum and transitions are documented in CLAUDE.md. From the **MD's perspective**:

```
Costing → MdPricing → CustomerConfirm → MdFinalSign → Signed
              ↘ Rejected
                                                       (terminal)
          (active for MD)        (active for MD)
```

### Status-to-screen mapping

| Status | MD screen |
|---|---|
| `Draft`, `Costing` | `ReadonlyMdView` (status banner: "Waiting on accountant") |
| `MdPricing` | `ActiveMdPricingView` — set margins, approve→CustomerConfirm OR reject→Rejected |
| `CustomerConfirm` | `ReadonlyMdView` (status banner: "Waiting on SP customer-confirm") |
| `MdFinalSign` | `ActiveMdFinalSignView` — full preview + sign-and-lock |
| `Signed` | `ReadonlyMdView` + `<FinalPriceCard>` |
| `Rejected`, `Cancelled` | `ReadonlyMdView` with reason banner |

### Status-to-tab mapping (D6)

| Tab | Statuses | MD action? |
|---|---|---|
| **Queue** | `MdPricing`, `MdFinalSign` | ✅ Active |
| **In Flight** | `CustomerConfirm`, `Costing` | ❌ Waiting |
| **Done** | `Signed` | — |
| **Closed** | `Rejected`, `Cancelled` | — |

Optional sub-filter chips on In Flight: All / Customer (CustomerConfirm) / Costing.

### Dashboard KPI mapping

| KPI card | Source | Tap target |
|---|---|---|
| **Hero — To price** | MdPricing count | List → Queue tab |
| To sign | MdFinalSign count | List → Queue tab |
| In flight | CustomerConfirm + Costing count | List → In Flight tab |
| Signed today | Signed reqs `WHERE updatedAt::date = today` | List → Done tab |

## 6. Screen specs

### 6.1 Dashboard (`(md)/index.tsx`)

```
┌─────────────────────────┐
│ Welcome, [MD name]      │
│ ┌──────────────────────┐│
│ │ KPI HERO              ││
│ │  📋 To price          ││
│ │  N requisitions       ││
│ └──────────────────────┘│
│                         │
│ [N] To sign  [N] In flight │
│ [N] Signed today           │
│                         │
│ ⚠️ No signature uploaded   │
│ → Tap to upload           │
└─────────────────────────┘
```

Inline banner ("⚠️ No signature uploaded") visible only when `useOwnSignature()` returns 404. Tap routes to `/profile`.

### 6.2 List (`(md)/list.tsx`)

4-tab control. `<ReqCard>` (from D-2's promotion to `src/components/`) reused. Sub-filter chips on `In Flight` for the All/Customer/Costing split.

### 6.3 Detail page (`(md)/[id].tsx`)

```ts
if (req.status === "MdPricing")     return <ActiveMdPricingView req={req} />;
if (req.status === "MdFinalSign")   return <ActiveMdFinalSignView req={req} />;
return <ReadonlyMdView req={req} />;
```

V3-status-not-recognized fallback (matches D-1 pattern): error card "view on web".

### 6.4 ActiveMdPricingView

Per-FG editable card:
```
┌────────────────────────────┐
│ FG 1: PE Bag 30kg · 5000kg │
│   Cost/kg:  AED 3.20       │
│   Margin/kg [    1.80    ] │
│   Sale/kg:  AED 5.00 ←live │
│   Total:    AED 25,000     │
└────────────────────────────┘
```

- `Cost/kg` is `req.finishedGoods[i].costs[0].totalCostPerKg` from the existing V3Requisition payload (already populated by D-2's accountant flow).
- `Margin/kg` is text input parsed to decimal. Default empty (placeholder "0.00").
- `Sale/kg` and `Total` are derived live from inputs (mirrors backend `FinalPriceComputer`).
- Grand-total row at bottom of FG list.
- `Notes` optional textarea.
- **Reject button** (left) → `<RejectReqModal>` → reason text → `POST /api/approvals/{id}/reject`.
- **Approve button** (right, primary) — enabled only when all margins parse to ≥ 0 → `POST /api/approvals/{id}/set-margin`.

### 6.5 ActiveMdFinalSignView

```
┌────────────────────────────┐
│ ⚠️ Sign & Lock — irreversible │
├────────────────────────────┤
│ Customer + summary         │
├────────────────────────────┤
│ FG 1: 5000kg × AED 5.00    │
│       = AED 25,000         │
│ FG 2: 1000kg × AED 6.50    │
│       = AED 6,500          │
│ ─────────────────────────  │
│ TOTAL: AED 31,500          │
├────────────────────────────┤
│ Your signature:            │
│ [signature.png preview]    │
├────────────────────────────┤
│ Notes: [optional]          │
│ Type SIGN: [_________]     │
├────────────────────────────┤
│ [ Cancel ] [ Sign & Lock ] │
└────────────────────────────┘
```

- Top warning banner (orange `#fed7aa` bg, dark `#9a3412` text).
- Pricing summary table from `req.finalPrice` payload.
- Signature preview (`<Image source={{ uri, headers: { Authorization: bearer } }}>`). If `useOwnSignature()` returns 404 → show full-screen blocker:
  ```
  ⚠️ No signature uploaded
  Please upload your signature in Profile before signing.
  [ Open Profile ]
  ```
- "Type SIGN" input. `Sign and Lock` button enabled only when input === "SIGN" (case-sensitive — matches backend D22).
- On submit → `POST /api/approvals/{id}/final-sign` with `{ confirmationToken: "SIGN", notes? }`.

### 6.6 ReadonlyMdView

Same shape as D-1's `ReadonlyDetailView`:
- Status banner at top (info/danger/muted tone per status).
- Per-FG read cards (no edit) — reuse D-1 `<FgReadCard>`.
- For Signed status: append `<FinalPriceCard req={req} />` (existing D-1 component; works once `finalPrice` ships).
- For Rejected/Cancelled: reason banner.

### 6.7 Profile signature section

Extend `app/profile.tsx` — MD-role-gated section:

```
─────────────────────────
Signature (MD only)
[ Existing image preview if uploaded ]
[ Upload from gallery ] [ Take photo ]
Max 500KB · PNG/JPG
Last uploaded: 2 days ago
```

- `expo-image-picker` for both `launchImageLibraryAsync` (gallery) and `launchCameraAsync` (camera).
- Permissions requested at first use; explainer dialog before camera permission.
- Uploaded → multipart `POST /api/profile/signature`.
- Preview re-fetched via `GET /api/profile/signature` after upload (cache-busted).

## 7. Code architecture

### 7.1 Purge list

V2.3 MD app routes:
- `bom-mobile/app/(md)/[id].tsx`
- `bom-mobile/app/(md)/pending.tsx`
- `bom-mobile/app/(md)/historical/[id].tsx`
- `bom-mobile/app/(md)/historical/_layout.tsx` (if exists)
- `bom-mobile/app/(md)/item/[reqId]/[itemId].tsx`
- `bom-mobile/app/(md)/item/` (folder)

V2.3 MD-only components:
- `bom-mobile/src/components/HistoricalRequisitionScreen.tsx`
- `bom-mobile/src/components/BranchSwapSheet.tsx`
- `bom-mobile/src/components/BranchChangeHistorySheet.tsx`
- `bom-mobile/src/components/RequisitionCard.tsx` (V3 `ReqCard.tsx` is canonical)

Net delete: ~1100 lines.

### 7.2 New routes

```
bom-mobile/app/(md)/
  _layout.tsx        [keep — MD role guard]
  index.tsx          [REWRITE — MD V3 dashboard]
  list.tsx           [NEW]
  [id].tsx           [REWRITE — status-branched detail]
```

### 7.3 New / rewritten components

```
bom-mobile/src/features/md/
  dashboard/
    MdDashboard.tsx                — orchestrator
    MdKpiHero.tsx                  — large "To price" card
    MdKpiRow.tsx                   — 3-card secondary row
    SignatureMissingBanner.tsx     — warning + "Open Profile" tap
  list/
    MdListScreen.tsx
    MdTabs.tsx                     — 4-tab control
    InFlightSubFilter.tsx          — All/Customer/Costing chips
    tabFilters.ts                  — pure functions: tab+sub → status[]
  detail/
    MdDetailScreen.tsx             — status branch dispatcher
    ActiveMdPricingView.tsx
    ActiveMdFinalSignView.tsx
    ReadonlyMdView.tsx
    FgPricingCard.tsx              — editable per-FG with live preview
    FinalSignSummary.tsx           — pricing table for sign screen
    SignaturePreview.tsx           — auth-headered Image
  modal/
    RejectReqModal.tsx             — reason input + POST
  state/
    useMdPricingState.ts
    finalPriceClient.ts            — mirror of backend FinalPriceComputer
  api/
    approvals.ts                   — useSetMargin, useRejectRequisition, useFinalSign

bom-mobile/src/features/profile/
  ProfileSignatureSection.tsx
  api/
    signature.ts                   — useUploadSignature, useOwnSignature
```

### 7.4 API hook strategy

| Hook | Path | Method |
|---|---|---|
| `useSetMargin(reqId)` | `/api/approvals/{id}/set-margin` | POST |
| `useRejectRequisition(reqId)` | `/api/approvals/{id}/reject` | POST |
| `useFinalSign(reqId)` | `/api/approvals/{id}/final-sign` | POST |
| `useUploadSignature()` | `/api/profile/signature` | POST multipart |
| `useOwnSignature()` | `/api/profile/signature` | GET (returns image blob URL) |

All mutation hooks invalidate `requisitionKeys.detail(id)` + `requisitionKeys.lists()`. `useUploadSignature` invalidates `profileKeys.signature()`.

### 7.5 Reused components (no changes)

| Component | Source | Use |
|---|---|---|
| `<ReqCard>` | `src/components/` (D-2) | MD list cards |
| `<FinalPriceCard>` | `src/features/sales/detail/` (D-1) | Signed read view |
| `<DetailHeader>`, `<FgReadCard>` | `src/features/sales/detail/` (D-1) | ReadonlyMdView |
| `<ScreenHeader>`, `<NotificationBell>` | `src/components/` | All MD screens |
| `<ErrorBanner>`, `<LoadingView>`, `<EmptyState>` | `src/components/` | List + detail |

`<KpiHeroCard>`, `<KpiRow>` from D-2 accountant: **promote to `src/components/`** if generic enough; else copy + customize.

## 8. Backend prerequisites

| ID | What |
|---|---|
| **B1** | `V3Requisition.finalPrice` field on response. Populated when `status ∈ {MdFinalSign, Signed}`, null otherwise. |
| **B2** | `FinalPriceComputer` service (pure compute, DI singleton). Inputs: approval + req → outputs: per-FG sale/total + grand totalAed. Unit-tested. |
| **B3** | `/api/stats/v3-dashboard` extended with MD-role branch returning `{ toPrice, toSign, inFlight, signedToday }`. |
| **B4** | `set-margin` notification list extended to include accountants assigned to the branch (currently SP-only). |

`finalPrice` payload shape:
```ts
{
  totalAed: number,
  currencyCode: string,
  rateSnapshot: number | null,    // null when AED
  perFg: [
    {
      requisitionItemId: number,
      itemId: number,
      description: string,
      expectedQty: number,
      costPerKg: number,           // req currency
      marginPerKg: number,         // req currency
      salePerKg: number,           // cost + margin (req currency)
      salePerKgAed: number,        // sale × rateSnapshot (or sale if AED)
      totalAed: number,            // salePerKgAed × expectedQty
    }
  ]
}
```

## 9. Deploy strategy

### 9.1 EAS — OTA vs rebuild

D-3 adds **`expo-image-picker`** — a native module. Per CLAUDE.md mobile drift table, this requires **rebuild** (`eas build --profile preview --platform android`), not OTA.

### 9.2 Drift check before D-3 ship

```bash
git log mobile-shipped-vc1..HEAD --oneline -- bom-mobile/
```

Will show D-1 + D-2 + D-3 commits. All ride the new build.

After D-3 merge:
```bash
eas build --profile preview --platform android
git tag mobile-shipped-vc2 <sha> -m "EAS preview build vc2: 2026-05-XX"
git push origin mobile-shipped-vc2
```

D-1 and D-2 will smoke together with D-3 on `mobile-shipped-vc2`.

## 10. Testing strategy

### Backend

- `FinalPriceComputerTests` — pure unit tests covering AED + foreign FX, multi-FG, zero-margin, missing-approval cases.
- `SetMarginTests` extended — assert `finalPrice` shape after subsequent `GET /api/requisitions/{id}`.
- `MdDashboardTests` — 4 KPI counts under varied status mixes.
- `NotificationResilienceTests` extended — `set-margin` notifies branch accountants.

### Mobile

- `useMdPricingState` jest — margin parse, `isValid`, `liveFinalPrice` matches backend cases.
- `finalPriceClient` jest — pure compute mirror tested against same fixtures as backend.
- No Detox.

### On-device smoke (post-rebuild, manual)

1. Login as MD.
2. Dashboard renders 4 KPI cards.
3. Profile → Upload signature from gallery works.
4. Profile → Take photo for signature works.
5. Profile shows preview of uploaded signature.
6. List → Queue tab → tap MdPricing req → ActiveMdPricingView opens.
7. Enter margins → live preview updates → Approve & send → status flips to CustomerConfirm → list refreshes.
8. Tap MdFinalSign req → ActiveMdFinalSignView opens with full pricing summary + signature preview.
9. Type "SIGN" → tap Sign and Lock → status → Signed.
10. Open Signed req → ReadonlyMdView shows `<FinalPriceCard>` with totalAed.
11. Reject path: tap MdPricing req → tap Reject → reason → submit → status → Rejected.
12. Block test: with no signature uploaded → MdFinalSign blocks with "Open Profile" CTA.
13. D-1 SP smoke: SP CustomerConfirm modal still works.
14. D-2 accountant smoke: drawer Save & Close + Submit to MD still works.

## 11. Acceptance criteria

- [ ] `FinalPriceComputer` shipped with unit tests
- [ ] `V3Requisition.finalPrice` populated for status ∈ {MdFinalSign, Signed}, null otherwise
- [ ] `set-margin` notifies branch accountants
- [ ] `/api/stats/v3-dashboard` returns MD-role 4 counts
- [ ] All V2.3 MD routes deleted
- [ ] All V2.3 MD-only components deleted
- [ ] Mobile MD dashboard with 4 KPI cards + signature-missing banner
- [ ] Mobile MD 4-tab list with In Flight sub-filter
- [ ] `ActiveMdPricingView` with cost/margin/sale/total + live preview + Reject CTA
- [ ] `ActiveMdFinalSignView` with full summary + signature preview + SIGN-token gate
- [ ] `ReadonlyMdView` for non-active statuses (Costing/CustomerConfirm/Signed/Rejected/Cancelled)
- [ ] Profile signature upload section (gallery + camera) MD-only
- [ ] `tsc --noEmit` net-improved (V2.3 MD residuals from D-2's 19-error baseline → zero)
- [ ] On-device smoke checklist passes (incl. D-1 + D-2 regression checks)
- [ ] EAS rebuild + `mobile-shipped-vc2` tag pushed

## 12. Open questions / risks

| Item | Status |
|---|---|
| `expo-image-picker` permissions on Android | Add `READ_MEDIA_IMAGES` + `CAMERA` to `app.config.ts` plugins config; rebuild required |
| Signature `<Image>` auth header on Android | RN `<Image>` supports `headers` prop; verify behavior on Android emulator before relying on it |
| Live final-price client/server math drift | `finalPriceClient.ts` mirror tested against same fixtures as backend `FinalPriceComputer` |
| MD signs from web AND mobile concurrently | Backend state machine + `IsSuperseded` guards make this safe |
| MD has multiple signatures? | OUT OF SCOPE — single signature per user |
| Per-quote signature override | OUT OF SCOPE — single MD, single signature |

## 13. Implementation phasing within D-3

Subagent-driven workflow (validated in D-1/D-2): single PR with internal phases.

| Phase | Tasks | Notes |
|---|---|---|
| **Phase 0 — Backend** | B1 finalPrice DTO + integration into Get, B2 FinalPriceComputer + unit tests, B3 stats MD branch, B4 set-margin notify accountants | Single PR, tested locally + CI |
| **Phase 1 — Mobile prep** | API hooks + state hooks + types | Foundation work |
| **Phase 2 — V2.3 purge + skeleton** | Delete V2.3 routes + components; create empty V3 structure | tsc improves significantly |
| **Phase 3 — Profile signature** | `<ProfileSignatureSection>` + `expo-image-picker` install + permissions config | Independent, can ship first |
| **Phase 4 — MdDashboard + List** | KPI cards, 4-tab list, sub-filters | Mirrors D-2 patterns |
| **Phase 5 — ActiveMdPricingView** | Per-FG card, live preview, reject modal, submit flow | Most complex screen |
| **Phase 6 — ActiveMdFinalSignView** | Full summary, signature preview, SIGN-token, lock flow | High-stakes screen |
| **Phase 7 — ReadonlyMdView** | Status-branched read views | Last; reuses D-1 components |
| **Phase 8 — Smoke + EAS rebuild + tag** | Manual smoke; `eas build` + `mobile-shipped-vc2` tag |

## 14. Maintenance

This spec captures decisions Q1–Q5 from the 2026-05-02 brainstorm session. After D-3 ships, V3 mobile rebuild is complete (SP + Accountant + MD all V3-native, V2.3 mobile code purged entirely). Future workflow changes should reference V3 status enum and the 3-role split (SP / Accountant / MD).
