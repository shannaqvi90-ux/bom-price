# Plan 2 — Requisitions Core — Design

**Date:** 2026-04-14
**Status:** Approved (awaiting user spec review)
**Author:** Brainstorming session (Claude Code + user)
**Supersedes:** n/a (first slice after Plan 1: Foundation & Auth)
**Parent spec:** `docs/superpowers/specs/2026-04-13-web-frontend-design.md`

---

## Overview

Plan 2 is the first vertical slice built on top of the Plan 1 foundation (auth, stores, axios/refresh, routing, AppShell, theme, placeholder dashboards). It delivers the **Requisitions core**: three routes (list, new, detail) and the shared primitives the later plans will reuse.

After Plan 2, a SalesPerson can create a requisition end-to-end and every role can view branch-scoped lists and detail pages. BOM/Costing/Approval entry pages (Plans 3–4) and real-time notifications (Plan 5) are out of scope.

---

## Scope

### In scope

- **Routes**
  - `/requisitions` — list page (all roles except Admin)
  - `/requisitions/new` — create form (SalesPerson only)
  - `/requisitions/:id` — detail page (all roles except Admin)
- **Shared primitives** (reused by Plans 3–6)
  - `StatusBadge` — colour-coded requisition status pill
  - `DataTable<TData>` — TanStack Table v8 wrapper
  - `SearchableSelect<T>` — hand-rolled combobox over a prefetched list
  - `RequisitionTimeline` — dumb component deriving step list from status enum
- **Shared lookup hooks** (`src/api/lookups.ts`)
  - `useCustomers`, `useItems`, `useActiveExchangeRates`
- **Feature API hooks** (`src/features/requisitions/requisitionsApi.ts`)
  - `useRequisitions`, `useRequisition`, `useCreateRequisition`
- **Client-side filters** on the list page (status multiselect, branch select, date range)
- **Dashboard upgrades** — replace Plan 1 placeholders in Sales/Bom/Accountant/Md dashboards with role-appropriate filtered requisition lists, reusing the same `useRequisitions` + `DataTable` pair
- **Tests** — Vitest + RTL for every new component and page (~29 tests total)
- **Manual smoke** — seven-step verification script (see Success Criteria)

### Out of scope

- Server-side pagination/filtering on `GET /api/requisitions` — flagged as backend tech debt, client-side filters only for Plan 2
- Inline customer/item creation in the new-requisition form — select-only, flagged as enhancement for a later plan
- Real per-step audit trail / actor names in the timeline — the backend does not yet expose this. Timeline derives solely from the status enum + `createdAt` + `updatedAt`. Adding this requires a later backend plan.
- BOM entry page (`/requisitions/:id/bom`) — Plan 3
- Costing entry page (`/requisitions/:id/costing`) — Plan 3
- MD approval page (`/requisitions/:id/approval`) — Plan 4
- SignalR, notifications, toasts — Plan 5
- Admin pages — Plan 6
- Toast infrastructure (no toast library added in Plan 2; inline error messages instead)
- Pagination inside `DataTable` (list is small; add later if needed)

---

## Success Criteria

Plan 2 is done when:

1. **SalesPerson end-to-end works**: log in → Sidebar shows "Requisitions" → open `/requisitions/new` → select existing customer, item, currency, enter qty → submit → land on `/requisitions/:id` with timeline step 1 complete and step 2 in-progress.
2. **Other roles see the requisition**: BomCreator logging in sees the same requisition in the (branch-scoped) list and can open the detail page. "Start BOM" action button appears disabled with "Coming soon" tooltip.
3. **List filters work** client-side: status multiselect, branch select (hidden for SalesPerson/BomCreator), date range, with a "Clear filters" action on empty filtered state.
4. **Dashboards are role-appropriate**: each role's dashboard shows a filtered slice of their requisitions (Sales: mine, Bom: `BomPending`, Accountant: `CostingPending`, Md: `MdReview`).
5. **Tests green**: all new Vitest suites pass. Plan 1's 12 tests still pass. Total ~40 tests.
6. **Build clean**: `npm run build` in `bom-web/` succeeds with no new warnings beyond the pre-existing chunk-size advisory.
7. **Theme + layout still work** on every new page (dark/light toggle, sidebar collapse persist on reload).
8. **No backend changes** — every API call uses endpoints already present in `BomPriceApproval.API`.

---

## Backend API Surface (already exists)

Plan 2 integrates with these existing endpoints — no backend changes required.

| Method | Path | Source | Role | Notes |
|---|---|---|---|---|
| GET | `/api/requisitions` | `RequisitionsController.GetAll` | All authenticated | Branch/role scoped server-side; returns full list |
| GET | `/api/requisitions/{id}` | `RequisitionsController.Get` | Authenticated + `CanAccess` | Returns `RequisitionDetail` with nested `BomSummary?` and `ApprovalSummary?` |
| POST | `/api/requisitions` | `RequisitionsController.Create` | SalesPerson only | Body: `CreateRequisitionRequest` |
| GET | `/api/customers` | `CustomersController` | All authenticated | Used for customer picker |
| GET | `/api/items` | `ItemsController` | All authenticated | Used for item picker |
| GET | `/api/exchange-rates` (active) | `ExchangeRatesController` | All authenticated | Exact query-param to confirm during implementation |

Backend DTOs to mirror:
- `RequisitionListItem(Id, RefNo, Status, ItemDescription, CustomerName, ExpectedQty, CurrencyCode, BranchName, SalesPersonName, CreatedAt)`
- `RequisitionDetail(Id, RefNo, Status, ItemId, ItemDescription, CustomerId, CustomerName, CustomerEmail, CustomerPhone, CustomerAddress, ExpectedQty, CurrencyCode, ExchangeRateSnapshot, BranchId, BranchName, SalesPersonId, SalesPersonName, CreatedAt, UpdatedAt, Bom?, Approval?)`
- `BomSummary(Id, TotalCostPerKg, HasCost)`
- `ApprovalSummary(SalesPriceAed, SalesPriceForeign?, ProfitMarginPct, IsApproved)`
- `CreateRequisitionRequest(CustomerId, ItemId, ExpectedQty, CurrencyCode)`

During implementation the first task is to confirm each DTO shape against the backend source and lock the TypeScript interfaces. `Customer`, `Item`, and `ExchangeRate` shapes also need confirming at that time.

---

## File Layout

### New files

```
bom-web/src/
  api/
    lookups.ts                                      # useCustomers, useItems, useActiveExchangeRates
  features/
    requisitions/
      requisitionsApi.ts                            # useRequisitions, useRequisition, useCreateRequisition
      RequisitionListPage.tsx
      RequisitionListPage.test.tsx
      RequisitionDetailPage.tsx
      RequisitionDetailPage.test.tsx
      NewRequisitionPage.tsx
      NewRequisitionPage.test.tsx
      components/
        RequisitionFilters.tsx                      # Status / branch / date-range filter bar
        RequisitionTimeline.tsx
        RequisitionTimeline.test.tsx
  components/
    ui/
      StatusBadge.tsx
      StatusBadge.test.tsx
      DataTable.tsx
      DataTable.test.tsx
      SearchableSelect.tsx
      SearchableSelect.test.tsx
  utils/
    date.ts                                         # formatRelative(iso) via Intl.RelativeTimeFormat
    date.test.ts
```

### Modified files

- `src/types/api.ts` — add `RequisitionStatus`, `RequisitionListItem`, `RequisitionDetail`, `BomSummary`, `ApprovalSummary`, `CreateRequisitionRequest`, `Customer`, `Item`, `ExchangeRate`
- `src/App.tsx` — register `/requisitions`, `/requisitions/new` (with `ProtectedRoute allow={["SalesPerson"]}`), and `/requisitions/:id` under the existing `AppShell` branch
- `src/components/layout/Sidebar.tsx` — add "Requisitions" nav item (visible to `SalesPerson | BomCreator | Accountant | ManagingDirector`)
- `src/features/dashboard/SalesDashboard.tsx` — replace placeholder with "My Recent Requisitions" `DataTable` + "New Requisition" button
- `src/features/dashboard/BomDashboard.tsx` — replace placeholder with `BomPending` list
- `src/features/dashboard/AccountantDashboard.tsx` — replace placeholder with `CostingPending` list
- `src/features/dashboard/MdDashboard.tsx` — replace placeholder with `MdReview` list

### New dependencies

- `@tanstack/react-table` — for the `DataTable` wrapper. No other new runtime deps.

---

## TypeScript Types

Additions to `src/types/api.ts`:

```ts
export type RequisitionStatus =
  | "Draft"
  | "BomPending"
  | "BomInProgress"
  | "CostingPending"
  | "CostingInProgress"
  | "MdReview"
  | "Approved"
  | "Rejected";

export interface RequisitionListItem {
  id: number;
  refNo: string;
  status: RequisitionStatus;
  itemDescription: string;
  customerName: string;
  expectedQty: number;
  currencyCode: string;
  branchName: string;
  salesPersonName: string;
  createdAt: string; // ISO 8601
}

export interface BomSummary {
  id: number;
  totalCostPerKg: number;
  hasCost: boolean;
}

export interface ApprovalSummary {
  salesPriceAed: number;
  salesPriceForeign: number | null;
  profitMarginPct: number;
  isApproved: boolean;
}

export interface RequisitionDetail {
  id: number;
  refNo: string;
  status: RequisitionStatus;
  itemId: number;
  itemDescription: string;
  customerId: number;
  customerName: string;
  customerEmail: string;
  customerPhone: string;
  customerAddress: string;
  expectedQty: number;
  currencyCode: string;
  exchangeRateSnapshot: number | null;
  branchId: number;
  branchName: string;
  salesPersonId: number;
  salesPersonName: string;
  createdAt: string;
  updatedAt: string;
  bom: BomSummary | null;
  approval: ApprovalSummary | null;
}

export interface CreateRequisitionRequest {
  customerId: number;
  itemId: number;
  expectedQty: number;
  currencyCode: string;
}

export interface Customer {
  id: number;
  name: string;
  email: string;
  phoneNumber: string;
  address: string;
}

export interface Item {
  id: number;
  description: string;
  // Additional fields confirmed against backend DTO during implementation
}

export interface ExchangeRate {
  id: number;
  currencyCode: string;
  rateToAed: number;
  isActive: boolean;
  effectiveDate: string;
}
```

---

## API Hooks

### `src/features/requisitions/requisitionsApi.ts`

```ts
const keys = {
  all: ["requisitions"] as const,
  list: () => [...keys.all, "list"] as const,
  detail: (id: number) => [...keys.all, "detail", id] as const,
};

export function useRequisitions() {
  return useQuery({
    queryKey: keys.list(),
    queryFn: () => api.get<RequisitionListItem[]>("/requisitions").then(r => r.data),
  });
}

export function useRequisition(id: number) {
  return useQuery({
    queryKey: keys.detail(id),
    queryFn: () => api.get<RequisitionDetail>(`/requisitions/${id}`).then(r => r.data),
    enabled: Number.isFinite(id),
  });
}

export function useCreateRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateRequisitionRequest) =>
      api.post<{ id: number; refNo: string }>("/requisitions", body).then(r => r.data),
    onSuccess: () => { qc.invalidateQueries({ queryKey: keys.all }); },
  });
}
```

### `src/api/lookups.ts`

```ts
export function useCustomers() {
  return useQuery({
    queryKey: ["customers"],
    queryFn: () => api.get<Customer[]>("/customers").then(r => r.data),
    staleTime: 5 * 60_000,
  });
}

export function useItems() {
  return useQuery({
    queryKey: ["items"],
    queryFn: () => api.get<Item[]>("/items").then(r => r.data),
    staleTime: 5 * 60_000,
  });
}

export function useActiveExchangeRates() {
  return useQuery({
    queryKey: ["exchangeRates", "active"],
    queryFn: () => api.get<ExchangeRate[]>("/exchange-rates?active=true").then(r => r.data),
    staleTime: 5 * 60_000,
  });
}
```

The exact path/query shape for the exchange-rates endpoint is confirmed against the backend controller during the first implementation task.

---

## Shared UI Primitives

### `StatusBadge`

`src/components/ui/StatusBadge.tsx` — takes a `RequisitionStatus`, renders a Tailwind pill.

| Status | Colour family |
|---|---|
| `Draft` | slate |
| `BomPending`, `CostingPending`, `MdReview` | amber |
| `BomInProgress`, `CostingInProgress` | blue |
| `Approved` | emerald |
| `Rejected` | rose |

Classes like `bg-amber-500/10 text-amber-600 dark:text-amber-400` work in both themes (CSS variables handled by Tailwind v4).

### `DataTable<TData>`

`src/components/ui/DataTable.tsx` — thin wrapper over TanStack Table v8 (`useReactTable` + `getCoreRowModel` + `getSortedRowModel`).

```ts
interface DataTableProps<TData> {
  columns: ColumnDef<TData>[];
  data: TData[];
  isLoading?: boolean;
  emptyState?: ReactNode;
  onRowClick?: (row: TData) => void;
  initialSort?: SortingState;
}
```

Responsibilities:
- Render `thead`/`tbody` with Tailwind styles
- Show 5 skeleton rows when `isLoading`
- Render `emptyState` when `data.length === 0 && !isLoading`
- Sortable via column header click
- Call `onRowClick` on tbody row click (also makes the row keyboard-focusable with `role="button"` + Enter/Space)

No pagination. No column resizing. Extend later if needed.

### `SearchableSelect<T>`

`src/components/ui/SearchableSelect.tsx` — hand-rolled combobox (~80 LOC) over a prefetched list.

```ts
interface SearchableSelectProps<T> {
  options: T[];
  value: T | null;
  onChange: (v: T | null) => void;
  getLabel: (o: T) => string;
  getValue: (o: T) => string | number;
  placeholder?: string;
  disabled?: boolean;
}
```

Behaviour:
- `<input>` displays the selected label or the typed filter
- Dropdown list filters options by case-insensitive substring match on `getLabel(option)`
- Keyboard: Arrow down/up to move highlight, Enter to select, Escape to close
- Click outside to close (via `useRef` + `mousedown` listener on `document`)

Hand-rolled rather than added-dep because it's small, only two consumers in Plan 2, and avoids pulling a headless UI library for one widget.

### `RequisitionTimeline`

`src/features/requisitions/components/RequisitionTimeline.tsx` — dumb component.

```ts
interface Props {
  status: RequisitionStatus;
  createdAt: string;
  updatedAt: string;
}
```

Renders five fixed steps derived from the status enum:

| # | Label | Role | State logic |
|---|---|---|---|
| 1 | Submitted | SalesPerson | Always completed; shows `createdAt` via `formatRelative` |
| 2 | BOM | BomCreator | `pending` when `status = Draft`, `in-progress` when `BomPending`/`BomInProgress`, `completed` when later |
| 3 | Costing | Accountant | `pending` when ≤ `BomInProgress`, `in-progress` when `CostingPending`/`CostingInProgress`, `completed` when later |
| 4 | MD Review | ManagingDirector | `pending` when ≤ `CostingInProgress`, `in-progress` when `MdReview`, `completed` when `Approved`/`Rejected` |
| 5 | Result | — | `pending` until terminal; then `Approved` (emerald) or `Rejected` (rose) |

Per the design decision:
- The current step shows `updatedAt` via `formatRelative`. Other steps (except 1) show no timestamp.
- `Rejected` status collapses steps 2–4 to a cancelled visual (muted line, grey circles) and step 5 shows "Rejected".
- No actor names beyond the role label (no audit trail from the backend yet).

Visual: vertical line, circles per step (filled / ringed with spinner / empty). Framer Motion `staggerChildren` on mount (50ms delta). Colours follow `StatusBadge` palette for consistency.

### `formatRelative`

`src/utils/date.ts` — `formatRelative(iso: string, now = new Date()): string` using `Intl.RelativeTimeFormat`. Thresholds: <60s → "just now"; <60m → minutes; <24h → hours; <7d → days; otherwise `toLocaleDateString`. No new dep.

---

## Pages

### Requisition List — `/requisitions`

**Guard:** `ProtectedRoute allow={["SalesPerson","BomCreator","Accountant","ManagingDirector"]}`.

**Layout:**
- Page header: `Requisitions` title + "New Requisition" button (SalesPerson only, links to `/requisitions/new`)
- `RequisitionFilters` component below the header — status multiselect, branch select (hidden when user has a `branchId`), date-range (from/to inputs). Filter state lives in the page via `useState`; "Clear filters" resets.
- `DataTable<RequisitionListItem>` below filters

**Columns:**

| Column | Render | Sortable |
|---|---|---|
| RefNo | plain text, mono font | yes |
| Status | `<StatusBadge>` | yes |
| Item | `itemDescription` truncated at 40 chars + tooltip on hover | no |
| Customer | `customerName` | yes |
| Qty | `expectedQty` + `currencyCode` | no |
| Branch | `branchName` (column hidden when user has `branchId`) | yes |
| Created | `formatRelative(createdAt)` | yes |

**Data flow:**
1. `useRequisitions()` fetches the full list (backend already scopes by role/branch).
2. `useMemo` filters the fetched list by the active filter state (status set, branch, date range).
3. `DataTable` receives the filtered data plus `onRowClick={(row) => navigate(\`/requisitions/${row.id}\`)}`.
4. Loading → `DataTable isLoading`. Empty after filter → "Clear filters" action. Empty unfiltered → role-appropriate message (SalesPerson: "Create your first requisition" + button; others: "No requisitions waiting").

**Error state:** query error → inline error `Card` above the table with a Retry button that calls `refetch()`.

### New Requisition — `/requisitions/new`

**Guard:** `ProtectedRoute allow={["SalesPerson"]}`. Any other role → redirect to `/dashboard`.

**Layout:** Single centered `Card`, max-width ~640px. Back button ("← Back to Requisitions") in the header.

**Form** (React Hook Form + Zod, same pattern as `LoginPage`):

| Field | Control | Validation |
|---|---|---|
| Customer | `SearchableSelect<Customer>` | required |
| Item | `SearchableSelect<Item>` | required |
| Expected Qty | number input | required, `> 0`, up to 4 decimals |
| Currency | `<select>` | required, defaults to `"AED"` |

**Data sources:**
- `useCustomers()` → customer options
- `useItems()` → item options
- `useActiveExchangeRates()` → currency options; always prepended with synthetic `{ currencyCode: "AED", rateToAed: 1 }`
- Any lookup loading → form shows a skeleton. Any lookup errors → error `Card` with a Retry button.

**Submit flow:**
1. `useCreateRequisition()` mutation fires with the validated body.
2. On success → `navigate(\`/requisitions/${created.id}\`, { replace: true })` so Back doesn't return to the form.
3. On error → inline error under the submit button (server message preferred, generic fallback). Form stays populated.
4. Submit button disabled + spinner while `isPending`.

No toast infrastructure yet — toast system lands in Plan 5 with notifications.

### Requisition Detail — `/requisitions/:id`

**Guard:** `ProtectedRoute allow={["SalesPerson","BomCreator","Accountant","ManagingDirector"]}`. Backend enforces per-record access via `CanAccess`; on 403, show "You don't have access to this requisition" card with back link.

**Layout:** Two-column on ≥lg (left: header + timeline; right: summary cards). Single-column stacked below lg.

**Header block:**
- Back button → `/requisitions`
- Line 1: `RefNo` (mono, large) + `<StatusBadge>` + `formatRelative(createdAt)`
- Line 2: Item description, customer name
- Action button row (role-gated, all disabled with "Coming soon" tooltip):
  - `BomCreator` when `status ∈ {BomPending, BomInProgress}` → "Start BOM"
  - `Accountant` when `status ∈ {CostingPending, CostingInProgress}` → "Start Costing"
  - `ManagingDirector` when `status = MdReview` → "Review & Approve"
  - `SalesPerson` → no button
  - Any other role/status combination → no button

**Left column:** `RequisitionTimeline` with `status`, `createdAt`, `updatedAt`.

**Right column — four summary cards:**

1. **Customer** — name, email, phone, address
2. **Quotation** — expected qty, currency, exchange rate snapshot (if non-AED), branch, sales person
3. **BOM** — `"BOM not yet created"` when `bom === null`; else `Total cost / kg` + `Has cost` badge
4. **Approval** — `"Not yet submitted for approval"` when `approval === null`; else `Sales price (AED)`, `Sales price (foreign)` if present, `Profit margin %`, `Approved` badge

A co-located `LabeledValue` helper component renders repeated "label → value" rows.

**Data flow:**
- `const { id } = useParams(); useRequisition(Number(id))`
- Loading → full-page skeleton (uses existing `Card` primitives; no new skeleton component needed)
- 404 → "Requisition not found" card
- 403 → "Access denied" card
- Other errors → generic error card with retry

---

## State Management & Error Handling Notes

- No additions to Zustand. All server state flows through TanStack Query cache.
- All new API calls go through the existing `api` axios instance — JWT refresh interceptor from Plan 1 handles token rotation transparently.
- HTTP error status codes surface via Axios errors; pages inspect `error.response?.status` to decide between 404 / 403 / generic.
- The existing `ProtectedRoute` component already handles the `allow` prop (verified in Plan 1 tests).

---

## Testing Strategy

**Stack:** Vitest 4 + React Testing Library (already installed in Plan 1). No MSW. Module-level mocking of `@/api/axios` per test file, matching `axios.test.ts` from Plan 1.

**Test inventory:**

| File | Approx count | Focus |
|---|---|---|
| `StatusBadge.test.tsx` | 2 | Renders each status with correct colour/label |
| `DataTable.test.tsx` | 5 | Rows, skeleton, empty state, row click, sort |
| `SearchableSelect.test.tsx` | 3 | Filters options, selects value, keyboard nav basics |
| `RequisitionTimeline.test.tsx` | 4 | Step states for each status value; timestamp placement |
| `date.test.ts` | 3 | `formatRelative` thresholds |
| `RequisitionListPage.test.tsx` | 5 | Loading/data/empty, filter behaviour, role-gated New button, row-click nav |
| `NewRequisitionPage.test.tsx` | 4 | Form render, validation, submit success navigation, submit error |
| `RequisitionDetailPage.test.tsx` | 6 | Header, timeline, cards, role-gated buttons, 404, 403 |

Expected totals after Plan 2: ~12 (Plan 1) + 32 (new) ≈ **44 tests**.

**Test doubles:**
- `@/api/axios` — `vi.mock("@/api/axios", ...)`, same pattern as Plan 1
- `useNavigate` — mocked via `vi.mock("react-router-dom", async () => ({ ...(await vi.importActual<...>("react-router-dom")), useNavigate: () => mockNav }))`
- `useAuthStore` — real store, `setState` in `beforeEach` to configure role/branch per test
- Lookup hooks — module-mocked to return canned arrays

**Not tested in Plan 2:** backend contracts (covered by `BomPriceApproval.Tests`), cross-route navigation (Router internals), SignalR/PDF/email (later plans).

---

## Manual Smoke Test (final task of Plan 2)

1. Start the API and web app (as per Plan 1's smoke test).
2. Log in as `admin@test.com` / `Admin@1234` → Sidebar shows "Requisitions" → list loads (empty or populated across branches).
3. Log out, log in as a seeded SalesPerson → Sidebar shows "Requisitions" → SalesDashboard shows "My Recent Requisitions" with a "New Requisition" button.
4. Click "New Requisition" → form loads with customers/items/currencies populated → submit with a valid selection → land on `/requisitions/:id` with timeline step 1 complete and step 2 in-progress.
5. Log in as a seeded BomCreator in the same branch → list shows the new requisition → open detail → "Start BOM" button visible but disabled with "Coming soon" tooltip.
6. Try navigating to `/requisitions/new` as a non-SalesPerson → redirected to `/dashboard`.
7. Theme toggle still works on every new page. Sidebar collapse persists across reloads.

---

## Plan 2 Deliverables Summary

- 3 route pages + 4 shared UI primitives + 1 timeline component + 2 API hook modules + 1 date utility
- 1 new runtime dep (`@tanstack/react-table`)
- ~32 new Vitest tests; Plan 1's 12 still passing
- Dashboard placeholders upgraded to real filtered lists for 4 roles
- No backend changes
- Commits: one per completed task on `master` (matching Plan 1's discipline)

---

## Flagged for later plans

- **Backend pagination/filtering** — `GET /api/requisitions` currently returns the full list. Client-side filters work but won't scale past a few hundred rows. Add `?status=&branchId=&from=&to=&page=&pageSize=` in a future backend plan.
- **Audit trail** — timeline is status-derived. Expose per-step actor/timestamp history in a future backend change; upgrade `RequisitionTimeline` to consume it.
- **Inline customer/item creation** — enhancement once the core flow is stable.
- **Toast infrastructure** — lands in Plan 5 alongside notifications.
- **`SkeletonCard` shared primitive** — Plan 2 does not need one; add when Plan 5 or a later plan requires a richer skeleton than the current `Card`-based placeholder.
