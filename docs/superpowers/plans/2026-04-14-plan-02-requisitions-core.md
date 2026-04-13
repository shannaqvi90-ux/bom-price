# Plan 2 — Requisitions Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the `/requisitions` list, `/requisitions/new` create form, and `/requisitions/:id` detail page, plus the shared primitives (StatusBadge, DataTable, SearchableSelect, RequisitionTimeline) and lookup hooks that later plans will reuse. Upgrade the four role dashboards to show role-appropriate requisition lists.

**Architecture:** Pure frontend slice on top of Plan 1 foundations. All three routes call existing backend endpoints — no backend changes. Server state flows through TanStack Query; client-side filtering on the list page; forms use React Hook Form + Zod; status-derived timeline (no audit log backend yet). Disabled "Coming soon" role-gated action buttons on the detail page stand in for Plans 3–4 routes.

**Tech Stack:** React 19, Vite 8, TypeScript 5, TanStack Query v5, `@tanstack/react-table` v8 (new dep), React Hook Form + Zod, Framer Motion, Tailwind CSS v4, Vitest 4 + React Testing Library.

**Spec:** `docs/superpowers/specs/2026-04-14-plan-02-requisitions-core-design.md`

---

## File Structure

### New files

```
bom-web/src/
  api/
    lookups.ts                                      — useCustomers, useItems, useActiveExchangeRates hooks
    lookups.test.ts                                 — covers the three lookup hooks with mocked axios
  utils/
    date.ts                                         — formatRelative(iso) via Intl.RelativeTimeFormat
    date.test.ts                                    — thresholds + edge cases
  components/ui/
    StatusBadge.tsx                                 — colour-coded status pill
    StatusBadge.test.tsx                            — label/colour per status value
    DataTable.tsx                                   — thin wrapper over TanStack Table v8
    DataTable.test.tsx                              — rows, loading, empty, click, sort
    SearchableSelect.tsx                            — hand-rolled combobox over prefetched list
    SearchableSelect.test.tsx                       — filter, select, keyboard nav
  features/requisitions/
    requisitionsApi.ts                              — useRequisitions, useRequisition, useCreateRequisition
    RequisitionListPage.tsx                         — route: /requisitions
    RequisitionListPage.test.tsx
    NewRequisitionPage.tsx                          — route: /requisitions/new
    NewRequisitionPage.test.tsx
    RequisitionDetailPage.tsx                       — route: /requisitions/:id
    RequisitionDetailPage.test.tsx
    components/
      RequisitionFilters.tsx                        — status/branch/date filter bar (internal)
      RequisitionTimeline.tsx                       — status-derived timeline
      RequisitionTimeline.test.tsx
```

### Modified files

```
bom-web/package.json                                — add @tanstack/react-table
bom-web/src/types/api.ts                            — add Requisition/Customer/Item/ExchangeRate types
bom-web/src/App.tsx                                 — register 3 new routes
bom-web/src/features/dashboard/SalesDashboard.tsx   — replace placeholder with filtered list
bom-web/src/features/dashboard/BomDashboard.tsx     — replace placeholder with BomPending list
bom-web/src/features/dashboard/AccountantDashboard.tsx — replace placeholder with CostingPending list
bom-web/src/features/dashboard/MdDashboard.tsx      — replace placeholder with MdReview list
```

### Unchanged (pre-confirmed in Plan 1 readout)

- `bom-web/src/components/layout/Sidebar.tsx` — already contains the "Requisitions" nav item with the correct role allow-list
- `bom-web/src/api/axios.ts`, `queryClient.ts`, `store/*`, auth feature

---

## Backend endpoints the plan consumes

All endpoints already exist. Shapes confirmed against the current controller source:

**`GET /api/requisitions`** → `RequisitionListItem[]` (from `RequisitionsController.GetAll`)
```
(Id, RefNo, Status, ItemDescription, CustomerName, ExpectedQty, CurrencyCode, BranchName, SalesPersonName, CreatedAt)
```
Branch/role scoped server-side. No pagination.

**`GET /api/requisitions/{id}`** → `RequisitionDetail` (from `RequisitionsController.Get`)
```
(Id, RefNo, Status, ItemId, ItemDescription, CustomerId, CustomerName, CustomerEmail, CustomerPhone, CustomerAddress,
 ExpectedQty, CurrencyCode, ExchangeRateSnapshot, BranchId, BranchName, SalesPersonId, SalesPersonName, CreatedAt, UpdatedAt,
 Bom?: (Id, TotalCostPerKg, HasCost), Approval?: (SalesPriceAed, SalesPriceForeign?, ProfitMarginPct, IsApproved))
```

**`POST /api/requisitions`** — SalesPerson only. Body `{customerId, itemId, expectedQty, currencyCode}`. Returns `{id, refNo}`.

**`GET /api/customers`** → `CustomerResponse[]`: `(Id, Name, Address, Email, PhoneNumber, BranchId, CreatedByUserId)`

**`GET /api/items`** → `ItemResponse[]`: `(Id, Code, Description, Type, BranchId, IsActive)` — filters inactive server-side.

**`GET /api/exchange-rates/active`** → `ExchangeRateResponse[]`: `(Id, CurrencyCode, CurrencyName, RateToAed, EffectiveDate, IsActive, SetByName)` — note: path segment, not a query parameter.

---

## How tasks are structured

Every task has a small batch of files, a failing test, an implementation, a passing test, and a commit. Each task should take 5–20 minutes. Tasks are TDD-ordered: shared primitives first (used by later tasks), then API hooks, then pages, then wiring.

**Before starting:** `cd bom-web && npm test -- --run` should show the Plan 1 suite at 12 passing, 0 failing. If not, stop and investigate.

---

## Task 1: Install @tanstack/react-table and extend types

**Files:**
- Modify: `bom-web/package.json` (add dependency)
- Modify: `bom-web/src/types/api.ts` (extend)

- [ ] **Step 1: Install the new dependency**

```bash
cd bom-web
npm install @tanstack/react-table@^8.20.0
```

Expected: `package.json` gains `"@tanstack/react-table": "^8.20.0"` under `dependencies`.

- [ ] **Step 2: Extend `src/types/api.ts`**

Append the following to the existing file (do not remove the Plan 1 types at the top):

```ts
// ─── Plan 2: Requisitions & lookups ──────────────────────────────────────────

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
  createdAt: string;
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
  address: string;
  email: string;
  phoneNumber: string;
  branchId: number;
  createdByUserId: number;
}

export type ItemKind = "RawMaterial" | "FinishedGood" | "Packaging" | "Other";

export interface Item {
  id: number;
  code: string;
  description: string;
  type: ItemKind;
  branchId: number;
  isActive: boolean;
}

export interface ExchangeRate {
  id: number;
  currencyCode: string;
  currencyName: string;
  rateToAed: number;
  effectiveDate: string;
  isActive: boolean;
  setByName: string;
}
```

- [ ] **Step 3: Verify the build still typechecks**

```bash
cd bom-web
npm run build
```

Expected: build succeeds. If `ItemKind` union doesn't match the backend enum values, correct it — open `BomPriceApproval.API/Domain/Enums/ItemType.cs` and mirror its member names. Do not invent values.

- [ ] **Step 4: Run existing tests to confirm no regressions**

```bash
cd bom-web
npm test -- --run
```

Expected: 12/12 passing (Plan 1 suite intact).

- [ ] **Step 5: Commit**

```bash
cd "D:/shan projects/BOM_Price_Approval"
git add bom-web/package.json bom-web/package-lock.json bom-web/src/types/api.ts
git commit -m "feat(web): add @tanstack/react-table and plan 2 API types"
```

---

## Task 2: Add `formatRelative` date utility (TDD)

**Files:**
- Create: `bom-web/src/utils/date.ts`
- Create: `bom-web/src/utils/date.test.ts`

- [ ] **Step 1: Write the failing test**

Create `bom-web/src/utils/date.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { formatRelative } from "./date";

const base = new Date("2026-04-14T12:00:00Z");

describe("formatRelative", () => {
  it('returns "just now" for timestamps under 60 seconds ago', () => {
    const iso = new Date(base.getTime() - 30_000).toISOString();
    expect(formatRelative(iso, base)).toBe("just now");
  });

  it("returns minutes ago for timestamps under an hour", () => {
    const iso = new Date(base.getTime() - 5 * 60_000).toISOString();
    expect(formatRelative(iso, base)).toBe("5 minutes ago");
  });

  it("returns hours ago for timestamps under a day", () => {
    const iso = new Date(base.getTime() - 3 * 60 * 60_000).toISOString();
    expect(formatRelative(iso, base)).toBe("3 hours ago");
  });

  it("returns days ago for timestamps under a week", () => {
    const iso = new Date(base.getTime() - 2 * 24 * 60 * 60_000).toISOString();
    expect(formatRelative(iso, base)).toBe("2 days ago");
  });

  it("returns an absolute locale date for timestamps older than a week", () => {
    const iso = new Date(base.getTime() - 30 * 24 * 60 * 60_000).toISOString();
    const result = formatRelative(iso, base);
    // Shape check only — locale output varies, so assert it's not one of the relative forms
    expect(result).not.toMatch(/ago|just now/);
    expect(result.length).toBeGreaterThan(0);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd bom-web
npm test -- --run src/utils/date.test.ts
```

Expected: FAIL — "Cannot find module './date'" or similar.

- [ ] **Step 3: Write minimal implementation**

Create `bom-web/src/utils/date.ts`:

```ts
const rtf = new Intl.RelativeTimeFormat("en", { numeric: "always" });

export function formatRelative(iso: string, now: Date = new Date()): string {
  const then = new Date(iso);
  const diffMs = then.getTime() - now.getTime();
  const absSec = Math.abs(diffMs) / 1000;

  if (absSec < 60) return "just now";
  if (absSec < 3600) return rtf.format(Math.round(diffMs / 60_000), "minute");
  if (absSec < 86_400) return rtf.format(Math.round(diffMs / 3_600_000), "hour");
  if (absSec < 7 * 86_400) return rtf.format(Math.round(diffMs / 86_400_000), "day");

  return then.toLocaleDateString();
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
cd bom-web
npm test -- --run src/utils/date.test.ts
```

Expected: 5/5 passing.

- [ ] **Step 5: Commit**

```bash
cd "D:/shan projects/BOM_Price_Approval"
git add bom-web/src/utils/date.ts bom-web/src/utils/date.test.ts
git commit -m "feat(web): add formatRelative date helper"
```

---

## Task 3: `StatusBadge` component (TDD)

**Files:**
- Create: `bom-web/src/components/ui/StatusBadge.tsx`
- Create: `bom-web/src/components/ui/StatusBadge.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `bom-web/src/components/ui/StatusBadge.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StatusBadge } from "./StatusBadge";
import type { RequisitionStatus } from "@/types/api";

const cases: Array<{ status: RequisitionStatus; label: string }> = [
  { status: "Draft", label: "Draft" },
  { status: "BomPending", label: "BOM Pending" },
  { status: "BomInProgress", label: "BOM In Progress" },
  { status: "CostingPending", label: "Costing Pending" },
  { status: "CostingInProgress", label: "Costing In Progress" },
  { status: "MdReview", label: "MD Review" },
  { status: "Approved", label: "Approved" },
  { status: "Rejected", label: "Rejected" },
];

describe("StatusBadge", () => {
  it.each(cases)("renders a badge with readable label for $status", ({ status, label }) => {
    render(<StatusBadge status={status} />);
    expect(screen.getByText(label)).toBeInTheDocument();
  });

  it("applies an amber colour class for pending statuses", () => {
    const { container } = render(<StatusBadge status="BomPending" />);
    expect(container.firstChild).toHaveClass("bg-amber-500/10");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd bom-web
npm test -- --run src/components/ui/StatusBadge.test.tsx
```

Expected: FAIL — module not found.

- [ ] **Step 3: Write implementation**

Create `bom-web/src/components/ui/StatusBadge.tsx`:

```tsx
import { cn } from "@/lib/cn";
import type { RequisitionStatus } from "@/types/api";

const LABELS: Record<RequisitionStatus, string> = {
  Draft: "Draft",
  BomPending: "BOM Pending",
  BomInProgress: "BOM In Progress",
  CostingPending: "Costing Pending",
  CostingInProgress: "Costing In Progress",
  MdReview: "MD Review",
  Approved: "Approved",
  Rejected: "Rejected",
};

const COLOURS: Record<RequisitionStatus, string> = {
  Draft: "bg-slate-500/10 text-slate-600 dark:text-slate-300 ring-slate-500/20",
  BomPending: "bg-amber-500/10 text-amber-600 dark:text-amber-400 ring-amber-500/20",
  CostingPending: "bg-amber-500/10 text-amber-600 dark:text-amber-400 ring-amber-500/20",
  MdReview: "bg-amber-500/10 text-amber-600 dark:text-amber-400 ring-amber-500/20",
  BomInProgress: "bg-blue-500/10 text-blue-600 dark:text-blue-400 ring-blue-500/20",
  CostingInProgress: "bg-blue-500/10 text-blue-600 dark:text-blue-400 ring-blue-500/20",
  Approved: "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 ring-emerald-500/20",
  Rejected: "bg-rose-500/10 text-rose-600 dark:text-rose-400 ring-rose-500/20",
};

interface Props {
  status: RequisitionStatus;
  className?: string;
}

export function StatusBadge({ status, className }: Props) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset",
        COLOURS[status],
        className,
      )}
    >
      {LABELS[status]}
    </span>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
cd bom-web
npm test -- --run src/components/ui/StatusBadge.test.tsx
```

Expected: 9/9 passing (8 parameterised + 1 colour check).

- [ ] **Step 5: Commit**

```bash
cd "D:/shan projects/BOM_Price_Approval"
git add bom-web/src/components/ui/StatusBadge.tsx bom-web/src/components/ui/StatusBadge.test.tsx
git commit -m "feat(web): add StatusBadge component"
```

---

## Task 4: `DataTable` wrapper (TDD)

**Files:**
- Create: `bom-web/src/components/ui/DataTable.tsx`
- Create: `bom-web/src/components/ui/DataTable.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `bom-web/src/components/ui/DataTable.test.tsx`:

```tsx
import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { DataTable } from "./DataTable";
import type { ColumnDef } from "@tanstack/react-table";

interface Row {
  id: number;
  name: string;
  qty: number;
}

const rows: Row[] = [
  { id: 1, name: "Alpha", qty: 10 },
  { id: 2, name: "Bravo", qty: 20 },
];

const columns: ColumnDef<Row>[] = [
  { accessorKey: "name", header: "Name" },
  { accessorKey: "qty", header: "Qty" },
];

describe("DataTable", () => {
  it("renders column headers and rows", () => {
    render(<DataTable columns={columns} data={rows} />);
    expect(screen.getByText("Name")).toBeInTheDocument();
    expect(screen.getByText("Qty")).toBeInTheDocument();
    expect(screen.getByText("Alpha")).toBeInTheDocument();
    expect(screen.getByText("Bravo")).toBeInTheDocument();
  });

  it("shows skeleton rows when isLoading", () => {
    render(<DataTable columns={columns} data={[]} isLoading />);
    expect(screen.getAllByTestId("data-table-skeleton-row")).toHaveLength(5);
  });

  it("renders an empty state when data is empty and not loading", () => {
    render(
      <DataTable
        columns={columns}
        data={[]}
        emptyState={<div>no rows</div>}
      />,
    );
    expect(screen.getByText("no rows")).toBeInTheDocument();
  });

  it("fires onRowClick when a row is clicked", () => {
    const onRowClick = vi.fn();
    render(<DataTable columns={columns} data={rows} onRowClick={onRowClick} />);
    fireEvent.click(screen.getByText("Alpha"));
    expect(onRowClick).toHaveBeenCalledWith(rows[0]);
  });

  it("sorts rows when a sortable header is clicked", () => {
    render(<DataTable columns={columns} data={rows} />);
    // Click "Name" header — default ascending → Alpha, Bravo is already correct, so click twice for desc
    fireEvent.click(screen.getByText("Name"));
    fireEvent.click(screen.getByText("Name"));
    const visibleRows = screen.getAllByRole("row").slice(1); // skip header row
    expect(visibleRows[0]).toHaveTextContent("Bravo");
    expect(visibleRows[1]).toHaveTextContent("Alpha");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd bom-web
npm test -- --run src/components/ui/DataTable.test.tsx
```

Expected: FAIL — module not found.

- [ ] **Step 3: Write implementation**

Create `bom-web/src/components/ui/DataTable.tsx`:

```tsx
import { useState, type ReactNode } from "react";
import {
  flexRender,
  getCoreRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type SortingState,
} from "@tanstack/react-table";
import { cn } from "@/lib/cn";

interface DataTableProps<TData> {
  columns: ColumnDef<TData>[];
  data: TData[];
  isLoading?: boolean;
  emptyState?: ReactNode;
  onRowClick?: (row: TData) => void;
  initialSort?: SortingState;
}

const SKELETON_ROW_COUNT = 5;

export function DataTable<TData>({
  columns,
  data,
  isLoading = false,
  emptyState,
  onRowClick,
  initialSort = [],
}: DataTableProps<TData>) {
  const [sorting, setSorting] = useState<SortingState>(initialSort);

  const table = useReactTable({
    data,
    columns,
    state: { sorting },
    onSortingChange: setSorting,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
  });

  const showEmpty = !isLoading && data.length === 0;

  return (
    <div className="overflow-x-auto rounded-lg border border-border bg-card">
      <table className="w-full text-sm">
        <thead className="border-b border-border bg-muted/40">
          {table.getHeaderGroups().map((headerGroup) => (
            <tr key={headerGroup.id}>
              {headerGroup.headers.map((header) => {
                const canSort = header.column.getCanSort();
                return (
                  <th
                    key={header.id}
                    className={cn(
                      "px-4 py-2 text-left font-medium text-muted-foreground",
                      canSort && "cursor-pointer select-none",
                    )}
                    onClick={canSort ? header.column.getToggleSortingHandler() : undefined}
                  >
                    {flexRender(header.column.columnDef.header, header.getContext())}
                  </th>
                );
              })}
            </tr>
          ))}
        </thead>
        <tbody>
          {isLoading &&
            Array.from({ length: SKELETON_ROW_COUNT }).map((_, i) => (
              <tr key={`skeleton-${i}`} data-testid="data-table-skeleton-row">
                {columns.map((_, j) => (
                  <td key={j} className="px-4 py-3">
                    <div className="h-4 w-3/4 animate-pulse rounded bg-muted" />
                  </td>
                ))}
              </tr>
            ))}

          {!isLoading &&
            table.getRowModel().rows.map((row) => (
              <tr
                key={row.id}
                className={cn(
                  "border-t border-border",
                  onRowClick && "cursor-pointer hover:bg-muted/50",
                )}
                onClick={onRowClick ? () => onRowClick(row.original) : undefined}
                role={onRowClick ? "button" : undefined}
                tabIndex={onRowClick ? 0 : undefined}
                onKeyDown={
                  onRowClick
                    ? (e) => {
                        if (e.key === "Enter" || e.key === " ") {
                          e.preventDefault();
                          onRowClick(row.original);
                        }
                      }
                    : undefined
                }
              >
                {row.getVisibleCells().map((cell) => (
                  <td key={cell.id} className="px-4 py-3">
                    {flexRender(cell.column.columnDef.cell, cell.getContext())}
                  </td>
                ))}
              </tr>
            ))}

          {showEmpty && (
            <tr>
              <td
                colSpan={columns.length}
                className="px-4 py-10 text-center text-muted-foreground"
              >
                {emptyState ?? "No results"}
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
cd bom-web
npm test -- --run src/components/ui/DataTable.test.tsx
```

Expected: 5/5 passing.

- [ ] **Step 5: Commit**

```bash
cd "D:/shan projects/BOM_Price_Approval"
git add bom-web/src/components/ui/DataTable.tsx bom-web/src/components/ui/DataTable.test.tsx
git commit -m "feat(web): add DataTable wrapper over tanstack-table"
```

---

## Task 5: `SearchableSelect` component (TDD)

**Files:**
- Create: `bom-web/src/components/ui/SearchableSelect.tsx`
- Create: `bom-web/src/components/ui/SearchableSelect.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `bom-web/src/components/ui/SearchableSelect.test.tsx`:

```tsx
import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { SearchableSelect } from "./SearchableSelect";

interface Opt {
  id: number;
  label: string;
}

const options: Opt[] = [
  { id: 1, label: "Apple" },
  { id: 2, label: "Apricot" },
  { id: 3, label: "Banana" },
];

function getLabel(o: Opt) {
  return o.label;
}
function getValue(o: Opt) {
  return o.id;
}

describe("SearchableSelect", () => {
  it("shows all options when focused with no filter", () => {
    const onChange = vi.fn();
    render(
      <SearchableSelect
        options={options}
        value={null}
        onChange={onChange}
        getLabel={getLabel}
        getValue={getValue}
        placeholder="Pick one"
      />,
    );
    fireEvent.focus(screen.getByPlaceholderText("Pick one"));
    expect(screen.getByText("Apple")).toBeInTheDocument();
    expect(screen.getByText("Apricot")).toBeInTheDocument();
    expect(screen.getByText("Banana")).toBeInTheDocument();
  });

  it("filters options by case-insensitive substring", () => {
    const onChange = vi.fn();
    render(
      <SearchableSelect
        options={options}
        value={null}
        onChange={onChange}
        getLabel={getLabel}
        getValue={getValue}
      />,
    );
    const input = screen.getByRole("combobox");
    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "ap" } });
    expect(screen.getByText("Apple")).toBeInTheDocument();
    expect(screen.getByText("Apricot")).toBeInTheDocument();
    expect(screen.queryByText("Banana")).not.toBeInTheDocument();
  });

  it("calls onChange with the selected option when a row is clicked", () => {
    const onChange = vi.fn();
    render(
      <SearchableSelect
        options={options}
        value={null}
        onChange={onChange}
        getLabel={getLabel}
        getValue={getValue}
      />,
    );
    const input = screen.getByRole("combobox");
    fireEvent.focus(input);
    fireEvent.click(screen.getByText("Banana"));
    expect(onChange).toHaveBeenCalledWith(options[2]);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd bom-web
npm test -- --run src/components/ui/SearchableSelect.test.tsx
```

Expected: FAIL — module not found.

- [ ] **Step 3: Write implementation**

Create `bom-web/src/components/ui/SearchableSelect.tsx`:

```tsx
import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/cn";

interface SearchableSelectProps<T> {
  options: T[];
  value: T | null;
  onChange: (v: T | null) => void;
  getLabel: (o: T) => string;
  getValue: (o: T) => string | number;
  placeholder?: string;
  disabled?: boolean;
  id?: string;
}

export function SearchableSelect<T>({
  options,
  value,
  onChange,
  getLabel,
  getValue,
  placeholder,
  disabled,
  id,
}: SearchableSelectProps<T>) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [highlight, setHighlight] = useState(0);
  const wrapperRef = useRef<HTMLDivElement>(null);

  const displayValue = open ? query : value ? getLabel(value) : "";

  const filtered = options.filter((o) =>
    getLabel(o).toLowerCase().includes(query.toLowerCase()),
  );

  useEffect(() => {
    if (!open) return;
    const onDocMouseDown = (e: MouseEvent) => {
      if (wrapperRef.current && !wrapperRef.current.contains(e.target as Node)) {
        setOpen(false);
        setQuery("");
      }
    };
    document.addEventListener("mousedown", onDocMouseDown);
    return () => document.removeEventListener("mousedown", onDocMouseDown);
  }, [open]);

  function select(option: T) {
    onChange(option);
    setOpen(false);
    setQuery("");
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setHighlight((h) => Math.min(h + 1, filtered.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setHighlight((h) => Math.max(h - 1, 0));
    } else if (e.key === "Enter" && filtered[highlight]) {
      e.preventDefault();
      select(filtered[highlight]);
    } else if (e.key === "Escape") {
      setOpen(false);
      setQuery("");
    }
  }

  return (
    <div ref={wrapperRef} className="relative">
      <input
        id={id}
        role="combobox"
        aria-expanded={open}
        disabled={disabled}
        value={displayValue}
        placeholder={placeholder}
        className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50"
        onFocus={() => setOpen(true)}
        onChange={(e) => {
          setQuery(e.target.value);
          setOpen(true);
          setHighlight(0);
        }}
        onKeyDown={onKeyDown}
      />
      {open && filtered.length > 0 && (
        <ul className="absolute z-20 mt-1 max-h-60 w-full overflow-auto rounded-md border border-border bg-popover text-sm shadow-md">
          {filtered.map((o, i) => (
            <li
              key={getValue(o)}
              className={cn(
                "cursor-pointer px-3 py-2 hover:bg-muted",
                i === highlight && "bg-muted",
              )}
              onMouseDown={(e) => {
                e.preventDefault();
                select(o);
              }}
            >
              {getLabel(o)}
            </li>
          ))}
        </ul>
      )}
      {open && filtered.length === 0 && (
        <div className="absolute z-20 mt-1 w-full rounded-md border border-border bg-popover px-3 py-2 text-sm text-muted-foreground shadow-md">
          No matches
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
cd bom-web
npm test -- --run src/components/ui/SearchableSelect.test.tsx
```

Expected: 3/3 passing.

- [ ] **Step 5: Commit**

```bash
cd "D:/shan projects/BOM_Price_Approval"
git add bom-web/src/components/ui/SearchableSelect.tsx bom-web/src/components/ui/SearchableSelect.test.tsx
git commit -m "feat(web): add SearchableSelect combobox component"
```

---

## Task 6: `RequisitionTimeline` component (TDD)

**Files:**
- Create: `bom-web/src/features/requisitions/components/RequisitionTimeline.tsx`
- Create: `bom-web/src/features/requisitions/components/RequisitionTimeline.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `bom-web/src/features/requisitions/components/RequisitionTimeline.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { RequisitionTimeline } from "./RequisitionTimeline";

const createdAt = "2026-04-14T10:00:00Z";
const updatedAt = "2026-04-14T11:30:00Z";

describe("RequisitionTimeline", () => {
  it("renders all five step labels", () => {
    render(
      <RequisitionTimeline
        status="BomPending"
        createdAt={createdAt}
        updatedAt={updatedAt}
      />,
    );
    expect(screen.getByText("Submitted")).toBeInTheDocument();
    expect(screen.getByText("BOM")).toBeInTheDocument();
    expect(screen.getByText("Costing")).toBeInTheDocument();
    expect(screen.getByText("MD Review")).toBeInTheDocument();
    expect(screen.getByText("Result")).toBeInTheDocument();
  });

  it('marks the BOM step as in-progress when status is "BomPending"', () => {
    render(
      <RequisitionTimeline
        status="BomPending"
        createdAt={createdAt}
        updatedAt={updatedAt}
      />,
    );
    expect(screen.getByTestId("step-BOM")).toHaveAttribute("data-state", "in-progress");
    expect(screen.getByTestId("step-Submitted")).toHaveAttribute("data-state", "completed");
    expect(screen.getByTestId("step-Costing")).toHaveAttribute("data-state", "pending");
  });

  it('marks all prior steps completed and Result as "approved" when status is "Approved"', () => {
    render(
      <RequisitionTimeline
        status="Approved"
        createdAt={createdAt}
        updatedAt={updatedAt}
      />,
    );
    expect(screen.getByTestId("step-Submitted")).toHaveAttribute("data-state", "completed");
    expect(screen.getByTestId("step-BOM")).toHaveAttribute("data-state", "completed");
    expect(screen.getByTestId("step-Costing")).toHaveAttribute("data-state", "completed");
    expect(screen.getByTestId("step-MD Review")).toHaveAttribute("data-state", "completed");
    expect(screen.getByTestId("step-Result")).toHaveAttribute("data-state", "approved");
  });

  it('collapses middle steps to cancelled when status is "Rejected"', () => {
    render(
      <RequisitionTimeline
        status="Rejected"
        createdAt={createdAt}
        updatedAt={updatedAt}
      />,
    );
    expect(screen.getByTestId("step-Submitted")).toHaveAttribute("data-state", "completed");
    expect(screen.getByTestId("step-BOM")).toHaveAttribute("data-state", "cancelled");
    expect(screen.getByTestId("step-Costing")).toHaveAttribute("data-state", "cancelled");
    expect(screen.getByTestId("step-MD Review")).toHaveAttribute("data-state", "cancelled");
    expect(screen.getByTestId("step-Result")).toHaveAttribute("data-state", "rejected");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd bom-web
npm test -- --run src/features/requisitions/components/RequisitionTimeline.test.tsx
```

Expected: FAIL — module not found.

- [ ] **Step 3: Write implementation**

Create `bom-web/src/features/requisitions/components/RequisitionTimeline.tsx`:

```tsx
import { motion } from "framer-motion";
import type { RequisitionStatus } from "@/types/api";
import { formatRelative } from "@/utils/date";
import { cn } from "@/lib/cn";

type StepState =
  | "pending"
  | "in-progress"
  | "completed"
  | "cancelled"
  | "approved"
  | "rejected";

interface Step {
  key: string;
  label: string;
  role: string;
  state: StepState;
  timestamp?: string;
}

function buildSteps(
  status: RequisitionStatus,
  createdAt: string,
  updatedAt: string,
): Step[] {
  const rejected = status === "Rejected";
  const order: RequisitionStatus[] = [
    "Draft",
    "BomPending",
    "BomInProgress",
    "CostingPending",
    "CostingInProgress",
    "MdReview",
    "Approved",
  ];
  const idx = order.indexOf(status === "Rejected" ? "MdReview" : status);

  const stateFor = (from: number, to: number): StepState => {
    if (rejected) return "cancelled";
    if (idx > to) return "completed";
    if (idx >= from && idx <= to) return "in-progress";
    return "pending";
  };

  const submitted: Step = {
    key: "Submitted",
    label: "Submitted",
    role: "Sales Person",
    state: "completed",
    timestamp: formatRelative(createdAt),
  };

  const bom: Step = {
    key: "BOM",
    label: "BOM",
    role: "BOM Creator",
    state: stateFor(1, 2), // BomPending (1) or BomInProgress (2)
  };

  const costing: Step = {
    key: "Costing",
    label: "Costing",
    role: "Accountant",
    state: stateFor(3, 4),
  };

  const mdReview: Step = {
    key: "MD Review",
    label: "MD Review",
    role: "Managing Director",
    state: stateFor(5, 5),
  };

  let resultState: StepState = "pending";
  if (status === "Approved") resultState = "approved";
  else if (status === "Rejected") resultState = "rejected";

  const result: Step = {
    key: "Result",
    label: "Result",
    role: "",
    state: resultState,
  };

  // Stamp the current in-progress step with updatedAt
  const steps = [submitted, bom, costing, mdReview, result];
  const active = steps.find((s) => s.state === "in-progress");
  if (active) active.timestamp = formatRelative(updatedAt);

  return steps;
}

const CIRCLE_STYLES: Record<StepState, string> = {
  pending: "bg-muted border-border",
  "in-progress": "bg-amber-500/10 border-amber-500 ring-2 ring-amber-500/30",
  completed: "bg-primary border-primary",
  cancelled: "bg-muted border-border opacity-60",
  approved: "bg-emerald-500 border-emerald-500",
  rejected: "bg-rose-500 border-rose-500",
};

interface Props {
  status: RequisitionStatus;
  createdAt: string;
  updatedAt: string;
}

export function RequisitionTimeline({ status, createdAt, updatedAt }: Props) {
  const steps = buildSteps(status, createdAt, updatedAt);

  return (
    <ol className="relative ml-3 border-l border-border pl-6">
      {steps.map((step, i) => (
        <motion.li
          key={step.key}
          data-testid={`step-${step.key}`}
          data-state={step.state}
          initial={{ opacity: 0, x: -8 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ delay: i * 0.05 }}
          className="relative mb-6 last:mb-0"
        >
          <span
            className={cn(
              "absolute -left-[33px] mt-1 h-4 w-4 rounded-full border-2",
              CIRCLE_STYLES[step.state],
            )}
          />
          <div className="flex items-baseline justify-between gap-4">
            <div>
              <p className="text-sm font-medium">{step.label}</p>
              {step.role && <p className="text-xs text-muted-foreground">{step.role}</p>}
            </div>
            {step.timestamp && (
              <p className="text-xs text-muted-foreground">{step.timestamp}</p>
            )}
          </div>
        </motion.li>
      ))}
    </ol>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
cd bom-web
npm test -- --run src/features/requisitions/components/RequisitionTimeline.test.tsx
```

Expected: 4/4 passing.

- [ ] **Step 5: Commit**

```bash
cd "D:/shan projects/BOM_Price_Approval"
git add bom-web/src/features/requisitions/components/RequisitionTimeline.tsx bom-web/src/features/requisitions/components/RequisitionTimeline.test.tsx
git commit -m "feat(web): add RequisitionTimeline component"
```

---

## Task 7: Lookup hooks (`useCustomers`, `useItems`, `useActiveExchangeRates`)

**Files:**
- Create: `bom-web/src/api/lookups.ts`
- Create: `bom-web/src/api/lookups.test.ts`

- [ ] **Step 1: Write the failing test**

Create `bom-web/src/api/lookups.test.ts`:

```ts
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";

vi.mock("@/api/axios", () => {
  const get = vi.fn();
  return { api: { get } };
});

import { api } from "@/api/axios";
import { useCustomers, useItems, useActiveExchangeRates } from "./lookups";

function wrapper(client: QueryClient) {
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
}

function freshClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false } } });
}

describe("lookup hooks", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it("useCustomers fetches /customers", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: [{ id: 1, name: "ACME" }] });
    const client = freshClient();
    const { result } = renderHook(() => useCustomers(), { wrapper: wrapper(client) });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(api.get).toHaveBeenCalledWith("/customers");
    expect(result.current.data).toEqual([{ id: 1, name: "ACME" }]);
  });

  it("useItems fetches /items", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: [{ id: 2, description: "Widget" }] });
    const client = freshClient();
    const { result } = renderHook(() => useItems(), { wrapper: wrapper(client) });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(api.get).toHaveBeenCalledWith("/items");
    expect(result.current.data).toEqual([{ id: 2, description: "Widget" }]);
  });

  it("useActiveExchangeRates fetches /exchange-rates/active", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({
      data: [{ id: 3, currencyCode: "USD", rateToAed: 3.67 }],
    });
    const client = freshClient();
    const { result } = renderHook(() => useActiveExchangeRates(), {
      wrapper: wrapper(client),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(api.get).toHaveBeenCalledWith("/exchange-rates/active");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd bom-web
npm test -- --run src/api/lookups.test.ts
```

Expected: FAIL — module not found.

- [ ] **Step 3: Write implementation**

Create `bom-web/src/api/lookups.ts`:

```ts
import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type { Customer, Item, ExchangeRate } from "@/types/api";

const FIVE_MINUTES = 5 * 60 * 1000;

export function useCustomers() {
  return useQuery({
    queryKey: ["customers"],
    queryFn: () => api.get<Customer[]>("/customers").then((r) => r.data),
    staleTime: FIVE_MINUTES,
  });
}

export function useItems() {
  return useQuery({
    queryKey: ["items"],
    queryFn: () => api.get<Item[]>("/items").then((r) => r.data),
    staleTime: FIVE_MINUTES,
  });
}

export function useActiveExchangeRates() {
  return useQuery({
    queryKey: ["exchangeRates", "active"],
    queryFn: () =>
      api.get<ExchangeRate[]>("/exchange-rates/active").then((r) => r.data),
    staleTime: FIVE_MINUTES,
  });
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
cd bom-web
npm test -- --run src/api/lookups.test.ts
```

Expected: 3/3 passing.

- [ ] **Step 5: Commit**

```bash
cd "D:/shan projects/BOM_Price_Approval"
git add bom-web/src/api/lookups.ts bom-web/src/api/lookups.test.ts
git commit -m "feat(web): add lookup hooks for customers, items, exchange rates"
```

---

## Task 8: Requisitions API hooks

**Files:**
- Create: `bom-web/src/features/requisitions/requisitionsApi.ts`

Tests for the hooks are embedded in the page tests (Tasks 9–11), so no standalone test file — this keeps the contract exercised end-to-end rather than through isolated mocks twice.

- [ ] **Step 1: Write implementation**

Create `bom-web/src/features/requisitions/requisitionsApi.ts`:

```ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type {
  CreateRequisitionRequest,
  RequisitionDetail,
  RequisitionListItem,
} from "@/types/api";

export const requisitionKeys = {
  all: ["requisitions"] as const,
  list: () => [...requisitionKeys.all, "list"] as const,
  detail: (id: number) => [...requisitionKeys.all, "detail", id] as const,
};

export function useRequisitions() {
  return useQuery({
    queryKey: requisitionKeys.list(),
    queryFn: () =>
      api.get<RequisitionListItem[]>("/requisitions").then((r) => r.data),
  });
}

export function useRequisition(id: number) {
  return useQuery({
    queryKey: requisitionKeys.detail(id),
    queryFn: () =>
      api.get<RequisitionDetail>(`/requisitions/${id}`).then((r) => r.data),
    enabled: Number.isFinite(id) && id > 0,
  });
}

interface CreateResponse {
  id: number;
  refNo: string;
}

export function useCreateRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateRequisitionRequest) =>
      api.post<CreateResponse>("/requisitions", body).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: requisitionKeys.all });
    },
  });
}
```

- [ ] **Step 2: Run all tests — nothing should break**

```bash
cd bom-web
npm test -- --run
```

Expected: all previously-passing tests still pass. No new tests added yet.

- [ ] **Step 3: Commit**

```bash
cd "D:/shan projects/BOM_Price_Approval"
git add bom-web/src/features/requisitions/requisitionsApi.ts
git commit -m "feat(web): add requisitions API hooks"
```

---

## Task 9: `RequisitionListPage` — structure + data (TDD)

**Files:**
- Create: `bom-web/src/features/requisitions/RequisitionListPage.tsx`
- Create: `bom-web/src/features/requisitions/RequisitionListPage.test.tsx`
- Create: `bom-web/src/features/requisitions/components/RequisitionFilters.tsx`

Note: this task introduces the page with **filters included from the start** — skipping the separate filter task because splitting would force duplicating the test setup boilerplate.

- [ ] **Step 1: Write the failing test**

Create `bom-web/src/features/requisitions/RequisitionListPage.test.tsx`:

```tsx
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import type { ReactNode } from "react";
import type { RequisitionListItem } from "@/types/api";
import { useAuthStore } from "@/store/authStore";

const mockNavigate = vi.fn();
vi.mock("react-router-dom", async () => {
  const actual = await vi.importActual<typeof import("react-router-dom")>("react-router-dom");
  return { ...actual, useNavigate: () => mockNavigate };
});

vi.mock("@/api/axios", () => ({ api: { get: vi.fn() } }));

import { api } from "@/api/axios";
import RequisitionListPage from "./RequisitionListPage";

function wrap(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>
  );
}

const sampleRows: RequisitionListItem[] = [
  {
    id: 1,
    refNo: "REQ-0001",
    status: "BomPending",
    itemDescription: "HDPE Pipe 20mm",
    customerName: "ACME",
    expectedQty: 100,
    currencyCode: "AED",
    branchName: "Fujairah",
    salesPersonName: "Ali",
    createdAt: new Date().toISOString(),
  },
  {
    id: 2,
    refNo: "REQ-0002",
    status: "Approved",
    itemDescription: "LDPE Sheet",
    customerName: "BetaCorp",
    expectedQty: 50,
    currencyCode: "USD",
    branchName: "Fujairah",
    salesPersonName: "Ali",
    createdAt: new Date().toISOString(),
  },
];

describe("RequisitionListPage", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    mockNavigate.mockReset();
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "SalesPerson",
      userId: 10,
      name: "Ali",
      branchId: 1,
    });
  });

  afterEach(() => {
    useAuthStore.getState().logout();
  });

  it("shows a loading state and then the rows", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleRows });
    render(wrap(<RequisitionListPage />));
    expect(screen.getAllByTestId("data-table-skeleton-row").length).toBeGreaterThan(0);
    await waitFor(() =>
      expect(screen.getByText("REQ-0001")).toBeInTheDocument(),
    );
    expect(screen.getByText("REQ-0002")).toBeInTheDocument();
  });

  it('renders the "New Requisition" button only for SalesPerson', async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    render(wrap(<RequisitionListPage />));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /new requisition/i })).toBeInTheDocument(),
    );

    // Switch role → button disappears
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "BomCreator",
      userId: 11,
      name: "Bob",
      branchId: 1,
    });
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    const { rerender } = render(wrap(<RequisitionListPage />));
    rerender(wrap(<RequisitionListPage />));
    const buttons = screen.queryAllByRole("button", { name: /new requisition/i });
    // Only the previously-rendered instance counts — the BomCreator render has none
    expect(buttons.length).toBeLessThanOrEqual(1);
  });

  it("navigates to the detail page when a row is clicked", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleRows });
    render(wrap(<RequisitionListPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    fireEvent.click(screen.getByText("REQ-0001"));
    expect(mockNavigate).toHaveBeenCalledWith("/requisitions/1");
  });

  it("filters rows by status", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleRows });
    render(wrap(<RequisitionListPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());

    const statusFilter = screen.getByLabelText(/status/i);
    fireEvent.change(statusFilter, { target: { value: "Approved" } });

    expect(screen.queryByText("REQ-0001")).not.toBeInTheDocument();
    expect(screen.getByText("REQ-0002")).toBeInTheDocument();
  });

  it('shows a "Create your first requisition" empty state for a SalesPerson with no data', async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    render(wrap(<RequisitionListPage />));
    await waitFor(() =>
      expect(screen.getByText(/create your first requisition/i)).toBeInTheDocument(),
    );
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd bom-web
npm test -- --run src/features/requisitions/RequisitionListPage.test.tsx
```

Expected: FAIL — module not found.

- [ ] **Step 3: Write `RequisitionFilters`**

Create `bom-web/src/features/requisitions/components/RequisitionFilters.tsx`:

```tsx
import type { RequisitionStatus } from "@/types/api";

export interface Filters {
  status: RequisitionStatus | "";
  from: string;
  to: string;
}

interface Props {
  value: Filters;
  onChange: (next: Filters) => void;
}

const STATUS_OPTIONS: Array<RequisitionStatus | ""> = [
  "",
  "Draft",
  "BomPending",
  "BomInProgress",
  "CostingPending",
  "CostingInProgress",
  "MdReview",
  "Approved",
  "Rejected",
];

export function RequisitionFilters({ value, onChange }: Props) {
  return (
    <div className="flex flex-wrap items-end gap-3 rounded-lg border border-border bg-card p-4">
      <div className="flex flex-col gap-1">
        <label htmlFor="filter-status" className="text-xs font-medium text-muted-foreground">
          Status
        </label>
        <select
          id="filter-status"
          className="h-9 rounded-md border border-input bg-background px-2 text-sm"
          value={value.status}
          onChange={(e) => onChange({ ...value, status: e.target.value as Filters["status"] })}
        >
          {STATUS_OPTIONS.map((s) => (
            <option key={s || "all"} value={s}>
              {s || "All"}
            </option>
          ))}
        </select>
      </div>

      <div className="flex flex-col gap-1">
        <label htmlFor="filter-from" className="text-xs font-medium text-muted-foreground">
          From
        </label>
        <input
          id="filter-from"
          type="date"
          className="h-9 rounded-md border border-input bg-background px-2 text-sm"
          value={value.from}
          onChange={(e) => onChange({ ...value, from: e.target.value })}
        />
      </div>

      <div className="flex flex-col gap-1">
        <label htmlFor="filter-to" className="text-xs font-medium text-muted-foreground">
          To
        </label>
        <input
          id="filter-to"
          type="date"
          className="h-9 rounded-md border border-input bg-background px-2 text-sm"
          value={value.to}
          onChange={(e) => onChange({ ...value, to: e.target.value })}
        />
      </div>

      <button
        type="button"
        className="h-9 rounded-md border border-input bg-background px-3 text-sm hover:bg-muted"
        onClick={() => onChange({ status: "", from: "", to: "" })}
      >
        Clear
      </button>
    </div>
  );
}
```

- [ ] **Step 4: Write `RequisitionListPage`**

Create `bom-web/src/features/requisitions/RequisitionListPage.tsx`:

```tsx
import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { useRequisitions } from "./requisitionsApi";
import { RequisitionFilters, type Filters } from "./components/RequisitionFilters";
import { DataTable } from "@/components/ui/DataTable";
import { StatusBadge } from "@/components/ui/StatusBadge";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { useAuthStore } from "@/store/authStore";
import { formatRelative } from "@/utils/date";
import type { RequisitionListItem } from "@/types/api";

const columns: ColumnDef<RequisitionListItem>[] = [
  {
    accessorKey: "refNo",
    header: "Ref No",
    cell: (info) => (
      <span className="font-mono text-xs">{info.getValue() as string}</span>
    ),
  },
  {
    accessorKey: "status",
    header: "Status",
    cell: (info) => <StatusBadge status={info.getValue() as RequisitionListItem["status"]} />,
  },
  {
    accessorKey: "itemDescription",
    header: "Item",
    cell: (info) => {
      const v = info.getValue() as string;
      return <span title={v}>{v.length > 40 ? `${v.slice(0, 40)}…` : v}</span>;
    },
    enableSorting: false,
  },
  { accessorKey: "customerName", header: "Customer" },
  {
    id: "qty",
    header: "Qty",
    accessorFn: (row) => `${row.expectedQty} ${row.currencyCode}`,
    enableSorting: false,
  },
  { accessorKey: "branchName", header: "Branch" },
  {
    accessorKey: "createdAt",
    header: "Created",
    cell: (info) => formatRelative(info.getValue() as string),
  },
];

const EMPTY_FILTERS: Filters = { status: "", from: "", to: "" };

export default function RequisitionListPage() {
  const role = useAuthStore((s) => s.user?.role);
  const branchId = useAuthStore((s) => s.user?.branchId);
  const navigate = useNavigate();
  const [filters, setFilters] = useState<Filters>(EMPTY_FILTERS);

  const { data, isLoading, isError, refetch } = useRequisitions();

  const visibleColumns = useMemo(() => {
    if (branchId) {
      return columns.filter((c) => (c as { accessorKey?: string }).accessorKey !== "branchName");
    }
    return columns;
  }, [branchId]);

  const filtered = useMemo(() => {
    if (!data) return [];
    return data.filter((r) => {
      if (filters.status && r.status !== filters.status) return false;
      if (filters.from && new Date(r.createdAt) < new Date(filters.from)) return false;
      if (filters.to) {
        const to = new Date(filters.to);
        to.setHours(23, 59, 59, 999);
        if (new Date(r.createdAt) > to) return false;
      }
      return true;
    });
  }, [data, filters]);

  const hasActiveFilters =
    filters.status !== "" || filters.from !== "" || filters.to !== "";

  const emptyState =
    hasActiveFilters ? (
      <div className="space-y-2">
        <p>No requisitions match your filters.</p>
        <Button variant="ghost" onClick={() => setFilters(EMPTY_FILTERS)}>
          Clear filters
        </Button>
      </div>
    ) : role === "SalesPerson" ? (
      <div className="space-y-2">
        <p>You haven't created any requisitions yet.</p>
        <Link to="/requisitions/new">
          <Button>Create your first requisition</Button>
        </Link>
      </div>
    ) : (
      <p>No requisitions waiting.</p>
    );

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">Requisitions</h1>
        {role === "SalesPerson" && (
          <Link to="/requisitions/new">
            <Button>New Requisition</Button>
          </Link>
        )}
      </div>

      <RequisitionFilters value={filters} onChange={setFilters} />

      {isError && (
        <Card>
          <CardContent className="flex items-center justify-between">
            <p className="text-destructive">Failed to load requisitions.</p>
            <Button variant="ghost" onClick={() => refetch()}>
              Retry
            </Button>
          </CardContent>
        </Card>
      )}

      <DataTable
        columns={visibleColumns}
        data={filtered}
        isLoading={isLoading}
        emptyState={emptyState}
        onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
      />
    </div>
  );
}
```

- [ ] **Step 5: Run test to verify it passes**

```bash
cd bom-web
npm test -- --run src/features/requisitions/RequisitionListPage.test.tsx
```

Expected: 5/5 passing. If the "New Requisition button only for SalesPerson" assertion is flaky due to how `render` accumulates DOM across test invocations, simplify to two separate `render` calls using `cleanup`.

- [ ] **Step 6: Commit**

```bash
cd "D:/shan projects/BOM_Price_Approval"
git add bom-web/src/features/requisitions/RequisitionListPage.tsx bom-web/src/features/requisitions/RequisitionListPage.test.tsx bom-web/src/features/requisitions/components/RequisitionFilters.tsx
git commit -m "feat(web): add RequisitionListPage with client-side filters"
```

---

## Task 10: `NewRequisitionPage` (TDD)

**Files:**
- Create: `bom-web/src/features/requisitions/NewRequisitionPage.tsx`
- Create: `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx`:

```tsx
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import type { ReactNode } from "react";
import { useAuthStore } from "@/store/authStore";

const mockNavigate = vi.fn();
vi.mock("react-router-dom", async () => {
  const actual = await vi.importActual<typeof import("react-router-dom")>("react-router-dom");
  return { ...actual, useNavigate: () => mockNavigate };
});

vi.mock("@/api/axios", () => ({
  api: { get: vi.fn(), post: vi.fn() },
}));

import { api } from "@/api/axios";
import NewRequisitionPage from "./NewRequisitionPage";

function wrap(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>
  );
}

const customers = [{ id: 1, name: "ACME", address: "", email: "", phoneNumber: "", branchId: 1, createdByUserId: 10 }];
const items = [{ id: 2, code: "I-001", description: "HDPE Pipe 20mm", type: "RawMaterial", branchId: 1, isActive: true }];
const rates = [{ id: 3, currencyCode: "USD", currencyName: "US Dollar", rateToAed: 3.67, effectiveDate: "2026-04-01", isActive: true, setByName: "Acc" }];

function mockLookups() {
  vi.mocked(api.get).mockImplementation((url: string) => {
    if (url === "/customers") return Promise.resolve({ data: customers });
    if (url === "/items") return Promise.resolve({ data: items });
    if (url === "/exchange-rates/active") return Promise.resolve({ data: rates });
    return Promise.reject(new Error(`unexpected url ${url}`));
  });
}

describe("NewRequisitionPage", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    vi.mocked(api.post).mockReset();
    mockNavigate.mockReset();
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "SalesPerson",
      userId: 10,
      name: "Ali",
      branchId: 1,
    });
  });

  afterEach(() => {
    useAuthStore.getState().logout();
  });

  it("populates lookups and renders the form", async () => {
    mockLookups();
    render(wrap(<NewRequisitionPage />));
    await waitFor(() => expect(screen.getByLabelText(/expected qty/i)).toBeInTheDocument());
    expect(screen.getByLabelText(/customer/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/item/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/currency/i)).toBeInTheDocument();
  });

  it("blocks submit and surfaces validation errors when fields are missing", async () => {
    mockLookups();
    render(wrap(<NewRequisitionPage />));
    await waitFor(() => expect(screen.getByLabelText(/expected qty/i)).toBeInTheDocument());

    fireEvent.click(screen.getByRole("button", { name: /create/i }));
    await waitFor(() =>
      expect(screen.getByText(/customer is required/i)).toBeInTheDocument(),
    );
    expect(api.post).not.toHaveBeenCalled();
  });

  it("submits and navigates to the detail page on success", async () => {
    mockLookups();
    vi.mocked(api.post).mockResolvedValueOnce({ data: { id: 42, refNo: "REQ-0042" } });
    render(wrap(<NewRequisitionPage />));
    await waitFor(() => expect(screen.getByLabelText(/expected qty/i)).toBeInTheDocument());

    // Pick customer via SearchableSelect
    const customerBox = screen.getByLabelText(/customer/i);
    fireEvent.focus(customerBox);
    fireEvent.click(screen.getByText("ACME"));

    // Pick item
    const itemBox = screen.getByLabelText(/item/i);
    fireEvent.focus(itemBox);
    fireEvent.click(screen.getByText("HDPE Pipe 20mm"));

    // Qty
    fireEvent.change(screen.getByLabelText(/expected qty/i), {
      target: { value: "100" },
    });

    fireEvent.click(screen.getByRole("button", { name: /create/i }));

    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith("/requisitions", {
        customerId: 1,
        itemId: 2,
        expectedQty: 100,
        currencyCode: "AED",
      }),
    );
    await waitFor(() =>
      expect(mockNavigate).toHaveBeenCalledWith("/requisitions/42", { replace: true }),
    );
  });

  it("shows a server error message when submission fails", async () => {
    mockLookups();
    vi.mocked(api.post).mockRejectedValueOnce({
      response: { data: { message: "Boom" } },
    });
    render(wrap(<NewRequisitionPage />));
    await waitFor(() => expect(screen.getByLabelText(/expected qty/i)).toBeInTheDocument());

    fireEvent.focus(screen.getByLabelText(/customer/i));
    fireEvent.click(screen.getByText("ACME"));
    fireEvent.focus(screen.getByLabelText(/item/i));
    fireEvent.click(screen.getByText("HDPE Pipe 20mm"));
    fireEvent.change(screen.getByLabelText(/expected qty/i), { target: { value: "10" } });

    fireEvent.click(screen.getByRole("button", { name: /create/i }));

    await waitFor(() => expect(screen.getByText(/boom/i)).toBeInTheDocument());
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd bom-web
npm test -- --run src/features/requisitions/NewRequisitionPage.test.tsx
```

Expected: FAIL — module not found.

- [ ] **Step 3: Write implementation**

Create `bom-web/src/features/requisitions/NewRequisitionPage.tsx`:

```tsx
import { useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Link, useNavigate } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { SearchableSelect } from "@/components/ui/SearchableSelect";
import { useCustomers, useItems, useActiveExchangeRates } from "@/api/lookups";
import { useCreateRequisition } from "./requisitionsApi";
import type { Customer, Item } from "@/types/api";

const schema = z.object({
  customer: z.object({ id: z.number() }, { required_error: "Customer is required" }).nullable()
    .refine((v) => v !== null, "Customer is required"),
  item: z.object({ id: z.number() }, { required_error: "Item is required" }).nullable()
    .refine((v) => v !== null, "Item is required"),
  expectedQty: z
    .number({ invalid_type_error: "Expected qty is required" })
    .positive("Qty must be greater than zero"),
  currencyCode: z.string().min(1, "Currency is required"),
});

type FormValues = z.infer<typeof schema>;

export default function NewRequisitionPage() {
  const navigate = useNavigate();
  const customersQ = useCustomers();
  const itemsQ = useItems();
  const ratesQ = useActiveExchangeRates();
  const create = useCreateRequisition();
  const [serverError, setServerError] = useState<string | null>(null);

  const { control, handleSubmit, register, formState: { errors, isSubmitting } } =
    useForm<FormValues>({
      resolver: zodResolver(schema),
      defaultValues: { customer: null, item: null, expectedQty: undefined as unknown as number, currencyCode: "AED" },
    });

  const isLoadingLookups = customersQ.isLoading || itemsQ.isLoading || ratesQ.isLoading;
  const lookupError = customersQ.isError || itemsQ.isError || ratesQ.isError;

  const currencies = ["AED", ...(ratesQ.data?.map((r) => r.currencyCode) ?? [])];
  const uniqueCurrencies = Array.from(new Set(currencies));

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null);
    try {
      const created = await create.mutateAsync({
        customerId: values.customer!.id,
        itemId: values.item!.id,
        expectedQty: values.expectedQty,
        currencyCode: values.currencyCode,
      });
      navigate(`/requisitions/${created.id}`, { replace: true });
    } catch (e) {
      const msg =
        (e as { response?: { data?: { message?: string } } }).response?.data?.message ??
        "Failed to create requisition";
      setServerError(msg);
    }
  });

  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <Link to="/requisitions" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="h-4 w-4" /> Back to Requisitions
      </Link>

      <Card>
        <CardHeader>
          <CardTitle>New Requisition</CardTitle>
        </CardHeader>
        <CardContent>
          {lookupError && (
            <p className="mb-4 text-sm text-destructive">
              Failed to load lookups. Please refresh.
            </p>
          )}

          {isLoadingLookups ? (
            <p className="text-sm text-muted-foreground">Loading…</p>
          ) : (
            <form onSubmit={onSubmit} className="space-y-4" noValidate>
              <div className="space-y-2">
                <Label htmlFor="customer">Customer</Label>
                <Controller
                  control={control}
                  name="customer"
                  render={({ field }) => (
                    <SearchableSelect<Customer>
                      id="customer"
                      options={customersQ.data ?? []}
                      value={field.value as Customer | null}
                      onChange={field.onChange}
                      getLabel={(c) => c.name}
                      getValue={(c) => c.id}
                      placeholder="Search customers…"
                    />
                  )}
                />
                {errors.customer && (
                  <p className="text-xs text-destructive">
                    {errors.customer.message as string}
                  </p>
                )}
              </div>

              <div className="space-y-2">
                <Label htmlFor="item">Item</Label>
                <Controller
                  control={control}
                  name="item"
                  render={({ field }) => (
                    <SearchableSelect<Item>
                      id="item"
                      options={itemsQ.data ?? []}
                      value={field.value as Item | null}
                      onChange={field.onChange}
                      getLabel={(i) => i.description}
                      getValue={(i) => i.id}
                      placeholder="Search items…"
                    />
                  )}
                />
                {errors.item && (
                  <p className="text-xs text-destructive">
                    {errors.item.message as string}
                  </p>
                )}
              </div>

              <div className="space-y-2">
                <Label htmlFor="expectedQty">Expected Qty</Label>
                <Input
                  id="expectedQty"
                  type="number"
                  step="0.0001"
                  {...register("expectedQty", { valueAsNumber: true })}
                />
                {errors.expectedQty && (
                  <p className="text-xs text-destructive">{errors.expectedQty.message}</p>
                )}
              </div>

              <div className="space-y-2">
                <Label htmlFor="currencyCode">Currency</Label>
                <select
                  id="currencyCode"
                  className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
                  {...register("currencyCode")}
                >
                  {uniqueCurrencies.map((c) => (
                    <option key={c} value={c}>
                      {c}
                    </option>
                  ))}
                </select>
                {errors.currencyCode && (
                  <p className="text-xs text-destructive">{errors.currencyCode.message}</p>
                )}
              </div>

              {serverError && (
                <p className="text-sm text-destructive">{serverError}</p>
              )}

              <Button type="submit" disabled={isSubmitting || create.isPending}>
                {create.isPending ? "Creating…" : "Create"}
              </Button>
            </form>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
cd bom-web
npm test -- --run src/features/requisitions/NewRequisitionPage.test.tsx
```

Expected: 4/4 passing. If zod message parsing differs, adjust test regex to match actual error string.

- [ ] **Step 5: Commit**

```bash
cd "D:/shan projects/BOM_Price_Approval"
git add bom-web/src/features/requisitions/NewRequisitionPage.tsx bom-web/src/features/requisitions/NewRequisitionPage.test.tsx
git commit -m "feat(web): add NewRequisitionPage form"
```

---

## Task 11: `RequisitionDetailPage` (TDD)

**Files:**
- Create: `bom-web/src/features/requisitions/RequisitionDetailPage.tsx`
- Create: `bom-web/src/features/requisitions/RequisitionDetailPage.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `bom-web/src/features/requisitions/RequisitionDetailPage.test.tsx`:

```tsx
import { render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import type { ReactNode } from "react";
import type { RequisitionDetail } from "@/types/api";
import { useAuthStore } from "@/store/authStore";

vi.mock("@/api/axios", () => ({ api: { get: vi.fn() } }));

import { api } from "@/api/axios";
import RequisitionDetailPage from "./RequisitionDetailPage";

function wrap(ui: ReactNode, path = "/requisitions/1") {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/requisitions/:id" element={ui} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

const sample: RequisitionDetail = {
  id: 1,
  refNo: "REQ-0001",
  status: "BomPending",
  itemId: 2,
  itemDescription: "HDPE Pipe 20mm",
  customerId: 3,
  customerName: "ACME",
  customerEmail: "sales@acme.test",
  customerPhone: "+971501234567",
  customerAddress: "Fujairah FZ",
  expectedQty: 100,
  currencyCode: "AED",
  exchangeRateSnapshot: null,
  branchId: 1,
  branchName: "Fujairah",
  salesPersonId: 10,
  salesPersonName: "Ali",
  createdAt: "2026-04-14T10:00:00Z",
  updatedAt: "2026-04-14T11:00:00Z",
  bom: null,
  approval: null,
};

describe("RequisitionDetailPage", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "BomCreator",
      userId: 11,
      name: "Bob",
      branchId: 1,
    });
  });

  afterEach(() => {
    useAuthStore.getState().logout();
  });

  it("renders the header, timeline, and summary cards", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sample });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    expect(screen.getByText("ACME")).toBeInTheDocument();
    expect(screen.getByText(/BOM not yet created/i)).toBeInTheDocument();
    expect(screen.getByText(/Not yet submitted for approval/i)).toBeInTheDocument();
    expect(screen.getByTestId("step-Submitted")).toBeInTheDocument();
  });

  it('renders a disabled "Start BOM" button for BomCreator when status is BomPending', async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sample });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    const btn = screen.getByRole("button", { name: /start bom/i });
    expect(btn).toBeDisabled();
  });

  it("does not render action buttons for SalesPerson", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "SalesPerson",
      userId: 10,
      name: "Ali",
      branchId: 1,
    });
    vi.mocked(api.get).mockResolvedValueOnce({ data: sample });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /start bom/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /start costing/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /review/i })).not.toBeInTheDocument();
  });

  it('shows a "not found" card on 404', async () => {
    vi.mocked(api.get).mockRejectedValueOnce({ response: { status: 404 } });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() =>
      expect(screen.getByText(/requisition not found/i)).toBeInTheDocument(),
    );
  });

  it("shows an access-denied card on 403", async () => {
    vi.mocked(api.get).mockRejectedValueOnce({ response: { status: 403 } });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() =>
      expect(screen.getByText(/don't have access/i)).toBeInTheDocument(),
    );
  });

  it("shows populated BOM and Approval cards when present", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({
      data: {
        ...sample,
        status: "Approved",
        bom: { id: 9, totalCostPerKg: 5.25, hasCost: true },
        approval: { salesPriceAed: 7.5, salesPriceForeign: null, profitMarginPct: 30, isApproved: true },
      },
    });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    expect(screen.getByText(/5\.25/)).toBeInTheDocument();
    expect(screen.getByText(/7\.5/)).toBeInTheDocument();
    expect(screen.getByText(/30/)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd bom-web
npm test -- --run src/features/requisitions/RequisitionDetailPage.test.tsx
```

Expected: FAIL — module not found.

- [ ] **Step 3: Write implementation**

Create `bom-web/src/features/requisitions/RequisitionDetailPage.tsx`:

```tsx
import { Link, useParams } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { StatusBadge } from "@/components/ui/StatusBadge";
import { RequisitionTimeline } from "./components/RequisitionTimeline";
import { useRequisition } from "./requisitionsApi";
import { useAuthStore } from "@/store/authStore";
import { formatRelative } from "@/utils/date";
import type { RequisitionDetail, RequisitionStatus, UserRole } from "@/types/api";

function LabeledValue({ label, value }: { label: string; value: string | number | null | undefined }) {
  return (
    <div className="flex justify-between gap-4 py-1 text-sm">
      <span className="text-muted-foreground">{label}</span>
      <span className="text-right">{value ?? "—"}</span>
    </div>
  );
}

function actionButtonFor(
  role: UserRole | undefined,
  status: RequisitionStatus,
): { label: string } | null {
  if (role === "BomCreator" && (status === "BomPending" || status === "BomInProgress")) {
    return { label: "Start BOM" };
  }
  if (role === "Accountant" && (status === "CostingPending" || status === "CostingInProgress")) {
    return { label: "Start Costing" };
  }
  if (role === "ManagingDirector" && status === "MdReview") {
    return { label: "Review & Approve" };
  }
  return null;
}

export default function RequisitionDetailPage() {
  const { id } = useParams<{ id: string }>();
  const numericId = Number(id);
  const { data, isLoading, error } = useRequisition(numericId);
  const role = useAuthStore((s) => s.user?.role);

  const status = (error as { response?: { status?: number } } | null)?.response?.status;

  if (status === 404) {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center">
          <p className="text-sm">Requisition not found.</p>
          <Link to="/requisitions" className="mt-4 inline-block text-sm underline">
            Back to Requisitions
          </Link>
        </CardContent>
      </Card>
    );
  }

  if (status === 403) {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center">
          <p className="text-sm">You don't have access to this requisition.</p>
          <Link to="/requisitions" className="mt-4 inline-block text-sm underline">
            Back to Requisitions
          </Link>
        </CardContent>
      </Card>
    );
  }

  if (error) {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center text-destructive">
          Failed to load requisition.
        </CardContent>
      </Card>
    );
  }

  if (isLoading || !data) {
    return <p className="text-sm text-muted-foreground">Loading…</p>;
  }

  const r: RequisitionDetail = data;
  const action = actionButtonFor(role, r.status);

  return (
    <div className="space-y-6">
      <Link to="/requisitions" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="h-4 w-4" /> Back to Requisitions
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <div className="flex items-center gap-3">
            <h1 className="font-mono text-2xl font-semibold">{r.refNo}</h1>
            <StatusBadge status={r.status} />
            <span className="text-xs text-muted-foreground">{formatRelative(r.createdAt)}</span>
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            {r.itemDescription} — {r.customerName}
          </p>
        </div>
        {action && (
          <Button disabled title="Coming soon">
            {action.label}
          </Button>
        )}
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1fr,1fr]">
        <Card>
          <CardHeader>
            <CardTitle>Progress</CardTitle>
          </CardHeader>
          <CardContent>
            <RequisitionTimeline status={r.status} createdAt={r.createdAt} updatedAt={r.updatedAt} />
          </CardContent>
        </Card>

        <div className="space-y-4">
          <Card>
            <CardHeader><CardTitle>Customer</CardTitle></CardHeader>
            <CardContent>
              <LabeledValue label="Name" value={r.customerName} />
              <LabeledValue label="Email" value={r.customerEmail} />
              <LabeledValue label="Phone" value={r.customerPhone} />
              <LabeledValue label="Address" value={r.customerAddress} />
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle>Quotation</CardTitle></CardHeader>
            <CardContent>
              <LabeledValue label="Expected Qty" value={`${r.expectedQty} ${r.currencyCode}`} />
              {r.exchangeRateSnapshot !== null && (
                <LabeledValue label="Exchange rate" value={r.exchangeRateSnapshot} />
              )}
              <LabeledValue label="Branch" value={r.branchName} />
              <LabeledValue label="Sales person" value={r.salesPersonName} />
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle>BOM</CardTitle></CardHeader>
            <CardContent>
              {r.bom ? (
                <>
                  <LabeledValue label="Total cost / kg" value={r.bom.totalCostPerKg} />
                  <LabeledValue label="Has cost" value={r.bom.hasCost ? "Yes" : "No"} />
                </>
              ) : (
                <p className="text-sm text-muted-foreground">BOM not yet created.</p>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle>Approval</CardTitle></CardHeader>
            <CardContent>
              {r.approval ? (
                <>
                  <LabeledValue label="Sales price (AED)" value={r.approval.salesPriceAed} />
                  {r.approval.salesPriceForeign !== null && (
                    <LabeledValue label="Sales price (foreign)" value={r.approval.salesPriceForeign} />
                  )}
                  <LabeledValue label="Profit margin" value={`${r.approval.profitMarginPct}%`} />
                  <LabeledValue label="Approved" value={r.approval.isApproved ? "Yes" : "No"} />
                </>
              ) : (
                <p className="text-sm text-muted-foreground">Not yet submitted for approval.</p>
              )}
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
cd bom-web
npm test -- --run src/features/requisitions/RequisitionDetailPage.test.tsx
```

Expected: 6/6 passing.

- [ ] **Step 5: Commit**

```bash
cd "D:/shan projects/BOM_Price_Approval"
git add bom-web/src/features/requisitions/RequisitionDetailPage.tsx bom-web/src/features/requisitions/RequisitionDetailPage.test.tsx
git commit -m "feat(web): add RequisitionDetailPage"
```

---

## Task 12: Wire the three routes into `App.tsx`

**Files:**
- Modify: `bom-web/src/App.tsx`

- [ ] **Step 1: Replace the router definition**

Current file (for reference):

```tsx
const router = createBrowserRouter([
  { path: "/login", element: <LoginPage /> },
  {
    path: "/",
    element: (
      <ProtectedRoute>
        <AppShell />
      </ProtectedRoute>
    ),
    children: [
      { index: true, element: <Navigate to="/dashboard" replace /> },
      { path: "dashboard", element: <DashboardRouter /> },
    ],
  },
  { path: "*", element: <Navigate to="/dashboard" replace /> },
]);
```

Replace with:

```tsx
import { createBrowserRouter, Navigate, RouterProvider } from "react-router-dom";
import LoginPage from "@/features/auth/LoginPage";
import { AppShell } from "@/components/layout/AppShell";
import { ProtectedRoute } from "@/components/layout/ProtectedRoute";
import DashboardRouter from "@/features/dashboard/DashboardRouter";
import RequisitionListPage from "@/features/requisitions/RequisitionListPage";
import NewRequisitionPage from "@/features/requisitions/NewRequisitionPage";
import RequisitionDetailPage from "@/features/requisitions/RequisitionDetailPage";

const router = createBrowserRouter([
  { path: "/login", element: <LoginPage /> },
  {
    path: "/",
    element: (
      <ProtectedRoute>
        <AppShell />
      </ProtectedRoute>
    ),
    children: [
      { index: true, element: <Navigate to="/dashboard" replace /> },
      { path: "dashboard", element: <DashboardRouter /> },
      {
        path: "requisitions",
        element: (
          <ProtectedRoute
            allow={["SalesPerson", "BomCreator", "Accountant", "ManagingDirector"]}
          >
            <RequisitionListPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "requisitions/new",
        element: (
          <ProtectedRoute allow={["SalesPerson"]}>
            <NewRequisitionPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "requisitions/:id",
        element: (
          <ProtectedRoute
            allow={["SalesPerson", "BomCreator", "Accountant", "ManagingDirector"]}
          >
            <RequisitionDetailPage />
          </ProtectedRoute>
        ),
      },
    ],
  },
  { path: "*", element: <Navigate to="/dashboard" replace /> },
]);

export default function App() {
  return <RouterProvider router={router} />;
}
```

- [ ] **Step 2: Run full test suite**

```bash
cd bom-web
npm test -- --run
```

Expected: all tests pass, nothing regresses.

- [ ] **Step 3: Run production build**

```bash
cd bom-web
npm run build
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
cd "D:/shan projects/BOM_Price_Approval"
git add bom-web/src/App.tsx
git commit -m "feat(web): wire requisitions routes in App"
```

---

## Task 13: Upgrade the four role dashboards

**Files:**
- Modify: `bom-web/src/features/dashboard/SalesDashboard.tsx`
- Modify: `bom-web/src/features/dashboard/BomDashboard.tsx`
- Modify: `bom-web/src/features/dashboard/AccountantDashboard.tsx`
- Modify: `bom-web/src/features/dashboard/MdDashboard.tsx`

Each dashboard uses the same pattern: fetch via `useRequisitions`, filter client-side to the relevant status, reuse `DataTable`. No new tests — integration is exercised by manual smoke. If the engineer feels the shared fetch-and-filter logic needs a helper, inline it rather than extracting (YAGNI — only 4 call sites).

- [ ] **Step 1: Update `SalesDashboard.tsx`**

```tsx
import { Link, useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { DataTable } from "@/components/ui/DataTable";
import { StatusBadge } from "@/components/ui/StatusBadge";
import { useRequisitions } from "@/features/requisitions/requisitionsApi";
import { formatRelative } from "@/utils/date";
import type { RequisitionListItem } from "@/types/api";

const columns: ColumnDef<RequisitionListItem>[] = [
  { accessorKey: "refNo", header: "Ref No" },
  {
    accessorKey: "status",
    header: "Status",
    cell: (info) => <StatusBadge status={info.getValue() as RequisitionListItem["status"]} />,
  },
  { accessorKey: "itemDescription", header: "Item" },
  { accessorKey: "customerName", header: "Customer" },
  {
    accessorKey: "createdAt",
    header: "Created",
    cell: (info) => formatRelative(info.getValue() as string),
  },
];

export default function SalesDashboard() {
  const navigate = useNavigate();
  const { data, isLoading } = useRequisitions();
  const rows = (data ?? []).slice(0, 10);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">My Recent Requisitions</h1>
        <Link to="/requisitions/new">
          <Button>New Requisition</Button>
        </Link>
      </div>
      <Card>
        <CardHeader>
          <CardTitle>Latest</CardTitle>
        </CardHeader>
        <CardContent>
          <DataTable
            columns={columns}
            data={rows}
            isLoading={isLoading}
            emptyState={<p>No requisitions yet.</p>}
            onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
          />
        </CardContent>
      </Card>
    </div>
  );
}
```

- [ ] **Step 2: Update `BomDashboard.tsx`**

```tsx
import { useMemo } from "react";
import { useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { DataTable } from "@/components/ui/DataTable";
import { StatusBadge } from "@/components/ui/StatusBadge";
import { useRequisitions } from "@/features/requisitions/requisitionsApi";
import { formatRelative } from "@/utils/date";
import type { RequisitionListItem } from "@/types/api";

const columns: ColumnDef<RequisitionListItem>[] = [
  { accessorKey: "refNo", header: "Ref No" },
  {
    accessorKey: "status",
    header: "Status",
    cell: (info) => <StatusBadge status={info.getValue() as RequisitionListItem["status"]} />,
  },
  { accessorKey: "itemDescription", header: "Item" },
  { accessorKey: "customerName", header: "Customer" },
  {
    accessorKey: "createdAt",
    header: "Created",
    cell: (info) => formatRelative(info.getValue() as string),
  },
];

export default function BomDashboard() {
  const navigate = useNavigate();
  const { data, isLoading } = useRequisitions();
  const rows = useMemo(
    () => (data ?? []).filter((r) => r.status === "BomPending" || r.status === "BomInProgress"),
    [data],
  );

  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">BOM Queue</h1>
      <Card>
        <CardHeader><CardTitle>Awaiting BOM</CardTitle></CardHeader>
        <CardContent>
          <DataTable
            columns={columns}
            data={rows}
            isLoading={isLoading}
            emptyState={<p>No requisitions waiting for BOM.</p>}
            onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
          />
        </CardContent>
      </Card>
    </div>
  );
}
```

- [ ] **Step 3: Update `AccountantDashboard.tsx`**

```tsx
import { useMemo } from "react";
import { useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { DataTable } from "@/components/ui/DataTable";
import { StatusBadge } from "@/components/ui/StatusBadge";
import { useRequisitions } from "@/features/requisitions/requisitionsApi";
import { formatRelative } from "@/utils/date";
import type { RequisitionListItem } from "@/types/api";

const columns: ColumnDef<RequisitionListItem>[] = [
  { accessorKey: "refNo", header: "Ref No" },
  {
    accessorKey: "status",
    header: "Status",
    cell: (info) => <StatusBadge status={info.getValue() as RequisitionListItem["status"]} />,
  },
  { accessorKey: "itemDescription", header: "Item" },
  { accessorKey: "customerName", header: "Customer" },
  {
    accessorKey: "createdAt",
    header: "Created",
    cell: (info) => formatRelative(info.getValue() as string),
  },
];

export default function AccountantDashboard() {
  const navigate = useNavigate();
  const { data, isLoading } = useRequisitions();
  const rows = useMemo(
    () =>
      (data ?? []).filter(
        (r) => r.status === "CostingPending" || r.status === "CostingInProgress",
      ),
    [data],
  );

  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Costing Queue</h1>
      <Card>
        <CardHeader><CardTitle>Awaiting Costing</CardTitle></CardHeader>
        <CardContent>
          <DataTable
            columns={columns}
            data={rows}
            isLoading={isLoading}
            emptyState={<p>No requisitions waiting for costing.</p>}
            onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
          />
        </CardContent>
      </Card>
    </div>
  );
}
```

- [ ] **Step 4: Update `MdDashboard.tsx`**

```tsx
import { useMemo } from "react";
import { useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { DataTable } from "@/components/ui/DataTable";
import { StatusBadge } from "@/components/ui/StatusBadge";
import { useRequisitions } from "@/features/requisitions/requisitionsApi";
import { formatRelative } from "@/utils/date";
import type { RequisitionListItem } from "@/types/api";

const columns: ColumnDef<RequisitionListItem>[] = [
  { accessorKey: "refNo", header: "Ref No" },
  {
    accessorKey: "status",
    header: "Status",
    cell: (info) => <StatusBadge status={info.getValue() as RequisitionListItem["status"]} />,
  },
  { accessorKey: "itemDescription", header: "Item" },
  { accessorKey: "customerName", header: "Customer" },
  {
    accessorKey: "createdAt",
    header: "Created",
    cell: (info) => formatRelative(info.getValue() as string),
  },
];

export default function MdDashboard() {
  const navigate = useNavigate();
  const { data, isLoading } = useRequisitions();
  const rows = useMemo(
    () => (data ?? []).filter((r) => r.status === "MdReview"),
    [data],
  );

  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Awaiting Approval</h1>
      <Card>
        <CardHeader><CardTitle>Pending MD Review</CardTitle></CardHeader>
        <CardContent>
          <DataTable
            columns={columns}
            data={rows}
            isLoading={isLoading}
            emptyState={<p>No requisitions awaiting approval.</p>}
            onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
          />
        </CardContent>
      </Card>
    </div>
  );
}
```

- [ ] **Step 5: Run the full test suite**

```bash
cd bom-web
npm test -- --run
```

Expected: all tests pass. Dashboards have no dedicated tests, but the shared components they use are already covered.

- [ ] **Step 6: Run production build**

```bash
cd bom-web
npm run build
```

Expected: build succeeds with no new warnings beyond the pre-existing chunk-size advisory from Plan 1.

- [ ] **Step 7: Commit**

```bash
cd "D:/shan projects/BOM_Price_Approval"
git add bom-web/src/features/dashboard/SalesDashboard.tsx bom-web/src/features/dashboard/BomDashboard.tsx bom-web/src/features/dashboard/AccountantDashboard.tsx bom-web/src/features/dashboard/MdDashboard.tsx
git commit -m "feat(web): upgrade role dashboards to show filtered requisitions"
```

---

## Task 14: Manual smoke verification

**Files:** none. This is a user-driven verification step.

- [ ] **Step 1: Start the API**

Terminal 1:

```bash
dotnet run --project BomPriceApproval.API
```

Wait until the console shows the API listening on `http://localhost:7300`.

- [ ] **Step 2: Start the web app**

Terminal 2:

```bash
cd bom-web
npm run dev
```

Wait for Vite to log `Local: http://localhost:5300/`.

- [ ] **Step 3: Verify Admin path**

1. Open `http://localhost:5300` → redirected to `/login`
2. Log in as `admin@test.com` / `Admin@1234` → redirected to `/dashboard`
3. Sidebar: verify "Requisitions" nav item present
4. Click it → land on `/requisitions` → see list (may be empty if seed data is absent)

- [ ] **Step 4: Verify SalesPerson create flow**

1. Log out, log in as a seeded SalesPerson. If no SalesPerson is seeded, create one via Swagger at `http://localhost:7300/swagger` using the admin bearer token, or run a known seed command for this project.
2. Verify SalesDashboard shows "My Recent Requisitions" + "New Requisition" button
3. Click "New Requisition"
4. Verify form loads with customers, items, currencies populated
5. Select a customer, an item, enter qty `100`, leave currency on AED
6. Click "Create" → expect redirect to `/requisitions/<id>`
7. On the detail page, verify:
   - Ref No (e.g. `REQ-0001`) is rendered in mono
   - `StatusBadge` shows `BOM Pending` in amber
   - Timeline: step 1 (`Submitted`) completed with relative time, step 2 (`BOM`) in-progress, later steps pending
   - BOM card: "BOM not yet created"
   - Approval card: "Not yet submitted for approval"

- [ ] **Step 5: Verify BomCreator view**

1. Log out, log in as a seeded BomCreator in the same branch
2. Sidebar → Requisitions → verify the requisition created in Step 4 is visible
3. Open its detail page
4. Verify a disabled "Start BOM" button is shown with a "Coming soon" tooltip (hover to confirm)

- [ ] **Step 6: Verify guards**

1. As BomCreator, manually navigate to `http://localhost:5300/requisitions/new`
2. Expect redirect to `/dashboard` (the `ProtectedRoute allow={["SalesPerson"]}` gate)

- [ ] **Step 7: Verify filters and theme**

1. Back as SalesPerson or BomCreator
2. On `/requisitions`, select "Approved" in the status filter → verify the unapproved rows disappear
3. Click "Clear" → verify rows return
4. Toggle the theme from the topbar → verify new pages render correctly in both themes
5. Reload the page → verify theme and sidebar collapse state persist

- [ ] **Step 8: Commit the finalised plan marker**

Plan 2 is complete when all the above pass. No code commit — just confirm with the user and mark the plan done.

---

## Definition of Done

- All Task 1–13 commits landed on `master`
- `cd bom-web && npm test -- --run` reports roughly 44 passing tests (12 from Plan 1 + ~32 new)
- `cd bom-web && npm run build` succeeds with no new warnings
- Manual smoke steps in Task 14 all pass
- No backend changes were made

---

## Spec coverage checklist

- [x] Route `/requisitions` (list) — Task 9
- [x] Route `/requisitions/new` (create) — Task 10
- [x] Route `/requisitions/:id` (detail) — Task 11
- [x] `StatusBadge` — Task 3
- [x] `DataTable` — Task 4
- [x] `SearchableSelect` — Task 5
- [x] `RequisitionTimeline` — Task 6
- [x] `useCustomers`, `useItems`, `useActiveExchangeRates` — Task 7
- [x] `useRequisitions`, `useRequisition`, `useCreateRequisition` — Task 8
- [x] Client-side filters (status, date range) — Task 9
- [x] Dashboard upgrades for 4 roles — Task 13
- [x] Role-gated disabled action buttons — Task 11
- [x] `formatRelative` helper — Task 2
- [x] TanStack Table dependency — Task 1
- [x] Extended API types — Task 1
- [x] Route wiring — Task 12
- [x] Manual smoke — Task 14
- [x] Branch column hidden for branch-scoped users — Task 9
- [x] 404 and 403 error handling on detail page — Task 11
- [x] Empty-state variants (filtered vs unfiltered, SalesPerson vs other roles) — Task 9
