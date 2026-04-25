# Mobile V2.1 — Accountant Mobile Stack Implementation Plan (Phase 1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the Phase 1 MVP of the V2.1 spec — a role-guarded `(accountant)/` route group on mobile so Sara can drill into a `CostingPending`/`CostingInProgress` requisition, fill costing per item with hybrid auto-save, and submit. Phase 2 (all-list, customer change, deep-link upgrade) is a separate plan.

**Architecture:** A new mobile route group at `bom-mobile/app/(accountant)/`. The costing form is a single-scroll screen (layout A from brainstorm) reachable via drill-in from a per-item card on the requisition detail screen. State is local (`useReducer`) with hybrid auto-save (debounce 2 s + on blur + on screen exit). Backend endpoints are reused as-is — one new integration test guards the auto-transition from "last item submitted" to `MdReview`.

**Tech Stack:** React Native 0.81.5 + Expo Router + TanStack Query v5 + TypeScript (mobile); ASP.NET Core 8 + xUnit + FluentAssertions + WebApplicationFactory (backend tests).

**Spec:** [docs/superpowers/specs/2026-04-25-mobile-v21-accountant-design.md](../specs/2026-04-25-mobile-v21-accountant-design.md)

> **Note on test posture:** The spec defers mobile component tests; this plan honors that. (The project does have `jest-expo` configured under the `rn` jest project, contradicting a line in the spec. If the team later wants component tests for the new pieces, they slot in alongside the existing `__tests__/*.test.tsx` files without re-architecting.)

---

## File structure

### New files

- `BomPriceApproval.Tests/Costing/CostingLastItemTransitionTests.cs` — guards `Submit last item → MdReview`.
- `bom-mobile/src/utils/apiError.ts` — port of `bom-web/src/lib/apiError.ts`.
- `bom-mobile/__tests__/apiError.test.ts` — node-jest tests for the helper.
- `bom-mobile/src/api/costing.ts` — `useCostingReview` (query) + `useStartCostingItem`, `useSaveCostingItemDraft`, `useSubmitCostingItem` (mutations).
- `bom-mobile/src/components/StaleCostBadge.tsx` — `⚠ X days` inline tag.
- `bom-mobile/src/components/CurrencyPickerSheet.tsx` — wrapper around `SearchablePicker`.
- `bom-mobile/src/components/SaveStatusBadge.tsx` — `idle / saving / saved / error` pill.
- `bom-mobile/src/components/CostLineCard.tsx` — layout A compact card (per BOM line).
- `bom-mobile/src/components/LandedCostSection.tsx` — `Percentage / Fixed AED` segmented + input.
- `bom-mobile/src/components/FohSection.tsx` — single AED amount input.
- `bom-mobile/app/(accountant)/_layout.tsx` — Stack + role guard.
- `bom-mobile/app/(accountant)/index.tsx` — pending list (Phase 1 only).
- `bom-mobile/app/(accountant)/[id].tsx` — req detail with item cards + auto-start.
- `bom-mobile/app/(accountant)/item/[reqId]/[itemId].tsx` — costing form.

### Modified files

- `bom-mobile/src/types/api.ts` — add `LandedCostType`, `LastCostReference`, `CostingBomLine`, `CostingLineDraft`, `CostingDraft`, `CostingItemResponse`, `CostingReviewResponse`.
- `bom-mobile/app/index.tsx` — add `Accountant` role redirect.

---

## Task 1: Backend integration test for last-item transition

**Files:**
- Create: `BomPriceApproval.Tests/Costing/CostingLastItemTransitionTests.cs`

This is a regression guard. The behavior already works in production; we lock it in.

- [ ] **Step 1: Verify the test backend is available**

Run: `curl -s -o /dev/null -w "%{http_code}\n" http://localhost:7300/swagger/index.html`
Expected: `200` (start the API if not — `dotnet run --project BomPriceApproval.API`).

- [ ] **Step 2: Write the failing test file**

Use the existing `CreateRequisitionWithBomInCostingPendingAsync` helper from `CostingTests.cs` as a reference. Mirror its structure (single-item requisition through to `CostingPending`). Then submit costing and assert the requisition flips to `MdReview`.

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Costing;

public class CostingLastItemTransitionTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    [Fact]
    public async Task Costing_Submit_LastItem_TransitionsRequisitionToMdReview()
    {
        // Arrange: build a single-item requisition through to CostingPending using the
        // same flow as CostingTests. Re-use the helper from that test class verbatim
        // (copy-paste; abstraction can wait for a third caller).
        var (requisitionId, requisitionItemId) =
            await CostingTestFixture.CreateRequisitionWithBomInCostingPendingAsync(_client);

        // Act: Accountant starts costing, saves a minimal draft, and submits.
        var accountantToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accountantToken);

        await _client.PostAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/start", null);

        // Get the costing review to obtain the BOM line id we need for the draft.
        var review = await _client.GetFromJsonAsync<CostingReviewDto>($"/api/costing/{requisitionId}");
        var bomLineId = review!.Items.Single().BomLines.Single().BomLineId;

        await _client.PutAsJsonAsync(
            $"/api/costing/{requisitionId}/items/{requisitionItemId}/draft",
            new
            {
                Lines = new[] { new { BomLineId = bomLineId, CostPerKg = 1.25m, CurrencyCode = "AED" } },
                LandedCostType = "Percentage",
                LandedCostValue = 0m,
                FohAmount = 0m
            });

        var submitResp = await _client.PostAsync(
            $"/api/costing/{requisitionId}/items/{requisitionItemId}/submit", null);
        submitResp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        // Assert: any authenticated role can read the requisition and see status MdReview.
        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var reqDetail = await _client.GetFromJsonAsync<RequisitionDetailDto>($"/api/requisitions/{requisitionId}");
        reqDetail!.Status.Should().Be("MdReview");
    }

    private record LoginResponse(string AccessToken, string RefreshToken);
    private record CostingReviewDto(List<CostingItemDto> Items);
    private record CostingItemDto(int RequisitionItemId, List<BomLineDto> BomLines);
    private record BomLineDto(int BomLineId);
    private record RequisitionDetailDto(int Id, string Status);
}
```

- [ ] **Step 3: Extract or duplicate the requisition-creation helper**

The current helper `CreateRequisitionWithBomInCostingPendingAsync` lives as an instance method inside `CostingTests.cs`. The new test file needs the same flow. Duplicate it as a `static` helper on a new internal `CostingTestFixture` class colocated in the test project (one file: `BomPriceApproval.Tests/Costing/CostingTestFixture.cs`):

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BomPriceApproval.Tests.Costing;

internal static class CostingTestFixture
{
    public static async Task<(int RequisitionId, int RequisitionItemId)>
        CreateRequisitionWithBomInCostingPendingAsync(HttpClient client, string quoteCurrency = "AED")
    {
        // Copy the body of CostingTests.CreateRequisitionWithBomInCostingPendingAsync verbatim,
        // replacing `_client` with `client`. Helper is intentionally static so other test files
        // can share it without inheritance.
        // (The existing instance method in CostingTests.cs stays — it's the same logic.)
        // ... (see CostingTests.cs lines 28–79 for the body to copy)
    }

    private record ItemDto(int Id);
    private record CustomerDto(int Id);
    private record ProcessDto(int Id);
    private record CreatedRequisition(int Id);
    private record RequisitionDetailDto(int Id, List<ItemDto> Items);
    private record LoginResponse(string AccessToken);
}
```

- [ ] **Step 4: Run the test and confirm it passes**

Run: `dotnet test --filter "FullyQualifiedName~CostingLastItemTransitionTests"`
Expected: 1/1 passed (the behavior already works in production).

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.Tests/Costing/CostingLastItemTransitionTests.cs \
        BomPriceApproval.Tests/Costing/CostingTestFixture.cs
git commit -m "test(costing): guard last-item submit transitions requisition to MdReview"
```

---

## Task 2: Port `apiError` helper to mobile

**Files:**
- Create: `bom-mobile/src/utils/apiError.ts`
- Create: `bom-mobile/__tests__/apiError.test.ts`

- [ ] **Step 1: Write the failing test**

Create `bom-mobile/__tests__/apiError.test.ts`:

```ts
import { extractApiError, extractFieldErrors } from "@/utils/apiError";

describe("extractApiError", () => {
  it("returns response.data.detail when present", () => {
    const err = { response: { data: { detail: "boom" } } };
    expect(extractApiError(err)).toBe("boom");
  });

  it("returns fallback when detail missing", () => {
    expect(extractApiError(null)).toBe("Something went wrong");
    expect(extractApiError({ response: { data: {} } }, "fb")).toBe("fb");
  });
});

describe("extractFieldErrors", () => {
  it("flattens ASP.NET ValidationProblemDetails errors map", () => {
    const err = { response: { data: { errors: { "Items[0].ExpectedQty": ["Must be > 0."] } } } };
    expect(extractFieldErrors(err)).toEqual({ "items.0.expectedQty": "Must be > 0." });
  });

  it("camelCases segment heads, keeps numeric segments untouched", () => {
    const err = {
      response: {
        data: {
          errors: {
            "Lines[2].CostPerKg": ["required"],
            "LandedCostValue": ["must be >= 0"],
          },
        },
      },
    };
    expect(extractFieldErrors(err)).toEqual({
      "lines.2.costPerKg": "required",
      "landedCostValue": "must be >= 0",
    });
  });

  it("returns {} when no errors", () => {
    expect(extractFieldErrors({ response: { data: { detail: "x" } } })).toEqual({});
    expect(extractFieldErrors(null)).toEqual({});
  });
});
```

- [ ] **Step 2: Run the test and verify it fails**

Run: `cd bom-mobile && npx jest --selectProjects=node apiError`
Expected: FAIL — `Cannot find module '@/utils/apiError'`.

- [ ] **Step 3: Implement the helper**

Create `bom-mobile/src/utils/apiError.ts`. This is a verbatim port of `bom-web/src/lib/apiError.ts`:

```ts
export function extractApiError(err: unknown, fallback = "Something went wrong"): string {
  if (err && typeof err === "object" && "response" in err) {
    const resp = (err as { response?: { data?: { detail?: unknown } } }).response;
    const detail = resp?.data?.detail;
    if (typeof detail === "string" && detail.length > 0) return detail;
  }
  return fallback;
}

export function extractFieldErrors(err: unknown): Record<string, string> {
  if (!err || typeof err !== "object" || !("response" in err)) return {};
  const raw = (err as { response?: { data?: { errors?: unknown } } }).response?.data?.errors;
  if (!raw || typeof raw !== "object") return {};

  const out: Record<string, string> = {};
  for (const [key, value] of Object.entries(raw)) {
    if (Array.isArray(value) && typeof value[0] === "string") {
      out[normalizeFieldKey(key)] = value[0];
    }
  }
  return out;
}

function normalizeFieldKey(key: string): string {
  return key
    .replace(/\[(\d+)\]/g, ".$1")
    .split(".")
    .map((seg) => (seg === "" || /^\d+$/.test(seg) ? seg : seg.charAt(0).toLowerCase() + seg.slice(1)))
    .join(".");
}
```

- [ ] **Step 4: Run the test and verify it passes**

Run: `cd bom-mobile && npx jest --selectProjects=node apiError`
Expected: 6/6 passed.

- [ ] **Step 5: Commit**

```bash
git add bom-mobile/src/utils/apiError.ts bom-mobile/__tests__/apiError.test.ts
git commit -m "feat(mobile): port apiError helper from web (extractApiError + extractFieldErrors)"
```

---

## Task 3: Add costing types

**Files:**
- Modify: `bom-mobile/src/types/api.ts`

- [ ] **Step 1: Locate insertion point**

Open `bom-mobile/src/types/api.ts` and find the BOM-related type block (`BomReviewResponse`, etc.). The new costing types belong below that block, above the MD review types if present.

- [ ] **Step 2: Append the new types**

```ts
export type LandedCostType = "Percentage" | "Fixed";

export type LastCostReference = {
  costPerKg: number;
  currencyCode: string;
  updatedAt: string;
};

export type CostingBomLine = {
  bomLineId: number;
  processId: number;
  processName: string;
  rawMaterialItemId: number;
  rawMaterialDescription: string;
  qtyPerKg: number;
  wastagePct: number;
  lastCost: LastCostReference | null;
};

export type CostingLineDraft = {
  bomLineId: number;
  costPerKg: number;
  currencyCode: string;
};

export type CostingDraft = {
  lines: CostingLineDraft[];
  landedCostType: LandedCostType;
  landedCostValue: number;
  fohAmount: number;
};

export type CostingItemResponse = {
  requisitionItemId: number;
  itemDescription: string;
  expectedQty: number;
  costStatus: "NotStarted" | "InProgress" | "Submitted";
  bomLines: CostingBomLine[];
  draft: CostingDraft | null;
};

export type CostingReviewResponse = {
  requisitionId: number;
  refNo: string;
  status: string;
  currencyCode: string;
  items: CostingItemResponse[];
};
```

- [ ] **Step 3: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/src/types/api.ts
git commit -m "feat(mobile): add costing types mirroring backend CostingDtos"
```

---

## Task 4: Costing API hooks

**Files:**
- Create: `bom-mobile/src/api/costing.ts`

This is the data-access layer. One TanStack Query for reading, three mutations for transitions.

- [ ] **Step 1: Create the file**

```ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import type {
  CostingDraft,
  CostingReviewResponse,
} from "@/types/api";

export const costingKeys = {
  review: (requisitionId: number) => ["costing", "review", requisitionId] as const,
};

export function useCostingReview(requisitionId: number, enabled = true) {
  return useQuery({
    queryKey: costingKeys.review(requisitionId),
    queryFn: async () => {
      const res = await api.get<CostingReviewResponse>(`/api/costing/${requisitionId}`);
      return res.data;
    },
    enabled: enabled && Number.isFinite(requisitionId) && requisitionId > 0,
    staleTime: 10_000,
    retry: false,
  });
}

export function useStartCostingItem(requisitionId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (requisitionItemId: number) => {
      await api.post(
        `/api/costing/${requisitionId}/items/${requisitionItemId}/start`,
      );
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: costingKeys.review(requisitionId) }),
  });
}

export function useSaveCostingItemDraft(requisitionId: number, requisitionItemId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (draft: CostingDraft) => {
      await api.put(
        `/api/costing/${requisitionId}/items/${requisitionItemId}/draft`,
        draft,
      );
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: costingKeys.review(requisitionId) }),
  });
}

export function useSubmitCostingItem(requisitionId: number, requisitionItemId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      await api.post(
        `/api/costing/${requisitionId}/items/${requisitionItemId}/submit`,
      );
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: costingKeys.review(requisitionId) });
      qc.invalidateQueries({ queryKey: ["requisition", requisitionId] });
      qc.invalidateQueries({ queryKey: ["requisitions"] });
    },
  });
}
```

- [ ] **Step 2: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/api/costing.ts
git commit -m "feat(mobile): add costing API hooks (review query + start/draft/submit mutations)"
```

---

## Task 5: `StaleCostBadge` component

**Files:**
- Create: `bom-mobile/src/components/StaleCostBadge.tsx`

- [ ] **Step 1: Create the component**

```tsx
import { Text, View } from "react-native";
import { formatCurrency } from "@/utils/numbers";

interface Props {
  daysAgo: number;
  costPerKg: number;
  currencyCode: string;
}

export function StaleCostBadge({ daysAgo, costPerKg, currencyCode }: Props) {
  return (
    <View
      style={{
        marginTop: 8,
        backgroundColor: "#fef2f2",
        paddingVertical: 6,
        paddingHorizontal: 10,
        borderRadius: 6,
      }}
    >
      <Text style={{ color: "#dc2626", fontSize: 12 }}>
        ⚠ Last cost {formatCurrency(costPerKg)} {currencyCode} · {daysAgo} days ago (stale)
      </Text>
    </View>
  );
}
```

- [ ] **Step 2: Verify `formatCurrency` exists**

Run: `grep -n "export function formatCurrency" bom-mobile/src/utils/numbers.ts`
Expected: a single match. If absent, use plain `costPerKg.toFixed(4)` instead.

- [ ] **Step 3: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/src/components/StaleCostBadge.tsx
git commit -m "feat(mobile): add StaleCostBadge component for >10 day old cost references"
```

---

## Task 6: `CurrencyPickerSheet` component

**Files:**
- Create: `bom-mobile/src/components/CurrencyPickerSheet.tsx`

- [ ] **Step 1: Inspect `SearchablePicker` props**

Run: `grep -n "interface\|export function\|export default" bom-mobile/src/components/SearchablePicker.tsx | head -10`
Expected: see the prop names (`value`, `options`, `onChange`, `label`, `placeholder`, `searchEnabled`, etc.). Match these in the wrapper below.

- [ ] **Step 2: Create the wrapper**

```tsx
import { SearchablePicker } from "./SearchablePicker";

interface Props {
  value: string;
  options: string[];
  onChange: (code: string) => void;
}

// Thin wrapper so all currency pickers across the costing screen share a single
// configuration (label, placeholder, search-on for long lists).
export function CurrencyPickerSheet({ value, options, onChange }: Props) {
  return (
    <SearchablePicker
      value={value}
      options={options.map((c) => ({ id: c, label: c }))}
      onChange={(opt) => onChange(opt.id as string)}
      placeholder="Currency"
      searchEnabled={options.length > 6}
    />
  );
}
```

If `SearchablePicker` uses different prop names (the inspect step in Step 1 will reveal them), adapt the wrapper to match. Do **not** modify `SearchablePicker` itself — keep this wrapper as the only adapter.

- [ ] **Step 3: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/src/components/CurrencyPickerSheet.tsx
git commit -m "feat(mobile): add CurrencyPickerSheet wrapper around SearchablePicker"
```

---

## Task 7: `SaveStatusBadge` component

**Files:**
- Create: `bom-mobile/src/components/SaveStatusBadge.tsx`

- [ ] **Step 1: Create the component**

```tsx
import { Pressable, Text, View } from "react-native";

export type SaveStatus = "idle" | "saving" | "saved" | "error";

interface Props {
  status: SaveStatus;
  onRetry?: () => void;
}

const COLORS: Record<SaveStatus, { bg: string; fg: string; label: string }> = {
  idle:   { bg: "transparent", fg: "transparent", label: "" },
  saving: { bg: "#eff6ff",     fg: "#1e40af",     label: "Saving…" },
  saved:  { bg: "#ecfdf5",     fg: "#047857",     label: "Saved" },
  error:  { bg: "#fef2f2",     fg: "#b91c1c",     label: "Save failed — tap to retry" },
};

export function SaveStatusBadge({ status, onRetry }: Props) {
  if (status === "idle") return null;
  const { bg, fg, label } = COLORS[status];
  const Wrapper: any = status === "error" && onRetry ? Pressable : View;
  return (
    <Wrapper
      onPress={status === "error" ? onRetry : undefined}
      style={{
        backgroundColor: bg,
        paddingVertical: 4,
        paddingHorizontal: 10,
        borderRadius: 12,
      }}
    >
      <Text style={{ color: fg, fontSize: 12, fontWeight: "600" }}>{label}</Text>
    </Wrapper>
  );
}
```

- [ ] **Step 2: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/components/SaveStatusBadge.tsx
git commit -m "feat(mobile): add SaveStatusBadge for hybrid auto-save state display"
```

---

## Task 8: `CostLineCard` component (layout A)

**Files:**
- Create: `bom-mobile/src/components/CostLineCard.tsx`

This is the most user-facing piece of the screen. Layout matches mockup A from brainstorm.

- [ ] **Step 1: Create the component**

```tsx
import { useMemo } from "react";
import { Text, TextInput, View } from "react-native";
import type { CostingBomLine } from "@/types/api";
import { CurrencyPickerSheet } from "./CurrencyPickerSheet";
import { StaleCostBadge } from "./StaleCostBadge";

interface Props {
  line: CostingBomLine;
  value: { costPerKg: number; currencyCode: string };
  currencyOptions: string[];
  fieldError?: string;
  onChange: (v: { costPerKg: number; currencyCode: string }) => void;
  onBlur: () => void;
}

const STALE_DAYS = 10;

function daysSince(iso: string): number {
  return Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000);
}

export function CostLineCard({ line, value, currencyOptions, fieldError, onChange, onBlur }: Props) {
  const staleDays = useMemo(
    () => (line.lastCost ? daysSince(line.lastCost.updatedAt) : null),
    [line.lastCost],
  );

  return (
    <View
      style={{
        backgroundColor: "#fff",
        borderRadius: 10,
        padding: 12,
        marginBottom: 10,
        borderWidth: 1,
        borderColor: fieldError ? "#fca5a5" : "#e2e8f0",
      }}
    >
      <Text style={{ fontSize: 11, letterSpacing: 0.6, color: "#64748b", fontWeight: "700" }}>
        {line.processName.toUpperCase()}
      </Text>
      <Text style={{ fontSize: 15, fontWeight: "600", marginTop: 2 }}>
        {line.rawMaterialDescription}
      </Text>
      <Text style={{ fontSize: 13, color: "#64748b", marginTop: 2 }}>
        qty {line.qtyPerKg.toFixed(2)} / kg · wastage {line.wastagePct.toFixed(1)}%
      </Text>

      <View style={{ flexDirection: "row", gap: 8, marginTop: 10, alignItems: "center" }}>
        <Text style={{ fontSize: 12, color: "#475569", fontWeight: "600", width: 38 }}>Cost</Text>
        <View style={{ flex: 1 }}>
          <TextInput
            value={String(value.costPerKg ?? 0)}
            onChangeText={(t) => onChange({ ...value, costPerKg: Number(t) || 0 })}
            onBlur={onBlur}
            keyboardType="decimal-pad"
            style={{
              borderWidth: 1,
              borderColor: "#cbd5e1",
              borderRadius: 8,
              padding: 10,
              fontVariant: ["tabular-nums"],
              backgroundColor: "#fff",
            }}
          />
        </View>
        <View style={{ minWidth: 90 }}>
          <CurrencyPickerSheet
            value={value.currencyCode}
            options={currencyOptions}
            onChange={(code) => onChange({ ...value, currencyCode: code })}
          />
        </View>
      </View>

      {fieldError ? (
        <Text style={{ marginTop: 6, color: "#b91c1c", fontSize: 12 }}>{fieldError}</Text>
      ) : null}

      {staleDays !== null && staleDays > STALE_DAYS && line.lastCost ? (
        <StaleCostBadge
          daysAgo={staleDays}
          costPerKg={line.lastCost.costPerKg}
          currencyCode={line.lastCost.currencyCode}
        />
      ) : null}
    </View>
  );
}
```

- [ ] **Step 2: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/components/CostLineCard.tsx
git commit -m "feat(mobile): add CostLineCard component (layout A — compact card per BOM line)"
```

---

## Task 9: `LandedCostSection` component

**Files:**
- Create: `bom-mobile/src/components/LandedCostSection.tsx`

- [ ] **Step 1: Create the component**

```tsx
import { Pressable, Text, TextInput, View } from "react-native";
import type { LandedCostType } from "@/types/api";

interface Props {
  type: LandedCostType;
  value: number;
  fieldError?: string;
  onChange: (v: { type: LandedCostType; value: number }) => void;
  onBlur: () => void;
}

export function LandedCostSection({ type, value, fieldError, onChange, onBlur }: Props) {
  const SegButton = ({ label, val }: { label: string; val: LandedCostType }) => (
    <Pressable
      onPress={() => onChange({ type: val, value })}
      style={({ pressed }) => ({ flex: 1, opacity: pressed ? 0.7 : 1 })}
    >
      <View
        style={{
          paddingVertical: 8,
          alignItems: "center",
          backgroundColor: type === val ? "#1e40af" : "#f1f5f9",
          borderRadius: 8,
        }}
      >
        <Text style={{ color: type === val ? "#fff" : "#475569", fontWeight: "600" }}>{label}</Text>
      </View>
    </Pressable>
  );

  return (
    <View
      style={{
        backgroundColor: "#fff",
        borderRadius: 10,
        padding: 12,
        marginBottom: 10,
        borderWidth: 1,
        borderColor: fieldError ? "#fca5a5" : "#e2e8f0",
      }}
    >
      <Text style={{ fontSize: 14, fontWeight: "600", marginBottom: 8 }}>Landed Cost</Text>
      <View style={{ flexDirection: "row", gap: 8 }}>
        <SegButton label="Percentage" val="Percentage" />
        <SegButton label="Fixed AED" val="Fixed" />
      </View>
      <TextInput
        value={String(value ?? 0)}
        onChangeText={(t) => onChange({ type, value: Number(t) || 0 })}
        onBlur={onBlur}
        keyboardType="decimal-pad"
        placeholder={type === "Percentage" ? "% of raw material" : "AED amount"}
        style={{
          marginTop: 10,
          borderWidth: 1,
          borderColor: "#cbd5e1",
          borderRadius: 8,
          padding: 10,
          fontVariant: ["tabular-nums"],
          backgroundColor: "#fff",
        }}
      />
      {fieldError ? (
        <Text style={{ marginTop: 6, color: "#b91c1c", fontSize: 12 }}>{fieldError}</Text>
      ) : null}
    </View>
  );
}
```

- [ ] **Step 2: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/components/LandedCostSection.tsx
git commit -m "feat(mobile): add LandedCostSection (Pct / Fixed AED segmented + value input)"
```

---

## Task 10: `FohSection` component

**Files:**
- Create: `bom-mobile/src/components/FohSection.tsx`

- [ ] **Step 1: Create the component**

```tsx
import { Text, TextInput, View } from "react-native";

interface Props {
  amount: number;
  fieldError?: string;
  onChange: (v: number) => void;
  onBlur: () => void;
}

export function FohSection({ amount, fieldError, onChange, onBlur }: Props) {
  return (
    <View
      style={{
        backgroundColor: "#fff",
        borderRadius: 10,
        padding: 12,
        marginBottom: 10,
        borderWidth: 1,
        borderColor: fieldError ? "#fca5a5" : "#e2e8f0",
      }}
    >
      <Text style={{ fontSize: 14, fontWeight: "600", marginBottom: 8 }}>FOH per kg (AED)</Text>
      <TextInput
        value={String(amount ?? 0)}
        onChangeText={(t) => onChange(Number(t) || 0)}
        onBlur={onBlur}
        keyboardType="decimal-pad"
        style={{
          borderWidth: 1,
          borderColor: "#cbd5e1",
          borderRadius: 8,
          padding: 10,
          fontVariant: ["tabular-nums"],
          backgroundColor: "#fff",
        }}
      />
      {fieldError ? (
        <Text style={{ marginTop: 6, color: "#b91c1c", fontSize: 12 }}>{fieldError}</Text>
      ) : null}
    </View>
  );
}
```

- [ ] **Step 2: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/components/FohSection.tsx
git commit -m "feat(mobile): add FohSection (AED amount input)"
```

---

## Task 11: `(accountant)/_layout.tsx` with role guard

**Files:**
- Create: `bom-mobile/app/(accountant)/_layout.tsx`

- [ ] **Step 1: Reference the existing `(md)/_layout.tsx` and `(sales)/_layout.tsx`**

Run: `cat "bom-mobile/app/(md)/_layout.tsx"`
Note the role-guard pattern used (Redirect vs Stack with auth check). Mirror it.

- [ ] **Step 2: Create the layout file**

```tsx
import { Redirect, Stack } from "expo-router";
import { useAuth } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";

export default function AccountantLayout() {
  const { user, loading } = useAuth();
  if (loading) return <LoadingView />;
  if (!user) return <Redirect href="/login" />;
  if (user.role !== "Accountant") return <Redirect href="/" />;
  return <Stack screenOptions={{ headerShown: false }} />;
}
```

If `(md)/_layout.tsx` uses a different guard idiom (e.g., a custom `RoleGuard` component or hook), use that idiom verbatim instead — keeping the three layouts consistent.

- [ ] **Step 3: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/app/(accountant)/_layout.tsx
git commit -m "feat(mobile): add (accountant) route group with role guard"
```

---

## Task 12: `(accountant)/index.tsx` — pending list

**Files:**
- Create: `bom-mobile/app/(accountant)/index.tsx`

The pending list mirrors the sales/MD home pattern but filters to `CostingPending` + `CostingInProgress`.

- [ ] **Step 1: Reference the existing pending-list patterns**

Run: `cat "bom-mobile/app/(md)/pending.tsx"`
Note: how it loads data (`useRequisitions`), header layout, item-press routing, empty/loading states, and notification bell wiring. Match this shape.

- [ ] **Step 2: Create the screen**

```tsx
import { Pressable, ScrollView, Text, View } from "react-native";
import { Stack, useRouter } from "expo-router";
import * as Haptics from "expo-haptics";
import { useRequisitions } from "@/api/requisitions";
import { useAuth } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";
import { ErrorBanner } from "@/components/ErrorBanner";
import { ScreenHeader } from "@/components/ScreenHeader";
import { RequisitionCard } from "@/components/RequisitionCard";
import { EmptyState } from "@/components/EmptyState";
import { NotificationBell } from "@/components/NotificationBell";

export default function AccountantHome() {
  const router = useRouter();
  const { logout } = useAuth();
  const q = useRequisitions({ status: ["CostingPending", "CostingInProgress"] });

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
        style={{ paddingHorizontal: 12, paddingVertical: 9, borderRadius: 8, backgroundColor: "#f1f5f9" }}
      >
        <Text style={{ color: "#1e40af", fontSize: 15, fontWeight: "600" }}>Log out</Text>
      </Pressable>
    </>
  );

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader title="Pending Costing" right={HeaderRight} />
      {q.isPending ? <LoadingView /> : null}
      {q.isError ? <ErrorBanner message="Failed to load requisitions" onRetry={q.refetch} /> : null}
      {q.data && q.data.length === 0 ? (
        <EmptyState title="All caught up" subtitle="No requisitions awaiting costing." />
      ) : null}
      {q.data && q.data.length > 0 ? (
        <ScrollView contentContainerStyle={{ padding: 16, gap: 10 }}>
          {q.data.map((r) => (
            <Pressable
              key={r.id}
              onPress={() => {
                Haptics.selectionAsync();
                router.push(`/(accountant)/${r.id}`);
              }}
            >
              <RequisitionCard requisition={r} />
            </Pressable>
          ))}
        </ScrollView>
      ) : null}
    </View>
  );
}
```

- [ ] **Step 3: Verify `useRequisitions` accepts the `status` array filter**

Run: `grep -n "status" bom-mobile/src/api/requisitions.ts | head -10`
Expected: see how `status` is sent (multi-value query param). If `useRequisitions` doesn't yet accept the filter shape used above, extend its signature in `requisitions.ts` to match (`status?: string[]`), preserving its existing call sites by keeping the param optional.

- [ ] **Step 4: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add bom-mobile/app/(accountant)/index.tsx bom-mobile/src/api/requisitions.ts
git commit -m "feat(mobile): add Accountant pending list (CostingPending + CostingInProgress)"
```

(Drop `requisitions.ts` from the `add` if it wasn't modified.)

---

## Task 13: `(accountant)/[id].tsx` — req detail with item cards + auto-start

**Files:**
- Create: `bom-mobile/app/(accountant)/[id].tsx`

- [ ] **Step 1: Reference `(md)/historical/[id].tsx` for the layout shape**

Run: `cat "bom-mobile/app/(md)/historical/[id].tsx" | head -120`

- [ ] **Step 2: Create the screen**

```tsx
import { useEffect, useRef } from "react";
import { Pressable, ScrollView, Text, View } from "react-native";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import * as Haptics from "expo-haptics";
import { useCostingReview, useStartCostingItem } from "@/api/costing";
import { useRequisitionDetail } from "@/api/requisitions";
import { useAuth } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";
import { ErrorBanner } from "@/components/ErrorBanner";
import { ScreenHeader } from "@/components/ScreenHeader";
import { ItemCardShell } from "@/components/ItemCardShell";
import { ItemStageBadge } from "@/components/ItemStageBadge";
import { StatusPill } from "@/components/StatusPill";
import { NotificationBell } from "@/components/NotificationBell";

export default function AccountantReqDetail() {
  const router = useRouter();
  const { logout } = useAuth();
  const params = useLocalSearchParams<{ id: string }>();
  const id = Number(params.id);

  const reqQ = useRequisitionDetail(id);
  const costQ = useCostingReview(id);
  const startItem = useStartCostingItem(id);
  const hasAutoStarted = useRef(false);

  useEffect(() => {
    if (
      !hasAutoStarted.current &&
      reqQ.data?.status === "CostingPending" &&
      costQ.data &&
      costQ.data.items.length > 0
    ) {
      const first = costQ.data.items[0];
      if (first.costStatus === "NotStarted") {
        hasAutoStarted.current = true;
        startItem.mutate(first.requisitionItemId);
      }
    }
  }, [reqQ.data?.status, costQ.data, startItem]);

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
        style={{ paddingHorizontal: 12, paddingVertical: 9, borderRadius: 8, backgroundColor: "#f1f5f9" }}
      >
        <Text style={{ color: "#1e40af", fontSize: 15, fontWeight: "600" }}>Log out</Text>
      </Pressable>
    </>
  );

  if (reqQ.isPending || costQ.isPending) return <LoadingView />;
  if (reqQ.isError || costQ.isError || !reqQ.data || !costQ.data) {
    return (
      <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <Stack.Screen options={{ headerShown: false }} />
        <ScreenHeader title="Requisition" right={HeaderRight} />
        <ErrorBanner
          message="Failed to load requisition"
          onRetry={() => { reqQ.refetch(); costQ.refetch(); }}
        />
      </View>
    );
  }

  const r = reqQ.data;

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader title={r.refNo} right={HeaderRight} />
      <ScrollView contentContainerStyle={{ padding: 16, gap: 10 }}>
        <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center" }}>
          <Text style={{ fontSize: 14, color: "#64748b" }} numberOfLines={1}>
            {r.customerName}
          </Text>
          <StatusPill status={r.status} />
        </View>
        {costQ.data.items.map((it) => (
          <Pressable
            key={it.requisitionItemId}
            onPress={() => {
              Haptics.selectionAsync();
              router.push(`/(accountant)/item/${id}/${it.requisitionItemId}`);
            }}
          >
            <ItemCardShell>
              <Text style={{ fontSize: 15, fontWeight: "600" }}>{it.itemDescription}</Text>
              <Text style={{ fontSize: 13, color: "#64748b", marginTop: 2 }}>
                Expected qty: {it.expectedQty}
              </Text>
              <View style={{ marginTop: 6 }}>
                <ItemStageBadge status={it.costStatus} />
              </View>
            </ItemCardShell>
          </Pressable>
        ))}
      </ScrollView>
    </View>
  );
}
```

- [ ] **Step 3: Verify `ItemStageBadge` accepts the costing-status string set**

Run: `grep -n "status\|InProgress\|NotStarted\|Submitted" bom-mobile/src/components/ItemStageBadge.tsx`
Expected: it already supports these (used by other roles). If the badge maps strings to colors and a costing string is missing, add it inline.

- [ ] **Step 4: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add bom-mobile/app/(accountant)/[id].tsx
git commit -m "feat(mobile): add Accountant requisition detail with item cards + auto-start first item"
```

---

## Task 14: `(accountant)/item/[reqId]/[itemId].tsx` — costing form (the centerpiece)

**Files:**
- Create: `bom-mobile/app/(accountant)/item/[reqId]/[itemId].tsx`

This is the largest task. It composes all 6 components and runs the hybrid auto-save state machine. Implement in passes — get the layout and data wiring up first (Steps 1–3), then the auto-save state machine (Steps 4–5), then submit (Step 6).

### Pass A — scaffold + data hydration

- [ ] **Step 1: Create the screen file with scaffold + hydration**

```tsx
import { useEffect, useMemo, useReducer, useRef, useState } from "react";
import { Alert, Pressable, ScrollView, Text, View } from "react-native";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { useActiveExchangeRates } from "@/api/lookups";
import {
  useCostingReview,
  useSaveCostingItemDraft,
  useSubmitCostingItem,
} from "@/api/costing";
import type { CostingDraft, CostingLineDraft, LandedCostType } from "@/types/api";
import { extractFieldErrors } from "@/utils/apiError";
import { ScreenHeader } from "@/components/ScreenHeader";
import { LoadingView } from "@/components/LoadingView";
import { ErrorBanner } from "@/components/ErrorBanner";
import { Button } from "@/components/Button";
import { CostLineCard } from "@/components/CostLineCard";
import { LandedCostSection } from "@/components/LandedCostSection";
import { FohSection } from "@/components/FohSection";
import { SaveStatusBadge, type SaveStatus } from "@/components/SaveStatusBadge";

type FormState = {
  lines: Map<number, CostingLineDraft>;
  landedCostType: LandedCostType;
  landedCostValue: number;
  fohAmount: number;
};

type FormAction =
  | { type: "hydrate"; payload: FormState }
  | { type: "set-line"; bomLineId: number; line: CostingLineDraft }
  | { type: "set-landed"; landedCostType: LandedCostType; landedCostValue: number }
  | { type: "set-foh"; fohAmount: number };

function reducer(state: FormState, action: FormAction): FormState {
  switch (action.type) {
    case "hydrate": return action.payload;
    case "set-line": {
      const lines = new Map(state.lines);
      lines.set(action.bomLineId, action.line);
      return { ...state, lines };
    }
    case "set-landed": return { ...state, landedCostType: action.landedCostType, landedCostValue: action.landedCostValue };
    case "set-foh":    return { ...state, fohAmount: action.fohAmount };
  }
}

export default function CostingForm() {
  const router = useRouter();
  const params = useLocalSearchParams<{ reqId: string; itemId: string }>();
  const reqId = Number(params.reqId);
  const itemId = Number(params.itemId);

  const reviewQ = useCostingReview(reqId);
  const ratesQ = useActiveExchangeRates();
  const saveDraft = useSaveCostingItemDraft(reqId, itemId);
  const submit = useSubmitCostingItem(reqId, itemId);

  const item = reviewQ.data?.items.find((i) => i.requisitionItemId === itemId);
  const quoteCurrency = reviewQ.data?.currencyCode ?? "AED";

  const currencyOptions = useMemo(() => {
    const codes = new Set((ratesQ.data ?? []).map((r) => r.currencyCode));
    codes.add("AED");
    return Array.from(codes).sort();
  }, [ratesQ.data]);

  const [form, dispatch] = useReducer(reducer, null as unknown as FormState);
  const [hydrated, setHydrated] = useState(false);
  const [saveStatus, setSaveStatus] = useState<SaveStatus>("idle");
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  // Hydrate from server (item.draft if any, else seed with last-cost defaults).
  useEffect(() => {
    if (!item || hydrated) return;
    const seedLines = new Map<number, CostingLineDraft>();
    const draftByLine = new Map((item.draft?.lines ?? []).map((l) => [l.bomLineId, l]));
    for (const bl of item.bomLines) {
      const d = draftByLine.get(bl.bomLineId);
      seedLines.set(bl.bomLineId, {
        bomLineId: bl.bomLineId,
        costPerKg: d?.costPerKg ?? bl.lastCost?.costPerKg ?? 0,
        currencyCode: d?.currencyCode ?? bl.lastCost?.currencyCode ?? quoteCurrency,
      });
    }
    dispatch({
      type: "hydrate",
      payload: {
        lines: seedLines,
        landedCostType: item.draft?.landedCostType ?? "Percentage",
        landedCostValue: item.draft?.landedCostValue ?? 0,
        fohAmount: item.draft?.fohAmount ?? 0,
      },
    });
    setHydrated(true);
  }, [item, hydrated, quoteCurrency]);

  if (reviewQ.isPending || ratesQ.isPending || !hydrated || !item) return <LoadingView />;
  if (reviewQ.isError) return <ErrorBanner message="Failed to load costing" onRetry={reviewQ.refetch} />;

  // Render below in Step 2 — keep this pass minimal so the file compiles.
  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader title={item.itemDescription} right={<SaveStatusBadge status={saveStatus} />} />
      <Text style={{ padding: 16 }}>Costing form scaffold OK</Text>
    </View>
  );
}
```

- [ ] **Step 2: Type-check the scaffold**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

### Pass B — full UI render

- [ ] **Step 3: Replace the scaffold render with the full UI**

Replace the `return (...)` block of `CostingForm` with:

```tsx
return (
  <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
    <Stack.Screen options={{ headerShown: false }} />
    <ScreenHeader
      title={item.itemDescription}
      right={
        <SaveStatusBadge
          status={saveStatus}
          onRetry={() => fireSave(form)}
        />
      }
    />
    <ScrollView contentContainerStyle={{ padding: 16, paddingBottom: 96 }}>
      <Text style={{ fontSize: 13, color: "#64748b", marginBottom: 12 }}>
        Expected qty: {item.expectedQty} kg
      </Text>

      <Text style={{ fontSize: 11, letterSpacing: 0.6, color: "#64748b", fontWeight: "700", marginBottom: 6 }}>
        BOM LINES ({item.bomLines.length})
      </Text>
      {item.bomLines.map((bl) => {
        const cur = form.lines.get(bl.bomLineId)!;
        return (
          <CostLineCard
            key={bl.bomLineId}
            line={bl}
            value={{ costPerKg: cur.costPerKg, currencyCode: cur.currencyCode }}
            currencyOptions={currencyOptions}
            fieldError={fieldErrors[`lines.${bl.bomLineId}.costPerKg`]}
            onChange={(v) => {
              dispatch({ type: "set-line", bomLineId: bl.bomLineId, line: { bomLineId: bl.bomLineId, ...v } });
              scheduleDebouncedSave();
            }}
            onBlur={() => fireSave(form)}
          />
        );
      })}

      <LandedCostSection
        type={form.landedCostType}
        value={form.landedCostValue}
        fieldError={fieldErrors["landedCostValue"]}
        onChange={(v) => {
          dispatch({ type: "set-landed", landedCostType: v.type, landedCostValue: v.value });
          scheduleDebouncedSave();
        }}
        onBlur={() => fireSave(form)}
      />
      <FohSection
        amount={form.fohAmount}
        fieldError={fieldErrors["fohAmount"]}
        onChange={(v) => {
          dispatch({ type: "set-foh", fohAmount: v });
          scheduleDebouncedSave();
        }}
        onBlur={() => fireSave(form)}
      />
    </ScrollView>

    <View
      style={{
        position: "absolute",
        left: 16,
        right: 16,
        bottom: 16,
      }}
    >
      <Button
        label={submit.isPending ? "Submitting…" : "Submit"}
        onPress={onSubmit}
        disabled={submit.isPending || saveStatus === "saving"}
      />
    </View>
  </View>
);
```

This compile will fail — `scheduleDebouncedSave`, `fireSave`, and `onSubmit` are not yet defined. Pass C wires them up.

### Pass C — hybrid auto-save + submit

- [ ] **Step 4: Add the auto-save helpers above the `if (reviewQ.isPending …)` early return**

```tsx
const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
const formRef = useRef<FormState>(form);
useEffect(() => { formRef.current = form; }, [form]);

const buildDraft = (s: FormState): CostingDraft => ({
  lines: Array.from(s.lines.values()),
  landedCostType: s.landedCostType,
  landedCostValue: s.landedCostValue,
  fohAmount: s.fohAmount,
});

const fireSave = (s: FormState) => {
  if (debounceRef.current) {
    clearTimeout(debounceRef.current);
    debounceRef.current = null;
  }
  setSaveStatus("saving");
  saveDraft.mutate(buildDraft(s), {
    onSuccess: () => {
      setSaveStatus("saved");
      setTimeout(() => setSaveStatus((cur) => (cur === "saved" ? "idle" : cur)), 5000);
    },
    onError: () => setSaveStatus("error"),
  });
};

const scheduleDebouncedSave = () => {
  if (debounceRef.current) clearTimeout(debounceRef.current);
  debounceRef.current = setTimeout(() => fireSave(formRef.current), 2000);
};

// Screen-exit save (best-effort).
useEffect(() => {
  return () => {
    if (debounceRef.current) {
      clearTimeout(debounceRef.current);
      // Fire and forget — we cannot await before unmount.
      saveDraft.mutate(buildDraft(formRef.current));
    }
  };
  // eslint-disable-next-line react-hooks/exhaustive-deps
}, []);
```

- [ ] **Step 5: Add the `onSubmit` handler**

```tsx
const onSubmit = () => {
  Alert.alert(
    "Submit costing?",
    `Item "${item.itemDescription}" will be submitted to MD.`,
    [
      { text: "Cancel", style: "cancel" },
      {
        text: "Submit",
        onPress: () => {
          // Flush any pending debounced save first.
          if (debounceRef.current) {
            clearTimeout(debounceRef.current);
            debounceRef.current = null;
          }
          submit.mutate(undefined, {
            onSuccess: () => router.back(),
            onError: (err) => {
              const errs = extractFieldErrors(err);
              if (Object.keys(errs).length > 0) {
                setFieldErrors(errs);
              } else {
                Alert.alert("Submit failed", "Please retry. If it persists, contact admin.");
              }
            },
          });
        },
      },
    ],
  );
};
```

- [ ] **Step 6: Type-check the full file**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add bom-mobile/app/(accountant)/item/[reqId]/[itemId].tsx
git commit -m "feat(mobile): add Accountant per-item costing form with hybrid auto-save"
```

---

## Task 15: Wire root `app/index.tsx` redirect for Accountant role

**Files:**
- Modify: `bom-mobile/app/index.tsx`

- [ ] **Step 1: Read the current file**

Run: `cat bom-mobile/app/index.tsx`
Expected current state: redirects for `SalesPerson → /(sales)`, `ManagingDirector → /(md)`, fallthrough to `/login`.

- [ ] **Step 2: Add the Accountant redirect**

Replace the file contents with:

```tsx
import { Redirect } from "expo-router";
import { useAuth } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";

export default function Index() {
  const { user, loading } = useAuth();
  if (loading) return <LoadingView />;
  if (!user) return <Redirect href="/login" />;
  if (user.role === "SalesPerson") return <Redirect href="/(sales)" />;
  if (user.role === "ManagingDirector") return <Redirect href="/(md)" />;
  if (user.role === "Accountant") return <Redirect href="/(accountant)" />;
  return <Redirect href="/login" />;
}
```

- [ ] **Step 3: Type-check**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/app/index.tsx
git commit -m "feat(mobile): redirect Accountant role to (accountant) route group"
```

---

## Task 16: Manual smoke + memory update

**Files:**
- Update: `memory/project_mobile_plan1_status.md` (post-execution housekeeping)

The mobile smoke must run on a real device with the LAN backend, exactly as the V2.2 smoke ran (see `memory/project_mobile_v22_bom_drilldown.md`).

- [ ] **Step 1: Start the backend bound to LAN**

```bash
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://0.0.0.0:7300 \
dotnet run --project BomPriceApproval.API --no-launch-profile
```

Verify: `curl -s -o /dev/null -w "%{http_code}\n" http://192.168.1.216:7300/swagger/index.html` → `200` (substitute the LAN IP from `ipconfig`).

- [ ] **Step 2: Start the mobile app**

```bash
cd bom-mobile
EXPO_PUBLIC_API_BASE_URL=http://192.168.1.216:7300 npx expo start
```

Connect Expo Go to `exp://192.168.1.216:8081`.

- [ ] **Step 3: Identify test data**

Login as `sara@test.com / Test@1234`. Confirm at least one `CostingPending` requisition exists in her branch (the integration test from Task 1 leaves one behind; or pick from existing data).

- [ ] **Step 4: Run the smoke checklist (spec §9)**

| # | Case | Expected |
|---|---|---|
| M1 | Pending list shows only `CostingPending` + `CostingInProgress` for current branch | ✓ list filtered |
| M2 | Tap req → detail screen → first NotStarted item auto-starts (badge flips InProgress) | ✓ no extra tap |
| M3 | Drill into item → form renders BOM lines (layout A) + landed + FOH | ✓ matches mockup |
| M4 | Edit cost on a line → wait 2s → SaveStatusBadge transitions saving → saved | debounce trigger |
| M5 | Edit + immediately blur input → save fires immediately | blur trigger |
| M6 | Edit + immediately back-tap → save fires before unmount | screen-exit trigger |
| M7 | Submit → confirmation → back to detail → item badge = Submitted | success path |
| M8 | Submit with 0 cost on a line → server returns field error → inline shown | validation path |
| M9 | Stale BOM-line lastCost (manually tweaked DB) → ⚠ badge shows | stale UI |
| M10 | Submit last item → req moves to MdReview → toast + auto-pop to pending list | last-item transition |

- [ ] **Step 5: Stop the dev servers**

Stop the Expo Metro process (kill the node PID listening on `:8081`) and the dotnet process (Ctrl+C in its terminal, or kill the PID listening on `:7300`).

- [ ] **Step 6: Update memory**

Edit `C:/Users/Administrator/.claude/projects/D--shan-projects-BOM-Price-Approval/memory/project_mobile_plan1_status.md`:

- Move "V2.1 — Accountant mobile stack (NOT started)" out of the outstanding list.
- Add a new section: "V2.1 Phase 1 — MERGED <date>" with the head SHA and the smoke checklist outcome (mirror the format used for V2.2).
- Update the master state line at the top.

Edit `C:/Users/Administrator/.claude/projects/D--shan-projects-BOM-Price-Approval/memory/MEMORY.md`:

- Add an index entry for the new V2.1 file (only if you create a separate `project_mobile_v21_accountant.md`; otherwise update the V1+V2 status line in place).

- [ ] **Step 7: Final commit (only the memory updates if needed; mobile changes are already committed per task)**

```bash
git status
# If only memory files changed and they live outside the repo, no commit needed.
# If you added a memory note inside the repo (e.g., docs/), commit it:
# git add <path>
# git commit -m "docs: record V2.1 Phase 1 smoke pass"
```

---

## Self-review checklist (run before declaring the plan ready)

- [x] Every spec section has a task. (§§1–11 of the spec map onto Tasks 1–16.)
- [x] Phase 1 cut-line honored — no Phase 2 work in this plan.
- [x] No TBD / TODO / "implement later" anywhere.
- [x] Type names used in tasks (`CostingDraft`, `CostingLineDraft`, etc.) all defined in Task 3.
- [x] File paths match between "File structure" and individual tasks.
- [x] Backend test in Task 1 uses real seeded credentials from `user id pwd.txt`.
- [x] Mobile screens reference reusable components by their actual filenames in `bom-mobile/src/components/`.
- [x] Hybrid auto-save state machine (debounce + blur + screen exit) is fully specified in Task 14, Pass C.
- [x] Submit handler extracts field errors via the helper from Task 2.
- [x] Memory housekeeping is the last task, not assumed.

---

## Execution

Plan saved. Two execution options:

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints.

Per the project's `CLAUDE.md` Workflow Rules, the user prefers `/superpowers:execute-plan` (inline). Default to **Inline** unless the user requests otherwise.
