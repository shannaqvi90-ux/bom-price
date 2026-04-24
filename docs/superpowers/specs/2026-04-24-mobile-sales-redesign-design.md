# Mobile Sales Stack Redesign — Design Spec

**Date:** 2026-04-24
**Author:** Shan (with Claude assistance)
**Status:** Draft — pending user review
**Parent design direction:** `feedback_design_direction.md` (corporate + playful; locked 2026-04-22)
**Predecessor:** `2026-04-22-mobile-redesign-corporate-playful-design.md` (applied to MD stack)

---

## 1. Problem

The mobile MD stack (`(md)/*`) and shared components (`ScreenHeader`, `BomDetailSheet`, `CustomerQuickCreateSheet` as of 2026-04-24 polish) follow the corporate + playful language — Stripe/Vercel-style crisp surfaces with Moti spring motion, factory-blue `#1e40af` primary, slate-50 background, bigger typography. The Sales stack (`(sales)/_layout.tsx`, `(sales)/index.tsx`, `(sales)/new.tsx`, `(sales)/[id].tsx`) still uses:

- `brand-*` NativeWind aliases (`brand-600`, `brand-700` — aliases kept for back-compat but visually identical to primary; the issue is inconsistency, not color)
- The default expo-router stack header (different look-and-feel from `ScreenHeader`)
- Smaller fonts (title `text-xl = 20pt`, body `text-sm = 14pt`) below the 22+/15+ locked spec
- No Moti animations on sales-specific UI (except where inherited from reused components)
- Flat form layout without grouped section-cards
- The `CustomerQuickCreateSheet` was already polished in `63b1402`; the remaining gap is the parent Sales screens

The bottom-sheet polish committed in `63b1402` is out of scope for this spec (already shipped). The new-req, list, and detail screens are in scope.

## 2. Goals

1. Sales stack visual consistency with MD stack — same `ScreenHeader` pattern, same card style, same tokens.
2. Replace `brand-*` aliases with factory-blue `#1e40af` and slate tokens throughout.
3. Apply spec-mandated typography (titles 22+, body 15+, meta 13).
4. Group the new-requisition form into section-cards for scannability.
5. No behavior regressions — customer auto-select, currency chip toggle, item add/remove, form validation, PDF download, rejection notes visibility all preserved.
6. All 33 existing Jest tests remain green.

## 3. Non-goals

- No API changes.
- No Accountant mobile stack (they use the web flow for now).
- No new animations beyond what the locked direction prescribes (stagger on list, spring on header entrance, haptics on press).
- No redesign of shared components already polished (`ScreenHeader`, `RequisitionCard`, `CustomerQuickCreateSheet`, `StatusPill`, `ItemStageBadge`).
- No visual regression test harness.
- No dark-mode audit (current state ambivalent, out of scope).

## 4. Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                  Sales stack (expo-router)                   │
│                                                              │
│  (sales)/_layout.tsx                                         │
│    └─ Stack screenOptions: { headerShown: false }            │
│       (HeaderRight moves into each screen's ScreenHeader)    │
│                                                              │
│  (sales)/index.tsx    (sales)/new.tsx    (sales)/[id].tsx    │
│    ↓                      ↓                      ↓           │
│  ScreenHeader        ScreenHeader           ScreenHeader     │
│    label=SALES         title="New..."         label=QUOT…    │
│    title=Requisitions                         title=refNo    │
│    count=N                                    right=Status   │
│    right=HeaderRight  right=HeaderRight                      │
│    ↓                      ↓                      ↓           │
│  RequisitionCard     Section-cards          Section-cards    │
│    (FlatList)          Customer · Currency     Customer      │
│    + FAB               · Items · Submit        · Items       │
│                                                · Approval    │
└─────────────────────────────────────────────────────────────┘
```

All three screens use the same `ScreenHeader` already present at `bom-mobile/src/components/ScreenHeader.tsx`. `HeaderRight` becomes a local component (or reused from the current `_layout.tsx`) that wraps `NotificationBell` + the Logout button styled with the new primary color.

## 5. Screen Specs

### 5.1 `_layout.tsx`

Before: `headerShown: true` and `headerRight: () => <HeaderRight />`.
After:
- `headerShown: false` on stack `screenOptions`
- `HeaderRight` component extracted into a shared file (e.g., `bom-mobile/src/components/SalesHeaderRight.tsx`) so each sales screen can pass it into `ScreenHeader.right`
- `HeaderRight` internal styling: logout text color changes from `text-brand-600` to factory-blue (via inline style `color: "#1e40af"`) for consistency with MD stack patterns that use inline styles

Route guard + auth behavior unchanged.

### 5.2 `index.tsx` (list)

Structure:
```
<View flex:1, bg:#f8fafc>
  <ScreenHeader
    label="SALES"
    title="Requisitions"
    count={q.data?.length ?? 0}
    right={<SalesHeaderRight/>}
  />
  {q.isError && <ErrorBanner .../>}
  <FlatList
    data={q.data ?? []}
    renderItem={<RequisitionCard onPress={...}/>}
    refreshControl={<RefreshControl .../>}
    ListEmptyComponent={<EmptyState ...">}
  />
  <Pressable onPress={→ new} className="absolute bottom-6 right-6 rounded-full w-14 h-14 items-center justify-center shadow-lg" style={{ backgroundColor: "#1e40af" }}>
    <Text className="text-white text-3xl leading-none">+</Text>
  </Pressable>
</View>
```

- `LoadingView` stays for the pending state.
- `bg-brand-600 active:bg-brand-700` on the FAB is replaced with an inline `backgroundColor: "#1e40af"` + `active:opacity-90` (or keep with the new tokenized class if tailwind-config is extended; inline keeps the screen independent of alias changes).
- RequisitionCard already has the correct design language (per MD stack use). No card-level changes.

### 5.3 `new.tsx` (form)

Structure (top to bottom):
```
<KeyboardAvoidingView>
  <ScrollView>
    <ScreenHeader title="New requisition" right={<SalesHeaderRight/>} />
    {topError && <ErrorBanner .../>}

    <SectionCard title="Customer">
      <SearchablePicker label=null ... customer ... />
      <Text onPress={() => setAddSheetOpen(true)} ...>+ New customer</Text>
      <CustomerQuickCreateSheet ... />
    </SectionCard>

    <SectionCard title="Currency">
      <chip row — factory-blue active state />
      {errors.currencyCode && ...}
    </SectionCard>

    {/* Items are a labeled LIST (not nested in a SectionCard) — each item is its
        own bordered card to mirror the mockup and avoid nested borders. */}
    <Text style={labelStyle}>{`ITEMS (${fields.length})`}</Text>
    {fields.map((f, idx) => (
      <View key={f.id} style={itemCardStyle}>
        {/* existing item row content: item picker, qty, Remove link — restyled with tokens */}
      </View>
    ))}
    <Text onPress={append} style={linkStyle}>+ Add another item</Text>
    {errors.items?.root && <Text style={errorStyle}>...</Text>}

    <Button title="Create requisition" onPress={onSubmit} loading={...} />
  </ScrollView>
</KeyboardAvoidingView>
```

- `SectionCard` is a new local component in the same file (not extracted):
  ```
  function SectionCard({ title, children }: { title: string; children: ReactNode }) {
    return (
      <View style={{ backgroundColor: "#ffffff", borderWidth: 1, borderColor: "#e2e8f0", borderRadius: 14, padding: 14, marginBottom: 12 }}>
        <Text style={{ fontSize: 13, fontWeight: "700", color: "#64748b", marginBottom: 10, letterSpacing: 0.3 }}>{title.toUpperCase()}</Text>
        {children}
      </View>
    );
  }
  ```
- Currency chip styles: active = `backgroundColor: "#1e40af"` + white text; inactive = white bg + slate-300 border + slate-700 text.
- "+ New customer" / "+ Add another item" link color: `#1e40af`, font-weight 600.
- "Create requisition" Button is the existing `<Button>` component (primary variant defaults to factory-blue already).
- No sticky submit bar (Q2=A).

### 5.4 `[id].tsx` (detail)

Structure:
```
<ScrollView bg=#f8fafc>
  <ScreenHeader
    label="QUOTATION"
    title={r.refNo}
    right={<StatusPill status={r.status} />}
  />

  {isRejected && r.approval?.notes && <RejectionCard notes={...}/>}

  <SectionCard title="Customer">
    <Text ...>{r.customerName}</Text>
    <Text meta>{r.branchName} · {r.currencyCode} · Created {formatShortDate(r.createdAt)}</Text>
  </SectionCard>

  {/* Same labeled-list pattern as new.tsx — label + individual item cards. */}
  <Text style={labelStyle}>{`ITEMS (${r.items.length})`}</Text>
  {r.items.map((it) => (
    <View key={it.id} style={itemCardStyle}>
      {/* item description + expectedQty + ItemStageBadge (existing) */}
    </View>
  ))}

  {isApproved && <Button title="Download PDF" onPress={...}/>}
  {isApproved && r.approval && <Text meta centered>Approved on {...}</Text>}
</ScrollView>
```

- `SectionCard` is shared between `new.tsx` and `[id].tsx` (for Customer + Currency cards). **Decision:** extract to `bom-mobile/src/components/SectionCard.tsx` (used on 2 screens).
- Items on both screens use a **labeled list** pattern (`ITEMS (N)` label + individual item cards) — NOT nested in a SectionCard. This avoids nested borders and matches the detail mockup picked in Q3.
- `RejectionCard`: minor inline component, rose-colored variant of SectionCard. Optional extraction; current inline fine if small.
- `StatusPill` passed into `ScreenHeader.right` — confirm `ScreenHeader` accepts any ReactNode in `right` (it does per the current signature).
- Individual items use the existing white-card + `ItemStageBadge` pattern from current `[id].tsx`. Font sizes bumped to spec.
- PDF error banner + loading state preserved.

## 6. Shared design tokens (applied, not decided)

From `feedback_design_direction.md`:

| Token | Value |
|---|---|
| Primary (factory blue) | `#1e40af` |
| Screen background | `#f8fafc` (slate-50) |
| Card background | `#ffffff` |
| Card border | `#e2e8f0` (slate-200) |
| Card radius | `12-16px` (use `14` consistently in sales) |
| Title text | `#0f172a` |
| Body text | `#334155` |
| Meta text | `#64748b` |
| Placeholder | `#94a3b8` |
| Rose (rejection) | bg `#fef2f2`, border `#fecaca`, text `#991b1b` |
| Amber (warning) | bg `#fef3c7`, text `#92400e` |
| Title font | `22-26pt`, `fontWeight: 700` |
| Body font | `15pt`, `fontWeight: 400-600` |
| Meta font | `13pt`, `fontWeight: 600` (labels) or 400 (values) |

Animations: spring (damping 14-20, stiffness 140-220). Haptics: `Haptics.selectionAsync()` on interactive taps (pickers, chips, fab).

## 7. Error Handling & Edge Cases

All preserved behavior:

| Scenario | Behavior |
|---|---|
| List fetch fails | `ErrorBanner` at top, FlatList empty below, retry triggers refetch |
| Empty list | `EmptyState` with current hint text |
| Pull-to-refresh | Preserved via `RefreshControl` |
| New-req API failure | `topError` state + `ErrorBanner` at top (unchanged) |
| Zod validation errors | Inline per-field (unchanged) |
| Detail fetch fails | `ErrorBanner` in rose body (unchanged) |
| Rejection notes visible | Only when `isRejected && r.approval?.notes` (unchanged) |
| PDF download failure | `ErrorBanner` under the download button (unchanged) |
| Missing `ScreenHeader.right` support for ReactNode | Not possible — current signature is `right?: ReactNode` |
| Safe-area handling | `ScreenHeader` already applies `useSafeAreaInsets()` top padding |

## 8. Testing Plan

- No new unit tests (pure visual reskin — behavior unchanged).
- Existing 33 Jest tests must stay green after each commit.
- `npx tsc --noEmit` must stay clean after each commit.
- Acceptance gate: **manual phone smoke on Expo Go after each phase's commit** (requires user's device).

### Smoke checklist (at end)

- List screen: ScreenHeader visible with "SALES" label + count badge + NotificationBell + Logout; FAB lands on New form
- New-req: SectionCards visible; + New customer opens bottom-sheet (already polished); submit creates req
- Detail: ScreenHeader with label + refNo + StatusPill; Customer + Items cards; if rejected, rose box visible; if approved, Download PDF works

## 9. Rollout Strategy

Three small commits (batched per `feedback_batched_commits.md` — plan presented, one approval, execute):

1. `refactor(mobile): apply ScreenHeader + design tokens to (sales)/index.tsx + _layout.tsx`
2. `refactor(mobile): regroup (sales)/new.tsx into SectionCards + new tokens`
3. `refactor(mobile): apply ScreenHeader + SectionCards to (sales)/[id].tsx`

Feature branch: `feature/mobile-sales-redesign` off master. Commits land on the branch; push deferred per ongoing security-hardening-before-push blocker.

### Out-of-scope carry-overs (post-merge candidates)

- Dark-mode audit across mobile stack
- Stagger config review (current `RequisitionCard` cap at 80ms × 20 — revisit if list grows past 40 rows)
- Extract `ItemRow` (currently duplicated inline between `new.tsx` item rows and `[id].tsx` items) into shared component if a third consumer appears
- Accountant mobile stack (depends on product decision, out of scope for V1)

## 10. Open questions

None — all 3 clarifying picks resolved (Q1=FAB, Q2=inline submit, Q3=ScreenHeader+sectioned).
