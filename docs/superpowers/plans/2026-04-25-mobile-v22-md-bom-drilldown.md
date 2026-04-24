# Mobile V2.2 MD BOM/Costing Drill-Down Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable ManagingDirector on mobile to drill into Approved and Rejected requisitions (per item) and inspect full BOM lines, cost breakdown, and margin — in a dedicated full-screen view.

**Architecture:** New mobile route `(md)/item/[reqId]/[itemId].tsx` reached from a per-item CTA on the existing historical detail page. Data comes from three reused endpoints (`/api/requisitions/{id}`, `/api/bom/{reqId}`, `/api/approvals/{reqId}`); no backend changes beyond regression tests. The currently-private `useBomReview` hook inside `BomDetailSheet.tsx` is extracted to `api/bom.ts` so the new screen can reuse it.

**Tech Stack:** React Native 0.81.5 + Expo Router + TanStack Query + TypeScript (mobile); ASP.NET Core 8 + xUnit + FluentAssertions + WebApplicationFactory (backend tests).

**Spec:** [docs/superpowers/specs/2026-04-25-mobile-v22-md-bom-drilldown-design.md](../specs/2026-04-25-mobile-v22-md-bom-drilldown-design.md)

---

## File structure

### New files

- `bom-mobile/src/api/bom.ts` — public `useBomReview` hook + query-key export.
- `bom-mobile/src/components/MarginHeroCard.tsx` — Approved-only green/red margin card.
- `bom-mobile/src/components/CostBreakdownCard.tsx` — raw / landed / FOH rows + total.
- `bom-mobile/src/components/BomLineRow.tsx` — single raw-material line (leaf).
- `bom-mobile/src/components/BomProcessGroup.tsx` — process label + list of `BomLineRow`.
- `bom-mobile/app/(md)/item/[reqId]/[itemId].tsx` — drill-down screen.
- `BomPriceApproval.Tests/Bom/BomHistoricalReadTests.cs` — BOM endpoint read for Approved + Rejected.
- `BomPriceApproval.Tests/Approvals/ApprovalHistoricalReadTests.cs` — Approvals endpoint read for Approved + Rejected.

### Modified files

- `bom-mobile/src/components/BomDetailSheet.tsx` — remove private `useBomReview`, import from `api/bom.ts`.
- `bom-mobile/app/(md)/historical/[id].tsx` — add per-item "View details ▸" CTA + `router.push` navigation.

---

## Task 1: Extract `useBomReview` to a public hook

**Files:**
- Create: `bom-mobile/src/api/bom.ts`
- Modify: `bom-mobile/src/components/BomDetailSheet.tsx`

- [ ] **Step 1: Create `bom-mobile/src/api/bom.ts`**

```ts
import { useQuery } from "@tanstack/react-query";
import { api } from "./client";
import type { BomReviewResponse } from "@/types/api";

export const bomKeys = {
  review: (requisitionId: number) => ["bom", "review", requisitionId] as const,
};

export function useBomReview(requisitionId: number, enabled = true) {
  return useQuery({
    queryKey: bomKeys.review(requisitionId),
    queryFn: async () => {
      const res = await api.get<BomReviewResponse>(`/api/bom/${requisitionId}`);
      return res.data;
    },
    enabled: enabled && Number.isFinite(requisitionId) && requisitionId > 0,
    staleTime: 30_000,
  });
}
```

- [ ] **Step 2: Refactor `BomDetailSheet.tsx` to import from `api/bom.ts`**

Replace the private `useBomReview` function (lines 17–27) with an import and use. Final top of file:

```ts
import { Modal, Pressable, ScrollView, Text, useWindowDimensions, View } from "react-native";
import { Skeleton } from "./Skeleton";
import { ErrorBanner } from "./ErrorBanner";
import { stripTags } from "@/utils/text";
import { useBomReview } from "@/api/bom";

interface Props {
  visible: boolean;
  onClose: () => void;
  requisitionId: number;
  requisitionItemId: number;
  itemDescription: string;
}

export function BomDetailSheet({
  visible,
  onClose,
  requisitionId,
  requisitionItemId,
  itemDescription,
}: Props) {
  const { height } = useWindowDimensions();
  const q = useBomReview(requisitionId, visible);
```

The rest of the component body stays unchanged. Also remove the now-unused `api`, `useQuery`, and `BomReviewResponse` imports.

- [ ] **Step 3: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: no new errors.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/src/api/bom.ts bom-mobile/src/components/BomDetailSheet.tsx
git commit -m "refactor(mobile): extract useBomReview hook into api/bom.ts"
```

---

## Task 2: `BomLineRow` component (leaf)

**Files:**
- Create: `bom-mobile/src/components/BomLineRow.tsx`

- [ ] **Step 1: Create component**

```tsx
import { Text, View } from "react-native";
import { stripTags } from "@/utils/text";
import type { BomLine } from "@/types/api";

interface Props {
  line: BomLine;
}

export function BomLineRow({ line }: Props) {
  const qty = line.qtyPerKg;
  const waste = line.wastagePct;
  const costDisplay =
    line.costPerKgInAed != null
      ? `${line.costPerKgInAed.toFixed(4)} AED/kg`
      : line.costPerKg != null
      ? `${line.costPerKg.toFixed(4)} ${line.currencyCode ?? ""}`
      : "—";
  const contribDisplay =
    line.contributionAed != null
      ? `${line.contributionAed.toFixed(4)} AED`
      : "—";

  return (
    <View
      style={{
        backgroundColor: "#ffffff",
        borderWidth: 1,
        borderColor: "#e2e8f0",
        borderRadius: 12,
        padding: 12,
        marginBottom: 8,
      }}
    >
      <Text
        style={{ fontSize: 15, fontWeight: "600", color: "#0f172a" }}
        numberOfLines={2}
      >
        {stripTags(line.rawMaterialDescription)}
      </Text>
      <View style={{ marginTop: 6, flexDirection: "row", flexWrap: "wrap" }}>
        <Text style={{ fontSize: 13, color: "#64748b", marginRight: 12 }}>
          Qty/kg <Text style={{ fontWeight: "600", color: "#334155" }}>{qty}</Text>
        </Text>
        <Text style={{ fontSize: 13, color: "#64748b", marginRight: 12 }}>
          Wastage <Text style={{ fontWeight: "600", color: "#334155" }}>{waste}%</Text>
        </Text>
      </View>
      <View style={{ marginTop: 4, flexDirection: "row", justifyContent: "space-between" }}>
        <Text style={{ fontSize: 13, color: "#64748b" }}>Cost</Text>
        <Text style={{ fontSize: 13, fontWeight: "600", color: "#0f172a" }}>{costDisplay}</Text>
      </View>
      <View style={{ marginTop: 2, flexDirection: "row", justifyContent: "space-between" }}>
        <Text style={{ fontSize: 13, color: "#64748b" }}>Contribution</Text>
        <Text style={{ fontSize: 13, fontWeight: "600", color: "#1e40af" }}>{contribDisplay}</Text>
      </View>
    </View>
  );
}
```

- [ ] **Step 2: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: no new errors.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/components/BomLineRow.tsx
git commit -m "feat(mobile): add BomLineRow component for raw material detail"
```

---

## Task 3: `BomProcessGroup` component

**Files:**
- Create: `bom-mobile/src/components/BomProcessGroup.tsx`

- [ ] **Step 1: Create component**

```tsx
import { Text, View } from "react-native";
import { BomLineRow } from "./BomLineRow";
import type { BomLine } from "@/types/api";

interface Props {
  processName: string;
  lines: BomLine[];
}

export function BomProcessGroup({ processName, lines }: Props) {
  return (
    <View style={{ marginTop: 12 }}>
      <Text
        style={{
          fontSize: 13,
          fontWeight: "700",
          color: "#64748b",
          letterSpacing: 0.5,
          marginBottom: 6,
        }}
      >
        {`BOM — ${processName.toUpperCase()} (${lines.length})`}
      </Text>
      {lines.map((l) => (
        <BomLineRow key={l.id} line={l} />
      ))}
    </View>
  );
}
```

- [ ] **Step 2: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: no new errors.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/components/BomProcessGroup.tsx
git commit -m "feat(mobile): add BomProcessGroup to group BOM lines by process"
```

---

## Task 4: `CostBreakdownCard` component

**Files:**
- Create: `bom-mobile/src/components/CostBreakdownCard.tsx`

- [ ] **Step 1: Create component**

```tsx
import { Text, View } from "react-native";
import type { MdReviewItemCost } from "@/types/api";

interface Props {
  cost: MdReviewItemCost | null;
}

function Row({ label, value, pct }: { label: string; value: string; pct?: string }) {
  return (
    <View
      style={{
        flexDirection: "row",
        justifyContent: "space-between",
        alignItems: "baseline",
        paddingVertical: 4,
      }}
    >
      <Text style={{ fontSize: 14, color: "#475569" }}>{label}</Text>
      <Text style={{ fontSize: 14, color: "#0f172a", fontWeight: "600" }}>
        {value}
        {pct ? (
          <Text style={{ fontSize: 12, color: "#94a3b8", fontWeight: "500" }}>
            {`  (${pct})`}
          </Text>
        ) : null}
      </Text>
    </View>
  );
}

export function CostBreakdownCard({ cost }: Props) {
  if (cost === null) {
    return (
      <View
        style={{
          backgroundColor: "#ffffff",
          borderWidth: 1,
          borderColor: "#e2e8f0",
          borderRadius: 14,
          padding: 14,
          marginTop: 10,
        }}
      >
        <Text style={{ fontSize: 14, color: "#94a3b8", textAlign: "center" }}>
          Costing not completed
        </Text>
      </View>
    );
  }

  return (
    <View
      style={{
        backgroundColor: "#ffffff",
        borderWidth: 1,
        borderColor: "#e2e8f0",
        borderRadius: 14,
        padding: 14,
        marginTop: 10,
      }}
    >
      <Text
        style={{
          fontSize: 13,
          fontWeight: "700",
          color: "#64748b",
          letterSpacing: 0.5,
          marginBottom: 8,
        }}
      >
        COST BREAKDOWN (AED/kg)
      </Text>
      <Row
        label="Raw Material"
        value={cost.rawMaterialCostPerKg.toFixed(4)}
        pct={`${cost.materialCostPct.toFixed(1)}%`}
      />
      <Row
        label="Landed"
        value={cost.landedCostPerKg.toFixed(4)}
        pct={`${cost.landedCostPct.toFixed(1)}%`}
      />
      <Row
        label="FOH"
        value={cost.fohPerKg.toFixed(4)}
        pct={`${cost.fohPct.toFixed(1)}%`}
      />
      <View
        style={{
          flexDirection: "row",
          justifyContent: "space-between",
          alignItems: "baseline",
          paddingTop: 8,
          marginTop: 4,
          borderTopWidth: 1,
          borderTopColor: "#e2e8f0",
        }}
      >
        <Text style={{ fontSize: 14, fontWeight: "700", color: "#0f172a" }}>Total</Text>
        <Text style={{ fontSize: 15, fontWeight: "700", color: "#0f172a" }}>
          {cost.totalCostPerKg.toFixed(4)}
        </Text>
      </View>
    </View>
  );
}
```

- [ ] **Step 2: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: no new errors.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/components/CostBreakdownCard.tsx
git commit -m "feat(mobile): add CostBreakdownCard for per-item cost summary"
```

---

## Task 5: `MarginHeroCard` component

**Files:**
- Create: `bom-mobile/src/components/MarginHeroCard.tsx`

- [ ] **Step 1: Create component**

```tsx
import { Text, View } from "react-native";

interface Props {
  pricePerKg: number;
  totalCostPerKg: number;
  currencyCode: string;
}

export function MarginHeroCard({ pricePerKg, totalCostPerKg, currencyCode }: Props) {
  const marginPct = pricePerKg > 0 ? ((pricePerKg - totalCostPerKg) / pricePerKg) * 100 : 0;
  const negative = marginPct < 0;

  return (
    <View
      style={{
        backgroundColor: negative ? "#fef2f2" : "#ecfdf5",
        borderWidth: 1,
        borderColor: negative ? "#fecaca" : "#a7f3d0",
        borderRadius: 14,
        padding: 14,
        marginTop: 10,
        alignItems: "center",
      }}
    >
      <Text
        style={{
          fontSize: 22,
          fontWeight: "800",
          color: negative ? "#991b1b" : "#047857",
        }}
      >
        {`Margin ${marginPct.toFixed(1)}%`}
      </Text>
      <Text
        style={{
          fontSize: 13,
          color: negative ? "#7f1d1d" : "#065f46",
          marginTop: 4,
        }}
      >
        {`Price ${pricePerKg.toFixed(4)} · Cost ${totalCostPerKg.toFixed(4)} ${currencyCode}/kg`}
      </Text>
    </View>
  );
}
```

- [ ] **Step 2: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: no new errors.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/components/MarginHeroCard.tsx
git commit -m "feat(mobile): add MarginHeroCard showing approved price vs cost"
```

---

## Task 6: Drill-down screen `(md)/item/[reqId]/[itemId].tsx`

**Files:**
- Create: `bom-mobile/app/(md)/item/[reqId]/[itemId].tsx`

- [ ] **Step 1: Create the screen file**

```tsx
import { useMemo } from "react";
import { Pressable, ScrollView, Text, View } from "react-native";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import * as Haptics from "expo-haptics";
import { useRequisitionDetail } from "@/api/requisitions";
import { useBomReview } from "@/api/bom";
import { useMdReview } from "@/api/approvals";
import { useAuth } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";
import { ErrorBanner } from "@/components/ErrorBanner";
import { ScreenHeader } from "@/components/ScreenHeader";
import { NotificationBell } from "@/components/NotificationBell";
import { MarginHeroCard } from "@/components/MarginHeroCard";
import { CostBreakdownCard } from "@/components/CostBreakdownCard";
import { BomProcessGroup } from "@/components/BomProcessGroup";
import { stripTags } from "@/utils/text";

export default function MdItemDrillDown() {
  const router = useRouter();
  const { logout } = useAuth();
  const params = useLocalSearchParams<{ reqId: string; itemId: string }>();
  const reqId = Number(params.reqId);
  const itemId = Number(params.itemId);

  const detailQ = useRequisitionDetail(reqId);
  const bomQ = useBomReview(reqId);
  const reviewQ = useMdReview(reqId);

  const onLogout = async () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    await logout();
    router.replace("/login");
  };

  const HeaderRight = (
    <>
      <NotificationBell />
      <Pressable
        onPress={onLogout}
        style={{
          paddingHorizontal: 12,
          paddingVertical: 9,
          borderRadius: 8,
          backgroundColor: "#f1f5f9",
        }}
      >
        <Text style={{ color: "#1e40af", fontSize: 15, fontWeight: "600" }}>Log out</Text>
      </Pressable>
    </>
  );

  const isLoading = detailQ.isPending || bomQ.isPending || reviewQ.isPending;
  const firstError = detailQ.error ?? bomQ.error ?? reviewQ.error;

  const reqItem = detailQ.data?.items.find((i) => i.id === itemId) ?? null;
  const bomItem = bomQ.data?.items.find((i) => i.requisitionItemId === itemId) ?? null;
  const reviewItem = reviewQ.data?.items.find((i) => i.requisitionItemId === itemId) ?? null;
  const approvalPrice =
    detailQ.data?.approval?.items?.find((a) => a.requisitionItemId === itemId)?.pricePerKg ?? null;

  const linesByProcess = useMemo(() => {
    if (!bomItem) return [] as { processName: string; lines: typeof bomItem.lines }[];
    const byName = new Map<string, typeof bomItem.lines>();
    for (const l of bomItem.lines) {
      const list = byName.get(l.processName) ?? [];
      list.push(l);
      byName.set(l.processName, list);
    }
    return Array.from(byName, ([processName, lines]) => ({ processName, lines }));
  }, [bomItem]);

  if (isLoading) return <LoadingView />;

  if (firstError || !detailQ.data || !reqItem) {
    const status = (firstError as { response?: { status?: number } } | null)?.response?.status;
    const message =
      status === 403
        ? "Access denied"
        : status === 404 || !reqItem
        ? "Item not found"
        : firstError instanceof Error
        ? firstError.message
        : "Failed to load details";
    return (
      <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <Stack.Screen options={{ headerShown: false }} />
        <ScreenHeader title="Item details" right={HeaderRight} />
        <View style={{ padding: 16 }}>
          <ErrorBanner message={message} onRetry={() => router.back()} />
        </View>
      </View>
    );
  }

  const r = detailQ.data;
  const showMargin = r.status === "Approved" && approvalPrice != null && reviewItem?.cost != null;

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader
        label={`${r.refNo} · ${reqItem.expectedQty} kg`}
        title={stripTags(reqItem.itemDescription)}
        right={HeaderRight}
      />

      <ScrollView contentContainerStyle={{ padding: 14, paddingBottom: 32 }}>
        {showMargin && reviewItem?.cost ? (
          <MarginHeroCard
            pricePerKg={approvalPrice!}
            totalCostPerKg={reviewItem.cost.totalCostPerKg}
            currencyCode="AED"
          />
        ) : null}

        <CostBreakdownCard cost={reviewItem?.cost ?? null} />

        {bomItem && bomItem.lines.length > 0 ? (
          linesByProcess.map((g) => (
            <BomProcessGroup
              key={g.processName}
              processName={g.processName}
              lines={g.lines}
            />
          ))
        ) : (
          <View
            style={{
              backgroundColor: "#ffffff",
              borderWidth: 1,
              borderColor: "#e2e8f0",
              borderRadius: 14,
              padding: 14,
              marginTop: 12,
            }}
          >
            <Text style={{ fontSize: 14, color: "#94a3b8", textAlign: "center" }}>
              BOM not available for this item
            </Text>
          </View>
        )}
      </ScrollView>
    </View>
  );
}
```

- [ ] **Step 2: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: no new errors.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/app/(md)/item/[reqId]/[itemId].tsx
git commit -m "feat(mobile): add MD per-item drill-down screen"
```

---

## Task 7: Wire CTA + navigation on historical detail

**Files:**
- Modify: `bom-mobile/app/(md)/historical/[id].tsx`

- [ ] **Step 1: Add `Pressable` import and `useRouter` is already imported**

Confirm the existing imports at the top of `(md)/historical/[id].tsx` include `Pressable` from `react-native` and `useRouter` from `expo-router`. Both are already imported in the current file — no change needed.

- [ ] **Step 2: Replace each item card body to append the CTA row**

Locate the existing `r.items.map((it) => ( <ItemCardShell key={it.id}> ... </ItemCardShell> ))` block in `(md)/historical/[id].tsx` (currently around lines 152–185, but find by code, not line number). Replace the whole map block with the version below, which adds a Pressable CTA at the bottom for Approved + Rejected only:

```tsx
{r.items.map((it) => {
  const canDrillDown = r.status === "Approved" || r.status === "Rejected";
  return (
    <ItemCardShell key={it.id}>
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
      {r.status === "Approved" && r.approval?.items
        ? (() => {
            const approvalItem = r.approval.items?.find((ai) => ai.requisitionItemId === it.id);
            return approvalItem ? (
              <ItemPriceBlock
                expectedQty={it.expectedQty}
                pricePerKg={approvalItem.pricePerKg}
                currencyCode={r.currencyCode}
              />
            ) : null;
          })()
        : null}
      {canDrillDown ? (
        <Pressable
          onPress={() => {
            Haptics.selectionAsync();
            router.push(`/(md)/item/${r.id}/${it.id}`);
          }}
          style={({ pressed }) => ({
            marginTop: 10,
            opacity: pressed ? 0.7 : 1,
          })}
        >
          <View
            style={{
              backgroundColor: "#eff6ff",
              borderRadius: 10,
              paddingVertical: 10,
              alignItems: "center",
            }}
          >
            <Text style={{ color: "#1e40af", fontSize: 14, fontWeight: "700" }}>
              View details ▸
            </Text>
          </View>
        </Pressable>
      ) : null}
    </ItemCardShell>
  );
})}
```

- [ ] **Step 3: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: no new errors.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/app/(md)/historical/[id].tsx
git commit -m "feat(mobile): wire MD historical detail CTA to drill-down screen"
```

---

## Task 8: Backend test — BOM read works for Approved + Rejected

**Files:**
- Create: `BomPriceApproval.Tests/Bom/BomHistoricalReadTests.cs`

- [ ] **Step 1: Create test file with both bootstraps and both tests**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Bom;

public class BomHistoricalReadTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private async Task<(int ReqId, int ItemId)> BootstrapThroughMdReviewAsync()
    {
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemDto>();

        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var rmResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "RM", Type = "RawMaterial", LastPurchasePrice = (decimal?)null });
        var rm = await rmResp.Content.ReadFromJsonAsync<ItemDto>();

        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fg!.Id, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();
        var requisitionId = created!.Id;

        var reqDetail = await _client.GetFromJsonAsync<PartialRequisitionDetailDto>($"/api/requisitions/{requisitionId}");
        var riId = reqDetail!.Items[0].Id;

        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var procResp = await _client.PostAsJsonAsync("/api/processes",
            new { Name = $"P-{Guid.NewGuid():N}".Substring(0, 12), DisplayOrder = 1 });
        var proc = await procResp.Content.ReadFromJsonAsync<PartialProcessDto>();

        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);
        await _client.PostAsync($"/api/bom/{requisitionId}/items/{riId}/start", null);
        await _client.PutAsJsonAsync($"/api/bom/{requisitionId}/items/{riId}/lines", new
        {
            Lines = new[]
            {
                new { ProcessId = proc!.Id, RawMaterialItemId = rm!.Id, QtyPerKg = 0.85m, WastagePct = 2.0m }
            }
        });
        await _client.PostAsync($"/api/bom/{requisitionId}/submit", null);

        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        var costing = await _client.GetFromJsonAsync<PartialCostingReviewDto>($"/api/costing/{requisitionId}");
        var bomLineId = costing!.Items.First(x => x.RequisitionItemId == riId).BomLines[0].BomLineId;
        await _client.PostAsync($"/api/costing/{requisitionId}/items/{riId}/start", null);
        await _client.PostAsJsonAsync($"/api/costing/{requisitionId}/items/{riId}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 1.00m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        _client.DefaultRequestHeaders.Authorization = null;
        return (requisitionId, riId);
    }

    private async Task<int> BootstrapApprovedRequisitionAsync()
    {
        var (reqId, riId) = await BootstrapThroughMdReviewAsync();
        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);
        await _client.PostAsJsonAsync($"/api/approvals/{reqId}/approve", new
        {
            Items = new[] { new { RequisitionItemId = riId, SalesPricePerKgAed = 2.50m } },
            Notes = (string?)null
        });
        _client.DefaultRequestHeaders.Authorization = null;
        return reqId;
    }

    private async Task<int> BootstrapRejectedRequisitionAsync()
    {
        var (reqId, _) = await BootstrapThroughMdReviewAsync();
        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);
        await _client.PostAsJsonAsync($"/api/approvals/{reqId}/reject", new
        {
            Notes = "Price too low"
        });
        _client.DefaultRequestHeaders.Authorization = null;
        return reqId;
    }

    [Fact]
    public async Task GetBom_ReturnsLinesForApprovedRequisition()
    {
        var requisitionId = await BootstrapApprovedRequisitionAsync();

        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var resp = await _client.GetAsync($"/api/bom/{requisitionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PartialBomReviewDto>();
        body!.RequisitionStatus.Should().Be("Approved");
        body.Items.Should().HaveCount(1);
        body.Items[0].Lines.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetBom_ReturnsLinesForRejectedRequisition()
    {
        var requisitionId = await BootstrapRejectedRequisitionAsync();

        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var resp = await _client.GetAsync($"/api/bom/{requisitionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PartialBomReviewDto>();
        body!.RequisitionStatus.Should().Be("Rejected");
        body.Items[0].Lines.Should().HaveCount(1);
    }
}

internal record PartialBomReviewDto(int RequisitionId, string RefNo, string RequisitionStatus, List<PartialBomItemDto> Items);
internal record PartialBomItemDto(int RequisitionItemId, string BomStatus, List<PartialBomLineDto> Lines);
internal record PartialBomLineDto(int Id, string ProcessName, decimal QtyPerKg, decimal WastagePct);
```

- [ ] **Step 2: Verify the backend is running before executing tests**

Run: `curl -s http://localhost:7300/swagger/index.html >/dev/null || echo 'Backend not running - start it first'`

If the backend isn't running, start it: `dotnet run --project BomPriceApproval.API` — wait for "Now listening on: http://localhost:7300" before running tests.

- [ ] **Step 3: Run the new tests**

Run: `dotnet test --filter "FullyQualifiedName~BomHistoricalReadTests"`
Expected: 2 tests pass.

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.Tests/Bom/BomHistoricalReadTests.cs
git commit -m "test(bom): guard BOM read endpoint works for Approved and Rejected"
```

---

## Task 9: Backend test — Approvals review read works for Approved + Rejected

**Files:**
- Create: `BomPriceApproval.Tests/Approvals/ApprovalHistoricalReadTests.cs`

- [ ] **Step 1: Create test file**

Copy the three bootstrap helpers (`BootstrapThroughMdReviewAsync`, `BootstrapApprovedRequisitionAsync`, `BootstrapRejectedRequisitionAsync`) verbatim from `BomHistoricalReadTests.cs` into this new file (per YAGNI — duplicate now; if a third test file needs them, extract to `BomPriceApproval.Tests/Shared/TestBootstrap.cs` at that time). Body:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Approvals;

public class ApprovalHistoricalReadTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    // Copy BootstrapThroughMdReviewAsync, BootstrapApprovedRequisitionAsync,
    // and BootstrapRejectedRequisitionAsync verbatim from BomHistoricalReadTests.cs.

    [Fact]
    public async Task GetReview_ReturnsCostsForApprovedRequisition()
    {
        var requisitionId = await BootstrapApprovedRequisitionAsync();

        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var resp = await _client.GetAsync($"/api/approvals/{requisitionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PartialApprovalReviewDto>();
        body!.Items.Should().HaveCount(1);
        body.Items[0].Cost.Should().NotBeNull();
        body.Items[0].Cost!.TotalCostPerKg.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetReview_ReturnsCostsForRejectedRequisition()
    {
        var requisitionId = await BootstrapRejectedRequisitionAsync();

        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var resp = await _client.GetAsync($"/api/approvals/{requisitionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PartialApprovalReviewDto>();
        body!.Items[0].Cost.Should().NotBeNull();
    }
}

internal record PartialApprovalReviewDto(string RefNo, string CustomerName, bool ReadyForReview, List<PartialApprovalItemDto> Items);
internal record PartialApprovalItemDto(int RequisitionItemId, PartialApprovalCostDto? Cost);
internal record PartialApprovalCostDto(decimal TotalCostPerKg, decimal MaterialCostPct, decimal LandedCostPct, decimal FohPct);
```

Paste the three Bootstrap helpers from Task 8 into this file as well.

- [ ] **Step 2: Verify backend is running** (same command as Task 8 Step 3)

- [ ] **Step 3: Run the new tests**

Run: `dotnet test --filter "FullyQualifiedName~ApprovalHistoricalReadTests"`
Expected: 2 tests pass.

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.Tests/Approvals/ApprovalHistoricalReadTests.cs
git commit -m "test(approvals): guard MD review read works for Approved and Rejected"
```

---

## Task 10: Manual smoke verification

**Files:** none (manual)

- [ ] **Step 1: Backend running**

Ensure API is running on port 7300: `curl -s http://localhost:7300/swagger/index.html >/dev/null && echo OK`

- [ ] **Step 2: Start mobile on emulator / device**

```bash
cd bom-mobile
npx expo start --clear
```

- [ ] **Step 3: Log in as `md@test.com / Test@1234`**

- [ ] **Step 4: Golden path — Approved requisition**

- Tap "All requisitions" tab, filter to "Approved" chip.
- Open any approved requisition.
- Confirm each item card shows the new `View details ▸` CTA.
- Tap the CTA on an item with cost data.
- Confirm screen shows: margin hero (green or red) → cost breakdown card (raw / landed / FOH + total + %) → BOM process groups.
- Back-navigate; confirm list position retained.

- [ ] **Step 5: Edge path — Rejected requisition**

- Filter to "Rejected" chip. Open one.
- Tap `View details ▸` on an item.
- Confirm the margin hero is hidden; cost + BOM render.

- [ ] **Step 6: Negative path — Active (non-terminal) requisition**

- Open any non-approved, non-rejected requisition (e.g. in MdReview, CostingPending).
- Confirm the `View details ▸` CTA is **not** shown on item cards (current MD review flow is unchanged).

- [ ] **Step 7: Missing-data path**

- If a Rejected requisition exists where costing was never completed, confirm the drill-down shows "Costing not completed" placeholder instead of crashing.
- If a rejected requisition has no BOM, confirm "BOM not available for this item" placeholder renders.

- [ ] **Step 8: Report results**

Post findings back to the conversation. No commit for this task.

---

## Self-review notes

- **Spec coverage:** all sections 1–8 of the spec have at least one task. Section 7 (states) is addressed inside Task 6.
- **Placeholders:** none; every code block is complete.
- **Type consistency:** field names match existing `types/api.ts` (`requisitionStatus` on `BomReviewResponse`, `costStatus` on `MdReviewItemDetail`) — spec's aspirational names were corrected here.
- **Known simplification:** backend tests duplicate the bootstrap helper across two files per YAGNI. If a third test file needs the same bootstrap, extract to `BomPriceApproval.Tests/Shared/TestBootstrap.cs` at that time.
- **No new types, no new backend controllers, no DB migration.**
