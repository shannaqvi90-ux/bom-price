# Mobile Sales Stack Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** [docs/superpowers/specs/2026-04-24-mobile-sales-redesign-design.md](../specs/2026-04-24-mobile-sales-redesign-design.md)

**Goal:** Reskin `(sales)/_layout.tsx`, `(sales)/index.tsx`, `(sales)/new.tsx`, and `(sales)/[id].tsx` to match the corporate+playful design language already live on the MD stack — ScreenHeader pattern, shared SectionCard, factory-blue tokens, bigger typography, labeled items list, FAB + inline submit preserved.

**Architecture:** Three-commit reskin on a fresh branch `feature/mobile-sales-redesign` off master. Existing shared components (`ScreenHeader`, `RequisitionCard`, `StatusPill`, `ItemStageBadge`, `Button`, `Input`, `SearchablePicker`, `CustomerQuickCreateSheet`, `ErrorBanner`, `EmptyState`, `LoadingView`) unchanged. Two new files created: `SalesHeaderRight.tsx` (extracted from current `_layout.tsx`) and shared `SectionCard.tsx`. No API changes, no new tests (pure visual reskin per spec §8).

**Tech Stack:** React Native (Expo SDK 51), NativeWind (Tailwind), Moti, expo-haptics, React Hook Form + Zod, TanStack Query, react-native-safe-area-context, expo-router.

**Branch:** Create `feature/mobile-sales-redesign` off master before starting.

---

## Prerequisites (before Task 1.1)

- [ ] On master at `ec0458f` (or later). Working tree clean (`git status` empty).
- [ ] `cd bom-mobile && npx tsc --noEmit` returns 0 errors
- [ ] `cd bom-mobile && npx jest` returns 33 passing tests
- [ ] Create branch: `git checkout -b feature/mobile-sales-redesign`

---

## File structure (locked upfront)

**Create:**
- `bom-mobile/src/components/SalesHeaderRight.tsx` — NotificationBell + Logout button, factory-blue styled
- `bom-mobile/src/components/SectionCard.tsx` — shared labeled-card wrapper used on new + detail

**Modify:**
- `bom-mobile/app/(sales)/_layout.tsx` — `headerShown: false`, drop inline HeaderRight (now extracted)
- `bom-mobile/app/(sales)/index.tsx` — wire ScreenHeader, FAB gets inline `#1e40af` bg
- `bom-mobile/app/(sales)/new.tsx` — ScreenHeader, SectionCards for customer+currency, labeled items list, tokenized tokens
- `bom-mobile/app/(sales)/[id].tsx` — ScreenHeader with StatusPill in `right`, SectionCard for customer, labeled items list, tokens

**No unit tests added** (spec §8). Acceptance gate: `tsc --noEmit` clean + `jest` 33/33 green + manual phone smoke.

---

## Phase 1 — Layout + List

Commit: `refactor(mobile): apply ScreenHeader + design tokens to (sales)/index.tsx + _layout.tsx`

### Task 1.1: Extract `SalesHeaderRight`

**Files:**
- Create: `bom-mobile/src/components/SalesHeaderRight.tsx`

- [ ] **Step 1: Create file** with this content:

```tsx
import { Pressable, Text, View } from "react-native";
import { useRouter } from "expo-router";
import { useAuth } from "@/auth/AuthContext";
import { NotificationBell } from "@/components/NotificationBell";

export function SalesHeaderRight() {
  const { logout } = useAuth();
  const router = useRouter();

  const onLogout = async () => {
    await logout();
    router.replace("/login");
  };

  return (
    <View style={{ flexDirection: "row", alignItems: "center", gap: 4 }}>
      <NotificationBell />
      <Pressable onPress={onLogout} hitSlop={8} style={{ paddingHorizontal: 6, paddingVertical: 2 }}>
        <Text style={{ color: "#1e40af", fontSize: 15, fontWeight: "600" }}>Log out</Text>
      </Pressable>
    </View>
  );
}
```

- [ ] **Step 2: Verify TypeScript**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

### Task 1.2: Update `_layout.tsx`

**Files:**
- Modify: `bom-mobile/app/(sales)/_layout.tsx`

- [ ] **Step 1: Replace the file** with this content:

```tsx
import { Redirect, Stack } from "expo-router";
import { useRoleGuard } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";

export default function SalesLayout() {
  const { status } = useRoleGuard(["SalesPerson"]);
  if (status === "loading") return <LoadingView />;
  if (status !== "allowed") return <Redirect href="/login" />;
  return (
    <Stack
      screenOptions={{
        headerShown: false,
      }}
    />
  );
}
```

(Removes the inline `HeaderRight` function since it's extracted, and hides the stack header so each screen's `ScreenHeader` is the visual top.)

- [ ] **Step 2: Verify TypeScript**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

### Task 1.3: Redesign `(sales)/index.tsx`

**Files:**
- Modify: `bom-mobile/app/(sales)/index.tsx`

- [ ] **Step 1: Replace the file** with this content:

```tsx
import { FlatList, Pressable, RefreshControl, Text, View } from "react-native";
import { useRouter } from "expo-router";
import * as Haptics from "expo-haptics";
import { useRequisitionsList } from "@/api/requisitions";
import { RequisitionCard } from "@/components/RequisitionCard";
import { ScreenHeader } from "@/components/ScreenHeader";
import { SalesHeaderRight } from "@/components/SalesHeaderRight";
import { EmptyState } from "@/components/EmptyState";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";

export default function SalesRequisitionsList() {
  const router = useRouter();
  const q = useRequisitionsList();

  if (q.isPending) return <LoadingView />;

  const count = q.data?.length ?? 0;

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <ScreenHeader
        label="SALES"
        title="Requisitions"
        count={count}
        right={<SalesHeaderRight />}
      />

      {q.isError ? (
        <View style={{ paddingHorizontal: 16, paddingBottom: 8 }}>
          <ErrorBanner
            message={q.error instanceof Error ? q.error.message : "Failed to load requisitions"}
            onRetry={() => q.refetch()}
          />
        </View>
      ) : null}

      <FlatList
        data={q.data ?? []}
        keyExtractor={(r) => String(r.id)}
        contentContainerStyle={{ padding: 12, paddingBottom: 96 }}
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
        onPress={async () => {
          await Haptics.selectionAsync();
          router.push("/(sales)/new");
        }}
        style={({ pressed }) => ({
          position: "absolute",
          bottom: 24,
          right: 24,
          width: 56,
          height: 56,
          borderRadius: 28,
          backgroundColor: "#1e40af",
          alignItems: "center",
          justifyContent: "center",
          shadowColor: "#1e40af",
          shadowOffset: { width: 0, height: 4 },
          shadowOpacity: pressed ? 0.25 : 0.35,
          shadowRadius: 10,
          elevation: 5,
          opacity: pressed ? 0.9 : 1,
        })}
      >
        <Text style={{ color: "white", fontSize: 30, fontWeight: "700", lineHeight: 32 }}>+</Text>
      </Pressable>
    </View>
  );
}
```

(Replaces `bg-slate-50` NativeWind with inline style for consistency with MD stack; wraps FAB with haptic + shadow; uses `ScreenHeader` for the top; keeps all data/error/loading behaviors.)

- [ ] **Step 2: TypeScript check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 3: Jest**

Run: `cd bom-mobile && npx jest --silent`
Expected: 33 passed.

### Task 1.4: Commit Phase 1

- [ ] **Step 1: Show diff**

Run: `git diff --stat`

- [ ] **Step 2: Stage files**

```
git add bom-mobile/src/components/SalesHeaderRight.tsx \
        bom-mobile/app/\(sales\)/_layout.tsx \
        bom-mobile/app/\(sales\)/index.tsx
```

- [ ] **Step 3: Propose commit message, get user approval, commit**

Proposed: `refactor(mobile): apply ScreenHeader + design tokens to (sales)/index.tsx + _layout.tsx`

After user "haan":

```
git commit -m "refactor(mobile): apply ScreenHeader + design tokens to (sales)/index.tsx + _layout.tsx"
```

- [ ] **Step 4: Manual phone smoke on list screen**

Ask user to:
1. Reload Expo Go
2. Navigate to the sales list (login as `ali@test.com` / `Test@1234`)
3. Confirm: ScreenHeader shows "SALES / Requisitions" with count badge, NotificationBell + factory-blue "Log out" on the right, FAB at bottom-right still works to open New form

---

## Phase 2 — New requisition form

Commit: `refactor(mobile): regroup (sales)/new.tsx into SectionCards + new tokens`

### Task 2.1: Create shared `SectionCard`

**Files:**
- Create: `bom-mobile/src/components/SectionCard.tsx`

- [ ] **Step 1: Create file** with this content:

```tsx
import { type ReactNode } from "react";
import { Text, View } from "react-native";

interface Props {
  title: string;
  children: ReactNode;
}

export function SectionCard({ title, children }: Props) {
  return (
    <View
      style={{
        backgroundColor: "#ffffff",
        borderWidth: 1,
        borderColor: "#e2e8f0",
        borderRadius: 14,
        padding: 14,
        marginBottom: 12,
      }}
    >
      <Text
        style={{
          fontSize: 13,
          fontWeight: "700",
          color: "#64748b",
          marginBottom: 10,
          letterSpacing: 0.3,
        }}
      >
        {title.toUpperCase()}
      </Text>
      {children}
    </View>
  );
}
```

- [ ] **Step 2: Verify TypeScript**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

### Task 2.2: Redesign `(sales)/new.tsx`

**Files:**
- Modify: `bom-mobile/app/(sales)/new.tsx`

- [ ] **Step 1: Replace the file** with this content:

```tsx
import { useState } from "react";
import {
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  Text,
  View,
} from "react-native";
import { useRouter } from "expo-router";
import { useForm, Controller, useFieldArray } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import * as Haptics from "expo-haptics";
import { Button } from "@/components/Button";
import { Input } from "@/components/Input";
import { SearchablePicker } from "@/components/SearchablePicker";
import { ErrorBanner } from "@/components/ErrorBanner";
import { CustomerQuickCreateSheet } from "@/components/CustomerQuickCreateSheet";
import { ScreenHeader } from "@/components/ScreenHeader";
import { SalesHeaderRight } from "@/components/SalesHeaderRight";
import { SectionCard } from "@/components/SectionCard";
import { useCustomers, useExchangeRates, useItems } from "@/api/lookups";
import { useCreateRequisition } from "@/api/requisitions";
import {
  createRequisitionSchema,
  type CreateRequisitionInput,
} from "@/utils/validation";

export default function NewRequisition() {
  const router = useRouter();
  const customersQ = useCustomers();
  const itemsQ = useItems();
  const ratesQ = useExchangeRates();
  const createMut = useCreateRequisition();
  const [topError, setTopError] = useState<string | null>(null);
  const [addSheetOpen, setAddSheetOpen] = useState(false);

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

  const { fields, append, remove } = useFieldArray({ control, name: "items" });

  const currencyOptions = [
    { code: "AED", label: "AED — UAE Dirham" },
    ...(ratesQ.data ?? []).map((r) => ({
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
      style={{ flex: 1, backgroundColor: "#f8fafc" }}
    >
      <ScreenHeader title="New requisition" right={<SalesHeaderRight />} />

      <ScrollView
        contentContainerStyle={{ padding: 14, paddingBottom: 32 }}
        keyboardShouldPersistTaps="handled"
      >
        {topError ? (
          <ErrorBanner message={topError} onRetry={() => setTopError(null)} />
        ) : null}

        <SectionCard title="Customer">
          <Controller
            control={control}
            name="customerId"
            render={({ field }) => (
              <View>
                <SearchablePicker
                  label=""
                  placeholder="Select customer..."
                  value={field.value || null}
                  onChange={field.onChange}
                  loading={customersQ.isPending}
                  options={(customersQ.data ?? []).map((c) => ({
                    id: c.id,
                    label: c.name,
                    sublabel: c.code,
                  }))}
                  error={errors.customerId?.message}
                />
                <Pressable
                  onPress={async () => {
                    await Haptics.selectionAsync();
                    setAddSheetOpen(true);
                  }}
                  style={{ alignSelf: "flex-start", marginTop: 4 }}
                >
                  <Text style={{ color: "#1e40af", fontSize: 14, fontWeight: "600" }}>
                    + New customer
                  </Text>
                </Pressable>
                <CustomerQuickCreateSheet
                  open={addSheetOpen}
                  onClose={() => setAddSheetOpen(false)}
                  onCreated={(c) => {
                    field.onChange(c.id);
                  }}
                />
              </View>
            )}
          />
        </SectionCard>

        <SectionCard title="Currency">
          <Controller
            control={control}
            name="currencyCode"
            render={({ field }) => (
              <View>
                <View style={{ flexDirection: "row", flexWrap: "wrap", marginRight: -8 }}>
                  {currencyOptions.map((opt) => {
                    const selected = field.value === opt.code;
                    return (
                      <Pressable
                        key={opt.code}
                        onPress={async () => {
                          await Haptics.selectionAsync();
                          field.onChange(opt.code);
                        }}
                        style={{
                          paddingHorizontal: 14,
                          paddingVertical: 8,
                          marginRight: 8,
                          marginBottom: 8,
                          borderRadius: 8,
                          borderWidth: 1,
                          backgroundColor: selected ? "#1e40af" : "#ffffff",
                          borderColor: selected ? "#1e40af" : "#cbd5e1",
                        }}
                      >
                        <Text
                          style={{
                            color: selected ? "#ffffff" : "#334155",
                            fontSize: 14,
                            fontWeight: "600",
                          }}
                        >
                          {opt.code}
                        </Text>
                      </Pressable>
                    );
                  })}
                </View>
                {errors.currencyCode ? (
                  <Text style={{ color: "#be123c", fontSize: 12, marginTop: 4 }}>
                    {errors.currencyCode.message}
                  </Text>
                ) : null}
              </View>
            )}
          />
        </SectionCard>

        <Text
          style={{
            fontSize: 13,
            fontWeight: "700",
            color: "#64748b",
            marginBottom: 8,
            marginTop: 4,
            letterSpacing: 0.3,
          }}
        >
          {`ITEMS (${fields.length})`}
        </Text>

        {fields.map((f, idx) => (
          <View
            key={f.id}
            style={{
              backgroundColor: "#ffffff",
              borderWidth: 1,
              borderColor: "#e2e8f0",
              borderRadius: 14,
              padding: 14,
              marginBottom: 10,
            }}
          >
            <View
              style={{
                flexDirection: "row",
                alignItems: "center",
                justifyContent: "space-between",
                marginBottom: 8,
              }}
            >
              <Text style={{ fontSize: 14, fontWeight: "600", color: "#334155" }}>
                Item {idx + 1}
              </Text>
              {fields.length > 1 ? (
                <Pressable
                  onPress={async () => {
                    await Haptics.selectionAsync();
                    remove(idx);
                  }}
                  hitSlop={6}
                >
                  <Text style={{ color: "#be123c", fontSize: 14, fontWeight: "600" }}>
                    Remove
                  </Text>
                </Pressable>
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
                  options={(itemsQ.data ?? []).map((it) => ({
                    id: it.id,
                    label: it.description,
                    sublabel: it.code,
                  }))}
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

        <Pressable
          onPress={async () => {
            await Haptics.selectionAsync();
            append({ itemId: 0, expectedQty: 0 });
          }}
          style={{ alignSelf: "flex-start", marginBottom: 4 }}
        >
          <Text style={{ color: "#1e40af", fontSize: 14, fontWeight: "600" }}>
            + Add another item
          </Text>
        </Pressable>

        {errors.items?.root ? (
          <Text style={{ color: "#be123c", fontSize: 12, marginBottom: 8 }}>
            {errors.items.root.message}
          </Text>
        ) : null}

        <View style={{ marginTop: 20 }}>
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

**Key changes:**
- Removed `bg-slate-50` NativeWind in favor of inline style
- `ScreenHeader` at top
- Customer + Currency each wrapped in a `SectionCard`
- Items rendered as a **labeled list** (NOT nested in a SectionCard) — `ITEMS (N)` label followed by individual white cards per item
- All `brand-600` → `#1e40af` inline
- Haptic feedback added to every interactive press (chips, add item, remove item, + new customer)
- `SearchablePicker` called with `label=""` inside the Customer SectionCard so the section title carries the label role

- [ ] **Step 2: TypeScript check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 3: Jest**

Run: `cd bom-mobile && npx jest --silent`
Expected: 33 passed.

### Task 2.3: Commit Phase 2

- [ ] **Step 1: Show diff**

Run: `git diff --stat`

- [ ] **Step 2: Stage files**

```
git add bom-mobile/src/components/SectionCard.tsx \
        bom-mobile/app/\(sales\)/new.tsx
```

- [ ] **Step 3: Propose message + commit after user "haan"**

Proposed: `refactor(mobile): regroup (sales)/new.tsx into SectionCards + new tokens`

```
git commit -m "refactor(mobile): regroup (sales)/new.tsx into SectionCards + new tokens"
```

- [ ] **Step 4: Manual phone smoke on new-req screen**

Ask user to:
1. Reload Expo Go
2. Tap FAB on sales list → New requisition screen
3. Confirm: ScreenHeader at top · Customer SectionCard with label "CUSTOMER" + picker + "+ New customer" link · Currency SectionCard with factory-blue active chip · "ITEMS (N)" label · individual item cards with Remove link · "+ Add another item" link in factory-blue · Create requisition button at end
4. Tap "+ New customer" — confirm polished bottom-sheet still opens (should be unchanged from the `63b1402` commit)
5. Fill the form end-to-end and submit — confirm requisition is created and user navigates to detail

---

## Phase 3 — Detail screen

Commit: `refactor(mobile): apply ScreenHeader + SectionCards to (sales)/[id].tsx`

### Task 3.1: Redesign `(sales)/[id].tsx`

**Files:**
- Modify: `bom-mobile/app/(sales)/[id].tsx`

- [ ] **Step 1: Replace the file** with this content:

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
import { ScreenHeader } from "@/components/ScreenHeader";
import { SalesHeaderRight } from "@/components/SalesHeaderRight";
import { SectionCard } from "@/components/SectionCard";
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
      <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <ScreenHeader title="Requisition" right={<SalesHeaderRight />} />
        <View style={{ padding: 16 }}>
          <ErrorBanner
            message={
              q.error instanceof Error ? q.error.message : "Failed to load requisition"
            }
            onRetry={() => q.refetch()}
          />
        </View>
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
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <ScreenHeader
        label="QUOTATION"
        title={r.refNo}
        right={
          <View style={{ flexDirection: "row", alignItems: "center", gap: 6 }}>
            <StatusPill status={r.status as Parameters<typeof StatusPill>[0]["status"]} />
            <SalesHeaderRight />
          </View>
        }
      />

      <ScrollView contentContainerStyle={{ padding: 14, paddingBottom: 32 }}>
        {isRejected && r.approval?.notes ? (
          <View
            style={{
              backgroundColor: "#fef2f2",
              borderWidth: 1,
              borderColor: "#fecaca",
              borderRadius: 14,
              padding: 14,
              marginBottom: 12,
            }}
          >
            <Text
              style={{
                fontSize: 13,
                fontWeight: "700",
                color: "#991b1b",
                letterSpacing: 0.3,
                marginBottom: 6,
              }}
            >
              REJECTION REASON
            </Text>
            <Text style={{ fontSize: 15, color: "#7f1d1d" }}>{r.approval.notes}</Text>
          </View>
        ) : null}

        <SectionCard title="Customer">
          <Text style={{ fontSize: 16, fontWeight: "600", color: "#0f172a" }}>
            {r.customerName}
          </Text>
          <Text style={{ fontSize: 13, color: "#64748b", marginTop: 4 }}>
            {r.branchName} · {r.currencyCode} · Created {formatShortDate(r.createdAt)}
          </Text>
        </SectionCard>

        <Text
          style={{
            fontSize: 13,
            fontWeight: "700",
            color: "#64748b",
            marginBottom: 8,
            marginTop: 4,
            letterSpacing: 0.3,
          }}
        >
          {`ITEMS (${r.items.length})`}
        </Text>

        {r.items.map((it) => (
          <View
            key={it.id}
            style={{
              backgroundColor: "#ffffff",
              borderWidth: 1,
              borderColor: "#e2e8f0",
              borderRadius: 14,
              padding: 14,
              marginBottom: 10,
            }}
          >
            <View
              style={{
                flexDirection: "row",
                alignItems: "flex-start",
                justifyContent: "space-between",
              }}
            >
              <Text
                style={{ flex: 1, paddingRight: 12, fontSize: 15, fontWeight: "600", color: "#0f172a" }}
                numberOfLines={2}
              >
                {it.itemDescription}
              </Text>
              <Text style={{ fontSize: 15, color: "#334155", fontWeight: "600" }}>
                {it.expectedQty}
              </Text>
            </View>
            <View style={{ marginTop: 6 }}>
              <ItemStageBadge status={r.status} />
            </View>
          </View>
        ))}

        {isApproved ? (
          <View style={{ marginTop: 20 }}>
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
          <Text
            style={{
              fontSize: 12,
              color: "#64748b",
              textAlign: "center",
              marginTop: 12,
            }}
          >
            Approved on {formatShortDate(r.approval.approvedAt)}
          </Text>
        ) : null}
      </ScrollView>
    </View>
  );
}
```

**Key changes:**
- `ScreenHeader` with `label="QUOTATION"`, `title={r.refNo}`, and **both** `StatusPill` + `SalesHeaderRight` in `right` (StatusPill first for visual prominence of state)
- Rejection box restyled: rose bg + border, bolder "REJECTION REASON" label
- Customer wrapped in `SectionCard`
- Items as labeled list (NOT nested in SectionCard) — matches the new-req pattern
- Token-driven inline styles everywhere; no remaining NativeWind classes
- Error path also uses `ScreenHeader` for consistency

- [ ] **Step 2: TypeScript check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 3: Jest**

Run: `cd bom-mobile && npx jest --silent`
Expected: 33 passed.

### Task 3.2: Commit Phase 3

- [ ] **Step 1: Show diff**

Run: `git diff --stat`

- [ ] **Step 2: Stage file**

```
git add bom-mobile/app/\(sales\)/\[id\].tsx
```

- [ ] **Step 3: Propose message + commit after user "haan"**

Proposed: `refactor(mobile): apply ScreenHeader + SectionCards to (sales)/[id].tsx`

```
git commit -m "refactor(mobile): apply ScreenHeader + SectionCards to (sales)/[id].tsx"
```

- [ ] **Step 4: Manual phone smoke on detail screen**

Ask user to:
1. Reload Expo Go
2. Open an existing Sales requisition from the list
3. Confirm:
   - ScreenHeader with "QUOTATION" label + `REQ-xxxx` title + StatusPill + SalesHeaderRight
   - Customer SectionCard with name + branch/currency/date meta line
   - "ITEMS (N)" label + individual item cards with ItemStageBadge
4. Open a Rejected requisition if one exists — confirm rose rejection box
5. Open an Approved requisition — confirm Download PDF button + "Approved on X" footer

---

## Post-phase — final verification

### Task F.1: Full mobile test pass

- [ ] Run: `cd bom-mobile && npx tsc --noEmit` → 0 errors
- [ ] Run: `cd bom-mobile && npx jest` → 33 passed

### Task F.2: Full phone smoke walkthrough

Ask the user to exercise the complete Sales flow on Expo Go:

- [ ] Login as Sales (`ali@test.com` / `Test@1234`)
- [ ] List screen smoke
- [ ] Tap FAB → new screen smoke
- [ ] "+ New customer" bottom-sheet (already polished in `63b1402`) — confirm no regression
- [ ] Submit a new requisition with an inline-created customer
- [ ] Open the newly created requisition → detail screen smoke

### Task F.3: Merge decision

- [ ] Ask user: fast-forward merge `feature/mobile-sales-redesign` → master? Then defer push per the existing security-hardening gate.

---

## Self-review checklist (plan author)

**1. Spec coverage:**
- Spec §5.1 `_layout.tsx` → Task 1.2 ✅
- Spec §5.2 `index.tsx` → Task 1.3 ✅
- Spec §5.3 `new.tsx` → Task 2.2 (with labeled items list per the clarified spec §5.3) ✅
- Spec §5.4 `[id].tsx` → Task 3.1 ✅
- Spec §6 tokens → applied inline throughout all three screens ✅
- Spec §7 error handling → preserved (ErrorBanner, topError, pdfError, LoadingView all kept) ✅
- Spec §8 testing → Jest + tsc gates enforced at each task; no new unit tests added (matches spec) ✅
- Spec §9 rollout (3 commits) → 3 phases match ✅

**2. Placeholder scan:**
- No TBD / TODO / "fill in" placeholders
- Every code change shows the full file content
- Every command has an expected result
- No "similar to Task N" — each task is self-contained

**3. Type consistency:**
- `SectionCard` signature `{ title: string; children: ReactNode }` — consistent across Tasks 2.1, 2.2, 3.1
- `SalesHeaderRight` is a zero-prop component — consistent across Tasks 1.1, 1.3, 2.2, 3.1
- `ScreenHeader` API used matches its existing signature `{ label?, title, count?, right? }`
- `Haptics.selectionAsync()` used everywhere an interactive press fires (chips, FAB, add item, remove, + new customer link)
- Inline color tokens consistent: `#1e40af`, `#f8fafc`, `#ffffff`, `#e2e8f0`, `#0f172a`, `#334155`, `#64748b`, `#be123c` (rose destructive), `#fef2f2`/`#fecaca`/`#991b1b`/`#7f1d1d` (rejection box)
- No new types introduced; reuses `CreateRequisitionInput` + `RequisitionDetail` etc. from existing modules
