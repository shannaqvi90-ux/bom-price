# Mobile Redesign — Corporate + Playful Animations

**Date:** 2026-04-22
**Scope:** React Native mobile app (`bom-mobile/`)
**Status:** Design approved, ready for implementation plan

## 1. Goal

Replace the current glassmorphism UI (animated gradient, frosted BlurView cards) with a **Stripe/Vercel-style crisp corporate design** layered with **Duolingo-grade playful micro-interactions**.

The result should feel polished and enterprise-ready at rest, but alive and responsive when touched or updated.

## 2. Problem with current state

- Glassmorphism with animated blue gradient and floating blobs was shipped on `feature/mobile-md-features` as a spike and rejected by the user.
- Existing components `GradientBackground`, `GlassCard`, and the redesigned `login.tsx` / `(md)/index.tsx` need to be reverted or replaced.
- Packages `moti`, `expo-blur`, `expo-linear-gradient`, `expo-haptics` are already installed. Redesign uses `moti` and `expo-haptics` only. **Remove** `expo-blur` and `expo-linear-gradient` (unused after revert).

## 3. Design direction (approved)

Clean white/slate surfaces, navy-blue primary, thin borders, subtle shadows. Typography and spacing follow modern enterprise SaaS conventions (Stripe, Vercel, Linear-light).

Playfulness comes entirely from **motion**, not from bright colors or decorative shapes.

## 4. Design tokens

### 4.1 Colors

```
primary         #1e40af   navy / factory-blue
primary-soft    #dbeafe   pill/accent background
primary-fg      #ffffff   on primary

screen-bg       #f8fafc   slate-50 (app background)
surface         #ffffff   card/sheet background
border          #e2e8f0   slate-200 (card + input border)

text-strong     #0f172a   slate-900 (headings, ref numbers)
text-body       #475569   slate-600
text-muted      #94a3b8   slate-400

success         #059669   emerald-600
warning         #f59e0b   amber-500 (count badge when urgent)
danger          #dc2626   red-600 (error shake, destructive)
```

### 4.2 Radius

- Inputs / buttons: **10 px**
- Cards: **14 px**
- Modals / sheets: **20 px**
- Pills: fully rounded (`9999px`)

### 4.3 Shadows

- Card resting: `0 1px 3px rgba(0,0,0,0.04)`
- Card raised (hover/press start): `0 4px 12px rgba(0,0,0,0.08)`
- Primary button: `0 2px 4px rgba(30,64,175,0.25)`
- Count badge: `0 4px 10px rgba(30,64,175,0.3)`

### 4.4 Typography

System sans-serif (iOS: SF Pro, Android: Roboto). Weights:
- Display heading (screen title): **22px / 700 / -0.5 letter-spacing**
- Section heading: 17px / 700
- Card title (REQ-NNNN): 13–14px / 700
- Label: 11–12px / 600, uppercase optional for meta
- Body: 13–14px / 400–500
- Meta / muted: 10–11px / 500

### 4.5 Spacing

- Screen horizontal padding: **16 px**
- Screen top padding above header: **40–56 px** (accounts for safe area)
- Card internal padding: **14 px**
- Card vertical gap in list: **10 px**
- Section vertical gap: **20–24 px**

## 5. Animation contract

All animations use `moti` (declarative, already wraps `react-native-reanimated`). Haptics use `expo-haptics`.

| Interaction | Animation | Haptic |
|---|---|---|
| **Card entrance** | Spring slide-up 14 px + fade. Stagger **80 ms** per item. `damping: 14, stiffness: 140`. | — |
| **Button press** | `scale: 1 → 0.97` on `onPressIn`, spring release. | `selectionAsync` on press |
| **Primary button submit** | Brief scale pulse on success. | `notificationAsync(Success/Error/Warning)` |
| **Count badge (entrance)** | `scale: 0 → 1.2 → 1` with overshoot bounce. `cubic-bezier(.68,-.55,.27,1.55)`, 600 ms, delay 300 ms after list. | — |
| **Count badge (update)** | Brief scale pulse when number changes. | — |
| **Pull-to-refresh** | White tint spinner. Spring release. | `impactAsync(Medium)` on trigger |
| **Login form error** | Shake 3× (`translateX: 0 → -8 → 8 → -8 → 0`, 400 ms total). | `notificationAsync(Error)` |
| **Empty state icon** | Floats `translateY: 0 ↔ -4 px`, 1.8 s loop, ease-in-out. | — |
| **Loading (list)** | Skeleton card silhouettes with opacity shimmer `0.4 ↔ 1`, 1.2 s loop. | — |
| **Loading (button/inline)** | `ActivityIndicator` (native). | — |
| **Page transitions** | iOS-style push, but with spring damping so it feels alive. (Uses Expo Router defaults; override to `presentation: 'card'` with spring curve.) | — |
| **New notification (SignalR live)** | Bell icon wiggle (`rotate: 0 → -15 → 15 → 0`, 400 ms) + badge pop. | `impactAsync(Light)` |
| **Input focus** | Border color transition `#e2e8f0 → #1e40af`, 150 ms. Subtle scale 1 → 1.01. | — |

**Animation budget:** on app cold start, longest animation chain completes in ≤ 1.1 s so the user can interact quickly. No single element animates longer than 600 ms unless it's a loop (empty-state float, skeleton shimmer).

## 6. Component changes

### 6.1 Reverting / removing

- `src/components/GradientBackground.tsx` — **delete**
- `src/components/GlassCard.tsx` — **delete**
- `src/signalr/SignalRProvider.tsx` — keep the `LogLevel.None` fix (validated; unrelated to design)

### 6.2 Updating existing components

| File | Change |
|---|---|
| `tailwind.config.js` | Replace `brand.*` with new tokens (`primary`, `primary-soft`, etc.). Keep existing `status` map (reuse for status pills). |
| `src/components/Button.tsx` | Rewrite with Moti press animation (scale 0.97) + haptic on press. Variants: `primary` (navy, white text), `secondary` (white + border), `danger` (red), plus new `ghost` (text-only). |
| `src/components/Input.tsx` | Add Moti focus border transition. Keep API identical (label, value, onChangeText, error). |
| `src/components/RequisitionCard.tsx` | Restyle to new spec: white surface, thin border, ref number + status pill row, customer, item-count/currency/date meta row. Press scale to 0.98 + haptic. |
| `src/components/StatusPill.tsx` | Restyle: pill bg = soft primary `#dbeafe`, text = primary `#1e40af`, or per-status softened colors (warning/success/danger with alpha backgrounds). |
| `src/components/LoadingView.tsx` | Replace plain spinner with list of skeleton card silhouettes (for list screens) + keep simple spinner variant. |
| `src/components/EmptyState.tsx` | Add optional floating icon slot + gentle animation. |
| `src/components/NotificationBell.tsx` | Add wiggle animation on new notification arrival (trigger from SignalR event). |

### 6.3 New components

| File | Purpose |
|---|---|
| `src/components/Skeleton.tsx` | Reusable skeleton block with shimmer. Used by `LoadingView` list variant. |
| `src/components/ScreenHeader.tsx` | Shared in-screen header (label + title + optional count badge + right slot for bell/logout). Eliminates per-screen header boilerplate. |

## 7. Screen changes

### 7.1 Login (`app/login.tsx`)

**Layout (top → bottom):**
1. Safe-area padding
2. Logo square (48 × 48 px, primary bg, rounded 12, white inner square)
3. Title "Welcome back" (display heading)
4. Subtitle "Sign in to FPF Quotations" (body muted)
5. Email field (label + Input)
6. Password field (label + Input, secure)
7. Primary button "Sign in"

**Animations:**
1. Logo fades + slides up with spring entrance (delay 0)
2. Title+subtitle fade down (delay 150 ms)
3. Email input stagger (delay 250 ms)
4. Password input stagger (delay 330 ms)
5. Button entrance (delay 410 ms)
6. On submit: haptic + loading state
7. On error: form shakes + error haptic
8. On success: haptic success, `router.replace("/")`

**Uses:** `KeyboardAvoidingView`, `ScrollView` wrapping the content so it scrolls on small screens.

**No gradient, no glass.** Pure white bg.

### 7.2 MD Home — Pending Approvals (`app/(md)/index.tsx`)

**Layout:**
1. `ScreenHeader` — label "Managing Director" + title "Pending approvals" + count badge + right slot (NotificationBell + Logout button)
2. `FlatList` of `RequisitionCard`
   - `refreshControl` with pull-to-refresh (haptic + white spinner)
   - `ListEmptyComponent` → `EmptyState` with ✓ icon floating
   - Loading → skeleton list

**Animations:**
- Header fades down (delay 0)
- Count badge pops in (delay 300 ms)
- Cards stagger slide-up (delay 200 ms + 80 ms per index)
- Skeleton shimmer during `isPending`
- Pull-to-refresh haptic

**Native header:** hidden via `<Stack.Screen options={{ headerShown: false }} />` — the in-screen `ScreenHeader` replaces it. Logout button moves here.

## 8. Accessibility & polish

- Minimum touch target: 44 × 44 px (buttons, pills, bell icon all meet this).
- Text contrast: primary on white ≥ 4.5:1 (verified: #1e40af on #ffffff = 8.2:1 ✓).
- Animations respect `useReducedMotion` when detected — fall back to instant transitions.
- Haptics only on press/submit/refresh, never ambient.

## 9. Out of scope (explicit)

- Dark mode — Phase 2.
- iOS polish (shadows render differently) — design is iOS-ready but device testing Android-first.
- Sales role screens — only Login + MD Home redesigned in this pass. Sales uses the same token system but will be restyled in a later session.
- Detail screen (`app/(md)/[id].tsx`) — out of scope. Keep current implementation; will be redesigned after Login + MD Home land.
- Notifications screen — out of scope.
- Swipe gestures (swipe-to-approve) — Phase 2.
- Bottom-tab navigation — no change to navigation structure.

## 10. Success criteria

- Login screen on first cold open plays staggered entrance animation within 1.1 s, user can tap email input before animations finish.
- MD Home list renders skeleton for < 400 ms then transitions to populated cards with 80 ms stagger.
- Button press on primary CTA gives haptic tick + visible scale feedback within one frame (< 16 ms).
- Count badge "pops" (scale bounce) when entering and when value changes.
- Pull-to-refresh haptic fires at trigger point, not at release.
- No glassmorphism or gradient visible anywhere.
- `npx tsc --noEmit` clean, `npx jest` all existing tests still pass.
- On-device (Android) smoke test: login, see list, pull to refresh, tap card — all feel responsive and playful without being distracting.

## 11. Branch and delivery

Work continues on `feature/mobile-md-features` (bundled per user approval on 2026-04-22). When both this redesign and the Plan 3a MD features are device-verified, single fast-forward merge to `master`.

## 12. Estimated scope

- **~8 files** deleted/created/modified (2 components deleted, 2 new, ~6 updated, 2 screens rewritten, tailwind config updated).
- **~30–45 min** implementation time in a single focused session.
- Verification: tsc + jest suite (no new tests needed — design changes don't alter logic).
