# Mobile SalesPerson Implementation Plan (2 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the placeholder `(sales)/index.tsx` with three working screens — list, create, detail — so a SalesPerson can create multi-item requisitions from an Android/iOS phone and track each through to the approved PDF.

**Architecture:** TanStack Query v5 handles server state via hooks in `src/api/requisitions.ts` + `src/api/lookups.ts`. A reusable `SearchablePicker` component serves both customer and item selection. The create form uses React Hook Form + Zod for validation, with `useFieldArray` for the dynamic items list. PDF download uses `expo-file-system` + `expo-sharing` against the existing backend endpoint. Zero backend changes.

**Tech Stack:** React Native + Expo Router (existing), TanStack Query v5, Axios via `@/api/client`, React Hook Form + Zod, NativeWind. Tests continue using the two-project jest config from Plan 1 (`node` for logic, `rn` for components).

**Builds on:** Plan 1 (`2026-04-19-mobile-foundation.md`) merged to master at commit `a950823`. Depends on `@/api/client`, `@/auth/AuthContext`, `@/components/*`, `@/types/api`.

---

## Scope deviations from spec

The spec §4 listed two fields that don't exist in the backend DTOs at the time of planning:

1. **§4.2 "Delivery notes (optional, multiline)"** — `CreateRequisitionRequest` has no such field. Omitted from the form.
2. **§4.3 "Timeline — ordered events"** — `RequisitionDetail` has `createdAt`, `updatedAt`, and `approval.approvedAt` only; there is no per-event history array. The detail screen shows creation date + approval/rejection date (compressed "timeline"), not a full event log.

Both would be additive later without breaking changes if backend exposes them.

---

## Backend endpoints used (all exist, no changes)

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/requisitions` | List for current user (branch + ownership scoped) |
| GET | `/api/requisitions/{id}` | Detail with items + approval |
| POST | `/api/requisitions` | Create with items |
| GET | `/api/customers` | Branch-scoped customer picker |
| GET | `/api/items` | Branch-scoped item picker |
| GET | `/api/exchange-rates` | Active exchange rates for currency picker |
| GET | `/api/approvals/{requisitionId}/pdf` | PDF download (Approved only) |

Note: the spec §4.3 called the PDF endpoint `/api/requisitions/{id}/pdf`; the actual route is `/api/approvals/{requisitionId}/pdf` (verified in `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs:243`).

---

## DTO cheat sheet (already in `src/types/api.ts`)

- `RequisitionListItem` — `{ id, refNo, status, itemCount, customerName, currencyCode, branchName, salesPersonName, createdAt }`
- `RequisitionDetail` — adds `items: RequisitionItemDto[]` + `approval: ApprovalSummary | null`
- `RequisitionItemDto` — `{ id, itemId, itemDescription, expectedQty, sortOrder }`
- `CreateRequisitionRequest` — `{ customerId, items: RequisitionItemInput[], currencyCode }`
- `RequisitionItemInput` — `{ itemId, expectedQty }`
- `ApprovalSummary` — `{ isApproved, notes, approvedAt }`
- `Customer` — `{ id, code, name, address, email, phoneNumber, ... }`
- `Item` — `{ id, code, description, type, branchId, isActive, lastPurchasePrice }`
- `ExchangeRate` — `{ id, currencyCode, currencyName, rateToAed, effectiveDate, isActive, setByName }`
- `RequisitionStatus` — `"Draft" | "BomPending" | "BomInProgress" | "CostingPending" | "CostingInProgress" | "MdReview" | "Approved" | "Rejected"`

---

## File Structure (created by this plan)

```
bom-mobile/
  app/(sales)/
    index.tsx                     # replaces placeholder — requisitions list
    new.tsx                       # create form
    [id].tsx                      # requisition detail
  src/
    api/
      requisitions.ts             # list, detail, create hooks
      lookups.ts                  # customers, items, exchange-rates hooks
      pdf.ts                      # downloadRequisitionPdf helper
    components/
      SearchablePicker.tsx        # generic modal picker with search
      RequisitionCard.tsx         # row in the list
      ItemStageBadge.tsx          # small BOM/Costing/Price check indicator
    utils/
      validation.ts               # EXTENDED — add createRequisitionSchema
      dates.ts                    # NEW — formatShortDate
  __tests__/
    createRequisitionSchema.test.ts   # Zod schema unit tests (node project)
    dates.test.ts                     # formatShortDate tests (node project)
```

Existing files modified:
- `src/utils/validation.ts` — append `createRequisitionSchema`

---

## Task 1: Requisitions API module

**Files:**
- Create: `bom-mobile/src/api/requisitions.ts`

- [ ] **Step 1: Implementation**

```ts
import { useQuery, useMutation, useQueryClient, type UseQueryOptions } from "@tanstack/react-query";
import { api } from "./client";
import type {
  CreateRequisitionRequest,
  RequisitionDetail,
  RequisitionListItem,
} from "@/types/api";

const keys = {
  all: ["requisitions"] as const,
  list: () => [...keys.all, "list"] as const,
  detail: (id: number) => [...keys.all, "detail", id] as const,
};

async function fetchList(): Promise<RequisitionListItem[]> {
  const res = await api.get<RequisitionListItem[]>("/api/requisitions");
  return res.data;
}

async function fetchDetail(id: number): Promise<RequisitionDetail> {
  const res = await api.get<RequisitionDetail>(`/api/requisitions/${id}`);
  return res.data;
}

export function useRequisitionsList(options?: Partial<UseQueryOptions<RequisitionListItem[]>>) {
  return useQuery({
    queryKey: keys.list(),
    queryFn: fetchList,
    ...options,
  });
}

export function useRequisitionDetail(id: number, options?: Partial<UseQueryOptions<RequisitionDetail>>) {
  return useQuery({
    queryKey: keys.detail(id),
    queryFn: () => fetchDetail(id),
    enabled: Number.isFinite(id) && id > 0,
    ...options,
  });
}

export function useCreateRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: CreateRequisitionRequest): Promise<RequisitionDetail> => {
      const res = await api.post<RequisitionDetail>("/api/requisitions", input);
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: keys.list() });
    },
  });
}

export const requisitionKeys = keys;
```

- [ ] **Step 2: Verify compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/api/requisitions.ts
git commit -m "feat(mobile): add requisitions API hooks (list, detail, create)"
```

---

## Task 2: Lookups API module (customers, items, exchange rates)

**Files:**
- Create: `bom-mobile/src/api/lookups.ts`

- [ ] **Step 1: Implementation**

```ts
import { useQuery } from "@tanstack/react-query";
import { api } from "./client";
import type { Customer, Item, ExchangeRate } from "@/types/api";

export function useCustomers() {
  return useQuery({
    queryKey: ["customers"],
    queryFn: async () => {
      const res = await api.get<Customer[]>("/api/customers");
      return res.data;
    },
    staleTime: 60_000,
  });
}

export function useItems() {
  return useQuery({
    queryKey: ["items"],
    queryFn: async () => {
      const res = await api.get<Item[]>("/api/items");
      return res.data;
    },
    staleTime: 60_000,
    select: (items) => items.filter((i) => i.isActive),
  });
}

export function useExchangeRates() {
  return useQuery({
    queryKey: ["exchange-rates"],
    queryFn: async () => {
      const res = await api.get<ExchangeRate[]>("/api/exchange-rates");
      return res.data;
    },
    staleTime: 300_000,
    select: (rates) => rates.filter((r) => r.isActive),
  });
}
```

- [ ] **Step 2: Verify compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/api/lookups.ts
git commit -m "feat(mobile): add lookup hooks for customers, items, exchange rates"
```

---

## Task 3: Date formatting utility + test

**Files:**
- Create: `bom-mobile/src/utils/dates.ts`
- Create: `bom-mobile/__tests__/dates.test.ts`

- [ ] **Step 1: Write the failing test**

```ts
// bom-mobile/__tests__/dates.test.ts
import { formatShortDate } from "@/utils/dates";

test("formats an ISO string as dd MMM", () => {
  expect(formatShortDate("2026-04-17T10:30:00Z")).toBe("17 Apr");
});

test("returns '-' for null/empty/undefined", () => {
  expect(formatShortDate(null)).toBe("-");
  expect(formatShortDate(undefined)).toBe("-");
  expect(formatShortDate("")).toBe("-");
});

test("returns '-' for unparseable strings", () => {
  expect(formatShortDate("not-a-date")).toBe("-");
});
```

- [ ] **Step 2: Run — expect fail**

```bash
cd bom-mobile && npx jest __tests__/dates.test.ts
```

Expected: FAIL — `Cannot find module '@/utils/dates'`.

- [ ] **Step 3: Implement**

```ts
// bom-mobile/src/utils/dates.ts
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

export function formatShortDate(iso: string | null | undefined): string {
  if (!iso) return "-";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "-";
  return `${String(d.getUTCDate()).padStart(2, "0")} ${MONTHS[d.getUTCMonth()]}`;
}
```

- [ ] **Step 4: Run — expect pass**

```bash
cd bom-mobile && npx jest __tests__/dates.test.ts
```

Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add bom-mobile/src/utils/dates.ts bom-mobile/__tests__/dates.test.ts
git commit -m "feat(mobile): add formatShortDate utility with tests"
```

---

## Task 4: SearchablePicker component

**Files:**
- Create: `bom-mobile/src/components/SearchablePicker.tsx`

- [ ] **Step 1: Implementation**

Generic modal picker: label + search + list. Works for both customer and item pickers.

```tsx
import { useMemo, useState } from "react";
import { FlatList, Modal, Pressable, Text, TextInput, View } from "react-native";

interface Option {
  id: number;
  label: string;
  sublabel?: string;
}

interface Props {
  label: string;
  placeholder?: string;
  value: number | null;
  options: Option[];
  onChange: (id: number) => void;
  loading?: boolean;
  error?: string;
}

export function SearchablePicker({
  label,
  placeholder = "Select...",
  value,
  options,
  onChange,
  loading,
  error,
}: Props) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");

  const selected = useMemo(
    () => options.find((o) => o.id === value) ?? null,
    [options, value]
  );

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return options;
    return options.filter(
      (o) =>
        o.label.toLowerCase().includes(q) ||
        (o.sublabel ?? "").toLowerCase().includes(q)
    );
  }, [options, query]);

  return (
    <View className="mb-3">
      <Text className="text-sm text-slate-700 mb-1">{label}</Text>
      <Pressable
        onPress={() => setOpen(true)}
        className={`border rounded-md px-3 py-3 bg-white ${error ? "border-rose-500" : "border-slate-300"}`}
      >
        <Text className={selected ? "text-slate-900" : "text-slate-400"}>
          {loading ? "Loading..." : selected ? selected.label : placeholder}
        </Text>
      </Pressable>
      {error ? <Text className="text-xs text-rose-600 mt-1">{error}</Text> : null}

      <Modal visible={open} animationType="slide" onRequestClose={() => setOpen(false)}>
        <View className="flex-1 bg-slate-50">
          <View className="px-4 pt-12 pb-2 flex-row items-center">
            <Pressable onPress={() => setOpen(false)} className="py-2 pr-4">
              <Text className="text-brand-600 text-base">Cancel</Text>
            </Pressable>
            <Text className="text-lg font-semibold text-slate-900 flex-1">{label}</Text>
          </View>
          <View className="px-4 pb-2">
            <TextInput
              value={query}
              onChangeText={setQuery}
              placeholder="Search..."
              autoFocus
              placeholderTextColor="#94a3b8"
              className="border border-slate-300 rounded-md px-3 py-2 bg-white text-slate-900"
            />
          </View>
          <FlatList
            data={filtered}
            keyExtractor={(o) => String(o.id)}
            renderItem={({ item }) => (
              <Pressable
                onPress={() => {
                  onChange(item.id);
                  setQuery("");
                  setOpen(false);
                }}
                className="px-4 py-3 border-b border-slate-200 bg-white"
              >
                <Text className="text-base text-slate-900">{item.label}</Text>
                {item.sublabel ? (
                  <Text className="text-xs text-slate-500 mt-0.5">{item.sublabel}</Text>
                ) : null}
              </Pressable>
            )}
            ListEmptyComponent={
              <Text className="text-center text-slate-500 p-6">No matches</Text>
            }
          />
        </View>
      </Modal>
    </View>
  );
}

export type { Option as SearchablePickerOption };
```

- [ ] **Step 2: Verify compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/components/SearchablePicker.tsx
git commit -m "feat(mobile): add SearchablePicker modal component"
```

---

## Task 5: RequisitionCard component

**Files:**
- Create: `bom-mobile/src/components/RequisitionCard.tsx`

- [ ] **Step 1: Implementation**

```tsx
import { Pressable, Text, View } from "react-native";
import { StatusPill } from "./StatusPill";
import type { RequisitionListItem } from "@/types/api";
import { formatShortDate } from "@/utils/dates";

interface Props {
  item: RequisitionListItem;
  onPress: (id: number) => void;
}

export function RequisitionCard({ item, onPress }: Props) {
  return (
    <Pressable
      onPress={() => onPress(item.id)}
      className="bg-white border border-slate-200 rounded-md p-3 mb-2 active:bg-slate-50"
    >
      <View className="flex-row items-center justify-between mb-2">
        <Text className="text-base font-semibold text-slate-900">{item.refNo}</Text>
        <StatusPill status={item.status} />
      </View>
      <Text className="text-sm text-slate-700 mb-1" numberOfLines={1}>
        {item.customerName}
      </Text>
      <View className="flex-row justify-between">
        <Text className="text-xs text-slate-500">
          {item.itemCount} {item.itemCount === 1 ? "item" : "items"} · {item.currencyCode}
        </Text>
        <Text className="text-xs text-slate-500">{formatShortDate(item.createdAt)}</Text>
      </View>
    </Pressable>
  );
}
```

- [ ] **Step 2: Verify compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/components/RequisitionCard.tsx
git commit -m "feat(mobile): add RequisitionCard list component"
```

---

## Task 6: Requisitions list screen (replace placeholder)

**Files:**
- Modify: `bom-mobile/app/(sales)/index.tsx`

- [ ] **Step 1: Replace the placeholder**

```tsx
import { FlatList, Pressable, RefreshControl, Text, View } from "react-native";
import { useRouter } from "expo-router";
import { useRequisitionsList } from "@/api/requisitions";
import { RequisitionCard } from "@/components/RequisitionCard";
import { EmptyState } from "@/components/EmptyState";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";

export default function SalesRequisitionsList() {
  const router = useRouter();
  const q = useRequisitionsList();

  if (q.isPending) return <LoadingView />;

  return (
    <View className="flex-1 bg-slate-50">
      {q.isError ? (
        <View className="p-4">
          <ErrorBanner
            message={q.error instanceof Error ? q.error.message : "Failed to load requisitions"}
            onRetry={() => q.refetch()}
          />
        </View>
      ) : null}

      <FlatList
        data={q.data ?? []}
        keyExtractor={(r) => String(r.id)}
        contentContainerClassName="p-3"
        refreshControl={
          <RefreshControl refreshing={q.isRefetching} onRefresh={() => q.refetch()} />
        }
        renderItem={({ item }) => (
          <RequisitionCard item={item} onPress={(id) => router.push(`/(sales)/${id}`)} />
        )}
        ListEmptyComponent={
          !q.isError ? (
            <EmptyState
              title="No requisitions yet"
              hint="Tap + to create your first requisition."
            />
          ) : null
        }
      />

      <Pressable
        onPress={() => router.push("/(sales)/new")}
        className="absolute bottom-6 right-6 bg-brand-600 active:bg-brand-700 rounded-full w-14 h-14 items-center justify-center shadow-lg"
      >
        <Text className="text-white text-3xl leading-none">+</Text>
      </Pressable>
    </View>
  );
}
```

- [ ] **Step 2: Verify compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/app/\(sales\)/index.tsx
git commit -m "feat(mobile): replace placeholder with real requisitions list screen"
```

---

## Task 7: Create-requisition Zod schema + test

**Files:**
- Modify: `bom-mobile/src/utils/validation.ts` (append)
- Create: `bom-mobile/__tests__/createRequisitionSchema.test.ts`

- [ ] **Step 1: Write the failing test**

```ts
// bom-mobile/__tests__/createRequisitionSchema.test.ts
import { createRequisitionSchema } from "@/utils/validation";

const base = {
  customerId: 1,
  currencyCode: "AED",
  items: [{ itemId: 10, expectedQty: 5 }],
};

test("accepts a valid payload", () => {
  expect(createRequisitionSchema.safeParse(base).success).toBe(true);
});

test("rejects when customerId is missing or zero", () => {
  expect(createRequisitionSchema.safeParse({ ...base, customerId: 0 }).success).toBe(false);
});

test("rejects when currency is empty", () => {
  expect(createRequisitionSchema.safeParse({ ...base, currencyCode: "" }).success).toBe(false);
});

test("rejects when items array is empty", () => {
  expect(createRequisitionSchema.safeParse({ ...base, items: [] }).success).toBe(false);
});

test("rejects when an item has itemId=0", () => {
  expect(
    createRequisitionSchema.safeParse({
      ...base,
      items: [{ itemId: 0, expectedQty: 5 }],
    }).success
  ).toBe(false);
});

test("rejects when an item has expectedQty <= 0", () => {
  expect(
    createRequisitionSchema.safeParse({
      ...base,
      items: [{ itemId: 10, expectedQty: 0 }],
    }).success
  ).toBe(false);
  expect(
    createRequisitionSchema.safeParse({
      ...base,
      items: [{ itemId: 10, expectedQty: -1 }],
    }).success
  ).toBe(false);
});
```

- [ ] **Step 2: Run — expect fail**

```bash
cd bom-mobile && npx jest __tests__/createRequisitionSchema.test.ts
```

Expected: FAIL — `createRequisitionSchema` is not exported.

- [ ] **Step 3: Append to `src/utils/validation.ts`**

Add below the existing `loginSchema` export:

```ts
export const createRequisitionSchema = z.object({
  customerId: z.number().int().positive("Customer is required"),
  currencyCode: z.string().min(1, "Currency is required"),
  items: z
    .array(
      z.object({
        itemId: z.number().int().positive("Item is required"),
        expectedQty: z.number().positive("Quantity must be greater than zero"),
      })
    )
    .min(1, "At least one item is required"),
});

export type CreateRequisitionInput = z.infer<typeof createRequisitionSchema>;
```

- [ ] **Step 4: Run — expect pass**

```bash
cd bom-mobile && npx jest __tests__/createRequisitionSchema.test.ts
```

Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add bom-mobile/src/utils/validation.ts bom-mobile/__tests__/createRequisitionSchema.test.ts
git commit -m "test(mobile): create-requisition schema validates customer, currency, and items"
```

---

## Task 8: Create requisition screen — layout + customer + currency

**Files:**
- Create: `bom-mobile/app/(sales)/new.tsx`

- [ ] **Step 1: Initial screen with customer + currency fields only (items editor added in Task 9)**

```tsx
import { useState } from "react";
import {
  KeyboardAvoidingView,
  Platform,
  ScrollView,
  Text,
  View,
} from "react-native";
import { useRouter } from "expo-router";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/Button";
import { SearchablePicker } from "@/components/SearchablePicker";
import { ErrorBanner } from "@/components/ErrorBanner";
import { useCustomers, useExchangeRates } from "@/api/lookups";
import { useCreateRequisition } from "@/api/requisitions";
import {
  createRequisitionSchema,
  type CreateRequisitionInput,
} from "@/utils/validation";

export default function NewRequisition() {
  const router = useRouter();
  const customersQ = useCustomers();
  const ratesQ = useExchangeRates();
  const createMut = useCreateRequisition();
  const [topError, setTopError] = useState<string | null>(null);

  const {
    control,
    handleSubmit,
    formState: { errors },
  } = useForm<CreateRequisitionInput>({
    resolver: zodResolver(createRequisitionSchema),
    defaultValues: {
      customerId: 0,
      currencyCode: "AED",
      items: [{ itemId: 0, expectedQty: 0 }],
    },
  });

  const currencyOptions = [
    { id: 0, code: "AED", label: "AED — UAE Dirham" },
    ...(ratesQ.data ?? []).map((r) => ({
      id: r.id,
      code: r.currencyCode,
      label: `${r.currencyCode} — ${r.currencyName}`,
    })),
  ];

  const onSubmit = handleSubmit(async (values) => {
    setTopError(null);
    try {
      const created = await createMut.mutateAsync(values);
      router.replace(`/(sales)/${created.id}`);
    } catch (e: unknown) {
      const msg =
        (e as { response?: { data?: { message?: string } } }).response?.data?.message ??
        (e instanceof Error ? e.message : "Failed to create requisition");
      setTopError(msg);
    }
  });

  return (
    <KeyboardAvoidingView
      behavior={Platform.OS === "ios" ? "padding" : undefined}
      className="flex-1 bg-slate-50"
    >
      <ScrollView contentContainerClassName="p-4">
        <Text className="text-xl font-bold text-slate-900 mb-4">New requisition</Text>

        {topError ? (
          <ErrorBanner message={topError} onRetry={() => setTopError(null)} />
        ) : null}

        <Controller
          control={control}
          name="customerId"
          render={({ field }) => (
            <SearchablePicker
              label="Customer"
              placeholder="Select customer..."
              value={field.value || null}
              onChange={field.onChange}
              loading={customersQ.isPending}
              options={
                (customersQ.data ?? []).map((c) => ({
                  id: c.id,
                  label: c.name,
                  sublabel: c.code,
                }))
              }
              error={errors.customerId?.message}
            />
          )}
        />

        <Controller
          control={control}
          name="currencyCode"
          render={({ field }) => (
            <View className="mb-3">
              <Text className="text-sm text-slate-700 mb-1">Currency</Text>
              <View className="flex-row flex-wrap -mr-2">
                {currencyOptions.map((opt) => {
                  const selected = field.value === opt.code;
                  return (
                    <Text
                      key={opt.code}
                      onPress={() => field.onChange(opt.code)}
                      className={`px-3 py-2 mr-2 mb-2 rounded-md border ${
                        selected
                          ? "bg-brand-600 border-brand-600 text-white"
                          : "bg-white border-slate-300 text-slate-700"
                      }`}
                    >
                      {opt.code}
                    </Text>
                  );
                })}
              </View>
              {errors.currencyCode ? (
                <Text className="text-xs text-rose-600 mt-1">
                  {errors.currencyCode.message}
                </Text>
              ) : null}
            </View>
          )}
        />

        {/* Items — populated in Task 9 */}
        <Text className="text-base font-semibold text-slate-900 mt-4 mb-2">Items</Text>
        <Text className="text-slate-500 text-sm">Items editor added in next task.</Text>

        <View className="mt-6">
          <Button
            title="Create requisition"
            onPress={onSubmit}
            loading={createMut.isPending}
          />
        </View>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}
```

- [ ] **Step 2: Verify compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

Expected: no errors. The file renders a working submit button for customer + currency only; items are added in Task 9.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/app/\(sales\)/new.tsx
git commit -m "feat(mobile): create-requisition screen skeleton with customer + currency"
```

---

## Task 9: Create requisition — dynamic items array

**Files:**
- Modify: `bom-mobile/app/(sales)/new.tsx`

- [ ] **Step 1: Add imports for items editor**

At the top of `app/(sales)/new.tsx`, modify three import lines:

```tsx
// BEFORE
import { useForm, Controller } from "react-hook-form";
// AFTER
import { useForm, Controller, useFieldArray } from "react-hook-form";

// BEFORE
import { Button } from "@/components/Button";
// AFTER
import { Button } from "@/components/Button";
import { Input } from "@/components/Input";

// BEFORE
import { useCustomers, useExchangeRates } from "@/api/lookups";
// AFTER
import { useCustomers, useExchangeRates, useItems } from "@/api/lookups";
```

Inside `NewRequisition`, after the existing `useForm(...)` call, add:

```tsx
  const itemsQ = useItems();
  const { fields, append, remove } = useFieldArray({ control, name: "items" });
```

- [ ] **Step 2: Replace the stub items section**

Inside the JSX, replace the two lines:

```tsx
        <Text className="text-base font-semibold text-slate-900 mt-4 mb-2">Items</Text>
        <Text className="text-slate-500 text-sm">Items editor added in next task.</Text>
```

with:

```tsx
        <Text className="text-base font-semibold text-slate-900 mt-4 mb-2">Items</Text>

        {fields.map((f, idx) => (
          <View key={f.id} className="bg-white border border-slate-200 rounded-md p-3 mb-3">
            <View className="flex-row items-center justify-between mb-2">
              <Text className="text-sm font-medium text-slate-700">Item {idx + 1}</Text>
              {fields.length > 1 ? (
                <Text
                  onPress={() => remove(idx)}
                  className="text-rose-600 text-sm font-semibold"
                >
                  Remove
                </Text>
              ) : null}
            </View>

            <Controller
              control={control}
              name={`items.${idx}.itemId` as const}
              render={({ field }) => (
                <SearchablePicker
                  label="Item"
                  placeholder="Select item..."
                  value={field.value || null}
                  onChange={field.onChange}
                  loading={itemsQ.isPending}
                  options={
                    (itemsQ.data ?? []).map((it) => ({
                      id: it.id,
                      label: it.description,
                      sublabel: it.code,
                    }))
                  }
                  error={errors.items?.[idx]?.itemId?.message}
                />
              )}
            />

            <Controller
              control={control}
              name={`items.${idx}.expectedQty` as const}
              render={({ field }) => (
                <Input
                  label="Expected Qty"
                  keyboardType="decimal-pad"
                  value={field.value ? String(field.value) : ""}
                  onChangeText={(t) => {
                    const n = Number(t);
                    field.onChange(Number.isFinite(n) ? n : 0);
                  }}
                  error={errors.items?.[idx]?.expectedQty?.message}
                />
              )}
            />
          </View>
        ))}

        <Text
          onPress={() => append({ itemId: 0, expectedQty: 0 })}
          className="text-brand-600 font-semibold self-start mb-2"
        >
          + Add another item
        </Text>

        {errors.items?.root ? (
          <Text className="text-xs text-rose-600 mb-2">{errors.items.root.message}</Text>
        ) : null}
```

- [ ] **Step 3: Verify compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/app/\(sales\)/new.tsx
git commit -m "feat(mobile): dynamic items field array on create-requisition form"
```

---

## Task 10: ItemStageBadge component

**Files:**
- Create: `bom-mobile/src/components/ItemStageBadge.tsx`

Each requisition item is shown with small badges indicating which stages are complete. For V1 we derive from the `RequisitionStatus`: BOM done if status ≥ CostingPending; costing done if status ≥ MdReview; price set if status = Approved.

- [ ] **Step 1: Implementation**

```tsx
import { Text, View } from "react-native";
import type { RequisitionStatus } from "@/types/api";

const STAGE_ORDER: RequisitionStatus[] = [
  "Draft",
  "BomPending",
  "BomInProgress",
  "CostingPending",
  "CostingInProgress",
  "MdReview",
  "Approved",
];

function indexOf(status: RequisitionStatus): number {
  const i = STAGE_ORDER.indexOf(status);
  return i < 0 ? 0 : i;
}

function isAtLeast(status: RequisitionStatus, target: RequisitionStatus): boolean {
  if (status === "Rejected") return false;
  return indexOf(status) >= indexOf(target);
}

interface Props {
  status: RequisitionStatus;
}

export function ItemStageBadge({ status }: Props) {
  const bomDone = isAtLeast(status, "CostingPending");
  const costingDone = isAtLeast(status, "MdReview");
  const priceSet = status === "Approved";

  return (
    <View className="flex-row flex-wrap mt-1">
      <Badge label="BOM" done={bomDone} />
      <Badge label="Costing" done={costingDone} />
      <Badge label="Price" done={priceSet} />
    </View>
  );
}

function Badge({ label, done }: { label: string; done: boolean }) {
  return (
    <View
      className={`px-2 py-0.5 mr-1 rounded ${
        done ? "bg-emerald-100" : "bg-slate-100"
      }`}
    >
      <Text
        className={`text-xs ${done ? "text-emerald-700" : "text-slate-500"}`}
      >
        {done ? "✓" : "○"} {label}
      </Text>
    </View>
  );
}
```

- [ ] **Step 2: Verify compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/components/ItemStageBadge.tsx
git commit -m "feat(mobile): add ItemStageBadge for per-item BOM/Costing/Price indicators"
```

---

## Task 11: PDF download helper

**Files:**
- Create: `bom-mobile/src/api/pdf.ts`

- [ ] **Step 1: Implementation**

```ts
import * as FileSystem from "expo-file-system";
import * as Sharing from "expo-sharing";
import { getAccess } from "@/auth/secureStore";
import Constants from "expo-constants";

const baseURL =
  (Constants.expoConfig?.extra?.apiBaseUrl as string) ?? "http://localhost:7300";

export async function downloadRequisitionPdf(requisitionId: number, refNo: string) {
  const token = await getAccess();
  if (!token) throw new Error("Not authenticated");

  const url = `${baseURL}/api/approvals/${requisitionId}/pdf`;
  const target = `${FileSystem.cacheDirectory}${refNo}-Quotation.pdf`;

  const result = await FileSystem.downloadAsync(url, target, {
    headers: { Authorization: `Bearer ${token}` },
  });

  if (result.status !== 200) {
    throw new Error(`PDF download failed (HTTP ${result.status})`);
  }

  const canShare = await Sharing.isAvailableAsync();
  if (!canShare) throw new Error("Sharing not supported on this device");

  await Sharing.shareAsync(result.uri, {
    mimeType: "application/pdf",
    dialogTitle: `${refNo} Quotation`,
    UTI: "com.adobe.pdf",
  });
}
```

- [ ] **Step 2: Verify compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/api/pdf.ts
git commit -m "feat(mobile): PDF download helper using expo-file-system + expo-sharing"
```

---

## Task 12: Requisition detail screen

**Files:**
- Create: `bom-mobile/app/(sales)/[id].tsx`

- [ ] **Step 1: Implementation**

```tsx
import { useState } from "react";
import { ScrollView, Text, View } from "react-native";
import { useLocalSearchParams } from "expo-router";
import { useRequisitionDetail } from "@/api/requisitions";
import { downloadRequisitionPdf } from "@/api/pdf";
import { Button } from "@/components/Button";
import { StatusPill } from "@/components/StatusPill";
import { ItemStageBadge } from "@/components/ItemStageBadge";
import { LoadingView } from "@/components/LoadingView";
import { ErrorBanner } from "@/components/ErrorBanner";
import { formatShortDate } from "@/utils/dates";

export default function RequisitionDetail() {
  const params = useLocalSearchParams<{ id: string }>();
  const id = Number(params.id);
  const q = useRequisitionDetail(id);
  const [pdfError, setPdfError] = useState<string | null>(null);
  const [pdfLoading, setPdfLoading] = useState(false);

  if (q.isPending) return <LoadingView />;
  if (q.isError || !q.data) {
    return (
      <View className="flex-1 p-4 bg-slate-50">
        <ErrorBanner
          message={
            q.error instanceof Error ? q.error.message : "Failed to load requisition"
          }
          onRetry={() => q.refetch()}
        />
      </View>
    );
  }

  const r = q.data;
  const isApproved = r.status === "Approved";
  const isRejected = r.status === "Rejected";

  const onDownload = async () => {
    setPdfError(null);
    setPdfLoading(true);
    try {
      await downloadRequisitionPdf(r.id, r.refNo);
    } catch (e: unknown) {
      setPdfError(e instanceof Error ? e.message : "PDF download failed");
    } finally {
      setPdfLoading(false);
    }
  };

  return (
    <ScrollView className="flex-1 bg-slate-50" contentContainerClassName="p-4">
      <View className="flex-row items-center justify-between mb-2">
        <Text className="text-2xl font-bold text-slate-900">{r.refNo}</Text>
        <StatusPill status={r.status} />
      </View>
      <Text className="text-base text-slate-700">{r.customerName}</Text>
      <Text className="text-xs text-slate-500 mb-4">
        {r.branchName} · Created {formatShortDate(r.createdAt)} · {r.currencyCode}
      </Text>

      {isRejected && r.approval?.notes ? (
        <View className="bg-rose-50 border border-rose-200 rounded-md p-3 mb-4">
          <Text className="text-sm font-semibold text-rose-800 mb-1">
            Rejection reason
          </Text>
          <Text className="text-sm text-rose-900">{r.approval.notes}</Text>
        </View>
      ) : null}

      <Text className="text-base font-semibold text-slate-900 mb-2">Items</Text>
      {r.items.map((it) => (
        <View
          key={it.id}
          className="bg-white border border-slate-200 rounded-md p-3 mb-2"
        >
          <View className="flex-row justify-between">
            <Text className="text-sm font-medium text-slate-900 flex-1 pr-2" numberOfLines={2}>
              {it.itemDescription}
            </Text>
            <Text className="text-sm text-slate-700">{it.expectedQty}</Text>
          </View>
          <ItemStageBadge status={r.status} />
        </View>
      ))}

      {isApproved ? (
        <View className="mt-6">
          {pdfError ? (
            <ErrorBanner message={pdfError} onRetry={() => setPdfError(null)} />
          ) : null}
          <Button
            title={pdfLoading ? "Preparing PDF..." : "Download PDF"}
            onPress={onDownload}
            loading={pdfLoading}
          />
        </View>
      ) : null}

      {r.approval && isApproved ? (
        <Text className="text-xs text-slate-500 text-center mt-4">
          Approved on {formatShortDate(r.approval.approvedAt)}
        </Text>
      ) : null}
    </ScrollView>
  );
}
```

- [ ] **Step 2: Verify compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/app/\(sales\)/\[id\].tsx
git commit -m "feat(mobile): requisition detail screen with items, timeline, PDF download"
```

---

## Task 13: README update + milestone verify

**Files:**
- Modify: `bom-mobile/README.md`

- [ ] **Step 1: Append a "What's implemented" section**

Add to the bottom of `bom-mobile/README.md`:

```markdown
## What's implemented

**Plan 1 (merged):** login, role-based routing, secure-store tokens, axios 401 refresh, profile + logout, placeholder home screens.

**Plan 2 (this work):** SalesPerson flow — requisitions list, create multi-item requisition, detail view with per-item stage indicators and PDF download. Detail remains read-only in V1.

**Plan 3 (next):** MD approval screens, SignalR live updates, notifications, EAS Build.
```

- [ ] **Step 2: Run full test suite**

```bash
cd bom-mobile && npx jest
```

Expected: all tests PASS. At this point the suite should contain:
- `__tests__/client.test.ts` (3 tests, from Plan 1)
- `__tests__/loginSchema.test.ts` (3 tests, from Plan 1)
- `__tests__/roleGuard.test.tsx` (2 tests, from Plan 1)
- `__tests__/dates.test.ts` (4 tests, Task 3)
- `__tests__/createRequisitionSchema.test.ts` (6 tests, Task 7)

Total: **18 tests**, 5 suites.

- [ ] **Step 3: Type-check**

```bash
cd bom-mobile && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 4: Smoke test on Android (manual)**

Per the Plan 1 README dev setup: start the backend with
`ASPNETCORE_ENVIRONMENT=Development dotnet run --project BomPriceApproval.API --urls "http://0.0.0.0:7300"`,
create `bom-mobile/.env.development` with the dev machine LAN IP, start Expo with
`npx expo start --lan`, scan from Expo Go on the Android phone.

Log in as a SalesPerson. Verify each:
- [ ] List screen loads with existing requisitions (if any); shows empty state if none.
- [ ] Tapping the FAB navigates to `/(sales)/new`.
- [ ] Customer picker shows branch customers; searching filters them.
- [ ] Currency chips select correctly; AED selected by default.
- [ ] Item picker shows branch items (inactive ones hidden).
- [ ] "+ Add another item" appends a row; "Remove" deletes (except the last).
- [ ] Submitting with empty customer or zero qty shows per-field error.
- [ ] Successful submit routes to the new requisition's detail; list is invalidated.
- [ ] Detail shows header + items + per-item stage badges.
- [ ] On a Rejected requisition the rejection reason block shows.
- [ ] On an Approved requisition the Download PDF button shows; tapping it opens the OS share sheet with the PDF.

- [ ] **Step 5: Final commit**

```bash
git add bom-mobile/README.md
git commit -m "docs(mobile): note Plan 2 scope in README"
```

---

## Milestone

At the end of this plan:
- `(sales)/index.tsx` shows a real requisitions list with pull-to-refresh + FAB.
- `(sales)/new.tsx` creates a multi-item requisition via POST /api/requisitions.
- `(sales)/[id].tsx` shows detail with items, stage indicators, rejection reason, and PDF download (when approved).
- All existing + new unit tests pass (18 total).
- Android Expo Go smoke test covers the full SalesPerson loop end-to-end.

Next plan (`2026-04-21-mobile-md-and-deploy.md`) covers the MD approval screens, SignalR live updates, the notifications screen, EAS Build profiles, and deployment. The iOS-vs-Android deployment target is an open question to resolve before writing Plan 3.
