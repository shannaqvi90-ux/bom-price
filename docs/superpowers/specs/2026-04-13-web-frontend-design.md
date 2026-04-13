# Web Frontend Design — BOM & Price Approval

**Date:** 2026-04-13
**Status:** Approved
**Author:** Brainstorming session (Claude Code + user)

---

## Overview

A React 19 + Vite web application for the BOM & Price Approval quotation workflow system used by Fujairah Plastic Factory. The frontend communicates exclusively with the existing ASP.NET Core 8 API at `http://localhost:7300`. It serves five distinct roles (SalesPerson, BomCreator, Accountant, ManagingDirector, Admin) with role-specific dashboards and enforced routing.

---

## Section 1: Tech Stack

| Layer | Choice | Notes |
|---|---|---|
| Build tool | Vite 6 | Fast HMR, ESM-native, port 5300 |
| UI framework | React 19 | Concurrent features, `use()` hook |
| Styling | TailwindCSS v4 | CSS-variable-based theming for dark/light |
| Components | Shadcn/UI | Radix primitives + Tailwind; copy-paste components |
| Animation | Framer Motion | Page transitions, sidebar collapse, timeline, toasts |
| Routing | React Router v6 | `createBrowserRouter`, nested layouts, protected routes |
| Server state | TanStack Query v5 | All API calls; caching, background refetch, optimistic updates |
| Client state | Zustand | Auth slice (user, tokens) + theme slice (dark/light) |
| HTTP client | Axios | Single instance with JWT refresh interceptor |
| Real-time | `@microsoft/signalr` | HubConnection to `/hubs/notifications` |
| Charts | Recharts | Cost breakdown pie/bar on MD approval screen |
| Forms | React Hook Form + Zod | Typed validation; no uncontrolled inputs |
| Language | TypeScript 5 | Strict mode; all API response types defined |

**Dev port:** 5300 (Vite), proxied to API at 7300 to avoid CORS in development.

---

## Section 2: Project Structure (Feature-Sliced)

```
bom-web/
  src/
    api/
      axios.ts           # Axios instance + JWT refresh interceptor
      queryClient.ts     # TanStack Query client config
    store/
      authStore.ts       # Zustand: user, accessToken, refreshToken, actions
      themeStore.ts      # Zustand: theme ('dark'|'light'), toggle action
    hooks/
      useNotifications.ts  # SignalR connection lifecycle hook
    components/
      layout/
        AppShell.tsx     # Root layout: sidebar + topbar + outlet
        Sidebar.tsx      # Collapsible sidebar (220px ↔ 60px)
        Topbar.tsx       # User menu, notification bell, theme toggle
        ProtectedRoute.tsx  # Role-guard wrapper
      ui/                # Shadcn/UI components (copied in)
      shared/
        StatusBadge.tsx        # Colour-coded requisition status pill
        RequisitionTimeline.tsx # Vertical timeline with audit trail
        NotificationToast.tsx  # Framer Motion toast stack
        SkeletonCard.tsx       # Loading skeletons
    features/
      auth/
        LoginPage.tsx
        authApi.ts
      dashboard/
        SalesDashboard.tsx
        BomDashboard.tsx
        AccountantDashboard.tsx
        MdDashboard.tsx
        AdminDashboard.tsx
      requisitions/
        RequisitionListPage.tsx
        RequisitionDetailPage.tsx
        NewRequisitionPage.tsx
        requisitionsApi.ts
      bom/
        BomEntryPage.tsx
        bomApi.ts
      costing/
        CostingEntryPage.tsx
        costingApi.ts
      approval/
        MdApprovalPage.tsx
        approvalApi.ts
      notifications/
        NotificationsPage.tsx
        notificationsApi.ts
      admin/
        UsersPage.tsx
        BranchesPage.tsx
        ExchangeRatesPage.tsx
        ItemsPage.tsx
        ItemImportPage.tsx
        adminApi.ts
    types/
      api.ts             # All response/request TS types mirroring backend DTOs
    utils/
      currency.ts        # Format with exchange rate
      date.ts            # Relative/absolute date helpers
    App.tsx              # Router definition
    main.tsx             # React 19 root, theme class init
```

All feature folders follow the same pattern: one page component per route, one `*Api.ts` for TanStack Query hooks.

---

## Section 3: Routing

All routes except `/login` are wrapped in `<ProtectedRoute>` which reads from Zustand auth store. If no valid token, redirect to `/login`. Role mismatches redirect to `/dashboard`.

| Path | Component | Roles |
|---|---|---|
| `/login` | `LoginPage` | Public |
| `/dashboard` | Role-specific dashboard (auto-detected) | All |
| `/requisitions` | `RequisitionListPage` | All except Admin |
| `/requisitions/new` | `NewRequisitionPage` | SalesPerson |
| `/requisitions/:id` | `RequisitionDetailPage` | All except Admin |
| `/requisitions/:id/bom` | `BomEntryPage` | BomCreator |
| `/requisitions/:id/costing` | `CostingEntryPage` | Accountant |
| `/requisitions/:id/approval` | `MdApprovalPage` | ManagingDirector |
| `/notifications` | `NotificationsPage` | All |
| `/admin/users` | `UsersPage` | Admin |
| `/admin/branches` | `BranchesPage` | Admin |
| `/admin/exchange-rates` | `ExchangeRatesPage` | Admin, Accountant |
| `/admin/items` | `ItemsPage` | Admin |
| `/admin/items/import` | `ItemImportPage` | Admin |

`/dashboard` renders different components based on `user.role` from the Zustand store. No separate routes per role — one URL, branched rendering.

---

## Section 4: State Management

### Zustand — Client State

**Auth slice** (`authStore.ts`):
- Stores `user` (id, name, email, role, branchId), `accessToken`, `refreshToken`
- Persisted to `localStorage` via `zustand/middleware/persist`
- Actions: `setTokens`, `setUser`, `logout`
- On app init, reads persisted state and re-validates token expiry

**Theme slice** (`themeStore.ts`):
- Stores `theme: 'dark' | 'light'`
- Persisted to `localStorage`
- `toggle()` action flips theme and applies class to `document.documentElement`
- CSS variables on `:root.dark` vs `:root` in Tailwind config

### Axios — HTTP + JWT Refresh

Single Axios instance (`api/axios.ts`):
- `baseURL` = `http://localhost:7300`
- Request interceptor: attach `Authorization: Bearer <accessToken>` from Zustand store
- Response interceptor: on 401, call `/auth/refresh` with `refreshToken`, update Zustand store, retry original request
- On refresh failure: call `logout()`, redirect to `/login`

### TanStack Query — Server State

- All API data fetched via TanStack Query hooks defined in each feature's `*Api.ts`
- `queryClient` configured with `staleTime: 30s`, `gcTime: 5m`
- Mutations use `onSuccess` callbacks to invalidate related queries
- No manual state syncing; query cache is the source of truth for server data

### SignalR — Real-time Notifications

`useNotifications` hook:
- Starts `HubConnection` to `/hubs/notifications?access_token=<token>` on mount (authenticated users only)
- Joins role group automatically (server handles group assignment on connect)
- On `ReceiveNotification` event: push toast via `NotificationToast`, invalidate `notificationsApi` query
- Stops connection on unmount / logout
- Reconnect on disconnect with exponential backoff (built into SignalR client)

---

## Section 5: Key Screens

### Login Page
- Full-page centered card, dark/light aware
- Email + password, React Hook Form + Zod validation
- On success: store tokens + user, navigate to `/dashboard`
- Error state: inline message under the form

### App Shell
- `<AppShell>` wraps all authenticated pages
- **Sidebar** (left): collapsible via Framer Motion `layout` animation
  - Expanded: 220px — logo, nav items with icons + labels, collapse button at bottom
  - Collapsed: 60px — icons only, tooltips on hover
  - State persisted to localStorage (not Zustand — component-local `useLocalStorage`)
  - Nav items are role-filtered (Admin items hidden from non-Admins, etc.)
- **Topbar** (top): current page title, notification bell (unread count badge), user avatar dropdown (profile, theme toggle, logout)
- **Content area**: `<Outlet>` with Framer Motion `AnimatePresence` for page transitions (fade + slide up, 200ms)

### Role Dashboards

**SalesPerson:** My requisitions (filtered to own), status summary cards, quick "New Requisition" button.

**BomCreator:** Requisitions awaiting BOM (`BomPending` status), in-progress BOM items, branch-filtered.

**Accountant:** Requisitions awaiting costing (`CostingPending`), in-progress, summary of total quoted value.

**ManagingDirector:** Requisitions awaiting MD review, approved/rejected this month counts, cost overview charts (Recharts).

**Admin:** User count by role, branch list, recent items, quick links to admin sections.

### Requisition List Page
- Filterable table (Shadcn/UI `DataTable` with TanStack Table): filter by status, branch, date range
- Status badge column using `<StatusBadge>` component
- Row click navigates to detail page
- SalesPersons see only their own requisitions (enforced by API, reflected in empty states)

### Requisition Detail Page
- Header: RefNo, customer name, product, status badge, action button (context-sensitive per role)
- **Vertical timeline** (`<RequisitionTimeline>`): each workflow step as a row
  - Completed steps: filled indigo circle, actor name, relative timestamp
  - Active step: amber ring with spinner, "In Progress" label
  - Pending steps: grey circle, step name only
  - Connected by vertical line using absolute positioning
- Details section: product info, quantities, notes, attached files (if any)
- Action buttons: role-gated
  - BomCreator: "Start BOM" / "Submit BOM" → navigate to BOM entry
  - Accountant: "Start Costing" / "Submit Costing" → navigate to costing entry
  - MD: "Review & Approve" → navigate to approval screen

### BOM Entry Page (`/requisitions/:id/bom`)
- Item search input (debounced 300ms, queries `/items?search=`), results dropdown
- Add item: quantity, unit, notes
- **Duplicate item warning**: if an item already in the BOM list is selected again, inline amber warning appears before adding ("This item is already in the BOM. Add anyway?"). No modal — inline.
- Item table: description, qty, unit; remove row button
- Submit button: React Hook Form validation, TanStack Query mutation, optimistic row removal on delete

### Costing Entry Page (`/requisitions/:id/costing`)
- Read-only BOM items section at top
- Per-item costing: unit price (in selected currency), currency selector (dropdown, pulls from exchange rates)
- Landed cost items: type (Freight, Customs, etc.), amount, currency — add/remove rows
- Live totals: total cost, total landed, grand total — all recalculated client-side as user types
- Exchange rates fetched from `/exchange-rates/active`
- All amounts displayed in AED equivalent alongside source currency

### MD Approval Page (`/requisitions/:id/approval`)
- Summary: product, customer, BOM item count, total costing
- Recharts cost breakdown: pie chart of item costs vs landed costs; bar chart of top 5 items by cost
- Approve button → `POST /approvals/:id/approve` → triggers PDF generation + email dispatch on backend
- Reject button → opens Shadcn `Dialog` with required rejection reason textarea → `POST /approvals/:id/reject`
- PDF preview link (opens in new tab via blob URL or backend-generated URL)

### Notifications Page
- Full list of all notifications for the user (paginated, TanStack Query `useInfiniteQuery`)
- Mark as read on click; mark all read button
- Real-time new notifications appear at top (query invalidated by SignalR hook)

### Admin Pages
- Users: CRUD table — create user (role, branch, email, password), edit role/branch, deactivate
- Branches: Create/edit branches (name, location)
- Exchange Rates: Table of currencies, current rate to AED, activate/deactivate, edit rate
- Items: Master item list (searchable/filterable), create/edit/deactivate
- Item Import: File upload (xlsx or csv), preview parsed rows, confirm import — progress indicator

---

## Section 6: UX Details

### Dark/Light Theme
- Default: dark (`document.documentElement.classList.add('dark')` on app init if persisted or first visit)
- Toggle in topbar user menu: Framer Motion icon swap (moon ↔ sun, 150ms rotate)
- All Shadcn/UI components use CSS variables; no manual dark: class overrides needed

### Animations (Framer Motion)
- **Page transitions**: `AnimatePresence` in `<AppShell>` outlet — each page fades + slides up 20px, 200ms ease-out
- **Sidebar collapse**: `motion.div` with `animate={{ width }}` layout animation, 250ms spring
- **Notification toasts**: stacked at bottom-right, each slides in from right, auto-dismiss after 5s, drag to dismiss
- **Timeline items**: `staggerChildren` on mount — each row animates in 50ms apart (subtle, not distracting)

### Form Validation
- All forms use React Hook Form + Zod schemas
- Errors appear inline below each field (not at top of form)
- Submit button disabled during pending mutation
- Success redirects away; failure shows error toast (not form reset)

### Loading States
- `<SkeletonCard>` placeholder on initial data load (Shadcn/UI skeleton variant)
- Tables show skeleton rows (3–5 rows) while loading
- Buttons show spinner + disabled state during mutations

### Mobile Responsiveness
- App targets desktop/tablet primarily (internal tool used at desks)
- Sidebar collapses to 60px (icon-only) automatically below 1024px viewport width
- Tables scroll horizontally on small screens
- No dedicated mobile layout — responsive but not mobile-first

### Empty States
- Role-appropriate empty state message when lists are empty (e.g., "No requisitions waiting for BOM")
- Includes a contextual action button where applicable (e.g., SalesPerson empty list → "Create your first requisition")

---

## Section 7: API Integration Notes

- All API base types defined in `src/types/api.ts` to mirror backend DTOs
- Date fields returned as ISO 8601 strings; formatted using `utils/date.ts`
- Financial fields returned as numbers (up to 6 decimal places); formatted using `utils/currency.ts`
- Pagination: all list endpoints accept `page` and `pageSize`; TanStack Query `useInfiniteQuery` for notifications
- File upload (item import): `multipart/form-data` via Axios, progress tracked with `onUploadProgress`
- PDF download: `GET /approvals/:id/pdf` returns binary; opened in new tab via `window.open(blobUrl)`

---

## Out of Scope (this spec)

- Mobile app (React Native / Expo) — separate spec and plan
- Missing backend tests (`CostingTests.cs`, `ApprovalTests.cs`) — separate task
- Deployment / Docker / CI — not addressed here
- User profile edit page — not in requirements
- Audit log export — not in requirements
