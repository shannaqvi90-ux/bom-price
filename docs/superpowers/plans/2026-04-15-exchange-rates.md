# Exchange Rates Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a frontend page for viewing and managing currency exchange rates, accessible to all roles (read-only) with add/edit actions for Accountants only.

**Architecture:** New `features/exchange-rates/` folder following the Items pattern — TanStack Query hooks in `exchangeRatesApi.ts`, a list page with `DataTable`, and two modals for create/edit. Sidebar entry already exists but points to the wrong path; the route is new.

**Tech Stack:** React 19, TypeScript 5, TanStack Query v5, React Hook Form + Zod, `@tanstack/react-table` via `DataTable`, Vitest + RTL for tests.

---

### Task 1: Add request types to types/api.ts

**Files:**
- Modify: `bom-web/src/types/api.ts`

Note: The `ExchangeRate` response interface already exists at line 168. This task adds only the two request types.

- [ ] **Step 1: Add request types**

Open `bom-web/src/types/api.ts` and add the following after the `ExchangeRate` interface (after line 176, before the `// ─── BOM Entry` comment):

```typescript
export interface CreateExchangeRateRequest {
  currencyCode: string;
  currencyName: string;
  rateToAed: number;
  effectiveDate: string;
}

export interface UpdateExchangeRateRequest {
  rateToAed: number;
  effectiveDate: string;
  isActive: boolean;
}
```

- [ ] **Step 2: Verify TypeScript compiles**

Run from `bom-web/`:
```bash
npx tsc --noEmit
```
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/types/api.ts
git commit -m "feat(web): add CreateExchangeRateRequest and UpdateExchangeRateRequest types"
```

---

### Task 2: Create exchangeRatesApi.ts

**Files:**
- Create: `bom-web/src/features/exchange-rates/exchangeRatesApi.ts`

- [ ] **Step 1: Create the file**

```typescript
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type {
  ExchangeRate,
  CreateExchangeRateRequest,
  UpdateExchangeRateRequest,
} from "@/types/api";

export const exchangeRateKeys = {
  all: ["exchange-rates"] as const,
  list: () => [...exchangeRateKeys.all, "list"] as const,
};

export function useExchangeRates() {
  return useQuery({
    queryKey: exchangeRateKeys.list(),
    queryFn: () =>
      api.get<ExchangeRate[]>("/exchange-rates").then((r) => r.data),
  });
}

export function useCreateRate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateExchangeRateRequest) =>
      api.post<ExchangeRate>("/exchange-rates", body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: exchangeRateKeys.all }),
  });
}

export function useUpdateRate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: number; data: UpdateExchangeRateRequest }) =>
      api.put(`/exchange-rates/${id}`, data),
    onSuccess: () => qc.invalidateQueries({ queryKey: exchangeRateKeys.all }),
  });
}
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
npx tsc --noEmit
```
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/features/exchange-rates/exchangeRatesApi.ts
git commit -m "feat(web): add exchange rates API hooks"
```

---

### Task 3: Create AddRateModal.tsx

**Files:**
- Create: `bom-web/src/features/exchange-rates/AddRateModal.tsx`

- [ ] **Step 1: Create the file**

```typescript
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { useCreateRate } from "./exchangeRatesApi";

const schema = z.object({
  currencyCode: z
    .string()
    .min(1, "Currency code is required")
    .transform((v) => v.toUpperCase()),
  currencyName: z.string().min(1, "Currency name is required"),
  rateToAed: z.coerce.number().positive("Rate must be greater than 0"),
  effectiveDate: z.string().min(1, "Effective date is required"),
});

type FormValues = z.infer<typeof schema>;

interface Props {
  open: boolean;
  onClose: () => void;
}

export function AddRateModal({ open, onClose }: Props) {
  const create = useCreateRate();
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      currencyCode: "",
      currencyName: "",
      rateToAed: 0,
      effectiveDate: new Date().toISOString().split("T")[0],
    },
  });

  const onSubmit = handleSubmit(async (values) => {
    await create.mutateAsync(values);
    reset();
    onClose();
  });

  function handleClose() {
    reset();
    onClose();
  }

  return (
    <Dialog open={open} onClose={handleClose} title="Add Exchange Rate">
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div className="space-y-1">
          <Label htmlFor="rate-code">Currency Code</Label>
          <Input id="rate-code" placeholder="USD" {...register("currencyCode")} />
          {errors.currencyCode && (
            <p className="text-xs text-destructive">{errors.currencyCode.message}</p>
          )}
        </div>

        <div className="space-y-1">
          <Label htmlFor="rate-name">Currency Name</Label>
          <Input id="rate-name" placeholder="US Dollar" {...register("currencyName")} />
          {errors.currencyName && (
            <p className="text-xs text-destructive">{errors.currencyName.message}</p>
          )}
        </div>

        <div className="space-y-1">
          <Label htmlFor="rate-value">Rate to AED</Label>
          <Input
            id="rate-value"
            type="number"
            step="0.0001"
            min="0"
            {...register("rateToAed")}
          />
          {errors.rateToAed && (
            <p className="text-xs text-destructive">{errors.rateToAed.message}</p>
          )}
        </div>

        <div className="space-y-1">
          <Label htmlFor="rate-date">Effective Date</Label>
          <Input id="rate-date" type="date" {...register("effectiveDate")} />
          {errors.effectiveDate && (
            <p className="text-xs text-destructive">{errors.effectiveDate.message}</p>
          )}
        </div>

        {create.isError && (
          <p className="text-sm text-destructive">
            {(create.error as { response?: { data?: { message?: string } } })?.response
              ?.data?.message ?? "Failed to save"}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="ghost" onClick={handleClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={isSubmitting || create.isPending}>
            {create.isPending ? "Saving…" : "Save"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
npx tsc --noEmit
```
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/features/exchange-rates/AddRateModal.tsx
git commit -m "feat(web): add AddRateModal for exchange rates"
```

---

### Task 4: Create EditRateModal.tsx

**Files:**
- Create: `bom-web/src/features/exchange-rates/EditRateModal.tsx`

- [ ] **Step 1: Create the file**

```typescript
import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { useUpdateRate } from "./exchangeRatesApi";
import type { ExchangeRate } from "@/types/api";

const schema = z.object({
  rateToAed: z.coerce.number().positive("Rate must be greater than 0"),
  effectiveDate: z.string().min(1, "Effective date is required"),
  isActive: z.boolean(),
});

type FormValues = z.infer<typeof schema>;

interface Props {
  open: boolean;
  rate: ExchangeRate | null;
  onClose: () => void;
}

export function EditRateModal({ open, rate, onClose }: Props) {
  const update = useUpdateRate();
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
  });

  useEffect(() => {
    if (rate) {
      reset({
        rateToAed: rate.rateToAed,
        effectiveDate: rate.effectiveDate.split("T")[0],
        isActive: rate.isActive,
      });
    }
  }, [rate, reset]);

  const onSubmit = handleSubmit(async (values) => {
    if (!rate) return;
    await update.mutateAsync({ id: rate.id, data: values });
    onClose();
  });

  return (
    <Dialog open={open} onClose={onClose} title="Edit Exchange Rate">
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div className="space-y-1">
          <Label>Currency</Label>
          <p className="font-mono text-sm">
            {rate?.currencyCode} — {rate?.currencyName}
          </p>
        </div>

        <div className="space-y-1">
          <Label htmlFor="edit-rate-value">Rate to AED</Label>
          <Input
            id="edit-rate-value"
            type="number"
            step="0.0001"
            min="0"
            {...register("rateToAed")}
          />
          {errors.rateToAed && (
            <p className="text-xs text-destructive">{errors.rateToAed.message}</p>
          )}
        </div>

        <div className="space-y-1">
          <Label htmlFor="edit-rate-date">Effective Date</Label>
          <Input id="edit-rate-date" type="date" {...register("effectiveDate")} />
          {errors.effectiveDate && (
            <p className="text-xs text-destructive">{errors.effectiveDate.message}</p>
          )}
        </div>

        <div className="flex items-center gap-2">
          <input
            id="edit-rate-active"
            type="checkbox"
            className="h-4 w-4 rounded border-input"
            {...register("isActive")}
          />
          <Label htmlFor="edit-rate-active">Active</Label>
        </div>

        {update.isError && (
          <p className="text-sm text-destructive">
            {(update.error as { response?: { data?: { message?: string } } })?.response
              ?.data?.message ?? "Failed to save"}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={isSubmitting || update.isPending}>
            {update.isPending ? "Saving…" : "Save"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
npx tsc --noEmit
```
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/features/exchange-rates/EditRateModal.tsx
git commit -m "feat(web): add EditRateModal for exchange rates"
```

---

### Task 5: Write ExchangeRatesPage tests (failing first)

**Files:**
- Create: `bom-web/src/features/exchange-rates/ExchangeRatesPage.test.tsx`

Write the tests BEFORE the page. They will fail because `ExchangeRatesPage` does not exist yet.

- [ ] **Step 1: Create the test file**

```typescript
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import type { ReactNode } from "react";
import { useAuthStore } from "@/store/authStore";
import ExchangeRatesPage from "./ExchangeRatesPage";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({
  api: { get: vi.fn(), post: vi.fn(), put: vi.fn() },
}));

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>,
  );
}

const sampleRates = [
  {
    id: 1,
    currencyCode: "USD",
    currencyName: "US Dollar",
    rateToAed: 3.6725,
    effectiveDate: "2026-04-01T00:00:00Z",
    isActive: true,
    setByName: "Alice",
  },
];

beforeEach(() => {
  vi.mocked(api.get).mockReset();
  vi.mocked(api.post as ReturnType<typeof vi.fn>).mockReset();
  vi.mocked(api.put as ReturnType<typeof vi.fn>).mockReset();
  useAuthStore.getState().setSession({
    accessToken: "at",
    refreshToken: "rt",
    role: "Accountant",
    userId: 1,
    name: "Alice",
    branchId: null,
  });
});

afterEach(() => {
  useAuthStore.getState().logout();
});

describe("ExchangeRatesPage", () => {
  it("renders rate rows from API response", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleRates });
    wrap(<ExchangeRatesPage />);
    await waitFor(() => expect(screen.getByText("USD")).toBeInTheDocument());
    expect(screen.getByText("US Dollar")).toBeInTheDocument();
    expect(screen.getByText("3.6725")).toBeInTheDocument();
    expect(screen.getByText("Alice")).toBeInTheDocument();
  });

  it("shows Add Rate button for Accountant", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleRates });
    wrap(<ExchangeRatesPage />);
    await waitFor(() => expect(screen.getByText("USD")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /Add Rate/i })).toBeInTheDocument();
  });

  it("hides Add Rate button for non-Accountant roles", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "BomCreator",
      userId: 2,
      name: "Bob",
      branchId: 1,
    });
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleRates });
    wrap(<ExchangeRatesPage />);
    await waitFor(() => expect(screen.getByText("USD")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /Add Rate/i })).not.toBeInTheDocument();
  });

  it("submitting Add Rate modal calls POST with correct payload", async () => {
    vi.mocked(api.get).mockResolvedValue({ data: [] });
    vi.mocked(api.post as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      data: {
        id: 2,
        currencyCode: "EUR",
        currencyName: "Euro",
        rateToAed: 3.98,
        effectiveDate: "2026-04-01T00:00:00Z",
        isActive: true,
        setByName: "Alice",
      },
    });
    const user = userEvent.setup();
    wrap(<ExchangeRatesPage />);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Add Rate/i })).toBeInTheDocument(),
    );

    await user.click(screen.getByRole("button", { name: /Add Rate/i }));
    await user.type(screen.getByLabelText(/Currency Code/i), "EUR");
    await user.type(screen.getByLabelText(/Currency Name/i), "Euro");
    await user.clear(screen.getByLabelText(/Rate to AED/i));
    await user.type(screen.getByLabelText(/Rate to AED/i), "3.98");
    fireEvent.change(screen.getByLabelText(/Effective Date/i), {
      target: { value: "2026-04-01" },
    });
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(vi.mocked(api.post as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith(
        "/exchange-rates",
        expect.objectContaining({
          currencyCode: "EUR",
          currencyName: "Euro",
          rateToAed: 3.98,
        }),
      ),
    );
  });

  it("submitting Edit Rate modal calls PUT with correct payload", async () => {
    vi.mocked(api.get).mockResolvedValue({ data: sampleRates });
    vi.mocked(api.put as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ status: 204 });
    const user = userEvent.setup();
    wrap(<ExchangeRatesPage />);
    await waitFor(() => expect(screen.getByText("USD")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /Edit USD/i }));
    const rateInput = screen.getByLabelText(/Rate to AED/i);
    await user.clear(rateInput);
    await user.type(rateInput, "3.75");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(vi.mocked(api.put as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith(
        "/exchange-rates/1",
        expect.objectContaining({ rateToAed: 3.75 }),
      ),
    );
  });

  it("hides edit actions for non-Accountant roles", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "ManagingDirector",
      userId: 3,
      name: "MD",
      branchId: null,
    });
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleRates });
    wrap(<ExchangeRatesPage />);
    await waitFor(() => expect(screen.getByText("USD")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /Edit USD/i })).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests — expect FAIL**

```bash
cd bom-web && npx vitest run src/features/exchange-rates/ExchangeRatesPage.test.tsx
```
Expected: FAIL — `Cannot find module './ExchangeRatesPage'`

- [ ] **Step 3: Commit the failing tests**

```bash
git add bom-web/src/features/exchange-rates/ExchangeRatesPage.test.tsx
git commit -m "test(web): add failing tests for ExchangeRatesPage"
```

---

### Task 6: Create ExchangeRatesPage.tsx

**Files:**
- Create: `bom-web/src/features/exchange-rates/ExchangeRatesPage.tsx`

- [ ] **Step 1: Create the page**

```typescript
import { useState, useMemo } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { Pencil } from "lucide-react";
import { DataTable } from "@/components/ui/DataTable";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { useAuthStore } from "@/store/authStore";
import { useExchangeRates } from "./exchangeRatesApi";
import { AddRateModal } from "./AddRateModal";
import { EditRateModal } from "./EditRateModal";
import type { ExchangeRate } from "@/types/api";

export default function ExchangeRatesPage() {
  const role = useAuthStore((s) => s.user?.role);
  const { data, isLoading, isError, refetch } = useExchangeRates();
  const [addOpen, setAddOpen] = useState(false);
  const [editRate, setEditRate] = useState<ExchangeRate | null>(null);

  const canManage = role === "Accountant";

  const columns = useMemo<ColumnDef<ExchangeRate>[]>(
    () => [
      {
        accessorKey: "currencyCode",
        header: "Code",
        cell: (i) => (
          <span className="font-mono font-semibold">{i.getValue() as string}</span>
        ),
      },
      { accessorKey: "currencyName", header: "Currency" },
      {
        accessorKey: "rateToAed",
        header: "Rate to AED",
        cell: (i) => (i.getValue() as number).toFixed(4),
      },
      {
        accessorKey: "effectiveDate",
        header: "Effective Date",
        cell: (i) => (i.getValue() as string).split("T")[0],
      },
      {
        accessorKey: "isActive",
        header: "Status",
        cell: (i) =>
          (i.getValue() as boolean) ? (
            <span className="inline-flex items-center rounded-full bg-green-50 px-2 py-0.5 text-xs font-medium text-green-700">
              Active
            </span>
          ) : (
            <span className="inline-flex items-center rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
              Inactive
            </span>
          ),
      },
      { accessorKey: "setByName", header: "Set By" },
      ...(canManage
        ? [
            {
              id: "actions",
              header: "",
              cell: ({ row }: { row: { original: ExchangeRate } }) => {
                const rate = row.original;
                return (
                  <div className="flex justify-end">
                    <Button
                      variant="ghost"
                      size="icon"
                      aria-label={`Edit ${rate.currencyCode}`}
                      onClick={() => setEditRate(rate)}
                    >
                      <Pencil className="h-4 w-4" />
                    </Button>
                  </div>
                );
              },
            } as ColumnDef<ExchangeRate>,
          ]
        : []),
    ],
    [canManage],
  );

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">Exchange Rates</h1>
        {canManage && (
          <Button onClick={() => setAddOpen(true)}>Add Rate</Button>
        )}
      </div>

      {isError && (
        <Card>
          <CardContent className="flex items-center justify-between">
            <p className="text-destructive">Failed to load exchange rates.</p>
            <Button variant="ghost" onClick={() => refetch()}>
              Retry
            </Button>
          </CardContent>
        </Card>
      )}

      <DataTable
        columns={columns}
        data={data ?? []}
        isLoading={isLoading}
        emptyState={<p>No exchange rates yet.</p>}
      />

      <AddRateModal open={addOpen} onClose={() => setAddOpen(false)} />
      <EditRateModal
        open={editRate !== null}
        rate={editRate}
        onClose={() => setEditRate(null)}
      />
    </div>
  );
}
```

- [ ] **Step 2: Run the feature tests — expect PASS**

```bash
cd bom-web && npx vitest run src/features/exchange-rates/ExchangeRatesPage.test.tsx
```
Expected: PASS — 6 tests passing.

- [ ] **Step 3: Run full test suite**

```bash
cd bom-web && npx vitest run
```
Expected: all tests pass (previously passing tests unaffected).

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/features/exchange-rates/ExchangeRatesPage.tsx
git commit -m "feat(web): add ExchangeRatesPage with DataTable and modals"
```

---

### Task 7: Wire up route and sidebar

**Files:**
- Modify: `bom-web/src/App.tsx`
- Modify: `bom-web/src/components/layout/Sidebar.tsx`

The sidebar currently has Exchange Rates at `/admin/exchange-rates` restricted to `["Admin", "Accountant"]`. Change it to `/exchange-rates` with no role restriction (all roles see it). Add the matching route in App.tsx.

- [ ] **Step 1: Add import and route in App.tsx**

Add the import at the top of `bom-web/src/App.tsx` (after the `NotificationsPage` import on line 14):

```typescript
import ExchangeRatesPage from "@/features/exchange-rates/ExchangeRatesPage";
```

Add the route inside the `children` array in `bom-web/src/App.tsx`, after the `items` route block (after line 55):

```typescript
{
  path: "exchange-rates",
  element: (
    <ProtectedRoute
      allow={["Admin", "SalesPerson", "BomCreator", "Accountant", "ManagingDirector"]}
    >
      <ExchangeRatesPage />
    </ProtectedRoute>
  ),
},
```

- [ ] **Step 2: Update sidebar entry in Sidebar.tsx**

In `bom-web/src/components/layout/Sidebar.tsx`, find the Exchange Rates entry (around line 56–61):

```typescript
{
  to: "/admin/exchange-rates",
  label: "Exchange Rates",
  icon: Coins,
  roles: ["Admin", "Accountant"],
},
```

Replace it with (no `roles` = visible to all logged-in users):

```typescript
{ to: "/exchange-rates", label: "Exchange Rates", icon: Coins },
```

- [ ] **Step 3: Run full test suite**

```bash
cd bom-web && npx vitest run
```
Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/App.tsx bom-web/src/components/layout/Sidebar.tsx
git commit -m "feat(web): wire up exchange rates route and sidebar link"
```
