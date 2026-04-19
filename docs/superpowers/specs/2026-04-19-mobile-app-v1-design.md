# Mobile App V1 — Design Spec

**Date:** 2026-04-19
**Status:** Approved for planning
**Scope:** iOS-first mobile app for SalesPerson + ManagingDirector roles

---

## 1. Goals

Build a React Native + Expo mobile app that lets two roles work from their phones:

- **SalesPerson** — create quotation requisitions (multi-item), view status, download approved PDF.
- **ManagingDirector** — see pending approvals, set per-item prices, approve or reject.

Other roles (BomCreator, Accountant) remain web-only — their screens are keyboard-heavy and not well suited to mobile.

**V1 non-goals:** Android builds, push notifications, offline queue, BOM/costing entry, SalesPerson edit/cancel/resubmit, monorepo types.

---

## 2. Architecture

### 2.1 Tech stack

| Layer | Choice |
|---|---|
| Runtime | Expo SDK 52 (managed workflow), React Native 0.76+, React 19 |
| Language | TypeScript |
| Navigation | Expo Router (file-based) |
| Data fetching | TanStack Query v5 + Axios (mirrors web patterns) |
| Auth storage | `expo-secure-store` (iOS Keychain-backed) |
| Real-time | `@microsoft/signalr` (same library as web) |
| Forms | React Hook Form + Zod |
| UI | React Native core + NativeWind (Tailwind for RN) — matches web palette |
| Icons | `@expo/vector-icons` |

### 2.2 High-level decisions

- **Single app, role-based routing** — login determines which stack renders (`(sales)` or `(md)`). One App Store listing, shared auth + notifications + API layer.
- **iOS first; Android deferred to phase 2.** Dev on Windows via Expo Go on real iPhone; production builds via EAS Build (cloud). Apple Developer account required.
- **Shared types are copied manually** from `bom-web/src/types/api.ts` to `bom-mobile/src/types/api.ts` at V1 start. Drift is tolerated given the small V1 feature surface; monorepo migration is a phase-2 option.
- **Online-only.** TanStack Query built-in retry + error banners. No offline draft queue.
- **Real-time only in foreground** — SignalR connects while app is open; on app resume, refetch lists. No push / APNs in V1.
- **Backend changes: zero.** All endpoints + SignalR hub are used as-is.

### 2.3 Directory layout

Repo root:

```
BOM_Price_Approval/
  BomPriceApproval.API/        (existing)
  bom-web/                     (existing)
  bom-mobile/                  (new)
```

Inside `bom-mobile/`:

```
bom-mobile/
  app/                                   # Expo Router file-based routes
    _layout.tsx                          # QueryClient, AuthProvider, SignalRProvider, ToastHost
    index.tsx                            # Redirect to /login or role home
    login.tsx                            # Login screen
    (sales)/
      _layout.tsx                        # Role guard: SalesPerson
      index.tsx                          # Requisitions list
      new.tsx                            # Create requisition
      [id].tsx                           # Requisition detail
    (md)/
      _layout.tsx                        # Role guard: ManagingDirector
      index.tsx                          # Pending approvals list
      [id].tsx                           # Approval detail
    notifications.tsx                    # Shared notifications list
    profile.tsx                          # User info + logout
  src/
    api/
      client.ts                          # Axios + request/response interceptors
      auth.ts                            # login, refresh, logout
      requisitions.ts
      approvals.ts
      notifications.ts
      lookups.ts                         # customers, items pickers
    auth/
      AuthContext.tsx
      secureStore.ts                     # expo-secure-store wrapper
    signalr/
      SignalRProvider.tsx
      useNotificationEvents.ts
    components/
      Button.tsx, Input.tsx, ConfirmDialog.tsx,
      EmptyState.tsx, ErrorBanner.tsx, LoadingView.tsx,
      StatusPill.tsx, Toast.tsx
    types/
      api.ts                             # Manual copy of web types
    theme/
      tokens.ts                          # Colors, spacing, typography
      nativewind.config.ts
    utils/
      dates.ts, numbers.ts, validation.ts
  app.json                               # Expo config
  eas.json                               # EAS Build profiles
  package.json
  tsconfig.json
  babel.config.js
  README.md
```

### 2.4 Ports & networking

- Expo dev: Metro 8081, Expo CLI 19000–19002 (no conflict with web/API).
- Backend remains on 7300.
- Dev-time API base URL = `http://<dev-machine-LAN-ip>:7300` so the iPhone (same WiFi) can reach it.

---

## 3. Auth flow

### 3.1 Login

- `app/login.tsx` renders email + password inputs.
- On submit → `POST /api/auth/login` → `{ accessToken, refreshToken, user }`.
- Store tokens in `expo-secure-store` (iOS Keychain).
- Set user in `AuthContext`; router redirects based on `user.role`:
  - `SalesPerson` → `/(sales)`
  - `ManagingDirector` → `/(md)`
  - any other role → show error "This app is for Sales and Management only", force logout.

### 3.2 Token lifecycle

- Access token TTL = 15 min (backend default, unchanged).
- Request interceptor attaches `Authorization: Bearer <accessToken>`.
- Response interceptor on `401 token_expired` → single in-flight refresh via `POST /api/auth/refresh` → retry original request. Concurrent race is handled by sharing one in-flight promise (same pattern already solved in web).
- Refresh failure → clear secure store, redirect to `/login`.

### 3.3 Logout

- `POST /api/auth/logout` (revokes server-side refresh token).
- Disconnect SignalR.
- Clear secure store.
- Redirect to `/login`.

### 3.4 Cold start

Root `_layout.tsx` reads tokens from secure store on mount:

- Access + refresh present → set auth state, route to role home.
- Only refresh present → try refresh; success → home, fail → login.
- Neither → login.

### 3.5 Branch isolation

JWT carries `BranchId` — backend scopes queries. SalesPerson sees own branch + own requisitions. MD sees all. **No mobile-side enforcement.**

---

## 4. SalesPerson screens

### 4.1 Requisitions list — `app/(sales)/index.tsx`

- Data: `GET /api/requisitions` (backend filters to own branch + own requisitions).
- UI: `FlatList` of cards (RefNo, customer, status pill, item count, created date).
- Status pill colors: BomPending / CostingPending = amber; BomInProgress / CostingInProgress = blue; MdReview = purple; Approved = green; Rejected = red.
- Pull-to-refresh.
- Empty state: "No requisitions yet. Tap + to create one."
- Top-right FAB `+` → `/new`.
- Tap card → `/[id]`.

V1 has a single fetch; pagination is deferred.

### 4.2 Create requisition — `app/(sales)/new.tsx`

- React Hook Form + Zod.
- Fields:
  - **Customer** (required) — searchable picker from `GET /api/customers` (branch-scoped).
  - **Delivery notes** (optional, multiline).
  - **Items (repeatable, min 1)** — Item picker + ExpectedQty (>0). "+ Add item" button appends; trash icon removes (except the last remaining item).
- Validation matches backend DTO.
- Submit → `POST /api/requisitions` → success toast + replace-route to `/[newId]`.
- Error handling:
  - 400 per-field → inline errors under the offending field.
  - 409 / network → top error banner with Retry.

### 4.3 Requisition detail — `app/(sales)/[id].tsx`

- Data: `GET /api/requisitions/{id}`.
- Sections:
  - **Header** — RefNo, customer, status pill, created date.
  - **Items** — each with qty + per-item stage indicator (BOM done ✓, Costing done ✓, Price set ✓).
  - **Timeline** — ordered events (created, BOM submitted, costing submitted, approved/rejected with rejection reason).
  - **If Approved** — "Download PDF" button → `GET /api/requisitions/{id}/pdf` → save via `expo-file-system` → open with `expo-sharing` system viewer.
- V1 is read-only from SalesPerson side; edit / cancel / resubmit are phase 2.

---

## 5. ManagingDirector screens

### 5.1 Pending approvals list — `app/(md)/index.tsx`

- Data: `GET /api/requisitions?status=MdReview` (all branches).
- Sort oldest first (FIFO — what has waited longest).
- Card fields: RefNo, branch, customer, item count, total expected qty, submitted date, sales person name.
- Pull-to-refresh.
- Empty state: "Nothing pending. You're all caught up."
- SignalR `RequisitionSubmittedToMd` → invalidate this list.
- Tap card → `/[id]`.

### 5.2 Approval detail — `app/(md)/[id].tsx`

- Data: `GET /api/requisitions/{id}` (includes per-item costing + suggested margin).
- Sections:
  - **Header** — RefNo, customer, branch, sales person.
  - **Per-item approval rows**, each with:
    - Item name, qty, cost (from costing), current margin %, computed price.
    - **Editable fields:** margin % OR fixed price (either drives the other on-change). Only `price` is sent to the backend; margin is a UI convenience.
  - **Grand total** sticky at bottom.
- Sticky footer actions:
  - **Approve** → `ConfirmDialog` ("Approve and send quotation?") → `POST /api/approvals/{id}/approve` with per-item prices → toast "Quotation dispatched" → back to list.
  - **Reject** → prompt for `RejectionReason` (required multiline) → `POST /api/approvals/{id}/reject` → toast → back to list.
- Error handling:
  - 409 concurrent change → "This requisition has changed. Reloading." → refetch (preserve user-entered prices where item IDs still match).
  - 400 per-field → inline errors.
  - Network fail → retry banner; do not discard local price entries.

After Approve, backend handles PDF generation + customer email (existing behavior).

---

## 6. Shared infrastructure

### 6.1 Notifications screen — `app/notifications.tsx`

- Data: `GET /api/notifications` (paginated).
- Unread items bold; tap marks as read (`PATCH /api/notifications/{id}/read`) and navigates to the relevant requisition (sales or md route by role).
- SignalR `NotificationCreated` → prepend to list, bump unread badge.
- Tab badge shows unread count.

### 6.2 SignalR client

- `SignalRProvider` establishes connection after successful auth using `accessToken`.
- Auto-reconnect on transient disconnects (library default).
- On auth change (logout) → stop connection.
- On app resume (foreground event via `AppState`) → refetch role home list + notifications (belt-and-suspenders in case of missed events).
- Group join is implicit via JWT claims (backend handles).

### 6.3 Theme

`src/theme/tokens.ts` mirrors `bom-web/src/index.css` custom properties — same palette and spacing scale. NativeWind classes match Tailwind class names used on web so visual parity is straightforward.

---

## 7. Error / empty / loading states

Every list screen renders one of:

- **Loading** — centered `ActivityIndicator`.
- **Error** — `ErrorBanner` with retry button; uses `error.response?.data` problem-details message when available.
- **Empty** — `EmptyState` with icon + guidance text.
- **Data** — the actual content.

Every mutation uses:

- Optimistic toast on success (green).
- Inline per-field errors for 400.
- Top banner for network / 5xx / 409.

---

## 8. Testing

### 8.1 Unit tests

- Jest + React Native Testing Library.
- Coverage areas:
  - Axios interceptor: 401 refresh flow, concurrent 401 race (single in-flight promise).
  - Form validation: create-requisition schema, approval-price schema.
  - Role guard routing.
- Target ~20–30 unit tests V1.

### 8.2 Integration / E2E

Skipped V1. Manual on-device testing via Expo Go on real iPhone.

### 8.3 Backend

Zero new backend tests — no endpoint changes.

### 8.4 CI

Extend `.github/workflows/ci.yml` with a `bom-mobile` job: install → type-check → lint → unit tests. No EAS Build in CI (manual for V1).

---

## 9. Build & deployment

### 9.1 Dev loop (Windows-friendly)

1. `cd bom-mobile && npx expo start`.
2. Scan QR code from real iPhone in Expo Go app.
3. Backend runs locally (`dotnet run --project BomPriceApproval.API`).
4. iPhone reaches API over LAN at `http://<dev-machine-LAN-ip>:7300`.
5. Firewall rule needed on dev machine to allow inbound 7300 on the LAN.

### 9.2 EAS Build profiles (`eas.json`)

| Profile | Purpose |
|---|---|
| `development` | Dev client build for internal engineer testing |
| `preview` | Internal distribution IPA (ad-hoc) for MD + Sales beta testers |
| `production` | App Store / TestFlight submission |

### 9.3 Apple setup (user's task, documented in README)

- Apple Developer account ($99/yr) — individual or company.
- Bundle ID: `ae.fpf.quotations` (confirm with user).
- Register test devices (UDIDs) in Apple portal for preview IPAs.
- TestFlight for beta → App Store submission for production.

### 9.4 Environment config

- `.env.development` — `API_BASE_URL=http://<dev-LAN-ip>:7300`.
- `.env.production` — `API_BASE_URL=https://<prod-domain>` (user provides prod URL before first production build).
- Loaded via `expo-constants` + `app.config.ts` → exposed as `Constants.expoConfig.extra.apiBaseUrl`.

---

## 10. Success criteria

V1 is "done" when all five hold on a real iPhone with production build (TestFlight):

1. SalesPerson can log in, create a multi-item requisition, view its status, and download the approved PDF.
2. MD can log in, see pending approvals, enter per-item prices, and approve or reject (with reason).
3. SignalR events refresh the relevant list while the app is in the foreground.
4. Tokens persist across app restart; 401 refresh works transparently; logout revokes the refresh token server-side.
5. The app installs for MD + Sales team members via TestFlight.

---

## 11. Out of scope for V1

- Android build.
- Push notifications / APNs / Expo Push.
- Offline draft queue / background sync.
- BomCreator + Accountant screens.
- SalesPerson edit / cancel / resubmit.
- Monorepo / shared types package.
- In-app PDF preview (V1 opens system viewer).
- Automated App Store submission (manual EAS Submit V1).
- Pagination on list screens.
