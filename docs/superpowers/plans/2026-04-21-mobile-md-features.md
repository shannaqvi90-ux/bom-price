# Mobile MD Features Implementation Plan (3a of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the placeholder `(md)/index.tsx` with real MD approval screens, add a notifications screen, and wire SignalR so that approval-pending updates arrive live while the app is open. Mobile SalesPerson sees new notifications (e.g. "your requisition was rejected") in the same stream.

**Architecture:** New `src/api/approvals.ts` + `src/api/notifications.ts` hooks via TanStack Query. A new `SignalRProvider` mounted in the root layout establishes the `@microsoft/signalr` connection after login and invalidates relevant query keys on `ReceiveNotification` events. Two reusable modal components (`ConfirmDialog`, `RejectReasonPrompt`) land in the shared components folder. Approval detail uses a `useFieldArray` of per-item prices with live margin-computation. Zero backend changes.

**Tech Stack:** TanStack Query v5, Axios via `@/api/client` (existing), `@microsoft/signalr` (already a dep), React Hook Form + Zod, NativeWind. Tests use the existing two-project jest config (`node` for logic, `rn` for components).

**Builds on:** Plans 1 + 2 (both merged; master at `35ed3cd`). Depends on `@/api/client`, `@/auth/AuthContext`, `@/components/{Button,Input,EmptyState,ErrorBanner,LoadingView,StatusPill,SearchablePicker}`, `@/types/api`, `@/utils/{dates,validation}`.

---

## Scope deviations from spec

Two places where the spec described behavior the backend doesn't actually support:

1. **§6.1 "Mark as read on tap → `PATCH /api/notifications/{id}/read`"** — the backend has no such endpoint (only `GET /api/notifications` and `GET /api/notifications/unread-count`). The web app handles "read" as client-only UI state, and this plan does the same. A real mark-read endpoint can be added later without breaking changes.
2. **§5.1 SignalR event `RequisitionSubmittedToMd`** — the actual SignalR event emitted by `NotificationService.SendAsync` is `ReceiveNotification` (generic) with a `referenceType` + `message` payload. Mobile listens to that single event and invalidates queries based on the message/type.

Both are noted here so the implementing engineer isn't confused by the spec.

---

## Backend endpoints used (all exist, no changes)

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/requisitions?status=MdReview` | Pending approvals list (MD sees all branches) |
| GET | `/api/approvals/{requisitionId}` | `MdReviewDetail` — items, per-item cost breakdown |
| POST | `/api/approvals/{requisitionId}/approve` | Approve with `{ items: [{requisitionItemId, salesPricePerKgAed}], notes? }` |
| POST | `/api/approvals/{requisitionId}/reject` | Reject with `{ notes: string }` |
| GET | `/api/notifications` | All notifications for current user |
| GET | `/api/notifications/unread-count` | `{ count: number }` |
| WS | `/hubs/notifications?access_token=<jwt>` | SignalR hub; event `ReceiveNotification` |

Note on hub URL: web uses a relative `/hubs/notifications?access_token=...`. Mobile must use the **full** LAN URL (`http://<host>:7300/hubs/notifications?access_token=...`) because the app runs on a different origin than the backend.

---

## DTO cheat sheet (already in `src/types/api.ts`)

- `MdReviewDetail` — `{ refNo, customerName, currencyCode, exchangeRate?, readyForReview, items: MdReviewItemDetail[] }`
- `MdReviewItemDetail` — `{ requisitionItemId, itemDescription, expectedQty, costStatus, cost: MdReviewItemCost | null }`
- `MdReviewItemCost` — `{ rawMaterialCostPerKg, landedCostPerKg, fohPerKg, totalCostPerKg, materialCostPct, landedCostPct, fohPct }`
- `RequisitionListItem` — already used by SalesPerson list; re-used for MD pending list (filtered)
- `Notification` — `{ id, message, referenceId, referenceType, isRead, createdAt }`
- `UserRole` — `"SalesPerson" | "BomCreator" | "Accountant" | "ManagingDirector" | "Admin"`

New local types (created by this plan):
- `ApproveItemPayload` — `{ requisitionItemId: number, salesPricePerKgAed: number }`
- `ApprovePayload` — `{ items: ApproveItemPayload[], notes?: string }`
- `RejectPayload` — `{ notes: string }`

---

## File structure (created by this plan)

```
bom-mobile/
  app/
    (md)/
      index.tsx                         # replaces placeholder — pending approvals list
      [id].tsx                          # NEW — approval detail
    notifications.tsx                   # NEW — notifications list screen
    _layout.tsx                         # MODIFIED — mount SignalRProvider + bell header button
  src/
    api/
      approvals.ts                      # NEW — useMdReview, useApproveRequisition, useRejectRequisition
      notifications.ts                  # NEW — useNotifications, useUnreadCount, client markRead
    signalr/
      SignalRProvider.tsx               # NEW
    components/
      ConfirmDialog.tsx                 # NEW — reusable
      RejectReasonPrompt.tsx            # NEW — modal with textarea
      ApprovalItemRow.tsx               # NEW — per-item editable row
      NotificationBell.tsx              # NEW — header button with unread badge
    utils/
      validation.ts                     # EXTENDED — approveSchema
      numbers.ts                        # NEW — formatMoney, formatPct
  __tests__/
    approveSchema.test.ts               # NEW — Zod unit tests
    numbers.test.ts                     # NEW — unit tests
```

Files modified:
- `src/utils/validation.ts` — append `approveSchema`
- `app/_layout.tsx` — mount `SignalRProvider`
- `app/(md)/_layout.tsx` — add `NotificationBell` to header (next to `Log out`)
- `app/(sales)/_layout.tsx` — same (so SalesPerson sees notifications too)

---

## Task 1: Number formatting utility + tests (TDD)

**Files:**
- Create: `bom-mobile/src/utils/numbers.ts`
- Create: `bom-mobile/__tests__/numbers.test.ts`

- [ ] **Step 1: Write the failing tests**

```ts
// __tests__/numbers.test.ts
import { formatMoney, formatPct } from "@/utils/numbers";

test("formatMoney keeps four decimals", () => {
  expect(formatMoney(12.3456)).toBe("12.3456");
});

test("formatMoney rounds correctly", () => {
  expect(formatMoney(12.34567)).toBe("12.3457");
});

test("formatMoney returns '-' for null/undefined/NaN", () => {
  expect(formatMoney(null)).toBe("-");
  expect(formatMoney(undefined)).toBe("-");
  expect(formatMoney(Number.NaN)).toBe("-");
});

test("formatPct renders with one decimal and % suffix", () => {
  expect(formatPct(12.34)).toBe("12.3%");
});

test("formatPct returns '-' for null", () => {
  expect(formatPct(null)).toBe("-");
});
```

- [ ] **Step 2: Run — expect fail**

```bash
cd bom-mobile && npx jest __tests__/numbers.test.ts
```

Expected: FAIL — module missing.

- [ ] **Step 3: Implement**

```ts
// src/utils/numbers.ts
export function formatMoney(n: number | null | undefined): string {
  if (n == null || Number.isNaN(n)) return "-";
  return n.toFixed(4);
}

export function formatPct(n: number | null | undefined): string {
  if (n == null || Number.isNaN(n)) return "-";
  return `${n.toFixed(1)}%`;
}
```

- [ ] **Step 4: Run — expect pass**

```bash
cd bom-mobile && npx jest __tests__/numbers.test.ts
```

Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add bom-mobile/src/utils/numbers.ts bom-mobile/__tests__/numbers.test.ts
git commit -m "feat(mobile): add formatMoney + formatPct utilities with tests"
```

---

## Task 2: Approvals API module

**Files:**
- Create: `bom-mobile/src/api/approvals.ts`

- [ ] **Step 1: Implementation**

```ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import { requisitionKeys } from "./requisitions";
import type { MdReviewDetail } from "@/types/api";

export const approvalKeys = {
  review: (requisitionId: number) => ["approval", "review", requisitionId] as const,
};

export interface ApproveItemPayload {
  requisitionItemId: number;
  salesPricePerKgAed: number;
}

export interface ApprovePayload {
  items: ApproveItemPayload[];
  notes?: string;
}

export interface RejectPayload {
  notes: string;
}

export function useMdReview(requisitionId: number) {
  return useQuery({
    queryKey: approvalKeys.review(requisitionId),
    queryFn: async () => {
      const res = await api.get<MdReviewDetail>(`/api/approvals/${requisitionId}`);
      return res.data;
    },
    enabled: Number.isFinite(requisitionId) && requisitionId > 0,
    retry: false,
  });
}

export function useApproveRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      requisitionId,
      payload,
    }: {
      requisitionId: number;
      payload: ApprovePayload;
    }) => {
      await api.post(`/api/approvals/${requisitionId}/approve`, payload);
    },
    onSuccess: (_data, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.list() });
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(requisitionId) });
      qc.invalidateQueries({ queryKey: approvalKeys.review(requisitionId) });
    },
  });
}

export function useRejectRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      requisitionId,
      payload,
    }: {
      requisitionId: number;
      payload: RejectPayload;
    }) => {
      await api.post(`/api/approvals/${requisitionId}/reject`, payload);
    },
    onSuccess: (_data, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.list() });
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(requisitionId) });
      qc.invalidateQueries({ queryKey: approvalKeys.review(requisitionId) });
    },
  });
}
```

- [ ] **Step 2: Verify compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/api/approvals.ts
git commit -m "feat(mobile): add approvals API hooks (review, approve, reject)"
```

---

## Task 3: Notifications API module

**Files:**
- Create: `bom-mobile/src/api/notifications.ts`

- [ ] **Step 1: Implementation**

```ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import type { Notification } from "@/types/api";

export const notificationKeys = {
  all: ["notifications"] as const,
  list: () => [...notificationKeys.all, "list"] as const,
  unread: () => [...notificationKeys.all, "unread"] as const,
};

export function useNotifications() {
  return useQuery({
    queryKey: notificationKeys.list(),
    queryFn: async () => {
      const res = await api.get<Notification[]>("/api/notifications");
      return res.data;
    },
    staleTime: 10_000,
  });
}

export function useUnreadCount() {
  return useQuery({
    queryKey: notificationKeys.unread(),
    queryFn: async () => {
      const res = await api.get<{ count: number }>("/api/notifications/unread-count");
      return res.data.count;
    },
    staleTime: 10_000,
  });
}

/**
 * Client-only "mark as read" — backend has no PATCH endpoint (see plan's
 * scope deviations). Updates the TanStack Query cache in place so the UI
 * reflects the read state for the session; the server still sees it unread.
 */
export function useMarkReadLocal() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => id,
    onSuccess: (id) => {
      qc.setQueryData<Notification[]>(notificationKeys.list(), (prev) =>
        prev ? prev.map((n) => (n.id === id ? { ...n, isRead: true } : n)) : prev
      );
      qc.setQueryData<number>(notificationKeys.unread(), (prev) =>
        typeof prev === "number" && prev > 0 ? prev - 1 : prev
      );
    },
  });
}
```

- [ ] **Step 2: Verify compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/api/notifications.ts
git commit -m "feat(mobile): add notifications API hooks (list, unread-count, local mark-read)"
```

---

## Task 4: approveSchema Zod + tests (TDD)

**Files:**
- Modify: `bom-mobile/src/utils/validation.ts` (append)
- Create: `bom-mobile/__tests__/approveSchema.test.ts`

- [ ] **Step 1: Write the failing tests**

```ts
// __tests__/approveSchema.test.ts
import { approveSchema } from "@/utils/validation";

const base = {
  items: [{ requisitionItemId: 10, salesPricePerKgAed: 5 }],
  notes: "Looks good",
};

test("accepts a valid payload", () => {
  expect(approveSchema.safeParse(base).success).toBe(true);
});

test("accepts when notes is omitted", () => {
  expect(approveSchema.safeParse({ items: base.items }).success).toBe(true);
});

test("rejects when items is empty", () => {
  expect(approveSchema.safeParse({ items: [] }).success).toBe(false);
});

test("rejects when salesPricePerKgAed <= 0", () => {
  expect(
    approveSchema.safeParse({
      items: [{ requisitionItemId: 10, salesPricePerKgAed: 0 }],
    }).success
  ).toBe(false);
  expect(
    approveSchema.safeParse({
      items: [{ requisitionItemId: 10, salesPricePerKgAed: -1 }],
    }).success
  ).toBe(false);
});

test("rejects when requisitionItemId is missing", () => {
  expect(
    approveSchema.safeParse({
      items: [{ salesPricePerKgAed: 5 }],
    }).success
  ).toBe(false);
});

test("rejects when notes exceeds 2000 chars", () => {
  expect(
    approveSchema.safeParse({
      items: base.items,
      notes: "x".repeat(2001),
    }).success
  ).toBe(false);
});
```

- [ ] **Step 2: Run — expect fail**

```bash
cd bom-mobile && npx jest __tests__/approveSchema.test.ts
```

Expected: FAIL — `approveSchema` not exported.

- [ ] **Step 3: Append to `src/utils/validation.ts`**

Add below the existing `createRequisitionSchema`:

```ts
export const approveSchema = z.object({
  items: z
    .array(
      z.object({
        requisitionItemId: z.number().int().positive(),
        salesPricePerKgAed: z.number().positive("Price must be greater than zero"),
      })
    )
    .min(1, "At least one item is required"),
  notes: z.string().max(2000, "Notes must be 2000 characters or fewer").optional(),
});

export type ApproveInput = z.infer<typeof approveSchema>;
```

- [ ] **Step 4: Run — expect pass**

```bash
cd bom-mobile && npx jest __tests__/approveSchema.test.ts
```

Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add bom-mobile/src/utils/validation.ts bom-mobile/__tests__/approveSchema.test.ts
git commit -m "test(mobile): approve schema validates items and notes length"
```

---

## Task 5: ConfirmDialog component

**Files:**
- Create: `bom-mobile/src/components/ConfirmDialog.tsx`

- [ ] **Step 1: Implementation**

```tsx
import { Modal, Text, View } from "react-native";
import { Button } from "./Button";

interface Props {
  visible: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
  loading?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmDialog({
  visible,
  title,
  message,
  confirmLabel = "Confirm",
  cancelLabel = "Cancel",
  destructive,
  loading,
  onConfirm,
  onCancel,
}: Props) {
  return (
    <Modal
      transparent
      visible={visible}
      animationType="fade"
      onRequestClose={onCancel}
    >
      <View className="flex-1 bg-black/50 items-center justify-center p-6">
        <View className="bg-white rounded-lg p-5 w-full max-w-md">
          <Text className="text-lg font-bold text-slate-900 mb-2">{title}</Text>
          <Text className="text-sm text-slate-700 mb-5">{message}</Text>
          <View className="flex-row justify-end">
            <View className="mr-2">
              <Button
                title={cancelLabel}
                variant="secondary"
                onPress={onCancel}
                disabled={loading}
              />
            </View>
            <Button
              title={confirmLabel}
              variant={destructive ? "danger" : "primary"}
              onPress={onConfirm}
              loading={loading}
            />
          </View>
        </View>
      </View>
    </Modal>
  );
}
```

- [ ] **Step 2: Verify compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/components/ConfirmDialog.tsx
git commit -m "feat(mobile): add ConfirmDialog shared component"
```

---

## Task 6: RejectReasonPrompt component

**Files:**
- Create: `bom-mobile/src/components/RejectReasonPrompt.tsx`

- [ ] **Step 1: Implementation**

```tsx
import { useState } from "react";
import { KeyboardAvoidingView, Modal, Platform, Text, TextInput, View } from "react-native";
import { Button } from "./Button";

interface Props {
  visible: boolean;
  loading?: boolean;
  onConfirm: (notes: string) => void;
  onCancel: () => void;
}

export function RejectReasonPrompt({ visible, loading, onConfirm, onCancel }: Props) {
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);

  const submit = () => {
    const trimmed = notes.trim();
    if (trimmed.length < 1) {
      setError("Rejection reason is required");
      return;
    }
    if (trimmed.length > 2000) {
      setError("Must be 2000 characters or fewer");
      return;
    }
    setError(null);
    onConfirm(trimmed);
  };

  const cancel = () => {
    setNotes("");
    setError(null);
    onCancel();
  };

  return (
    <Modal transparent visible={visible} animationType="fade" onRequestClose={cancel}>
      <KeyboardAvoidingView
        behavior={Platform.OS === "ios" ? "padding" : undefined}
        className="flex-1 bg-black/50 items-center justify-center p-6"
      >
        <View className="bg-white rounded-lg p-5 w-full max-w-md">
          <Text className="text-lg font-bold text-slate-900 mb-1">Reject requisition</Text>
          <Text className="text-sm text-slate-600 mb-3">
            Please explain why. The sales person will see this.
          </Text>
          <TextInput
            value={notes}
            onChangeText={setNotes}
            placeholder="Reason..."
            multiline
            numberOfLines={4}
            placeholderTextColor="#94a3b8"
            className={`border rounded-md px-3 py-2 text-base text-slate-900 bg-white min-h-[100px] ${
              error ? "border-rose-500" : "border-slate-300"
            }`}
            textAlignVertical="top"
          />
          {error ? (
            <Text className="text-xs text-rose-600 mt-1">{error}</Text>
          ) : null}
          <View className="flex-row justify-end mt-4">
            <View className="mr-2">
              <Button title="Cancel" variant="secondary" onPress={cancel} disabled={loading} />
            </View>
            <Button
              title="Reject"
              variant="danger"
              onPress={submit}
              loading={loading}
            />
          </View>
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}
```

- [ ] **Step 2: Verify compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/components/RejectReasonPrompt.tsx
git commit -m "feat(mobile): add RejectReasonPrompt modal with notes validation"
```

---

## Task 7: ApprovalItemRow component

An editable row showing one requisition item with its cost breakdown + editable price field. Margin % is computed from cost + price (live) — displayed but not stored separately.

**Files:**
- Create: `bom-mobile/src/components/ApprovalItemRow.tsx`

- [ ] **Step 1: Implementation**

```tsx
import { Text, TextInput, View } from "react-native";
import type { MdReviewItemDetail } from "@/types/api";
import { formatMoney, formatPct } from "@/utils/numbers";

interface Props {
  item: MdReviewItemDetail;
  price: number;
  onPriceChange: (next: number) => void;
  error?: string;
}

function computeMarginPct(cost: number, price: number): number | null {
  if (!Number.isFinite(cost) || cost <= 0) return null;
  if (!Number.isFinite(price) || price <= 0) return null;
  return ((price - cost) / cost) * 100;
}

export function ApprovalItemRow({ item, price, onPriceChange, error }: Props) {
  const cost = item.cost?.totalCostPerKg ?? 0;
  const margin = computeMarginPct(cost, price);

  return (
    <View className="bg-white border border-slate-200 rounded-md p-3 mb-2">
      <Text className="text-sm font-semibold text-slate-900 mb-1" numberOfLines={2}>
        {item.itemDescription}
      </Text>
      <View className="flex-row justify-between mb-2">
        <Text className="text-xs text-slate-500">Qty: {item.expectedQty}</Text>
        <Text className="text-xs text-slate-500">Cost/kg: {formatMoney(cost)}</Text>
      </View>

      <Text className="text-sm text-slate-700 mb-1">Sales price per kg (AED)</Text>
      <TextInput
        keyboardType="decimal-pad"
        value={price > 0 ? String(price) : ""}
        onChangeText={(t) => {
          const n = Number(t);
          onPriceChange(Number.isFinite(n) ? n : 0);
        }}
        placeholder="0.0000"
        placeholderTextColor="#94a3b8"
        className={`border rounded-md px-3 py-2 text-base text-slate-900 bg-white ${
          error ? "border-rose-500" : "border-slate-300"
        }`}
      />
      {error ? <Text className="text-xs text-rose-600 mt-1">{error}</Text> : null}

      <View className="flex-row justify-between mt-2">
        <Text className="text-xs text-slate-500">Margin: {formatPct(margin)}</Text>
        <Text className="text-xs text-slate-500">
          Revenue: {formatMoney(price * item.expectedQty)}
        </Text>
      </View>
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
git add bom-mobile/src/components/ApprovalItemRow.tsx
git commit -m "feat(mobile): add ApprovalItemRow editable per-item approval card"
```

---

## Task 8: MD pending-approvals list screen

Replace the placeholder `(md)/index.tsx` with a real list filtered by backend to `status=MdReview`.

**Files:**
- Modify: `bom-mobile/app/(md)/index.tsx`

- [ ] **Step 1: Implementation**

```tsx
import { FlatList, RefreshControl, Text, View } from "react-native";
import { useRouter } from "expo-router";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/client";
import { requisitionKeys } from "@/api/requisitions";
import { RequisitionCard } from "@/components/RequisitionCard";
import { EmptyState } from "@/components/EmptyState";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";
import type { RequisitionListItem } from "@/types/api";

function useMdPending() {
  return useQuery({
    queryKey: [...requisitionKeys.list(), "mdReview"],
    queryFn: async () => {
      const res = await api.get<RequisitionListItem[]>("/api/requisitions", {
        params: { status: "MdReview" },
      });
      return res.data;
    },
  });
}

export default function MdPendingApprovals() {
  const router = useRouter();
  const q = useMdPending();

  if (q.isPending) return <LoadingView />;

  return (
    <View className="flex-1 bg-slate-50">
      {q.isError ? (
        <View className="p-4">
          <ErrorBanner
            message={q.error instanceof Error ? q.error.message : "Failed to load pending approvals"}
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
          <RequisitionCard
            item={item}
            onPress={(id) => router.push(`/(md)/${id}`)}
          />
        )}
        ListEmptyComponent={
          !q.isError ? (
            <EmptyState
              title="Nothing pending"
              hint="You're all caught up."
            />
          ) : null
        }
      />
    </View>
  );
}
```

- [ ] **Step 2: Verify compile**

```bash
cd bom-mobile && rm -f .expo/types/router.d.ts && npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/app/\(md\)/index.tsx
git commit -m "feat(mobile): replace placeholder with MD pending-approvals list"
```

---

## Task 9: MD approval detail screen

**Files:**
- Create: `bom-mobile/app/(md)/[id].tsx`

- [ ] **Step 1: Implementation**

```tsx
import { useEffect, useMemo, useState } from "react";
import { KeyboardAvoidingView, Platform, ScrollView, Text, View } from "react-native";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useMdReview, useApproveRequisition, useRejectRequisition } from "@/api/approvals";
import { ApprovalItemRow } from "@/components/ApprovalItemRow";
import { Button } from "@/components/Button";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { RejectReasonPrompt } from "@/components/RejectReasonPrompt";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";
import { formatMoney } from "@/utils/numbers";
import { approveSchema } from "@/utils/validation";

export default function MdApprovalDetail() {
  const params = useLocalSearchParams<{ id: string }>();
  const id = Number(params.id);
  const router = useRouter();
  const q = useMdReview(id);
  const approveMut = useApproveRequisition();
  const rejectMut = useRejectRequisition();

  const [prices, setPrices] = useState<Record<number, number>>({});
  const [itemErrors, setItemErrors] = useState<Record<number, string>>({});
  const [topError, setTopError] = useState<string | null>(null);
  const [approveOpen, setApproveOpen] = useState(false);
  const [rejectOpen, setRejectOpen] = useState(false);

  // Initialize price state from backend once data arrives
  useEffect(() => {
    if (!q.data) return;
    setPrices((prev) => {
      if (Object.keys(prev).length > 0) return prev;
      const seed: Record<number, number> = {};
      for (const it of q.data.items) seed[it.requisitionItemId] = 0;
      return seed;
    });
  }, [q.data]);

  const grandTotal = useMemo(() => {
    if (!q.data) return 0;
    return q.data.items.reduce((sum, it) => {
      const p = prices[it.requisitionItemId] ?? 0;
      return sum + p * it.expectedQty;
    }, 0);
  }, [q.data, prices]);

  if (q.isPending) return <LoadingView />;
  if (q.isError || !q.data) {
    return (
      <View className="flex-1 p-4 bg-slate-50">
        <ErrorBanner
          message={q.error instanceof Error ? q.error.message : "Failed to load review"}
          onRetry={() => q.refetch()}
        />
      </View>
    );
  }

  const r = q.data;

  const onApprove = async () => {
    setTopError(null);
    setItemErrors({});

    const payload = {
      items: r.items.map((it) => ({
        requisitionItemId: it.requisitionItemId,
        salesPricePerKgAed: prices[it.requisitionItemId] ?? 0,
      })),
    };

    const parsed = approveSchema.safeParse(payload);
    if (!parsed.success) {
      const errMap: Record<number, string> = {};
      for (const issue of parsed.error.issues) {
        if (issue.path[0] === "items" && typeof issue.path[1] === "number") {
          const item = r.items[issue.path[1] as number];
          if (item) errMap[item.requisitionItemId] = issue.message;
        }
      }
      setItemErrors(errMap);
      setTopError("Please enter a valid price for every item.");
      setApproveOpen(false);
      return;
    }

    try {
      await approveMut.mutateAsync({ requisitionId: id, payload: parsed.data });
      setApproveOpen(false);
      router.back();
    } catch (e: unknown) {
      setApproveOpen(false);
      const msg =
        (e as { response?: { status?: number; data?: { message?: string } } }).response?.data
          ?.message ??
        (e instanceof Error ? e.message : "Approve failed");
      const status = (e as { response?: { status?: number } }).response?.status;
      if (status === 409) {
        setTopError("This requisition has changed — reloading.");
        q.refetch();
      } else {
        setTopError(msg);
      }
    }
  };

  const onReject = async (notes: string) => {
    setTopError(null);
    try {
      await rejectMut.mutateAsync({ requisitionId: id, payload: { notes } });
      setRejectOpen(false);
      router.back();
    } catch (e: unknown) {
      setRejectOpen(false);
      setTopError(
        e instanceof Error ? e.message : "Reject failed"
      );
    }
  };

  return (
    <KeyboardAvoidingView
      behavior={Platform.OS === "ios" ? "padding" : undefined}
      className="flex-1 bg-slate-50"
    >
      <ScrollView contentContainerClassName="p-4 pb-32">
        <Text className="text-2xl font-bold text-slate-900">{r.refNo}</Text>
        <Text className="text-base text-slate-700">{r.customerName}</Text>
        <Text className="text-xs text-slate-500 mb-4">
          {r.currencyCode}
          {r.exchangeRate != null ? ` · Rate ${formatMoney(r.exchangeRate)}` : ""}
        </Text>

        {topError ? (
          <ErrorBanner message={topError} onRetry={() => setTopError(null)} />
        ) : null}

        {!r.readyForReview ? (
          <View className="bg-amber-50 border border-amber-200 rounded-md p-3 mb-4">
            <Text className="text-sm text-amber-800">
              Costing is still in progress for one or more items. Approval is disabled.
            </Text>
          </View>
        ) : null}

        <Text className="text-base font-semibold text-slate-900 mb-2">Items</Text>
        {r.items.map((it) => (
          <ApprovalItemRow
            key={it.requisitionItemId}
            item={it}
            price={prices[it.requisitionItemId] ?? 0}
            onPriceChange={(p) =>
              setPrices((prev) => ({ ...prev, [it.requisitionItemId]: p }))
            }
            error={itemErrors[it.requisitionItemId]}
          />
        ))}
      </ScrollView>

      <View className="border-t border-slate-200 bg-white p-3">
        <View className="flex-row justify-between mb-3">
          <Text className="text-sm text-slate-600">Total revenue</Text>
          <Text className="text-base font-bold text-slate-900">{formatMoney(grandTotal)}</Text>
        </View>
        <View className="flex-row">
          <View className="flex-1 mr-2">
            <Button
              title="Reject"
              variant="danger"
              onPress={() => setRejectOpen(true)}
              disabled={approveMut.isPending || rejectMut.isPending}
            />
          </View>
          <View className="flex-1">
            <Button
              title="Approve"
              onPress={() => setApproveOpen(true)}
              disabled={!r.readyForReview || approveMut.isPending || rejectMut.isPending}
            />
          </View>
        </View>
      </View>

      <ConfirmDialog
        visible={approveOpen}
        title="Approve and send quotation?"
        message="The quotation PDF will be emailed to the customer."
        confirmLabel="Approve"
        loading={approveMut.isPending}
        onCancel={() => setApproveOpen(false)}
        onConfirm={onApprove}
      />

      <RejectReasonPrompt
        visible={rejectOpen}
        loading={rejectMut.isPending}
        onCancel={() => setRejectOpen(false)}
        onConfirm={onReject}
      />
    </KeyboardAvoidingView>
  );
}
```

- [ ] **Step 2: Verify compile**

```bash
cd bom-mobile && rm -f .expo/types/router.d.ts && npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/app/\(md\)/\[id\].tsx
git commit -m "feat(mobile): MD approval detail screen with per-item prices and approve/reject"
```

---

## Task 10: SignalR provider + root layout integration

**Files:**
- Create: `bom-mobile/src/signalr/SignalRProvider.tsx`
- Modify: `bom-mobile/app/_layout.tsx`

- [ ] **Step 1: Implement `SignalRProvider`**

```tsx
// src/signalr/SignalRProvider.tsx
import { useEffect, useRef, type ReactNode } from "react";
import * as signalR from "@microsoft/signalr";
import Constants from "expo-constants";
import { useQueryClient } from "@tanstack/react-query";
import { useAuth } from "@/auth/AuthContext";
import { getAccess } from "@/auth/secureStore";
import { requisitionKeys } from "@/api/requisitions";
import { notificationKeys } from "@/api/notifications";
import { approvalKeys } from "@/api/approvals";
import type { Notification } from "@/types/api";

const baseURL =
  (Constants.expoConfig?.extra?.apiBaseUrl as string) ?? "http://localhost:7300";

export function SignalRProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const qc = useQueryClient();
  const connRef = useRef<signalR.HubConnection | null>(null);

  useEffect(() => {
    if (!user) {
      // logged out — tear down
      const c = connRef.current;
      if (c) {
        c.stop().catch(() => undefined);
        connRef.current = null;
      }
      return;
    }

    let cancelled = false;

    (async () => {
      const token = await getAccess();
      if (!token || cancelled) return;

      const connection = new signalR.HubConnectionBuilder()
        .withUrl(`${baseURL}/hubs/notifications?access_token=${token}`)
        .withAutomaticReconnect()
        .build();

      connection.on("ReceiveNotification", (n: Notification) => {
        qc.setQueryData<Notification[]>(notificationKeys.list(), (prev) =>
          prev ? [n, ...prev] : [n]
        );
        qc.setQueryData<number>(notificationKeys.unread(), (prev) =>
          typeof prev === "number" ? prev + 1 : 1
        );
        // Opportunistic invalidations based on the notification's referenceType
        if (n.referenceType === "QuotationRequest") {
          qc.invalidateQueries({ queryKey: requisitionKeys.list() });
          qc.invalidateQueries({ queryKey: requisitionKeys.detail(n.referenceId) });
          qc.invalidateQueries({ queryKey: approvalKeys.review(n.referenceId) });
        }
      });

      try {
        await connection.start();
        if (cancelled) {
          await connection.stop();
          return;
        }
        connRef.current = connection;
      } catch {
        // SignalR library retries via withAutomaticReconnect; ignore transient start failures.
      }
    })();

    return () => {
      cancelled = true;
      const c = connRef.current;
      if (c) {
        c.stop().catch(() => undefined);
        connRef.current = null;
      }
    };
  }, [user, qc]);

  return <>{children}</>;
}
```

- [ ] **Step 2: Mount in `app/_layout.tsx`**

Modify the existing root layout — wrap `Stack` with `SignalRProvider` (inside `AuthProvider` so `useAuth` resolves):

```tsx
// app/_layout.tsx  (final form)
import "../global.css";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Stack } from "expo-router";
import { AuthProvider } from "@/auth/AuthContext";
import { SignalRProvider } from "@/signalr/SignalRProvider";
import { StatusBar } from "expo-status-bar";

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, staleTime: 10_000 } },
});

export default function RootLayout() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <SignalRProvider>
          <StatusBar style="dark" />
          <Stack screenOptions={{ headerShown: false }} />
        </SignalRProvider>
      </AuthProvider>
    </QueryClientProvider>
  );
}
```

- [ ] **Step 3: Verify compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/src/signalr/SignalRProvider.tsx bom-mobile/app/_layout.tsx
git commit -m "feat(mobile): add SignalRProvider and mount it in root layout"
```

---

## Task 11: Notifications screen + bell header button

**Files:**
- Create: `bom-mobile/app/notifications.tsx`
- Create: `bom-mobile/src/components/NotificationBell.tsx`
- Modify: `bom-mobile/app/(sales)/_layout.tsx`
- Modify: `bom-mobile/app/(md)/_layout.tsx`

- [ ] **Step 1: Create `NotificationBell`**

```tsx
// src/components/NotificationBell.tsx
import { Pressable, Text, View } from "react-native";
import { useRouter } from "expo-router";
import { useUnreadCount } from "@/api/notifications";

export function NotificationBell() {
  const router = useRouter();
  const q = useUnreadCount();
  const count = q.data ?? 0;

  return (
    <Pressable onPress={() => router.push("/notifications")} className="pr-3">
      <View className="relative py-1">
        <Text className="text-brand-600 text-base font-semibold">🔔</Text>
        {count > 0 ? (
          <View className="absolute -top-1 -right-2 bg-rose-600 rounded-full min-w-[18px] h-[18px] items-center justify-center px-1">
            <Text className="text-white text-[10px] font-bold">
              {count > 99 ? "99+" : count}
            </Text>
          </View>
        ) : null}
      </View>
    </Pressable>
  );
}
```

- [ ] **Step 2: Create the notifications screen**

```tsx
// app/notifications.tsx
import { FlatList, Pressable, RefreshControl, Text, View } from "react-native";
import { useRouter } from "expo-router";
import { useMarkReadLocal, useNotifications } from "@/api/notifications";
import { EmptyState } from "@/components/EmptyState";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";
import { formatShortDate } from "@/utils/dates";
import { useAuth } from "@/auth/AuthContext";
import type { Notification } from "@/types/api";

function pathForNotification(
  n: Notification,
  role: string
): string | null {
  if (n.referenceType !== "QuotationRequest") return null;
  if (role === "ManagingDirector") return `/(md)/${n.referenceId}`;
  if (role === "SalesPerson") return `/(sales)/${n.referenceId}`;
  return null;
}

export default function Notifications() {
  const router = useRouter();
  const q = useNotifications();
  const markRead = useMarkReadLocal();
  const { user } = useAuth();

  if (q.isPending) return <LoadingView />;

  return (
    <View className="flex-1 bg-slate-50">
      {q.isError ? (
        <View className="p-4">
          <ErrorBanner
            message={
              q.error instanceof Error ? q.error.message : "Failed to load notifications"
            }
            onRetry={() => q.refetch()}
          />
        </View>
      ) : null}

      <FlatList
        data={q.data ?? []}
        keyExtractor={(n) => String(n.id)}
        contentContainerClassName="p-3"
        refreshControl={
          <RefreshControl refreshing={q.isRefetching} onRefresh={() => q.refetch()} />
        }
        renderItem={({ item }) => (
          <Pressable
            onPress={() => {
              markRead.mutate(item.id);
              const path = user ? pathForNotification(item, user.role) : null;
              if (path) router.push(path);
            }}
            className={`border rounded-md p-3 mb-2 active:bg-slate-50 ${
              item.isRead ? "bg-white border-slate-200" : "bg-brand-50 border-brand-100"
            }`}
          >
            <Text
              className={`text-sm text-slate-900 ${item.isRead ? "" : "font-semibold"}`}
            >
              {item.message}
            </Text>
            <Text className="text-xs text-slate-500 mt-1">
              {formatShortDate(item.createdAt)}
            </Text>
          </Pressable>
        )}
        ListEmptyComponent={
          !q.isError ? (
            <EmptyState
              title="No notifications yet"
              hint="You'll see approval requests and status changes here."
            />
          ) : null
        }
      />
    </View>
  );
}
```

- [ ] **Step 3: Add `NotificationBell` to both role layouts**

Update `app/(sales)/_layout.tsx` — replace the existing `HeaderLogout` rendering with a two-button group:

```tsx
// app/(sales)/_layout.tsx  (final)
import { Pressable, Text, View } from "react-native";
import { Redirect, Stack, useRouter } from "expo-router";
import { useAuth, useRoleGuard } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";
import { NotificationBell } from "@/components/NotificationBell";

function HeaderRight() {
  const { logout } = useAuth();
  const router = useRouter();
  const onLogout = async () => {
    await logout();
    router.replace("/login");
  };
  return (
    <View className="flex-row items-center">
      <NotificationBell />
      <Pressable onPress={onLogout} className="pr-3">
        <Text className="text-brand-600 text-base font-semibold">Log out</Text>
      </Pressable>
    </View>
  );
}

export default function SalesLayout() {
  const { status } = useRoleGuard(["SalesPerson"]);
  if (status === "loading") return <LoadingView />;
  if (status !== "allowed") return <Redirect href="/login" />;
  return (
    <Stack
      screenOptions={{
        headerShown: true,
        headerRight: () => <HeaderRight />,
      }}
    />
  );
}
```

Apply the identical change to `app/(md)/_layout.tsx` — substitute `"SalesPerson"` with `"ManagingDirector"`.

- [ ] **Step 4: Verify compile**

```bash
cd bom-mobile && rm -f .expo/types/router.d.ts && npx tsc --noEmit
```

- [ ] **Step 5: Commit**

```bash
git add \
  bom-mobile/app/notifications.tsx \
  bom-mobile/src/components/NotificationBell.tsx \
  bom-mobile/app/\(sales\)/_layout.tsx \
  bom-mobile/app/\(md\)/_layout.tsx
git commit -m "feat(mobile): notifications screen + bell badge in role headers"
```

---

## Task 12: README update + milestone verify

**Files:**
- Modify: `bom-mobile/README.md`

- [ ] **Step 1: Append to the "What's implemented" section**

Replace the existing section with:

```markdown
## What's implemented

**Plan 1 (merged):** login, role-based routing, secure-store tokens, axios 401 refresh, profile + logout, placeholder home screens.

**Plan 2 (merged):** SalesPerson flow — requisitions list, create multi-item requisition, detail view with per-item stage indicators and PDF download. Detail is read-only.

**Plan 3a (this work):** MD pending-approvals list, MD approval detail with per-item prices + approve/reject flow, notifications screen with bell badge, SignalR foreground live updates.

**Plan 3b (next):** EAS Build profiles for Android, Play Store Internal Testing distribution, deployment docs. iOS is a later phase.
```

- [ ] **Step 2: Run full test suite**

```bash
cd bom-mobile && npx jest
```

Expected: all tests PASS. New totals:
- Plan 1: 8 (client + loginSchema + roleGuard)
- Plan 2: 9 (dates + createRequisitionSchema)
- Plan 3a: 11 (5 numbers + 6 approveSchema)

Grand total: **28 tests, 7 suites**.

- [ ] **Step 3: Type-check**

```bash
cd bom-mobile && rm -f .expo/types/router.d.ts && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 4: Android smoke test (manual)**

Same dev setup as before: backend on `http://0.0.0.0:7300`, Expo `--lan`, Expo Go on Android phone.

**Log in as MD** and verify:
- [ ] Bell icon shows in the header. If there are unread notifications, the red badge shows the count.
- [ ] Tapping the bell opens the notifications screen. Unread items are highlighted; tap on one navigates to `/(md)/{id}` (the referenced requisition, if it was a QuotationRequest) and marks it read locally.
- [ ] The pending approvals list (home) shows requisitions with `status=MdReview`. Empty state if none.
- [ ] Tapping a pending card navigates to the approval detail. Items render with cost/kg; the price field is empty and editable; margin + revenue update live as you type.
- [ ] Pressing **Approve** with any price missing or zero shows per-field errors and a top banner.
- [ ] Pressing **Approve** with valid prices → confirmation dialog → on confirm, the mutation succeeds and you return to the list. The approved requisition is gone from pending.
- [ ] Pressing **Reject** opens the reason prompt; empty submit is rejected inline. Valid reject returns to the list.
- [ ] While the app is open, trigger a new notification from the web (e.g. submit a requisition to MD as a sales user) — the bell badge count increases and the notifications list updates without a refresh.

**Log out, log in as SalesPerson** and verify:
- [ ] The bell also appears in the sales header.
- [ ] Notifications received while the sales user was logged in appear in the list.
- [ ] Tapping an MD approval/rejection notification (referenceType=QuotationRequest) navigates to `/(sales)/{id}`.

- [ ] **Step 5: Final commit**

```bash
git add bom-mobile/README.md
git commit -m "docs(mobile): note Plan 3a scope (MD features + SignalR + notifications)"
```

---

## Milestone

At the end of this plan:
- `(md)/index.tsx` shows pending approvals filtered by `status=MdReview`.
- `(md)/[id].tsx` lets MD set per-item prices and approve or reject.
- SignalR `ReceiveNotification` events drive live unread-badge + notification list updates.
- A shared `notifications.tsx` screen lists events and routes to the right requisition detail on tap.
- `ConfirmDialog` and `RejectReasonPrompt` are reusable building blocks for future destructive confirmations.
- `npx jest` passes 28/28, `npx tsc --noEmit` is clean, `npx expo-doctor` stays 17/17.
- Android Expo Go smoke test covers the full MD approval loop + live-update path + cross-role notification routing.

Next plan (`2026-04-22-mobile-android-deploy.md`) will cover EAS Build configuration for Android (development / preview / production profiles), Play Store Internal Testing setup, keystore management via EAS, and deployment documentation.
