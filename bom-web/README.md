# bom-web — BOM & Price Approval Web UI

Frontend for the BOM & Price Approval quotation workflow system (Fujairah Plastic Factory). React 19 + Vite + TypeScript.

## Prerequisites

- Node.js 20+ and npm
- The backend API running on `http://localhost:7300` (see repo root `README` for API setup)

## Setup

```bash
npm install
```

## Commands

```bash
npm run dev        # Vite dev server on http://localhost:5300
npm run build      # Type-check (tsc -b) + production build to dist/
npm run lint       # ESLint
npm test           # Vitest (run once, no watch)
npm run test:watch # Vitest watch mode
npm run preview    # Serve the production build locally
```

The dev server proxies `/api/*` and `/hubs/*` calls to the backend at `http://localhost:7300`.

## Project Layout

```
src/
  api/           — Axios instance, lookup hooks (branches/customers/items/...)
  app/           — App entry, router, protected routes
  components/
    layout/      — AppShell, Sidebar, Topbar
    ui/          — Shared primitives (Button, Dialog, DataTable, ConfirmDialog, StatusBadge, …)
  features/
    auth/        — Login + auth wiring
    dashboard/   — Role-based dashboards (Admin / Sales / BOM / Accountant / MD)
    requisitions — Create / edit / list / detail
    bom          — BOM entry (per requisition item)
    costing      — Costing entry + draft auto-save
    approvals    — MD review + approve/reject
    items        — Item master + Excel/CSV import
    customers    — Customer master + import
    exchange-rates — AED conversion rates
    users        — Admin user management + force-logout
    notifications — In-app bell + SignalR toasts
  lib/           — JWT helpers, apiError mapping, notify wrapper
  store/         — Zustand stores: authStore, notificationsStore, themeStore
  types/         — Shared DTO types (api.ts)
```

## Key Conventions

- **Auth:** JWT in `localStorage` via `authStore`. Axios attaches `Authorization: Bearer ...` automatically.
- **Realtime:** SignalR hub at `/hubs/notifications`; connection lifecycle in `notificationsStore`.
- **Forms:** `react-hook-form` + `zod` schemas; server field errors surface via `extractFieldErrors` from `lib/apiError.ts`.
- **Queries:** TanStack Query v5 with scoped query keys exported from each feature's `*Api.ts`.
- **Role gating:** `ProtectedRoute` checks role. Backend is the authoritative guard — frontend role checks are UX only.
