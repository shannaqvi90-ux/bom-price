# V3 Mobile Phase D-1 (SalesPerson) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the SalesPerson surface of `bom-mobile` against the V3 backend contract — list, detail, combined create-with-BOM, and customer-confirm — replacing the V2.3 SP code that has been orphaned since the 2026-04-30 V3 cutover.

**Architecture:** V3-only purge of V2.3 SP screens; surgical rewrite of three V3-affected API hooks (`requisitions`, `approvals`, `customers`), delete one (`bom`), keep the V3-compatible rest. New components organized under `bom-mobile/src/features/sales/{list,create,detail,pickers}/`. Hybrid create layout (main FG list + per-FG bottom-sheet drawer); expand-in-place FG cards on detail; Pattern A picker (sheet → full-screen create modal) for Customer + FG + RM.

**Tech Stack:** React Native (Expo Router), TanStack Query, Zustand, Reanimated/Moti, Haptics, axios. Spec: `docs/superpowers/specs/2026-04-30-v3-mobile-phase-d-1-sp-design.md`. Verification: `tsc --noEmit` + Android emulator manual smoke + final on-device smoke + EAS OTA push.

---

## Pre-flight

### Task 0: Branch + clean state

**Files:**
- None — environment setup only

- [ ] **Step 1: Verify on master + clean tree**

```bash
git status -sb
git branch -v
```
Expected: `master`, working tree clean, master at `ec791b8` or later.

- [ ] **Step 2: Create feature branch**

```bash
git checkout -b feat/v3-mobile-phase-d-1-sp
```

- [ ] **Step 3: Verify mobile tooling baseline**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
```
Expected: 0 errors. Documents the pre-rewrite baseline.

---

## Phase 0 — API hooks rewrite (spec D-1.0)

Foundational. Every later task depends on these hook shapes. No UI work in this phase.

### Task 1: Define V3 requisition shape types

**Files:**
- Create: `bom-mobile/src/types/v3.ts`

- [ ] **Step 1: Write the V3 type definitions**

```typescript
// bom-mobile/src/types/v3.ts
export type V3Status =
  | "Draft" | "Costing" | "MdPricing" | "CustomerConfirm"
  | "MdFinalSign" | "Signed" | "Rejected" | "Cancelled";

export interface V3Customer {
  id: number; code: string; name: string;
  email?: string | null; phone?: string | null; address?: string | null;
}

export interface V3SalesPerson {
  id: number; name: string; email: string;
}

export interface V3BomLine {
  id?: number; processId: number; processName?: string;
  rawMaterialItemId: number; rawMaterialDescription?: string;
  qtyPerKg: number; wastagePct: number;
}

export interface V3FinishedGood {
  id?: number; itemId: number; code?: string; description?: string;
  expectedQty: number;
  bomLines: V3BomLine[];
  costs?: { foh?: number; transport?: number; commission?: number } | null;
}

export interface V3Requisition {
  id: number; refNo: string; status: V3Status; statusInt: number;
  branchId: number; branchName?: string;
  currencyCode: string; referenceNumber?: string | null; notes?: string | null;
  customer: V3Customer;
  salesPerson: V3SalesPerson;
  finishedGoods: V3FinishedGood[];
  createdAt: string; updatedAt: string;
  cancelReason?: string | null;
  cancelledAt?: string | null;
  cancelledByUserId?: number | null;
  finalPrice?: { totalAed: number; perFg: { itemId: number; priceAed: number }[] } | null;
}

export interface V3RequisitionListItem {
  id: number; refNo: string; status: V3Status; statusInt: number;
  customerName: string; currencyCode: string;
  branchId: number; branchName: string;
  salesPersonId: number; salesPersonName: string;
  createdAt: string;
  fgCount: number;
}
```

- [ ] **Step 2: Type-check passes**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/types/v3.ts
git commit -m "feat(mobile-d1): V3 requisition + FG + customer types"
```

---

### Task 2: Rewrite `src/api/requisitions.ts` for V3

**Files:**
- Modify: `bom-mobile/src/api/requisitions.ts` (full rewrite)

- [ ] **Step 1: Replace requisitions.ts with V3 hooks**

```typescript
// bom-mobile/src/api/requisitions.ts
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import type { V3Requisition, V3RequisitionListItem } from "../types/v3";

export const requisitionKeys = {
  all: ["requisitions"] as const,
  lists: () => [...requisitionKeys.all, "list"] as const,
  list: (params?: Record<string, unknown>) => [...requisitionKeys.lists(), params] as const,
  details: () => [...requisitionKeys.all, "detail"] as const,
  detail: (id: number) => [...requisitionKeys.details(), id] as const,
};

export interface CreateReqPayload {
  customerId: number; currencyCode: string;
  referenceNumber?: string; notes?: string;
  finishedGoods: {
    itemId: number; expectedQty: number;
    bomLines: { processId: number; rawMaterialItemId: number; qtyPerKg: number; wastagePct: number }[];
  }[];
}

export function useRequisitions(statuses?: string[]) {
  const params = statuses?.length ? { status: statuses.join(",") } : undefined;
  return useQuery({
    queryKey: requisitionKeys.list(params),
    queryFn: () => api.get<V3RequisitionListItem[]>("/api/requisitions", { params }).then((r) => r.data),
  });
}

export function useRequisition(id: number) {
  return useQuery({
    queryKey: requisitionKeys.detail(id),
    queryFn: () => api.get<V3Requisition>(`/api/requisitions/${id}`).then((r) => r.data),
    enabled: Number.isFinite(id) && id > 0,
  });
}

export function useCreateRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateReqPayload) =>
      api.post<{ id: number }>("/api/requisitions", payload).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: requisitionKeys.lists() }),
  });
}

export function useUpdateRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, payload }: { id: number; payload: CreateReqPayload }) =>
      api.put(`/api/requisitions/${id}`, payload),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(vars.id) });
      qc.invalidateQueries({ queryKey: requisitionKeys.lists() });
    },
  });
}

export function useSubmitToCosting() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => api.post(`/api/requisitions/${id}/submit`),
    onSuccess: (_d, id) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(id) });
      qc.invalidateQueries({ queryKey: requisitionKeys.lists() });
    },
  });
}
```

- [ ] **Step 2: Verify backend endpoints**

```bash
grep -E "HttpPost|HttpPut.*requisitions" BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs | head -10
```
If `PUT /api/requisitions/{id}` doesn't exist, mark Open Question #1 from spec — fall back to delete+recreate or change to PATCH per backend convention. Document the find in commit message.

- [ ] **Step 3: tsc clean**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
```
Expected: 0 errors. (Existing callers will break — fix in later tasks; verify here that the file itself is type-safe.)

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/src/api/requisitions.ts
git commit -m "feat(mobile-d1): rewrite requisitions hooks for V3 nested shape"
```

---

### Task 3: Rewrite `src/api/approvals.ts` (D-1 scope only)

**Files:**
- Modify: `bom-mobile/src/api/approvals.ts` (replace V2.3 endpoints with V3 customer-confirm endpoints only)

- [ ] **Step 1: Replace approvals.ts**

```typescript
// bom-mobile/src/api/approvals.ts
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import { requisitionKeys } from "./requisitions";

export function useAcceptCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (requisitionId: number) =>
      api.post(`/api/approvals/${requisitionId}/accept-customer`),
    onSuccess: (_d, id) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(id) });
      qc.invalidateQueries({ queryKey: requisitionKeys.lists() });
    },
  });
}

export function useRejectCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ requisitionId, reason }: { requisitionId: number; reason: string }) =>
      api.post(`/api/approvals/${requisitionId}/reject-customer`, { reason }),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(vars.requisitionId) });
      qc.invalidateQueries({ queryKey: requisitionKeys.lists() });
    },
  });
}
```

- [ ] **Step 2: tsc clean**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/api/approvals.ts
git commit -m "feat(mobile-d1): approvals hooks for V3 customer-confirm (accept/reject)"
```

---

### Task 4: Rewrite `src/api/customers.ts`

**Files:**
- Modify: `bom-mobile/src/api/customers.ts`

- [ ] **Step 1: Replace customers.ts**

```typescript
// bom-mobile/src/api/customers.ts
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";

export interface CustomerLite {
  id: number; code: string; name: string;
  email?: string | null; phone?: string | null; address?: string | null;
  isDeleted: boolean;
}

export interface CreateCustomerPayload {
  name: string; email?: string; phone?: string; address?: string;
}

export const customerKeys = {
  all: ["customers"] as const,
  list: (search?: string) => [...customerKeys.all, "list", search] as const,
};

export function useCustomers(search?: string) {
  return useQuery({
    queryKey: customerKeys.list(search),
    queryFn: () => api.get<CustomerLite[]>("/api/customers", {
      params: search ? { search } : undefined,
    }).then((r) => r.data.filter((c) => !c.isDeleted)),
    staleTime: 30_000,
  });
}

export function useCreateCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateCustomerPayload) =>
      api.post<CustomerLite>("/api/customers", payload).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: customerKeys.all }),
  });
}
```

- [ ] **Step 2: tsc clean**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/api/customers.ts
git commit -m "feat(mobile-d1): customers hooks for V3 (IsDeleted filter + inline create)"
```

---

### Task 5: Delete `src/api/bom.ts` + clean imports

**Files:**
- Delete: `bom-mobile/src/api/bom.ts`
- Modify: any file importing from `bom.ts` (cleanup)

- [ ] **Step 1: Find consumers of bom.ts**

```bash
grep -rn "from.*api/bom" bom-mobile/src bom-mobile/app
```
Expected output: list of files. Most will be in V2.3 SP screens we're about to purge. The MD `bom-mobile/app/(md)/item/[reqId]/[itemId].tsx` uses `useBomReview` for V2.3 BOM drilldown — leave it for now (out of D-1 scope), but if it breaks tsc, comment out the import with a `// TODO V3-mobile-D-3` note.

- [ ] **Step 2: Delete bom.ts**

```bash
rm bom-mobile/src/api/bom.ts
```

- [ ] **Step 3: Patch any leftover consumers**

If `bom-mobile/app/(md)/item/[reqId]/[itemId].tsx` or `bom-mobile/src/components/BomDetailSheet.tsx` still imports from `bom.ts`, comment those imports + render a placeholder `<Text>BOM view temporarily disabled — pending V3 mobile D-3 (MD)</Text>` with an inline TODO. Leave the rest of the file as-is.

- [ ] **Step 4: tsc clean**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
```
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add -A bom-mobile/
git commit -m "feat(mobile-d1): delete bom.ts (V3 has no separate BOM endpoints)"
```

---

### Task 6: Status mapping helper

**Files:**
- Create: `bom-mobile/src/features/sales/utils/statusMap.ts`

- [ ] **Step 1: Write status helper**

```typescript
// bom-mobile/src/features/sales/utils/statusMap.ts
import type { V3Status } from "../../../types/v3";

export type ListTab = "active" | "done" | "closed";

export const STATUS_TO_TAB: Record<V3Status, ListTab> = {
  Draft: "active", Costing: "active", MdPricing: "active",
  CustomerConfirm: "active", MdFinalSign: "active",
  Signed: "done",
  Rejected: "closed", Cancelled: "closed",
};

export const STATUSES_BY_TAB: Record<ListTab, V3Status[]> = {
  active: ["Draft", "Costing", "MdPricing", "CustomerConfirm", "MdFinalSign"],
  done: ["Signed"],
  closed: ["Rejected", "Cancelled"],
};

export const STATUS_COLOR: Record<V3Status, string> = {
  Draft: "#6b7280", Costing: "#f59e0b", MdPricing: "#3b82f6",
  CustomerConfirm: "#6366f1", MdFinalSign: "#8b5cf6",
  Signed: "#10b981", Rejected: "#ef4444", Cancelled: "#475569",
};

export const STATUS_LABEL: Record<V3Status, string> = {
  Draft: "Draft", Costing: "Costing", MdPricing: "MD Pricing",
  CustomerConfirm: "Customer Confirm", MdFinalSign: "MD Final Sign",
  Signed: "Signed", Rejected: "Rejected", Cancelled: "Cancelled",
};
```

- [ ] **Step 2: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
git add bom-mobile/src/features/sales/utils/statusMap.ts
git commit -m "feat(mobile-d1): V3 status-to-tab + color + label mapping"
```

---

## Phase 1 — List page (spec D-1.1)

### Task 7: StatusTabs component

**Files:**
- Create: `bom-mobile/src/features/sales/list/StatusTabs.tsx`

- [ ] **Step 1: Write component**

```typescript
// bom-mobile/src/features/sales/list/StatusTabs.tsx
import { Pressable, Text, View } from "react-native";
import * as Haptics from "expo-haptics";
import type { ListTab } from "../utils/statusMap";
import { theme } from "../../../theme";

interface Props {
  current: ListTab;
  counts: Record<ListTab, number>;
  onChange: (tab: ListTab) => void;
}

const TABS: { key: ListTab; label: string }[] = [
  { key: "active", label: "Active" },
  { key: "done", label: "Done" },
  { key: "closed", label: "Closed" },
];

export function StatusTabs({ current, counts, onChange }: Props) {
  return (
    <View style={{ flexDirection: "row", padding: 8, gap: 6 }}>
      {TABS.map(({ key, label }) => {
        const active = current === key;
        return (
          <Pressable
            key={key}
            onPress={() => {
              Haptics.selectionAsync();
              onChange(key);
            }}
            style={{
              flex: 1, paddingVertical: 10, borderRadius: 10,
              backgroundColor: active ? theme.colors.primary : "#f1f5f9",
              alignItems: "center",
            }}
          >
            <Text style={{ color: active ? "white" : "#0f172a", fontWeight: "600" }}>
              {label} {counts[key] > 0 ? `(${counts[key]})` : ""}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}
```

- [ ] **Step 2: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
git add bom-mobile/src/features/sales/list/StatusTabs.tsx
git commit -m "feat(mobile-d1): StatusTabs (Active / Done / Closed segmented control)"
```

---

### Task 8: ReqCard component

**Files:**
- Create: `bom-mobile/src/features/sales/list/ReqCard.tsx`

- [ ] **Step 1: Write component**

```typescript
// bom-mobile/src/features/sales/list/ReqCard.tsx
import { Pressable, Text, View } from "react-native";
import * as Haptics from "expo-haptics";
import type { V3RequisitionListItem } from "../../../types/v3";
import { STATUS_COLOR, STATUS_LABEL } from "../utils/statusMap";
import { OwnedByBadge } from "../../../components/OwnedByBadge";
import { useAuthStore } from "../../../store/auth";

interface Props {
  req: V3RequisitionListItem;
  onPress: (id: number) => void;
}

export function ReqCard({ req, onPress }: Props) {
  const { userId } = useAuthStore();
  const isPeer = userId !== null && req.salesPersonId !== userId;

  return (
    <Pressable
      onPress={() => {
        Haptics.selectionAsync();
        onPress(req.id);
      }}
      style={{
        backgroundColor: "white", marginHorizontal: 12, marginVertical: 4,
        padding: 14, borderRadius: 12, borderWidth: 1, borderColor: "#e5e7eb",
      }}
    >
      <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center" }}>
        <View style={{
          paddingHorizontal: 8, paddingVertical: 3, borderRadius: 6,
          backgroundColor: STATUS_COLOR[req.status],
        }}>
          <Text style={{ color: "white", fontSize: 11, fontWeight: "600" }}>
            {STATUS_LABEL[req.status]}
          </Text>
        </View>
        <Text style={{ fontWeight: "600", color: "#0f172a" }}>{req.refNo}</Text>
      </View>
      <Text style={{ marginTop: 8, fontSize: 15, color: "#0f172a" }} numberOfLines={1}>
        {req.customerName}
      </Text>
      <View style={{ flexDirection: "row", justifyContent: "space-between", marginTop: 6 }}>
        <Text style={{ fontSize: 12, color: "#64748b" }}>
          {req.fgCount} FG · {new Date(req.createdAt).toLocaleDateString()}
        </Text>
        <Text style={{ fontSize: 12, color: "#64748b" }}>{req.currencyCode}</Text>
      </View>
      {isPeer && <OwnedByBadge name={req.salesPersonName} />}
    </Pressable>
  );
}
```

- [ ] **Step 2: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
git add bom-mobile/src/features/sales/list/ReqCard.tsx
git commit -m "feat(mobile-d1): ReqCard (status chip + ref + customer + group-peer badge)"
```

---

### Task 9: SalesListScreen + replace `(sales)/index.tsx`

**Files:**
- Create: `bom-mobile/src/features/sales/list/SalesListScreen.tsx`
- Modify: `bom-mobile/app/(sales)/index.tsx` (replace V2.3 contents — delete-and-replace)

- [ ] **Step 1: Write SalesListScreen**

```typescript
// bom-mobile/src/features/sales/list/SalesListScreen.tsx
import { useMemo, useState } from "react";
import { View, FlatList, RefreshControl, Pressable, Text, ActivityIndicator } from "react-native";
import { useRouter } from "expo-router";
import * as Haptics from "expo-haptics";
import { useRequisitions } from "../../../api/requisitions";
import { STATUS_TO_TAB, type ListTab } from "../utils/statusMap";
import { StatusTabs } from "./StatusTabs";
import { ReqCard } from "./ReqCard";
import { theme } from "../../../theme";

export function SalesListScreen() {
  const router = useRouter();
  const [tab, setTab] = useState<ListTab>("active");
  const { data, isLoading, refetch, isRefetching } = useRequisitions();

  const counts = useMemo(() => {
    const c: Record<ListTab, number> = { active: 0, done: 0, closed: 0 };
    (data ?? []).forEach((r) => { c[STATUS_TO_TAB[r.status]] += 1; });
    return c;
  }, [data]);

  const filtered = useMemo(() => {
    return (data ?? []).filter((r) => STATUS_TO_TAB[r.status] === tab);
  }, [data, tab]);

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <StatusTabs current={tab} counts={counts} onChange={setTab} />
      {isLoading ? (
        <ActivityIndicator style={{ marginTop: 40 }} />
      ) : (
        <FlatList
          data={filtered}
          keyExtractor={(r) => String(r.id)}
          renderItem={({ item }) => <ReqCard req={item} onPress={(id) => router.push(`/(sales)/${id}`)} />}
          refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={refetch} />}
          contentContainerStyle={{ paddingVertical: 8, paddingBottom: 88 }}
          ListEmptyComponent={
            <Text style={{ textAlign: "center", marginTop: 48, color: "#64748b" }}>
              No {tab} requisitions yet.
            </Text>
          }
        />
      )}
      <Pressable
        onPress={() => {
          Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
          router.push("/(sales)/new");
        }}
        style={{
          position: "absolute", bottom: 24, right: 24,
          backgroundColor: theme.colors.primary, width: 56, height: 56, borderRadius: 28,
          alignItems: "center", justifyContent: "center",
          shadowColor: "#000", shadowOpacity: 0.15, shadowOffset: { width: 0, height: 4 }, shadowRadius: 6,
          elevation: 6,
        }}
      >
        <Text style={{ color: "white", fontSize: 28, lineHeight: 30, fontWeight: "300" }}>+</Text>
      </Pressable>
    </View>
  );
}
```

- [ ] **Step 2: Replace `app/(sales)/index.tsx`**

```typescript
// bom-mobile/app/(sales)/index.tsx
import { SalesListScreen } from "@/features/sales/list/SalesListScreen";
export default SalesListScreen;
```

- [ ] **Step 3: tsc + emulator smoke**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
```
Then on emulator: login as Ali → tab to (sales) → verify list renders, 3 tabs work, FAB navigates to /new (will 404 until Task 22; that's fine).

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/src/features/sales/list/ bom-mobile/app/\(sales\)/index.tsx
git commit -m "feat(mobile-d1): SalesListScreen with grouped tabs + pull-to-refresh + FAB"
```

---

## Phase 2 — Detail page read path (spec D-1.2)

### Task 10: DetailHeader component

**Files:**
- Create: `bom-mobile/src/features/sales/detail/DetailHeader.tsx`

- [ ] **Step 1: Write component**

```typescript
// bom-mobile/src/features/sales/detail/DetailHeader.tsx
import { View, Text } from "react-native";
import type { V3Requisition } from "../../../types/v3";
import { STATUS_COLOR, STATUS_LABEL } from "../utils/statusMap";

export function DetailHeader({ req }: { req: V3Requisition }) {
  return (
    <View style={{ padding: 16, backgroundColor: "white", borderBottomWidth: 1, borderColor: "#e5e7eb" }}>
      <View style={{ flexDirection: "row", alignItems: "center", justifyContent: "space-between" }}>
        <View style={{
          paddingHorizontal: 10, paddingVertical: 4, borderRadius: 6,
          backgroundColor: STATUS_COLOR[req.status],
        }}>
          <Text style={{ color: "white", fontSize: 12, fontWeight: "600" }}>
            {STATUS_LABEL[req.status]}
          </Text>
        </View>
        <Text style={{ fontWeight: "700", fontSize: 16, color: "#0f172a" }}>{req.refNo}</Text>
      </View>
      <Text style={{ marginTop: 10, fontSize: 18, color: "#0f172a", fontWeight: "600" }}>
        {req.customer.name}
      </Text>
      <Text style={{ fontSize: 12, color: "#64748b", marginTop: 2 }}>
        Updated {new Date(req.updatedAt).toLocaleString()}
      </Text>
    </View>
  );
}
```

- [ ] **Step 2: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
git add bom-mobile/src/features/sales/detail/DetailHeader.tsx
git commit -m "feat(mobile-d1): DetailHeader (status chip + ref + customer + updated-at)"
```

---

### Task 11: FgReadCard (expand-in-place) component

**Files:**
- Create: `bom-mobile/src/features/sales/detail/FgReadCard.tsx`

- [ ] **Step 1: Write component**

```typescript
// bom-mobile/src/features/sales/detail/FgReadCard.tsx
import { useState } from "react";
import { View, Text, Pressable } from "react-native";
import * as Haptics from "expo-haptics";
import type { V3FinishedGood } from "../../../types/v3";

export function FgReadCard({ fg, index }: { fg: V3FinishedGood; index: number }) {
  const [expanded, setExpanded] = useState(false);

  return (
    <View style={{
      marginHorizontal: 12, marginVertical: 6,
      backgroundColor: "white", borderRadius: 12,
      borderWidth: 1, borderColor: "#e5e7eb",
    }}>
      <Pressable
        onPress={() => {
          Haptics.selectionAsync();
          setExpanded((s) => !s);
        }}
        style={{ padding: 14 }}
      >
        <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center" }}>
          <View style={{ flex: 1 }}>
            <Text style={{ fontWeight: "600", fontSize: 14, color: "#0f172a" }}>
              FG #{index + 1} · {fg.code ?? `Item ${fg.itemId}`}
            </Text>
            <Text style={{ fontSize: 12, color: "#64748b", marginTop: 2 }}>
              {fg.description ?? ""} · {fg.expectedQty}kg · {fg.bomLines.length} BOM lines
            </Text>
          </View>
          <Text style={{ color: "#3b82f6", fontSize: 18 }}>{expanded ? "▾" : "▸"}</Text>
        </View>
      </Pressable>
      {expanded && (
        <View style={{ paddingHorizontal: 14, paddingBottom: 14, borderTopWidth: 1, borderColor: "#f1f5f9" }}>
          {fg.bomLines.map((line, i) => (
            <View key={line.id ?? i} style={{
              marginTop: 8, padding: 10, backgroundColor: "#f8fafc", borderRadius: 8,
            }}>
              <Text style={{ fontSize: 13, fontWeight: "500", color: "#0f172a" }}>
                {line.processName ?? `Process ${line.processId}`} · {line.rawMaterialDescription ?? `RM ${line.rawMaterialItemId}`}
              </Text>
              <Text style={{ fontSize: 11, color: "#64748b", marginTop: 2 }}>
                {line.qtyPerKg.toFixed(3)} kg/kg · wastage {line.wastagePct.toFixed(1)}%
              </Text>
            </View>
          ))}
          {fg.bomLines.length === 0 && (
            <Text style={{ marginTop: 8, color: "#94a3b8", fontStyle: "italic" }}>No BOM lines</Text>
          )}
        </View>
      )}
    </View>
  );
}
```

- [ ] **Step 2: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
git add bom-mobile/src/features/sales/detail/FgReadCard.tsx
git commit -m "feat(mobile-d1): FgReadCard expand-in-place (read-only BOM lines)"
```

---

### Task 12: FinalPriceCard (Signed status hero)

**Files:**
- Create: `bom-mobile/src/features/sales/detail/FinalPriceCard.tsx`

- [ ] **Step 1: Write component**

```typescript
// bom-mobile/src/features/sales/detail/FinalPriceCard.tsx
import { View, Text } from "react-native";
import type { V3Requisition } from "../../../types/v3";

export function FinalPriceCard({ req }: { req: V3Requisition }) {
  if (!req.finalPrice) return null;
  return (
    <View style={{
      margin: 12, padding: 18, borderRadius: 14,
      backgroundColor: "#10b981",
    }}>
      <Text style={{ color: "rgba(255,255,255,0.85)", fontSize: 12, fontWeight: "600", letterSpacing: 0.6, textTransform: "uppercase" }}>
        Final price
      </Text>
      <Text style={{ color: "white", fontSize: 32, fontWeight: "700", marginTop: 4 }}>
        AED {req.finalPrice.totalAed.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
      </Text>
      {req.finalPrice.perFg.length > 1 && (
        <View style={{ marginTop: 10 }}>
          {req.finalPrice.perFg.map((p) => (
            <Text key={p.itemId} style={{ color: "rgba(255,255,255,0.9)", fontSize: 12, marginTop: 2 }}>
              FG {p.itemId}: AED {p.priceAed.toFixed(2)}
            </Text>
          ))}
        </View>
      )}
    </View>
  );
}
```

- [ ] **Step 2: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
git add bom-mobile/src/features/sales/detail/FinalPriceCard.tsx
git commit -m "feat(mobile-d1): FinalPriceCard hero (Signed status)"
```

---

### Task 13: StatusFooterCta component

**Files:**
- Create: `bom-mobile/src/features/sales/detail/StatusFooterCta.tsx`

- [ ] **Step 1: Write component (placeholders for handlers)**

```typescript
// bom-mobile/src/features/sales/detail/StatusFooterCta.tsx
import { View, Pressable, Text, ActivityIndicator } from "react-native";
import type { V3Requisition } from "../../../types/v3";
import { useSubmitToCosting } from "../../../api/requisitions";
import { theme } from "../../../theme";

interface Props {
  req: V3Requisition;
  onCustomerConfirm: () => void;
  onDownloadPdf: () => void;
}

export function StatusFooterCta({ req, onCustomerConfirm, onDownloadPdf }: Props) {
  const submit = useSubmitToCosting();

  if (req.status === "Draft") {
    return (
      <Footer>
        <PrimaryButton
          loading={submit.isPending}
          onPress={() => submit.mutate(req.id)}
          label="Submit to Costing"
        />
      </Footer>
    );
  }
  if (req.status === "CustomerConfirm") {
    return (
      <Footer>
        <PrimaryButton onPress={onCustomerConfirm} label="Customer response" />
      </Footer>
    );
  }
  if (req.status === "Signed") {
    return (
      <Footer>
        <PrimaryButton onPress={onDownloadPdf} label="Download PDF" />
      </Footer>
    );
  }
  if (req.status === "Costing" || req.status === "MdPricing" || req.status === "MdFinalSign") {
    return (
      <Footer>
        <Text style={{ textAlign: "center", color: "#64748b" }}>
          Waiting on {req.status === "Costing" ? "Accountant" : req.status === "MdPricing" ? "MD pricing" : "MD final sign"}
        </Text>
      </Footer>
    );
  }
  return null; // Rejected + Cancelled = no footer
}

function Footer({ children }: { children: React.ReactNode }) {
  return (
    <View style={{
      padding: 16, paddingBottom: 32, backgroundColor: "white",
      borderTopWidth: 1, borderColor: "#e5e7eb",
    }}>
      {children}
    </View>
  );
}

function PrimaryButton({ onPress, label, loading }: { onPress: () => void; label: string; loading?: boolean }) {
  return (
    <Pressable
      onPress={onPress}
      disabled={loading}
      style={{
        backgroundColor: theme.colors.primary, padding: 14, borderRadius: 10,
        alignItems: "center", opacity: loading ? 0.6 : 1,
      }}
    >
      {loading ? <ActivityIndicator color="white" /> : (
        <Text style={{ color: "white", fontWeight: "600", fontSize: 15 }}>{label}</Text>
      )}
    </Pressable>
  );
}
```

- [ ] **Step 2: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
git add bom-mobile/src/features/sales/detail/StatusFooterCta.tsx
git commit -m "feat(mobile-d1): StatusFooterCta with status-driven CTAs"
```

---

### Task 14: SalesDetailScreen + replace `(sales)/[id].tsx`

**Files:**
- Create: `bom-mobile/src/features/sales/detail/SalesDetailScreen.tsx`
- Modify: `bom-mobile/app/(sales)/[id].tsx` (replace V2.3 contents)

- [ ] **Step 1: Write SalesDetailScreen**

```typescript
// bom-mobile/src/features/sales/detail/SalesDetailScreen.tsx
import { useState } from "react";
import { ScrollView, View, Text, ActivityIndicator, Pressable } from "react-native";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useRequisition } from "../../../api/requisitions";
import { DetailHeader } from "./DetailHeader";
import { FgReadCard } from "./FgReadCard";
import { FinalPriceCard } from "./FinalPriceCard";
import { StatusFooterCta } from "./StatusFooterCta";
import { theme } from "../../../theme";

export function SalesDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const reqId = Number(id);
  const router = useRouter();
  const { data: req, isLoading } = useRequisition(reqId);
  const [confirmModalOpen, setConfirmModalOpen] = useState(false);

  if (isLoading) return <ActivityIndicator style={{ marginTop: 40 }} />;
  if (!req) return <Text style={{ padding: 24 }}>Requisition not found.</Text>;

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <ScrollView contentContainerStyle={{ paddingBottom: 16 }}>
        <DetailHeader req={req} />
        {req.status === "Draft" && (
          <Pressable
            onPress={() => router.push(`/(sales)/edit/${req.id}`)}
            style={{ alignSelf: "flex-end", padding: 12 }}
          >
            <Text style={{ color: theme.colors.primary, fontWeight: "600" }}>Edit ✎</Text>
          </Pressable>
        )}
        {req.status === "Signed" && <FinalPriceCard req={req} />}
        {req.status === "Cancelled" && req.cancelReason && (
          <View style={{ margin: 12, padding: 12, backgroundColor: "#fef2f2", borderRadius: 10 }}>
            <Text style={{ fontWeight: "600", color: "#991b1b" }}>Cancelled</Text>
            <Text style={{ color: "#7f1d1d", marginTop: 4 }}>{req.cancelReason}</Text>
            {req.cancelledAt && <Text style={{ color: "#9a3412", fontSize: 11, marginTop: 4 }}>
              {new Date(req.cancelledAt).toLocaleString()}
            </Text>}
          </View>
        )}
        <View style={{ marginTop: 8 }}>
          {req.finishedGoods.map((fg, i) => <FgReadCard key={fg.id ?? i} fg={fg} index={i} />)}
        </View>
        {req.notes && (
          <View style={{ margin: 12, padding: 12, backgroundColor: "white", borderRadius: 10, borderWidth: 1, borderColor: "#e5e7eb" }}>
            <Text style={{ fontSize: 12, color: "#64748b", textTransform: "uppercase", fontWeight: "600", letterSpacing: 0.5 }}>Notes</Text>
            <Text style={{ marginTop: 4, color: "#0f172a" }}>{req.notes}</Text>
          </View>
        )}
      </ScrollView>
      <StatusFooterCta
        req={req}
        onCustomerConfirm={() => setConfirmModalOpen(true)}
        onDownloadPdf={() => { /* wire in Task 28 */ }}
      />
      {/* CustomerConfirmModal will be wired in Task 27 */}
    </View>
  );
}
```

- [ ] **Step 2: Replace `app/(sales)/[id].tsx`**

```typescript
// bom-mobile/app/(sales)/[id].tsx
import { SalesDetailScreen } from "@/features/sales/detail/SalesDetailScreen";
export default SalesDetailScreen;
```

- [ ] **Step 3: tsc + emulator smoke**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
```
Then emulator: tap any req in list → detail loads → expand FG cards → Edit button visible only on Draft → footer CTA matches status table from spec.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/src/features/sales/detail/ bom-mobile/app/\(sales\)/\[id\].tsx
git commit -m "feat(mobile-d1): SalesDetailScreen with V3 nested shape + status CTAs"
```

---

## Phase 3 — Picker pattern foundational (spec D-1.3)

Pattern A applied uniformly. Six tasks (sheet + modal pair × 3 entity types). The sheet/modal pattern is the same — only the entity-specific form fields differ.

### Task 15: CustomerPickerSheet

**Files:**
- Create: `bom-mobile/src/features/sales/pickers/CustomerPickerSheet.tsx`

- [ ] **Step 1: Write component**

```typescript
// bom-mobile/src/features/sales/pickers/CustomerPickerSheet.tsx
import { useState } from "react";
import { Modal, View, Text, TextInput, Pressable, FlatList, ActivityIndicator } from "react-native";
import { useCustomers, type CustomerLite } from "../../../api/customers";
import { theme } from "../../../theme";

interface Props {
  visible: boolean;
  onPick: (customer: CustomerLite) => void;
  onClose: () => void;
  onCreateNew: () => void;
}

export function CustomerPickerSheet({ visible, onPick, onClose, onCreateNew }: Props) {
  const [search, setSearch] = useState("");
  const { data, isLoading } = useCustomers(search);

  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
      <Pressable onPress={onClose} style={{ flex: 1, backgroundColor: "rgba(0,0,0,0.45)" }} />
      <View style={{
        backgroundColor: "white",
        borderTopLeftRadius: 18, borderTopRightRadius: 18,
        padding: 16, maxHeight: "70%",
      }}>
        <View style={{ alignSelf: "center", width: 40, height: 4, backgroundColor: "#cbd5e1", borderRadius: 2, marginBottom: 12 }} />
        <View style={{ flexDirection: "row", alignItems: "center", justifyContent: "space-between" }}>
          <Text style={{ fontWeight: "600", fontSize: 16 }}>Select customer</Text>
          <Pressable onPress={onCreateNew}>
            <Text style={{ color: theme.colors.primary, fontWeight: "600" }}>+ Create new</Text>
          </Pressable>
        </View>
        <TextInput
          value={search}
          onChangeText={setSearch}
          placeholder="Search by name…"
          style={{
            marginTop: 10, padding: 10, borderRadius: 8,
            backgroundColor: "#f1f5f9", fontSize: 14,
          }}
        />
        {isLoading ? <ActivityIndicator style={{ marginTop: 20 }} /> : (
          <FlatList
            data={data ?? []}
            keyExtractor={(c) => String(c.id)}
            keyboardShouldPersistTaps="handled"
            renderItem={({ item }) => (
              <Pressable
                onPress={() => onPick(item)}
                style={{ paddingVertical: 12, borderBottomWidth: 1, borderColor: "#f1f5f9" }}
              >
                <Text style={{ fontWeight: "500", color: "#0f172a" }}>{item.name}</Text>
                <Text style={{ fontSize: 12, color: "#64748b" }}>{item.code}</Text>
              </Pressable>
            )}
            ListEmptyComponent={<Text style={{ padding: 16, color: "#94a3b8" }}>No customers found.</Text>}
          />
        )}
      </View>
    </Modal>
  );
}
```

- [ ] **Step 2: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
git add bom-mobile/src/features/sales/pickers/CustomerPickerSheet.tsx
git commit -m "feat(mobile-d1): CustomerPickerSheet (Pattern A — sheet w/ + Create button)"
```

---

### Task 16: CustomerCreateModal

**Files:**
- Create: `bom-mobile/src/features/sales/pickers/CustomerCreateModal.tsx`

- [ ] **Step 1: Write component**

```typescript
// bom-mobile/src/features/sales/pickers/CustomerCreateModal.tsx
import { useState } from "react";
import { Modal, View, Text, TextInput, Pressable, KeyboardAvoidingView, Platform, ActivityIndicator } from "react-native";
import { useCreateCustomer, type CustomerLite } from "../../../api/customers";
import { theme } from "../../../theme";

interface Props {
  visible: boolean;
  onCreated: (customer: CustomerLite) => void;
  onClose: () => void;
}

export function CustomerCreateModal({ visible, onCreated, onClose }: Props) {
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [address, setAddress] = useState("");
  const create = useCreateCustomer();

  const reset = () => { setName(""); setEmail(""); setPhone(""); setAddress(""); };
  const submit = async () => {
    if (!name.trim()) return;
    const created = await create.mutateAsync({ name: name.trim(), email: email || undefined, phone: phone || undefined, address: address || undefined });
    onCreated(created);
    reset();
  };

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onClose}>
      <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === "ios" ? "padding" : undefined}>
        <View style={{ flexDirection: "row", padding: 16, borderBottomWidth: 1, borderColor: "#e5e7eb", justifyContent: "space-between", alignItems: "center" }}>
          <Pressable onPress={onClose}><Text style={{ color: theme.colors.primary }}>Cancel</Text></Pressable>
          <Text style={{ fontWeight: "600", fontSize: 16 }}>New customer</Text>
          <Pressable onPress={submit} disabled={!name.trim() || create.isPending}>
            {create.isPending ? <ActivityIndicator /> : <Text style={{ color: theme.colors.primary, fontWeight: "600", opacity: name.trim() ? 1 : 0.4 }}>Save</Text>}
          </Pressable>
        </View>
        <View style={{ padding: 16 }}>
          <FormField label="Name *" value={name} onChange={setName} />
          <FormField label="Email" value={email} onChange={setEmail} keyboardType="email-address" />
          <FormField label="Phone" value={phone} onChange={setPhone} keyboardType="phone-pad" />
          <FormField label="Address" value={address} onChange={setAddress} multiline />
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

function FormField({ label, value, onChange, keyboardType, multiline }: {
  label: string; value: string; onChange: (s: string) => void;
  keyboardType?: "email-address" | "phone-pad"; multiline?: boolean;
}) {
  return (
    <View style={{ marginTop: 12 }}>
      <Text style={{ fontSize: 12, color: "#64748b", textTransform: "uppercase", fontWeight: "600", letterSpacing: 0.5 }}>{label}</Text>
      <TextInput
        value={value}
        onChangeText={onChange}
        keyboardType={keyboardType}
        multiline={multiline}
        style={{
          marginTop: 4, padding: 10, borderRadius: 8,
          backgroundColor: "#f1f5f9", fontSize: 14,
          minHeight: multiline ? 80 : undefined,
          textAlignVertical: multiline ? "top" : "center",
        }}
      />
    </View>
  );
}
```

- [ ] **Step 2: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
git add bom-mobile/src/features/sales/pickers/CustomerCreateModal.tsx
git commit -m "feat(mobile-d1): CustomerCreateModal full-screen form"
```

---

### Task 17: FgPickerSheet + FgCreateModal

**Files:**
- Create: `bom-mobile/src/features/sales/pickers/FgPickerSheet.tsx`
- Create: `bom-mobile/src/features/sales/pickers/FgCreateModal.tsx`

- [ ] **Step 1: Write FgPickerSheet**

Same structure as `CustomerPickerSheet` but uses `useItems({ type: "FinishedGood", branchId })` from existing `bom-mobile/src/api/lookups.ts`. Replace customer-specific copy with FG copy ("Select Finished Good", searches by code/description). Tap returns `{ id, code, description }`.

- [ ] **Step 2: Write FgCreateModal**

Same structure as `CustomerCreateModal`. Fields: Description, LastPurchasePrice (optional, decimal). Wire to `POST /api/items` with `Type=1` (RawMaterial=1, FinishedGood=2 — verify in `Domain/Enums/ItemType.cs` first; `FinishedGood` is `Type=2`).

```bash
grep -n "FinishedGood\|RawMaterial" BomPriceApproval.API/Domain/Enums/ItemType.cs
```

- [ ] **Step 3: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
git add bom-mobile/src/features/sales/pickers/FgPickerSheet.tsx bom-mobile/src/features/sales/pickers/FgCreateModal.tsx
git commit -m "feat(mobile-d1): FgPickerSheet + FgCreateModal (Pattern A applied to FG)"
```

---

### Task 18: RmPickerSheet + RmCreateModal

**Files:**
- Create: `bom-mobile/src/features/sales/pickers/RmPickerSheet.tsx`
- Create: `bom-mobile/src/features/sales/pickers/RmCreateModal.tsx`

- [ ] **Step 1: Mirror Task 17 with `Type=RawMaterial` filter**

Pickers identical to FG variants except they query for `type=RawMaterial`. Modal fields: Description, LastPurchasePrice. POST to `/api/items` with `Type=1` (RawMaterial).

- [ ] **Step 2: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
git add bom-mobile/src/features/sales/pickers/RmPickerSheet.tsx bom-mobile/src/features/sales/pickers/RmCreateModal.tsx
git commit -m "feat(mobile-d1): RmPickerSheet + RmCreateModal (Pattern A applied to RM)"
```

---

## Phase 4 — Combined create Hybrid (spec D-1.4)

### Task 19: HeaderSection (customer + currency + reference + notes)

**Files:**
- Create: `bom-mobile/src/features/sales/create/HeaderSection.tsx`

- [ ] **Step 1: Write component**

```typescript
// bom-mobile/src/features/sales/create/HeaderSection.tsx
import { useState } from "react";
import { View, Text, Pressable, TextInput } from "react-native";
import { CustomerPickerSheet } from "../pickers/CustomerPickerSheet";
import { CustomerCreateModal } from "../pickers/CustomerCreateModal";
import type { CustomerLite } from "../../../api/customers";
import { theme } from "../../../theme";

interface Props {
  customer: CustomerLite | null;
  setCustomer: (c: CustomerLite) => void;
  currency: string; setCurrency: (c: string) => void;
  reference: string; setReference: (s: string) => void;
  notes: string; setNotes: (s: string) => void;
}

const CURRENCIES = ["AED", "USD", "EUR", "GBP", "SAR"];

export function HeaderSection({ customer, setCustomer, currency, setCurrency, reference, setReference, notes, setNotes }: Props) {
  const [pickerOpen, setPickerOpen] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);

  return (
    <View style={{ padding: 14, backgroundColor: "white", borderRadius: 12, margin: 12, borderWidth: 1, borderColor: "#e5e7eb" }}>
      <Pressable onPress={() => setPickerOpen(true)}>
        <Text style={{ fontSize: 11, color: "#64748b", textTransform: "uppercase", fontWeight: "600" }}>Customer</Text>
        <Text style={{ marginTop: 4, fontSize: 16, fontWeight: customer ? "600" : "400", color: customer ? "#0f172a" : "#94a3b8" }}>
          {customer ? customer.name : "Tap to pick…"}
        </Text>
      </Pressable>
      <View style={{ flexDirection: "row", gap: 8, marginTop: 12 }}>
        {CURRENCIES.map((c) => (
          <Pressable key={c} onPress={() => setCurrency(c)}
            style={{
              paddingHorizontal: 10, paddingVertical: 6, borderRadius: 6,
              backgroundColor: currency === c ? theme.colors.primary : "#f1f5f9",
            }}>
            <Text style={{ color: currency === c ? "white" : "#0f172a", fontWeight: "500", fontSize: 12 }}>{c}</Text>
          </Pressable>
        ))}
      </View>
      <View style={{ marginTop: 12 }}>
        <Text style={{ fontSize: 11, color: "#64748b", textTransform: "uppercase", fontWeight: "600" }}>Reference (optional)</Text>
        <TextInput value={reference} onChangeText={setReference}
          style={{ marginTop: 4, padding: 8, backgroundColor: "#f8fafc", borderRadius: 6, fontSize: 14 }} />
      </View>
      <View style={{ marginTop: 12 }}>
        <Text style={{ fontSize: 11, color: "#64748b", textTransform: "uppercase", fontWeight: "600" }}>Notes</Text>
        <TextInput value={notes} onChangeText={setNotes} multiline
          style={{ marginTop: 4, padding: 8, backgroundColor: "#f8fafc", borderRadius: 6, fontSize: 14, minHeight: 60, textAlignVertical: "top" }} />
      </View>

      <CustomerPickerSheet
        visible={pickerOpen}
        onPick={(c) => { setCustomer(c); setPickerOpen(false); }}
        onClose={() => setPickerOpen(false)}
        onCreateNew={() => { setPickerOpen(false); setCreateOpen(true); }}
      />
      <CustomerCreateModal
        visible={createOpen}
        onCreated={(c) => { setCustomer(c); setCreateOpen(false); }}
        onClose={() => setCreateOpen(false)}
      />
    </View>
  );
}
```

- [ ] **Step 2: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
git add bom-mobile/src/features/sales/create/HeaderSection.tsx
git commit -m "feat(mobile-d1): HeaderSection (customer picker + currency chips + ref + notes)"
```

---

### Task 20: BomLineRow + FgEditDrawer

**Files:**
- Create: `bom-mobile/src/features/sales/create/BomLineRow.tsx`
- Create: `bom-mobile/src/features/sales/create/FgEditDrawer.tsx`

- [ ] **Step 1: Write BomLineRow**

```typescript
// bom-mobile/src/features/sales/create/BomLineRow.tsx
import { View, Text, TextInput, Pressable } from "react-native";
import type { V3BomLine } from "../../../types/v3";

interface Props {
  line: V3BomLine; idx: number;
  onChange: (line: V3BomLine) => void;
  onPickRm: () => void;
  onPickProcess: () => void;
  onRemove: () => void;
}

export function BomLineRow({ line, idx, onChange, onPickRm, onPickProcess, onRemove }: Props) {
  return (
    <View style={{ marginTop: 10, padding: 10, backgroundColor: "#f8fafc", borderRadius: 8 }}>
      <View style={{ flexDirection: "row", justifyContent: "space-between" }}>
        <Text style={{ fontSize: 11, fontWeight: "600", color: "#64748b" }}>Line {idx + 1}</Text>
        <Pressable onPress={onRemove}><Text style={{ color: "#dc2626", fontSize: 12 }}>Remove</Text></Pressable>
      </View>
      <Pressable onPress={onPickProcess} style={{ marginTop: 6 }}>
        <Text style={{ fontSize: 10, color: "#94a3b8" }}>Process</Text>
        <Text style={{ fontWeight: "500", color: line.processName ? "#0f172a" : "#94a3b8" }}>
          {line.processName ?? "Tap to pick…"}
        </Text>
      </Pressable>
      <Pressable onPress={onPickRm} style={{ marginTop: 6 }}>
        <Text style={{ fontSize: 10, color: "#94a3b8" }}>Raw material</Text>
        <Text style={{ fontWeight: "500", color: line.rawMaterialDescription ? "#0f172a" : "#94a3b8" }}>
          {line.rawMaterialDescription ?? "Tap to pick…"}
        </Text>
      </Pressable>
      <View style={{ flexDirection: "row", gap: 8, marginTop: 6 }}>
        <View style={{ flex: 1 }}>
          <Text style={{ fontSize: 10, color: "#94a3b8" }}>Qty/kg</Text>
          <TextInput
            value={String(line.qtyPerKg)}
            keyboardType="decimal-pad"
            onChangeText={(t) => onChange({ ...line, qtyPerKg: parseFloat(t) || 0 })}
            style={{ padding: 6, backgroundColor: "white", borderRadius: 6, fontSize: 14 }}
          />
        </View>
        <View style={{ flex: 1 }}>
          <Text style={{ fontSize: 10, color: "#94a3b8" }}>Wastage %</Text>
          <TextInput
            value={String(line.wastagePct)}
            keyboardType="decimal-pad"
            onChangeText={(t) => onChange({ ...line, wastagePct: parseFloat(t) || 0 })}
            style={{ padding: 6, backgroundColor: "white", borderRadius: 6, fontSize: 14 }}
          />
        </View>
      </View>
    </View>
  );
}
```

- [ ] **Step 2: Write FgEditDrawer**

```typescript
// bom-mobile/src/features/sales/create/FgEditDrawer.tsx
import { useState } from "react";
import { Modal, View, Text, TextInput, Pressable, ScrollView, KeyboardAvoidingView, Platform } from "react-native";
import type { V3FinishedGood, V3BomLine } from "../../../types/v3";
import { BomLineRow } from "./BomLineRow";
import { RmPickerSheet } from "../pickers/RmPickerSheet";
import { theme } from "../../../theme";

interface Props {
  fg: V3FinishedGood;
  visible: boolean;
  onSave: (fg: V3FinishedGood) => void;
  onClose: () => void;
  onRemove: () => void;
}

export function FgEditDrawer({ fg, visible, onSave, onClose, onRemove }: Props) {
  const [draft, setDraft] = useState<V3FinishedGood>(fg);
  const [rmPickerForIdx, setRmPickerForIdx] = useState<number | null>(null);

  const updateLine = (idx: number, line: V3BomLine) => {
    const lines = [...draft.bomLines];
    lines[idx] = line;
    setDraft({ ...draft, bomLines: lines });
  };
  const removeLine = (idx: number) => {
    setDraft({ ...draft, bomLines: draft.bomLines.filter((_, i) => i !== idx) });
  };
  const addLine = () => {
    setDraft({ ...draft, bomLines: [...draft.bomLines, { processId: 0, rawMaterialItemId: 0, qtyPerKg: 0, wastagePct: 0 }] });
  };

  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
      <Pressable onPress={onClose} style={{ flex: 1, backgroundColor: "rgba(0,0,0,0.45)" }} />
      <KeyboardAvoidingView behavior={Platform.OS === "ios" ? "padding" : undefined}>
        <View style={{
          backgroundColor: "white", borderTopLeftRadius: 18, borderTopRightRadius: 18,
          padding: 16, maxHeight: "85%",
        }}>
          <View style={{ alignSelf: "center", width: 40, height: 4, backgroundColor: "#cbd5e1", borderRadius: 2, marginBottom: 12 }} />
          <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center" }}>
            <Text style={{ fontWeight: "600", fontSize: 16 }}>{draft.code ?? "Edit FG"}</Text>
            <Pressable onPress={() => { onSave(draft); }}>
              <Text style={{ color: theme.colors.primary, fontWeight: "600" }}>Done</Text>
            </Pressable>
          </View>
          <ScrollView contentContainerStyle={{ paddingVertical: 12 }} keyboardShouldPersistTaps="handled">
            <Text style={{ fontSize: 11, color: "#64748b", textTransform: "uppercase", fontWeight: "600" }}>Expected Qty (kg)</Text>
            <TextInput
              value={String(draft.expectedQty)}
              keyboardType="decimal-pad"
              onChangeText={(t) => setDraft({ ...draft, expectedQty: parseFloat(t) || 0 })}
              style={{ padding: 10, backgroundColor: "#f1f5f9", borderRadius: 8, marginTop: 4 }}
            />
            <Text style={{ marginTop: 16, fontSize: 13, fontWeight: "600", color: "#0f172a" }}>BOM Lines ({draft.bomLines.length})</Text>
            {draft.bomLines.map((line, i) => (
              <BomLineRow
                key={i} line={line} idx={i}
                onChange={(l) => updateLine(i, l)}
                onPickRm={() => setRmPickerForIdx(i)}
                onPickProcess={() => { /* TODO: simple Process picker (small enum from lookups.ts useProcesses) */ }}
                onRemove={() => removeLine(i)}
              />
            ))}
            <Pressable onPress={addLine}
              style={{ marginTop: 10, padding: 10, backgroundColor: "#eff6ff", borderRadius: 8, alignItems: "center" }}>
              <Text style={{ color: theme.colors.primary, fontWeight: "600" }}>+ Add line</Text>
            </Pressable>
            <Pressable onPress={onRemove}
              style={{ marginTop: 24, padding: 12, alignItems: "center" }}>
              <Text style={{ color: "#dc2626", fontWeight: "600" }}>Remove this FG</Text>
            </Pressable>
          </ScrollView>
        </View>
      </KeyboardAvoidingView>
      {rmPickerForIdx !== null && (
        <RmPickerSheet
          visible
          onPick={(rm) => {
            updateLine(rmPickerForIdx, { ...draft.bomLines[rmPickerForIdx], rawMaterialItemId: rm.id, rawMaterialDescription: rm.description });
            setRmPickerForIdx(null);
          }}
          onClose={() => setRmPickerForIdx(null)}
          onCreateNew={() => { /* RmCreateModal — could chain or omit if drawer-within-drawer fails on Android per spec open-question #2 */ }}
        />
      )}
    </Modal>
  );
}
```

- [ ] **Step 3: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
git add bom-mobile/src/features/sales/create/BomLineRow.tsx bom-mobile/src/features/sales/create/FgEditDrawer.tsx
git commit -m "feat(mobile-d1): FgEditDrawer + BomLineRow (Hybrid pattern)"
```

---

### Task 21: FgListMain + add FG flow

**Files:**
- Create: `bom-mobile/src/features/sales/create/FgListMain.tsx`

- [ ] **Step 1: Write component**

```typescript
// bom-mobile/src/features/sales/create/FgListMain.tsx
import { useState } from "react";
import { View, Text, Pressable } from "react-native";
import type { V3FinishedGood } from "../../../types/v3";
import { FgEditDrawer } from "./FgEditDrawer";
import { FgPickerSheet } from "../pickers/FgPickerSheet";
import { FgCreateModal } from "../pickers/FgCreateModal";
import { theme } from "../../../theme";

interface Props {
  fgs: V3FinishedGood[];
  setFgs: (fgs: V3FinishedGood[]) => void;
}

export function FgListMain({ fgs, setFgs }: Props) {
  const [pickerOpen, setPickerOpen] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [drawerForIdx, setDrawerForIdx] = useState<number | null>(null);

  const addFromPicker = (item: { id: number; code: string; description: string }) => {
    setFgs([...fgs, { itemId: item.id, code: item.code, description: item.description, expectedQty: 0, bomLines: [] }]);
    setPickerOpen(false);
    setDrawerForIdx(fgs.length); // open drawer for the just-added FG
  };

  return (
    <View style={{ marginHorizontal: 12, marginTop: 4 }}>
      <Text style={{ fontWeight: "600", fontSize: 14, marginVertical: 8, color: "#0f172a" }}>
        Finished Goods ({fgs.length})
      </Text>
      {fgs.map((fg, i) => (
        <Pressable
          key={i}
          onPress={() => setDrawerForIdx(i)}
          style={{ padding: 12, backgroundColor: "white", borderRadius: 10, marginBottom: 8, borderWidth: 1, borderColor: "#e5e7eb" }}
        >
          <View style={{ flexDirection: "row", justifyContent: "space-between" }}>
            <Text style={{ fontWeight: "600", color: "#0f172a" }}>{fg.code ?? `Item ${fg.itemId}`}</Text>
            <Text style={{
              color: fg.bomLines.length > 0 ? theme.colors.primary : "#92400e",
              fontWeight: "600", fontSize: 12,
            }}>
              {fg.bomLines.length > 0 ? "Edit ›" : "+ Lines"}
            </Text>
          </View>
          <Text style={{ fontSize: 12, color: "#64748b", marginTop: 4 }}>
            {fg.description} · {fg.expectedQty || "-"} kg · {fg.bomLines.length} lines
          </Text>
        </Pressable>
      ))}
      <Pressable
        onPress={() => setPickerOpen(true)}
        style={{ padding: 12, backgroundColor: "#eff6ff", borderRadius: 10, alignItems: "center" }}
      >
        <Text style={{ color: theme.colors.primary, fontWeight: "600" }}>+ Add FG</Text>
      </Pressable>

      <FgPickerSheet
        visible={pickerOpen}
        onPick={addFromPicker}
        onClose={() => setPickerOpen(false)}
        onCreateNew={() => { setPickerOpen(false); setCreateOpen(true); }}
      />
      <FgCreateModal
        visible={createOpen}
        onCreated={(newFg) => {
          addFromPicker(newFg);
          setCreateOpen(false);
        }}
        onClose={() => setCreateOpen(false)}
      />
      {drawerForIdx !== null && (
        <FgEditDrawer
          fg={fgs[drawerForIdx]}
          visible
          onSave={(updated) => {
            const next = [...fgs];
            next[drawerForIdx] = updated;
            setFgs(next);
            setDrawerForIdx(null);
          }}
          onRemove={() => {
            setFgs(fgs.filter((_, i) => i !== drawerForIdx));
            setDrawerForIdx(null);
          }}
          onClose={() => setDrawerForIdx(null)}
        />
      )}
    </View>
  );
}
```

- [ ] **Step 2: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
git add bom-mobile/src/features/sales/create/FgListMain.tsx
git commit -m "feat(mobile-d1): FgListMain (Hybrid main FG list + drawer integration)"
```

---

### Task 22: SubmitFooter + validation

**Files:**
- Create: `bom-mobile/src/features/sales/create/SubmitFooter.tsx`
- Create: `bom-mobile/src/features/sales/create/validate.ts`

- [ ] **Step 1: Write validate.ts**

```typescript
// bom-mobile/src/features/sales/create/validate.ts
import type { V3FinishedGood } from "../../../types/v3";
import type { CustomerLite } from "../../../api/customers";

export interface ValidationResult { ok: boolean; errors: string[]; }

export function validateRequisition(
  customer: CustomerLite | null,
  currency: string,
  fgs: V3FinishedGood[],
): ValidationResult {
  const errors: string[] = [];
  if (!customer) errors.push("Customer is required");
  if (!currency) errors.push("Currency is required");
  if (fgs.length === 0) errors.push("At least 1 FG required");
  fgs.forEach((fg, i) => {
    const tag = `FG #${i + 1}`;
    if (!(fg.expectedQty > 0)) errors.push(`${tag}: ExpectedQty must be > 0`);
    if (fg.bomLines.length === 0) errors.push(`${tag}: at least 1 BOM line required`);
    fg.bomLines.forEach((l, j) => {
      const lt = `${tag} line ${j + 1}`;
      if (!l.processId) errors.push(`${lt}: process required`);
      if (!l.rawMaterialItemId) errors.push(`${lt}: raw material required`);
      if (!(l.qtyPerKg > 0)) errors.push(`${lt}: qty/kg must be > 0`);
      if (l.wastagePct < 0) errors.push(`${lt}: wastage cannot be negative`);
    });
  });
  return { ok: errors.length === 0, errors };
}
```

- [ ] **Step 2: Write SubmitFooter**

```typescript
// bom-mobile/src/features/sales/create/SubmitFooter.tsx
import { View, Pressable, Text, ActivityIndicator } from "react-native";
import { theme } from "../../../theme";
import type { ValidationResult } from "./validate";

interface Props {
  validation: ValidationResult;
  onSubmit: () => void;
  loading: boolean;
  label: string;
}

export function SubmitFooter({ validation, onSubmit, loading, label }: Props) {
  return (
    <View style={{
      padding: 16, paddingBottom: 32, backgroundColor: "white",
      borderTopWidth: 1, borderColor: "#e5e7eb",
    }}>
      {!validation.ok && (
        <Text style={{ color: "#dc2626", fontSize: 11, marginBottom: 8 }}>
          {validation.errors[0]}
          {validation.errors.length > 1 ? ` (+ ${validation.errors.length - 1} more)` : ""}
        </Text>
      )}
      <Pressable
        onPress={onSubmit}
        disabled={!validation.ok || loading}
        style={{
          backgroundColor: theme.colors.primary, padding: 14, borderRadius: 10,
          alignItems: "center", opacity: !validation.ok || loading ? 0.5 : 1,
        }}
      >
        {loading ? <ActivityIndicator color="white" /> : (
          <Text style={{ color: "white", fontWeight: "600", fontSize: 15 }}>{label}</Text>
        )}
      </Pressable>
    </View>
  );
}
```

- [ ] **Step 3: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
git add bom-mobile/src/features/sales/create/SubmitFooter.tsx bom-mobile/src/features/sales/create/validate.ts
git commit -m "feat(mobile-d1): SubmitFooter + validation rules"
```

---

### Task 23: CombinedCreateScreen (mode=new) + replace `(sales)/new.tsx`

**Files:**
- Create: `bom-mobile/src/features/sales/create/CombinedCreateScreen.tsx`
- Modify: `bom-mobile/app/(sales)/new.tsx`

- [ ] **Step 1: Write CombinedCreateScreen**

```typescript
// bom-mobile/src/features/sales/create/CombinedCreateScreen.tsx
import { useState, useMemo } from "react";
import { ScrollView, View } from "react-native";
import { useRouter } from "expo-router";
import { HeaderSection } from "./HeaderSection";
import { FgListMain } from "./FgListMain";
import { SubmitFooter } from "./SubmitFooter";
import { validateRequisition } from "./validate";
import { useCreateRequisition } from "../../../api/requisitions";
import type { V3FinishedGood } from "../../../types/v3";
import type { CustomerLite } from "../../../api/customers";

interface Props { mode: "new" | "edit"; reqId?: number; initial?: { /* hydrated for edit-mode in Task 24 */ } }

export function CombinedCreateScreen({ mode }: Props) {
  const router = useRouter();
  const [customer, setCustomer] = useState<CustomerLite | null>(null);
  const [currency, setCurrency] = useState("AED");
  const [reference, setReference] = useState("");
  const [notes, setNotes] = useState("");
  const [fgs, setFgs] = useState<V3FinishedGood[]>([]);
  const create = useCreateRequisition();

  const validation = useMemo(() =>
    validateRequisition(customer, currency, fgs), [customer, currency, fgs]);

  const onSubmit = async () => {
    if (!customer || !validation.ok) return;
    const created = await create.mutateAsync({
      customerId: customer.id, currencyCode: currency,
      referenceNumber: reference || undefined, notes: notes || undefined,
      finishedGoods: fgs.map((fg) => ({
        itemId: fg.itemId, expectedQty: fg.expectedQty,
        bomLines: fg.bomLines.map((l) => ({
          processId: l.processId, rawMaterialItemId: l.rawMaterialItemId,
          qtyPerKg: l.qtyPerKg, wastagePct: l.wastagePct,
        })),
      })),
    });
    router.replace(`/(sales)/${created.id}`);
  };

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <ScrollView contentContainerStyle={{ paddingBottom: 16 }} keyboardShouldPersistTaps="handled">
        <HeaderSection
          customer={customer} setCustomer={setCustomer}
          currency={currency} setCurrency={setCurrency}
          reference={reference} setReference={setReference}
          notes={notes} setNotes={setNotes}
        />
        <FgListMain fgs={fgs} setFgs={setFgs} />
      </ScrollView>
      <SubmitFooter
        validation={validation}
        onSubmit={onSubmit}
        loading={create.isPending}
        label={mode === "new" ? "Submit to Costing" : "Save changes"}
      />
    </View>
  );
}
```

- [ ] **Step 2: Replace `app/(sales)/new.tsx`**

```typescript
// bom-mobile/app/(sales)/new.tsx
import { CombinedCreateScreen } from "@/features/sales/create/CombinedCreateScreen";
export default function NewReq() { return <CombinedCreateScreen mode="new" />; }
```

- [ ] **Step 3: tsc + emulator smoke**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
```
Then emulator: tap FAB → New screen → pick customer → add FG → fill BOM line → submit. Verify req appears in list as Costing (if backend transitions Draft→Costing on submit) or Draft (if separate submit step).

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/src/features/sales/create/CombinedCreateScreen.tsx bom-mobile/app/\(sales\)/new.tsx
git commit -m "feat(mobile-d1): CombinedCreateScreen (mode=new) wired end-to-end"
```

---

## Phase 5 — Edit-draft (spec D-1.5)

### Task 24: Edit route + hydration + save

**Files:**
- Create: `bom-mobile/app/(sales)/edit/[id].tsx`
- Modify: `bom-mobile/src/features/sales/create/CombinedCreateScreen.tsx` (add edit-mode hydration + update path)

- [ ] **Step 1: Add edit hydration to CombinedCreateScreen**

In `CombinedCreateScreen.tsx`, replace the `useState` initializers with logic that pre-populates from `useRequisition(reqId)` when `mode === "edit"`. Add `useUpdateRequisition` for the save path. Pseudo-diff:

```typescript
import { useEffect } from "react";
import { useRequisition, useUpdateRequisition } from "../../../api/requisitions";

interface Props { mode: "new" | "edit"; reqId?: number }

export function CombinedCreateScreen({ mode, reqId }: Props) {
  const { data: existing } = useRequisition(mode === "edit" && reqId ? reqId : 0);
  const update = useUpdateRequisition();
  // ... existing useState calls ...

  useEffect(() => {
    if (mode === "edit" && existing) {
      setCustomer({
        id: existing.customer.id, code: existing.customer.code, name: existing.customer.name,
        email: existing.customer.email, phone: existing.customer.phone, address: existing.customer.address,
        isDeleted: false,
      });
      setCurrency(existing.currencyCode);
      setReference(existing.referenceNumber ?? "");
      setNotes(existing.notes ?? "");
      setFgs(existing.finishedGoods);
    }
  }, [mode, existing]);

  const onSubmit = async () => {
    if (!customer || !validation.ok) return;
    const payload = { /* same shape as before */ };
    if (mode === "edit" && reqId) {
      await update.mutateAsync({ id: reqId, payload });
      router.replace(`/(sales)/${reqId}`);
    } else {
      const created = await create.mutateAsync(payload);
      router.replace(`/(sales)/${created.id}`);
    }
  };
  // ...
}
```

- [ ] **Step 2: Create edit route**

```typescript
// bom-mobile/app/(sales)/edit/[id].tsx
import { useLocalSearchParams } from "expo-router";
import { CombinedCreateScreen } from "@/features/sales/create/CombinedCreateScreen";

export default function EditReq() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <CombinedCreateScreen mode="edit" reqId={Number(id)} />;
}
```

- [ ] **Step 3: tsc + emulator smoke**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
```
Then emulator: open a Draft req detail → tap "Edit" → form pre-populated → modify → save → returns to detail with updated values.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/app/\(sales\)/edit/ bom-mobile/src/features/sales/create/CombinedCreateScreen.tsx
git commit -m "feat(mobile-d1): edit-draft mode (route + hydration + save)"
```

---

## Phase 6 — Customer-confirm modal (spec D-1.6)

### Task 25: CustomerConfirmModal

**Files:**
- Create: `bom-mobile/src/features/sales/detail/CustomerConfirmModal.tsx`

- [ ] **Step 1: Write component**

```typescript
// bom-mobile/src/features/sales/detail/CustomerConfirmModal.tsx
import { useState } from "react";
import { Modal, View, Text, TextInput, Pressable, ActivityIndicator } from "react-native";
import * as Haptics from "expo-haptics";
import { useAcceptCustomer, useRejectCustomer } from "../../../api/approvals";
import type { V3Requisition } from "../../../types/v3";

interface Props {
  visible: boolean;
  req: V3Requisition;
  onClose: () => void;
}

export function CustomerConfirmModal({ visible, req, onClose }: Props) {
  const [view, setView] = useState<"choose" | "reject">("choose");
  const [reason, setReason] = useState("");
  const accept = useAcceptCustomer();
  const reject = useRejectCustomer();

  const onAccept = async () => {
    Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    await accept.mutateAsync(req.id);
    onClose();
  };

  const onSubmitReject = async () => {
    if (reason.trim().length < 5) return;
    await reject.mutateAsync({ requisitionId: req.id, reason: reason.trim() });
    onClose();
  };

  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <Pressable onPress={onClose} style={{ flex: 1, backgroundColor: "rgba(0,0,0,0.45)" }} />
      <View style={{
        position: "absolute", left: 16, right: 16, top: "20%",
        backgroundColor: "white", borderRadius: 16, padding: 18,
      }}>
        <Text style={{ fontWeight: "700", fontSize: 16, color: "#0f172a" }}>Customer response on {req.refNo}</Text>
        <Text style={{ marginTop: 6, color: "#64748b", fontSize: 13 }}>
          {req.customer.name} · {req.currencyCode}
        </Text>

        {view === "choose" ? (
          <View style={{ marginTop: 16, gap: 10 }}>
            <Pressable onPress={onAccept} disabled={accept.isPending}
              style={{ backgroundColor: "#10b981", padding: 14, borderRadius: 10, alignItems: "center" }}>
              {accept.isPending ? <ActivityIndicator color="white" /> :
                <Text style={{ color: "white", fontWeight: "600" }}>Customer accepted</Text>}
            </Pressable>
            <Pressable onPress={() => setView("reject")}
              style={{ backgroundColor: "#fef3c7", padding: 14, borderRadius: 10, alignItems: "center" }}>
              <Text style={{ color: "#92400e", fontWeight: "600" }}>Customer rejected</Text>
            </Pressable>
          </View>
        ) : (
          <View style={{ marginTop: 16 }}>
            <Text style={{ fontSize: 11, color: "#64748b", fontWeight: "600", textTransform: "uppercase" }}>Reason (≥ 5 chars)</Text>
            <TextInput
              value={reason} onChangeText={setReason} multiline
              style={{ marginTop: 4, padding: 10, backgroundColor: "#f1f5f9", borderRadius: 8, minHeight: 80, textAlignVertical: "top" }}
            />
            <View style={{ flexDirection: "row", gap: 10, marginTop: 12 }}>
              <Pressable onPress={() => setView("choose")} style={{ flex: 1, padding: 12, alignItems: "center" }}>
                <Text style={{ color: "#64748b" }}>Back</Text>
              </Pressable>
              <Pressable
                onPress={onSubmitReject}
                disabled={reason.trim().length < 5 || reject.isPending}
                style={{
                  flex: 2, padding: 12, borderRadius: 10, alignItems: "center",
                  backgroundColor: "#dc2626", opacity: reason.trim().length < 5 ? 0.5 : 1,
                }}>
                {reject.isPending ? <ActivityIndicator color="white" /> :
                  <Text style={{ color: "white", fontWeight: "600" }}>Confirm rejection</Text>}
              </Pressable>
            </View>
          </View>
        )}
      </View>
    </Modal>
  );
}
```

- [ ] **Step 2: Wire CustomerConfirmModal into SalesDetailScreen**

In `SalesDetailScreen.tsx`, import + render `<CustomerConfirmModal>` controlled by the existing `confirmModalOpen` state. The `onCustomerConfirm` callback already passed to `StatusFooterCta` opens it.

- [ ] **Step 3: tsc + emulator smoke**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
```
Then emulator: with a CustomerConfirm-status req (seeded by Sara on dev backend in advance), open detail → tap "Customer response" → accept transitions to MdFinalSign; reject (with 5+ char reason) transitions back to MdPricing.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/src/features/sales/detail/CustomerConfirmModal.tsx bom-mobile/src/features/sales/detail/SalesDetailScreen.tsx
git commit -m "feat(mobile-d1): CustomerConfirmModal (accept + reject with reason)"
```

---

## Phase 7 — Wire-up + V2.3 purge (spec D-1.7)

### Task 26: Wire PDF download for Signed status

**Files:**
- Modify: `bom-mobile/src/features/sales/detail/SalesDetailScreen.tsx`

- [ ] **Step 1: Use existing `useDownloadPdf` from `src/api/pdf.ts`**

Verify the PDF hook exists:

```bash
grep -n "useDownloadPdf\|export function" bom-mobile/src/api/pdf.ts
```

If the existing hook downloads via `expo-file-system` + opens via `expo-sharing`, wire it into `SalesDetailScreen`'s `onDownloadPdf` prop on `<StatusFooterCta>`. If the hook doesn't exist or downloads inline-only, add a minimal version using `FileSystem.downloadAsync` + `Sharing.shareAsync`.

- [ ] **Step 2: tsc + emulator smoke + commit**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
git add bom-mobile/src/features/sales/detail/SalesDetailScreen.tsx
git commit -m "feat(mobile-d1): wire PDF download on Signed status"
```

---

### Task 27: V2.3 purge sweep + import cleanup

**Files:**
- Verify deletions: V2.3 SP screens were already replaced via earlier tasks (Task 9 + Task 14 + Task 23 + Task 24). Confirm no orphaned imports.

- [ ] **Step 1: Verify no V2.3 hook usages remain in (sales)**

```bash
grep -rn "useStartBomItem\|useSaveBomItemLines\|useSubmitBom" bom-mobile/app bom-mobile/src
```
Expected: no matches. If any match in (md) directory, leave with TODO comment per Task 5.

- [ ] **Step 2: tsc + verify clean**

```bash
cd bom-mobile && npx tsc --noEmit && cd ..
```

- [ ] **Step 3: Commit anything left + update memory pointer**

```bash
git status -s
# if any modifications: git add -A bom-mobile/ && git commit -m "chore(mobile-d1): V2.3 SP purge sweep"
```

---

## Phase 8 — Smoke + OTA (spec D-1.8)

### Task 28: On-device smoke checklist

**Files:**
- None — manual test session

- [ ] **Step 1: Build local with EAS dev client OR Expo Go**

```bash
cd bom-mobile && npx expo start
```
Connect phone via tunnel or LAN.

- [ ] **Step 2: Run smoke checklist**

Smoke against current dev backend (localhost:7300 via `adb reverse`). Login as Ali (SP).

- [ ] List page renders 3 tabs with correct counts; pull-to-refresh works
- [ ] FAB → New screen
- [ ] Customer picker opens; "+ Create new" opens modal; new customer auto-selected
- [ ] FG picker opens; create new FG; FG card shows on main; tapping opens drawer
- [ ] Drawer: ExpectedQty editable; "+ Add line" adds row; pick RM via sheet; pick process; qty/wastage editable
- [ ] Done → drawer closes; FG card shows updated line count
- [ ] Submit to Costing → req appears in Active tab as Costing
- [ ] Tap req in list → detail opens; FG cards expand-in-place; all data correct
- [ ] Edit-draft path: change to a Draft → Edit → modify → save → detail updated
- [ ] CustomerConfirm-status req (seed via Sara before this step): Customer response modal works for both accept + reject
- [ ] Cancelled-status req shows cancellation context
- [ ] Rejected-status req shows read-only with no CTA
- [ ] Group-peer reqs show OwnedByBadge

- [ ] **Step 3: Document any issues found**

If any checklist item fails, fix in a follow-up commit. If all pass, proceed to Task 29.

---

### Task 29: Push to remote + open PR (no auto-merge)

**Files:**
- None — git op only

- [ ] **Step 1: Push branch**

```bash
git push -u origin feat/v3-mobile-phase-d-1-sp
```

- [ ] **Step 2: Open PR — DO NOT AUTO-MERGE**

```bash
gh pr create --base master --head feat/v3-mobile-phase-d-1-sp \
  --title "feat(mobile-d1): V3 SalesPerson rebuild" \
  --body "Implements docs/superpowers/specs/2026-04-30-v3-mobile-phase-d-1-sp-design.md.

Locked decisions D1-D10 honored. 4 mandatory screens + 6 picker components + status mapping + V2.3 purge.

## Test plan
- [x] tsc clean
- [x] On-device smoke checklist passed (Task 28)
- [ ] EAS OTA push to preview channel (next task post-merge)
- [ ] Verify on physical device after OTA

🤖 Generated with [Claude Code](https://claude.com/claude-code)
" --label "hold"
```

The `hold` label prevents auto-merge until OTA verification on physical device passes (Task 30).

---

### Task 30: EAS OTA + physical-device verify

**Files:**
- None — release op

- [ ] **Step 1: Drift check pre-OTA**

```bash
git log mobile-shipped-vc1..HEAD --oneline -- bom-mobile/
```
Expected: only D-1 commits, no `app.config.ts` / `eas.json` / native dep changes. If anything native — abort OTA, switch to APK rebuild + tag bump.

- [ ] **Step 2: Push OTA**

```bash
cd bom-mobile && npx eas-cli update --branch preview --message "v3-mobile-d1-sp-$(git rev-parse --short HEAD)" && cd ..
```

- [ ] **Step 3: Verify on physical device**

Open the existing APK on phone (the one installed from `mobile-shipped-vc1`). Pull-to-refresh in the app or restart it — Expo should detect the new bundle and apply. Then re-run a 5-step abbreviated smoke (login → list → create → detail → confirm-modal).

- [ ] **Step 4: Remove `hold` label and merge**

```bash
gh pr edit <PR#> --remove-label "hold"
gh pr merge <PR#> --squash --delete-branch
```

- [ ] **Step 5: Memory + spec acceptance criteria check**

Update `memory/project_v3_mobile_d1_brainstorm.md` with the master SHA, PR number, and "Phase D-1 SHIPPED 2026-04-XX" marker. Walk through spec section 10 acceptance criteria and check off each.

---

## Self-review (engineer reading this plan)

Spec → plan coverage:

| Spec section | Plan coverage |
|---|---|
| 4. Locked decisions D1-D10 | All threaded into tasks (D2 purge in Tasks 9/14/23/24/27; D3 screens in Tasks 9/14/23/25; D4 modal in Task 25; D5 edit-draft in Task 24; D7 tabs in Task 7; D8 hybrid in Tasks 19-23; D9 picker pattern in Tasks 15-18; D10 expand-in-place in Task 11) |
| 5.1 List page | Tasks 6+7+8+9 |
| 5.2 Combined create | Tasks 19+20+21+22+23 |
| 5.3 Detail page | Tasks 10+11+12+13+14+26 |
| 5.4 Customer-confirm modal | Task 25 |
| 5.5 Picker sheets + create modals | Tasks 15+16+17+18 |
| 6.1 Purge list | Tasks 5 + 9 + 14 + 23 + 24 + 27 |
| 6.2 New routes | Tasks 9 + 14 + 23 + 24 |
| 6.3 New components | Distributed across all tasks |
| 6.4 API hook strategy | Tasks 1+2+3+4+5 |
| 6.5 Reused components | No new tasks (no changes) |
| 7. State machine + status palette | Task 6 |
| 8. EAS OTA | Task 30 |
| 9. Testing strategy | Task 28 |
| 10. Acceptance criteria | Task 30 step 5 |
| 11. Open questions | Verified during Task 2 (#1 edit endpoint), Task 17/20 (#2 sheet stacking), Task 30 (#3 OTA size) |
| 12. Implementation phasing | This whole plan |

Total: 31 tasks (0 + 1-30), ~150-200 commits estimated.

Frequency: 1 commit per task minimum, often 2-3.

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-04-30-v3-mobile-phase-d-1-sp-implementation.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — Dispatch a fresh subagent per task, review between tasks, fast iteration. Best for a 30-task plan; avoids context exhaustion.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints. Good for short plans; this one is too long.

Recommendation: **option 1 — subagent-driven**, in a fresh session with `/model sonnet` (per CLAUDE.md model strategy).

**Which approach?**
