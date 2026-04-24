# Mobile V2.0 — MD List + Approved Prices Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enhance the MD mobile stack with a full requisitions list (search + 6 status chips) and surface approved per-kg item prices on both Sales and MD detail pages.

**Architecture:** Backend-first — extend existing `/api/requisitions` list endpoint (multi-status + search) and detail endpoint (approval items with prices). Then mobile: add a `formatCurrency` helper + shared `ItemPriceBlock` component, integrate into both detail pages, and replace the MD pending screen's filter with a chip-based filter + search input.

**Tech Stack:** ASP.NET Core 8 (EF Core 8 + Npgsql), xUnit + Testcontainers, React Native 0.81.5 (Expo SDK 54), React Query v5 (infinite query), Jest + React Testing Library, TypeScript.

**Source spec:** `docs/superpowers/specs/2026-04-24-mobile-v2-md-list-prices-design.md`

---

## Pre-flight

- [ ] **Step 0.1: Create feature branch**

Run:
```bash
cd "D:/shan projects/BOM_Price_Approval"
git checkout master
git pull --ff-only  # no-op since no remote
git checkout -b feature/mobile-v2-md-list-prices
git status
```
Expected: on branch `feature/mobile-v2-md-list-prices`, working tree clean.

- [ ] **Step 0.2: Verify baseline is green**

Run:
```bash
dotnet build --nologo -v q
cd bom-mobile && npx tsc --noEmit && npx jest --silent && cd ..
```
Expected: build 0 errors, tsc 0 errors, jest 33/33 pass.

---

## File Structure

**Backend (create / modify):**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` — extend `GetAll` (status[], search) + extend `Get` DTO include
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs` — extend `ApprovalSummary` with items list + new `ApprovalItemPrice` record
- Modify: `BomPriceApproval.Tests/Requisitions/RequisitionWorkflowTests.cs` — add filter + search + approved-prices tests (or new file `RequisitionListFilterTests.cs`)

**Mobile (create / modify):**
- Modify: `bom-mobile/src/types/api.ts` — extend `ApprovalSummary` with items
- Modify: `bom-mobile/src/utils/numbers.ts` — add `formatCurrency(amount, code)`
- Modify: `bom-mobile/__tests__/numbers.test.ts` — test `formatCurrency`
- Create: `bom-mobile/src/components/ItemPriceBlock.tsx`
- Create: `bom-mobile/__tests__/itemPriceBlock.test.tsx`
- Modify: `bom-mobile/app/(sales)/[id].tsx` — render `ItemPriceBlock` when approved
- Modify: `bom-mobile/app/(md)/[id].tsx` — render `ItemPriceBlock` when approved
- Modify: `bom-mobile/app/(md)/pending.tsx` — add search + chips (rename title only, keep filename)
- Create: `bom-mobile/src/components/StatusChipRow.tsx` — horizontal scrollable chip component (reusable)
- Create: `bom-mobile/__tests__/statusChipRow.test.tsx` — mapping assertions

---

## Task 1: Backend — multi-status + search on list endpoint

**Files:**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs:26-65`
- Modify: `BomPriceApproval.Tests/Requisitions/RequisitionWorkflowTests.cs` (append tests)

- [ ] **Step 1.1: Write the failing test (multi-status filter)**

Open `BomPriceApproval.Tests/Requisitions/RequisitionWorkflowTests.cs` and append a test class or add to the existing test class. Add:

```csharp
[Fact]
public async Task GetAll_MultiStatus_ReturnsUnion()
{
    // Seed: 2 BomPending, 2 CostingPending, 1 Approved (as MD → sees all branches)
    var md = await Factory.LoginAsAsync(UserRole.ManagingDirector);

    var res = await md.GetAsync(
        "/api/requisitions?status=BomPending&status=CostingPending");
    res.EnsureSuccessStatusCode();
    var list = await res.Content.ReadFromJsonAsync<List<RequisitionListItem>>();

    Assert.NotNull(list);
    Assert.All(list!, r =>
        Assert.Contains(r.Status, new[] { "BomPending", "CostingPending" }));
    Assert.DoesNotContain(list, r => r.Status == "Approved");
}

[Fact]
public async Task GetAll_Search_MatchesRefNoAndCustomerName()
{
    var md = await Factory.LoginAsAsync(UserRole.ManagingDirector);

    // Seed a requisition for a customer whose name contains a unique token
    var custName = $"UniqueCustomer_{Guid.NewGuid():N}";
    await Factory.SeedRequisitionAsync(customerName: custName);

    // Match by customer name
    var byCustomer = await md.GetAsync(
        $"/api/requisitions?search={Uri.EscapeDataString(custName[..10])}");
    byCustomer.EnsureSuccessStatusCode();
    var list1 = await byCustomer.Content.ReadFromJsonAsync<List<RequisitionListItem>>();
    Assert.Contains(list1!, r => r.CustomerName == custName);

    // Match by RefNo — fetch known RefNo first, then search by partial
    var first = list1!.First();
    var byRef = await md.GetAsync(
        $"/api/requisitions?search={first.RefNo[..4]}");
    byRef.EnsureSuccessStatusCode();
    var list2 = await byRef.Content.ReadFromJsonAsync<List<RequisitionListItem>>();
    Assert.Contains(list2!, r => r.RefNo == first.RefNo);
}
```

Note: If `Factory.LoginAsAsync` / `Factory.SeedRequisitionAsync` helpers don't exist, use the existing `AuthHelper.LoginAs(...)` and raw seeding pattern visible in neighboring test files (read 2-3 test files in the folder to confirm pattern).

- [ ] **Step 1.2: Run tests to verify they fail**

Run:
```bash
dotnet test --filter "FullyQualifiedName~RequisitionWorkflowTests.GetAll_MultiStatus_ReturnsUnion|FullyQualifiedName~RequisitionWorkflowTests.GetAll_Search_MatchesRefNoAndCustomerName" --nologo
```
Expected: both fail — multi-status returns only 1st value; search param unknown.

- [ ] **Step 1.3: Extend controller signature + filter logic**

Replace the `GetAll` method signature at `RequisitionsController.cs:26-29`:

```csharp
[HttpGet]
public async Task<IActionResult> GetAll(
    [FromQuery(Name = "status")] string[]? statuses = null,
    [FromQuery] string? search = null,
    [FromQuery] int? page = null,
    [FromQuery] int? pageSize = null)
```

Replace the single-status block at lines 45-49 with multi-status + search:

```csharp
if (statuses is { Length: > 0 })
{
    var parsed = statuses
        .Select(s => Enum.TryParse<RequisitionStatus>(s, ignoreCase: true, out var r) ? r : (RequisitionStatus?)null)
        .Where(r => r.HasValue)
        .Select(r => r!.Value)
        .ToArray();
    if (parsed.Length > 0)
        query = query.Where(q => parsed.Contains(q.Status));
}

if (!string.IsNullOrWhiteSpace(search))
{
    var term = search.Trim();
    query = query.Where(q =>
        EF.Functions.ILike(q.RefNo, $"%{term}%") ||
        EF.Functions.ILike(q.Customer.Name, $"%{term}%"));
}
```

The `Count` endpoint (`RequisitionsController.cs:67-87`) is only used by MD dashboard for the single pending count. Leave it untouched — it still accepts single-status.

- [ ] **Step 1.4: Run tests to verify pass**

Run:
```bash
dotnet test --filter "FullyQualifiedName~RequisitionWorkflowTests.GetAll_MultiStatus_ReturnsUnion|FullyQualifiedName~RequisitionWorkflowTests.GetAll_Search_MatchesRefNoAndCustomerName" --nologo
```
Expected: both pass.

- [ ] **Step 1.5: Run full test suite**

Run:
```bash
dotnet test --nologo
```
Expected: no regressions (pre-existing flaky `AuthTests` timing test may retry — per memory, don't chase).

- [ ] **Step 1.6: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs \
        BomPriceApproval.Tests/Requisitions/RequisitionWorkflowTests.cs
git commit -m "$(cat <<'EOF'
feat(api): add multi-status + search filter to /api/requisitions

- status=X&status=Y binds to string[] and filters by union.
- search=... does case-insensitive ILIKE on RefNo OR Customer.Name.
- Branch isolation preserved.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Backend — expose approval item prices on detail

**Files:**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs:33`
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs:89-114`
- Modify: `BomPriceApproval.Tests/Requisitions/RequisitionWorkflowTests.cs`

- [ ] **Step 2.1: Write failing test**

Append to `RequisitionWorkflowTests.cs`:

```csharp
[Fact]
public async Task GetById_ApprovedRequisition_IncludesItemPrices()
{
    // Seed: an approved requisition with 2 items and persisted per-item approval prices
    var md = await Factory.LoginAsAsync(UserRole.ManagingDirector);
    var reqId = await Factory.SeedApprovedRequisitionAsync(
        itemPrices: new[] { 100m, 250m }, currencyCode: "AED");

    var res = await md.GetAsync($"/api/requisitions/{reqId}");
    res.EnsureSuccessStatusCode();
    var detail = await res.Content.ReadFromJsonAsync<RequisitionDetail>();

    Assert.NotNull(detail);
    Assert.NotNull(detail!.Approval);
    Assert.NotNull(detail.Approval!.Items);
    Assert.Equal(2, detail.Approval.Items!.Count);
    Assert.Contains(detail.Approval.Items, i => i.PricePerKg == 100m);
    Assert.Contains(detail.Approval.Items, i => i.PricePerKg == 250m);
}
```

If `Factory.SeedApprovedRequisitionAsync` doesn't exist, inspect how other tests seed approvals (search for `new QuotationApproval` or `new ApprovalItem` in the tests folder) and inline the seeding.

- [ ] **Step 2.2: Run test to verify it fails**

Run:
```bash
dotnet test --filter "FullyQualifiedName~RequisitionWorkflowTests.GetById_ApprovedRequisition_IncludesItemPrices" --nologo
```
Expected: fails with compile error (`ApprovalSummary.Items` doesn't exist) or assertion failure.

- [ ] **Step 2.3: Extend DTOs**

Replace `RequisitionDtos.cs:33`:

```csharp
public record ApprovalSummary(
    bool IsApproved,
    string? Notes,
    DateTime ApprovedAt,
    List<ApprovalItemPrice>? Items);

public record ApprovalItemPrice(
    int RequisitionItemId,
    decimal PricePerKg,
    decimal? PricePerKgForeign);
```

The choice to expose `PricePerKg` (= `SalesPricePerKgAed`) is deliberate — the UI needs the quoted AED price per kg. `PricePerKgForeign` is optional for future use; V2.0 mobile displays AED price. Margin / MaterialCostPct / OtherCostPct are **not** exposed — role visibility rules keep them internal.

- [ ] **Step 2.4: Update controller Get action**

Replace the projection at `RequisitionsController.cs:110-113` (the `q.Approvals.Where(...).Select(...).FirstOrDefault()` block). You need to `.Include(r => r.Approvals).ThenInclude(a => a.Items)` at line 96, and project with items:

At `RequisitionsController.cs:92-97`, change:
```csharp
var q = await db.QuotationRequests
    .Include(r => r.Items).ThenInclude(ri => ri.Item)
    .Include(r => r.Customer)
    .Include(r => r.Branch).Include(r => r.SalesPerson)
    .Include(r => r.Approvals).ThenInclude(a => a.Items)
    .FirstOrDefaultAsync(r => r.Id == id);
```

And at lines 110-113, change the approval projection to:
```csharp
q.Approvals
    .Where(a => !a.IsSuperseded)
    .Select(a => new ApprovalSummary(
        a.IsApproved, a.Notes, a.ApprovedAt,
        a.Items.Select(ai => new ApprovalItemPrice(
            ai.RequisitionItemId,
            ai.SalesPricePerKgAed,
            ai.SalesPricePerKgForeign)).ToList()))
    .FirstOrDefault()));
```

- [ ] **Step 2.5: Run test to verify pass**

Run:
```bash
dotnet test --filter "FullyQualifiedName~RequisitionWorkflowTests.GetById_ApprovedRequisition_IncludesItemPrices" --nologo
```
Expected: pass.

- [ ] **Step 2.6: Run full suite**

```bash
dotnet test --nologo
```
Expected: all previously-passing tests still pass.

- [ ] **Step 2.7: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs \
        BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs \
        BomPriceApproval.Tests/Requisitions/RequisitionWorkflowTests.cs
git commit -m "$(cat <<'EOF'
feat(api): expose approval item prices on requisition detail

ApprovalSummary DTO extended with optional Items list of
ApprovalItemPrice (RequisitionItemId, PricePerKg, PricePerKgForeign).
Margin / cost breakdown remains internal — not exposed.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Mobile — update TypeScript types

**Files:**
- Modify: `bom-mobile/src/types/api.ts:69-73`

- [ ] **Step 3.1: Extend ApprovalSummary type**

At `bom-mobile/src/types/api.ts:69-73`, replace:

```ts
export interface ApprovalItemPrice {
  requisitionItemId: number;
  pricePerKg: number;
  pricePerKgForeign: number | null;
}

export interface ApprovalSummary {
  isApproved: boolean;
  notes: string | null;
  approvedAt: string;
  items: ApprovalItemPrice[] | null;
}
```

- [ ] **Step 3.2: Verify tsc**

Run:
```bash
cd bom-mobile && npx tsc --noEmit && cd ..
```
Expected: 0 errors. (Existing `r.approval.notes` and `r.approval.isApproved` usages in `(sales)/[id].tsx` and `(md)/[id].tsx` keep working.)

- [ ] **Step 3.3: Commit**

```bash
git add bom-mobile/src/types/api.ts
git commit -m "$(cat <<'EOF'
types(mobile): extend ApprovalSummary with approval items + per-kg price

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Mobile — formatCurrency helper

**Files:**
- Modify: `bom-mobile/src/utils/numbers.ts`
- Modify: `bom-mobile/__tests__/numbers.test.ts`

- [ ] **Step 4.1: Write failing tests**

Append to `bom-mobile/__tests__/numbers.test.ts`:

```ts
import { formatCurrency } from "../src/utils/numbers";

describe("formatCurrency", () => {
  it("formats AED with thousand separators and 2 decimals", () => {
    expect(formatCurrency(2500, "AED")).toBe("AED 2,500.00");
  });

  it("handles zero", () => {
    expect(formatCurrency(0, "AED")).toBe("AED 0.00");
  });

  it("handles decimal precision", () => {
    expect(formatCurrency(125.5, "USD")).toBe("USD 125.50");
  });

  it("handles null / undefined / NaN", () => {
    expect(formatCurrency(null, "AED")).toBe("-");
    expect(formatCurrency(undefined, "AED")).toBe("-");
    expect(formatCurrency(Number.NaN, "AED")).toBe("-");
  });
});
```

- [ ] **Step 4.2: Run test to verify it fails**

Run:
```bash
cd bom-mobile && npx jest numbers --silent && cd ..
```
Expected: fails — `formatCurrency is not a function`.

- [ ] **Step 4.3: Implement helper**

Append to `bom-mobile/src/utils/numbers.ts`:

```ts
const currencyFormatter = new Intl.NumberFormat("en-US", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

export function formatCurrency(
  n: number | null | undefined,
  code: string
): string {
  if (n == null || Number.isNaN(n)) return "-";
  return `${code} ${currencyFormatter.format(n)}`;
}
```

- [ ] **Step 4.4: Run test to verify pass**

Run:
```bash
cd bom-mobile && npx jest numbers --silent && cd ..
```
Expected: all 4 new cases pass; existing `formatMoney` / `formatPct` tests still pass.

- [ ] **Step 4.5: Commit**

```bash
git add bom-mobile/src/utils/numbers.ts bom-mobile/__tests__/numbers.test.ts
git commit -m "$(cat <<'EOF'
feat(mobile): add formatCurrency helper

Code-prefixed (e.g. "AED 2,500.00") with 2-decimal precision and
thousand separators. Null/NaN → "-" placeholder.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Mobile — ItemPriceBlock component

**Files:**
- Create: `bom-mobile/src/components/ItemPriceBlock.tsx`
- Create: `bom-mobile/__tests__/itemPriceBlock.test.tsx`

- [ ] **Step 5.1: Write failing test**

Create `bom-mobile/__tests__/itemPriceBlock.test.tsx`:

```tsx
import React from "react";
import { render } from "@testing-library/react-native";
import { ItemPriceBlock } from "../src/components/ItemPriceBlock";

describe("ItemPriceBlock", () => {
  it("renders price per kg and line total for Approved", () => {
    const { getByText } = render(
      <ItemPriceBlock expectedQty={20} pricePerKg={125} currencyCode="AED" />
    );
    expect(getByText(/Price \/ kg/i)).toBeTruthy();
    expect(getByText("AED 125.00")).toBeTruthy();
    expect(getByText(/Line total/i)).toBeTruthy();
    expect(getByText("AED 2,500.00")).toBeTruthy();
  });

  it("renders zero price as AED 0.00", () => {
    const { getByText } = render(
      <ItemPriceBlock expectedQty={5} pricePerKg={0} currencyCode="AED" />
    );
    expect(getByText("AED 0.00")).toBeTruthy();
  });
});
```

- [ ] **Step 5.2: Run test to verify it fails**

Run:
```bash
cd bom-mobile && npx jest itemPriceBlock --silent && cd ..
```
Expected: fails — module not found.

- [ ] **Step 5.3: Implement component**

Create `bom-mobile/src/components/ItemPriceBlock.tsx`:

```tsx
import { Text, View } from "react-native";
import { formatCurrency } from "@/utils/numbers";

interface Props {
  expectedQty: number;
  pricePerKg: number;
  currencyCode: string;
}

export function ItemPriceBlock({ expectedQty, pricePerKg, currencyCode }: Props) {
  const lineTotal = expectedQty * pricePerKg;
  return (
    <View style={{ marginTop: 10, paddingTop: 10, borderTopWidth: 1, borderTopColor: "#f1f5f9" }}>
      <Row label="Price / kg" value={formatCurrency(pricePerKg, currencyCode)} />
      <Row label="Line total" value={formatCurrency(lineTotal, currencyCode)} bold />
    </View>
  );
}

function Row({ label, value, bold = false }: { label: string; value: string; bold?: boolean }) {
  return (
    <View style={{ flexDirection: "row", justifyContent: "space-between", paddingVertical: 3 }}>
      <Text style={{ fontSize: 13, color: "#64748b" }}>{label}</Text>
      <Text style={{ fontSize: 14, color: "#0f172a", fontWeight: bold ? "700" : "500" }}>
        {value}
      </Text>
    </View>
  );
}
```

- [ ] **Step 5.4: Run test to verify pass**

Run:
```bash
cd bom-mobile && npx jest itemPriceBlock --silent && cd ..
```
Expected: both cases pass.

- [ ] **Step 5.5: Commit**

```bash
git add bom-mobile/src/components/ItemPriceBlock.tsx bom-mobile/__tests__/itemPriceBlock.test.tsx
git commit -m "$(cat <<'EOF'
feat(mobile): add ItemPriceBlock component

Shows "Price / kg" + "Line total" for an approved item, using the
shared formatCurrency helper. Parallel to ItemCardShell/SectionCard —
inline-styled, reusable.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Mobile — apply ItemPriceBlock to Sales detail

**Files:**
- Modify: `bom-mobile/app/(sales)/[id].tsx:119-152` (item map block)

- [ ] **Step 6.1: Import and wire ItemPriceBlock**

At `bom-mobile/app/(sales)/[id].tsx`, add import near existing imports:

```tsx
import { ItemPriceBlock } from "@/components/ItemPriceBlock";
```

Then, inside the item map at the current position after the `<ItemStageBadge>` block (around line 149), add a conditional price block **before** the closing `</ItemCardShell>`:

```tsx
{r.status === "Approved" && r.approval?.items ? (() => {
  const approvalItem = r.approval.items?.find(ai => ai.requisitionItemId === it.id);
  return approvalItem ? (
    <ItemPriceBlock
      expectedQty={it.expectedQty}
      pricePerKg={approvalItem.pricePerKg}
      currencyCode={r.currencyCode}
    />
  ) : null;
})() : null}
```

- [ ] **Step 6.2: Verify tsc + jest**

Run:
```bash
cd bom-mobile && npx tsc --noEmit && npx jest --silent && cd ..
```
Expected: both clean.

- [ ] **Step 6.3: Commit**

```bash
git add "bom-mobile/app/(sales)/[id].tsx"
git commit -m "$(cat <<'EOF'
feat(mobile): show approved item prices on sales detail page

Uses ItemPriceBlock when status === "Approved" and approval.items
contains a matching entry. Hidden for all other statuses.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Mobile — apply ItemPriceBlock to MD detail

**Files:**
- Modify: `bom-mobile/app/(md)/[id].tsx`

The MD detail page has a similar structure to the Sales detail page. This task mirrors Task 6 for `(md)/[id].tsx`.

- [ ] **Step 7.1: Read current MD detail to locate item map block**

Run:
```bash
cat "bom-mobile/app/(md)/[id].tsx"
```
Look for where items are mapped (similar to the Sales page, probably around an `r.items.map(...)` loop or equivalent).

- [ ] **Step 7.2: Add import + conditional price block**

Add near top:
```tsx
import { ItemPriceBlock } from "@/components/ItemPriceBlock";
```

Inside the items loop, after the item description/qty/stage-badge markup, add (same pattern as Task 6):

```tsx
{r.status === "Approved" && r.approval?.items ? (() => {
  const approvalItem = r.approval.items?.find(ai => ai.requisitionItemId === it.id);
  return approvalItem ? (
    <ItemPriceBlock
      expectedQty={it.expectedQty}
      pricePerKg={approvalItem.pricePerKg}
      currencyCode={r.currencyCode}
    />
  ) : null;
})() : null}
```

Field names (`it.id`, `it.expectedQty`, `r.currencyCode`, `r.approval?.items`) match the existing types. If the MD detail screen uses a different variable binding (e.g., `item` instead of `it`), adjust accordingly.

- [ ] **Step 7.3: Verify tsc + jest**

Run:
```bash
cd bom-mobile && npx tsc --noEmit && npx jest --silent && cd ..
```
Expected: both clean.

- [ ] **Step 7.4: Commit**

```bash
git add "bom-mobile/app/(md)/[id].tsx"
git commit -m "$(cat <<'EOF'
feat(mobile): show approved item prices on MD detail page

Parallels the sales detail change — ItemPriceBlock when status is
Approved and approval items include a matching entry.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Mobile — StatusChipRow component

**Files:**
- Create: `bom-mobile/src/components/StatusChipRow.tsx`
- Create: `bom-mobile/__tests__/statusChipRow.test.tsx`

- [ ] **Step 8.1: Write failing tests**

Create `bom-mobile/__tests__/statusChipRow.test.tsx`:

```tsx
import React from "react";
import { render, fireEvent } from "@testing-library/react-native";
import { StatusChipRow, CHIP_TO_STATUSES, CHIPS } from "../src/components/StatusChipRow";

describe("StatusChipRow", () => {
  it("renders all 6 chips", () => {
    const onChange = jest.fn();
    const { getByText } = render(<StatusChipRow active="MD review" onChange={onChange} />);
    for (const c of CHIPS) {
      expect(getByText(c)).toBeTruthy();
    }
  });

  it("invokes onChange with the chip label when pressed", () => {
    const onChange = jest.fn();
    const { getByText } = render(<StatusChipRow active="All" onChange={onChange} />);
    fireEvent.press(getByText("BOM"));
    expect(onChange).toHaveBeenCalledWith("BOM");
  });
});

describe("CHIP_TO_STATUSES", () => {
  it("All returns empty array (no filter)", () => {
    expect(CHIP_TO_STATUSES["All"]).toEqual([]);
  });

  it("BOM groups BomPending + BomInProgress", () => {
    expect(CHIP_TO_STATUSES["BOM"]).toEqual(["BomPending", "BomInProgress"]);
  });

  it("Costing groups CostingPending + CostingInProgress", () => {
    expect(CHIP_TO_STATUSES["Costing"]).toEqual(["CostingPending", "CostingInProgress"]);
  });

  it("MD review maps to MdReview only", () => {
    expect(CHIP_TO_STATUSES["MD review"]).toEqual(["MdReview"]);
  });
});
```

- [ ] **Step 8.2: Run tests to verify they fail**

Run:
```bash
cd bom-mobile && npx jest statusChipRow --silent && cd ..
```
Expected: fails — module not found.

- [ ] **Step 8.3: Implement component**

Create `bom-mobile/src/components/StatusChipRow.tsx`:

```tsx
import { Pressable, ScrollView, Text } from "react-native";
import * as Haptics from "expo-haptics";
import type { RequisitionStatus } from "@/types/api";

export type ChipLabel = "All" | "BOM" | "Costing" | "MD review" | "Approved" | "Rejected";

export const CHIPS: ChipLabel[] = ["All", "BOM", "Costing", "MD review", "Approved", "Rejected"];

export const CHIP_TO_STATUSES: Record<ChipLabel, RequisitionStatus[]> = {
  "All":       [],
  "BOM":       ["BomPending", "BomInProgress"],
  "Costing":   ["CostingPending", "CostingInProgress"],
  "MD review": ["MdReview"],
  "Approved":  ["Approved"],
  "Rejected":  ["Rejected"],
};

interface Props {
  active: ChipLabel;
  onChange: (label: ChipLabel) => void;
}

export function StatusChipRow({ active, onChange }: Props) {
  return (
    <ScrollView
      horizontal
      showsHorizontalScrollIndicator={false}
      contentContainerStyle={{ paddingHorizontal: 14, paddingVertical: 8, gap: 6 }}
      style={{ backgroundColor: "#f8fafc", borderBottomWidth: 1, borderBottomColor: "#e2e8f0" }}
    >
      {CHIPS.map((label) => {
        const isActive = label === active;
        return (
          <Pressable
            key={label}
            onPress={async () => {
              await Haptics.selectionAsync();
              onChange(label);
            }}
            style={{
              paddingHorizontal: 12,
              paddingVertical: 6,
              borderRadius: 999,
              borderWidth: 1,
              backgroundColor: isActive ? "#1e40af" : "#ffffff",
              borderColor: isActive ? "#1e40af" : "#cbd5e1",
            }}
          >
            <Text style={{
              color: isActive ? "#ffffff" : "#334155",
              fontSize: 12,
              fontWeight: "600",
            }}>
              {label}
            </Text>
          </Pressable>
        );
      })}
    </ScrollView>
  );
}
```

- [ ] **Step 8.4: Run tests to verify they pass**

Run:
```bash
cd bom-mobile && npx jest statusChipRow --silent && cd ..
```
Expected: all cases pass.

- [ ] **Step 8.5: Commit**

```bash
git add bom-mobile/src/components/StatusChipRow.tsx bom-mobile/__tests__/statusChipRow.test.tsx
git commit -m "$(cat <<'EOF'
feat(mobile): add StatusChipRow component with chip-to-statuses map

6 pipeline-grouped chips (All / BOM / Costing / MD review /
Approved / Rejected). Horizontal scroll, haptic on press.
CHIP_TO_STATUSES map exported for use in list query builders.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Mobile — enhance MD pending screen with search + chips

**Files:**
- Modify: `bom-mobile/app/(md)/pending.tsx`

- [ ] **Step 9.1: Read current pending screen**

Already read during brainstorm (see spec). Key structure to preserve:
- `useMdPending` infinite query — will become generic `useMdRequisitions` with params
- `RequisitionCard` list render
- EmptyState / ErrorBanner / RefreshControl — keep
- Stagger Moti animations — keep

- [ ] **Step 9.2: Replace query hook + top of component**

Replace the `useMdPending` hook definition (`bom-mobile/app/(md)/pending.tsx:20-33`) with:

```tsx
import { useDebouncedValue } from "@/hooks/useDebouncedValue"; // see 9.2b

function useMdRequisitions(statuses: string[], search: string) {
  return useInfiniteQuery({
    queryKey: [...requisitionKeys.list(), "mdList", { statuses: statuses.join(","), search }],
    queryFn: async ({ pageParam }) => {
      const params = new URLSearchParams();
      for (const s of statuses) params.append("status", s);
      if (search) params.append("search", search);
      params.append("page", String(pageParam));
      params.append("pageSize", String(PAGE_SIZE));

      const res = await api.get<RequisitionListItem[]>(
        `/api/requisitions?${params.toString()}`
      );
      return res.data;
    },
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.length < PAGE_SIZE ? undefined : allPages.length + 1,
  });
}
```

- [ ] **Step 9.2b: Add useDebouncedValue hook**

Create `bom-mobile/src/hooks/useDebouncedValue.ts`:

```ts
import { useEffect, useState } from "react";

export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(t);
  }, [value, delayMs]);
  return debounced;
}
```

- [ ] **Step 9.3: Rewrite the component body**

Replace the body of `MdPendingApprovals` (`bom-mobile/app/(md)/pending.tsx:35-157`) with:

```tsx
export default function MdRequisitionsList() {
  const router = useRouter();
  const { logout } = useAuth();

  const [activeChip, setActiveChip] = useState<ChipLabel>("MD review");
  const [searchInput, setSearchInput] = useState("");
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  const statuses = CHIP_TO_STATUSES[activeChip];

  const q = useMdRequisitions(statuses, debouncedSearch);
  const items: RequisitionListItem[] = q.data?.pages.flat() ?? [];

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
        <Text style={{ color: "#1e40af", fontSize: 15, fontWeight: "600" }}>
          Log out
        </Text>
      </Pressable>
    </>
  );

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />

      <ScreenHeader
        label="MANAGING DIRECTOR"
        title="Requisitions"
        count={items.length}
        right={HeaderRight}
      />

      <View style={{ paddingHorizontal: 14, paddingTop: 8, paddingBottom: 2, backgroundColor: "#f8fafc" }}>
        <TextInput
          value={searchInput}
          onChangeText={setSearchInput}
          placeholder="Search REQ-xxxx or customer..."
          placeholderTextColor="#94a3b8"
          style={{
            borderWidth: 1,
            borderColor: "#cbd5e1",
            backgroundColor: "#ffffff",
            borderRadius: 10,
            paddingHorizontal: 12,
            paddingVertical: 9,
            fontSize: 14,
            color: "#0f172a",
          }}
          autoCorrect={false}
          autoCapitalize="none"
        />
      </View>

      <StatusChipRow active={activeChip} onChange={setActiveChip} />

      {q.isPending ? (
        <LoadingView variant="list" />
      ) : q.isError ? (
        <View style={{ padding: 16 }}>
          <ErrorBanner
            message={q.error instanceof Error ? q.error.message : "Failed to load requisitions"}
            onRetry={() => q.refetch()}
          />
        </View>
      ) : (
        <FlatList
          data={items}
          keyExtractor={(r) => String(r.id)}
          contentContainerStyle={{ padding: 14, paddingTop: 6 }}
          overScrollMode="never"
          bounces={false}
          onEndReached={() => { if (q.hasNextPage && !q.isFetchingNextPage) q.fetchNextPage(); }}
          onEndReachedThreshold={0.5}
          refreshControl={
            <RefreshControl
              refreshing={q.isRefetching && !q.isFetchingNextPage}
              onRefresh={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                q.refetch();
              }}
              tintColor="#1e40af"
              colors={["#1e40af"]}
            />
          }
          renderItem={({ item, index }) => (
            <MotiView
              from={{ opacity: 0, translateY: 14 }}
              animate={{ opacity: 1, translateY: 0 }}
              transition={{
                type: "spring",
                damping: 16,
                stiffness: 140,
                delay: index < STAGGER_CAP ? 200 + index * 80 : 0,
              }}
            >
              <RequisitionCard item={item} onPress={(id) => router.push(`/(md)/${id}`)} />
            </MotiView>
          )}
          ListFooterComponent={
            q.isFetchingNextPage ? (
              <View style={{ paddingVertical: 20, alignItems: "center" }}>
                <ActivityIndicator color="#1e40af" />
                <Text style={{ color: "#64748b", fontSize: 14, marginTop: 8 }}>Loading more…</Text>
              </View>
            ) : q.hasNextPage === false && items.length > PAGE_SIZE ? (
              <View style={{ paddingVertical: 20, alignItems: "center" }}>
                <Text style={{ color: "#94a3b8", fontSize: 13 }}>End of list · {items.length} total</Text>
              </View>
            ) : null
          }
          ListEmptyComponent={
            <EmptyState
              title={activeChip === "MD review" ? "All caught up" : "No requisitions"}
              hint={
                activeChip === "MD review"
                  ? "Nothing pending your review right now."
                  : debouncedSearch
                    ? `No matches for "${debouncedSearch}"`
                    : `No requisitions in the ${activeChip} stage.`
              }
              icon={<Text style={{ fontSize: 32 }}>{activeChip === "MD review" ? "✓" : "∅"}</Text>}
            />
          }
        />
      )}
    </View>
  );
}
```

- [ ] **Step 9.4: Update imports at top of `(md)/pending.tsx`**

Replace the imports block with:

```tsx
import { useState } from "react";
import {
  ActivityIndicator,
  FlatList,
  Pressable,
  RefreshControl,
  Text,
  TextInput,
  View,
} from "react-native";
import { Stack, useRouter } from "expo-router";
import { useInfiniteQuery } from "@tanstack/react-query";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { api } from "@/api/client";
import { requisitionKeys } from "@/api/requisitions";
import { RequisitionCard } from "@/components/RequisitionCard";
import { ScreenHeader } from "@/components/ScreenHeader";
import { EmptyState } from "@/components/EmptyState";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";
import { NotificationBell } from "@/components/NotificationBell";
import { StatusChipRow, CHIP_TO_STATUSES, type ChipLabel } from "@/components/StatusChipRow";
import { useDebouncedValue } from "@/hooks/useDebouncedValue";
import { useAuth } from "@/auth/AuthContext";
import type { RequisitionListItem } from "@/types/api";
```

- [ ] **Step 9.5: Verify tsc + jest**

Run:
```bash
cd bom-mobile && npx tsc --noEmit && npx jest --silent && cd ..
```
Expected: 0 tsc errors, all jest tests pass (no regressions).

- [ ] **Step 9.6: Commit**

```bash
git add bom-mobile/app/\(md\)/pending.tsx bom-mobile/src/hooks/useDebouncedValue.ts
git commit -m "$(cat <<'EOF'
feat(mobile): MD all-requisitions list with search + 6 status chips

- Replaces the pending-only screen with a full list, preserving the
  dashboard's "Pending approvals" entry point via default chip
  (MD review).
- Debounced search (300 ms) matches RefNo OR customer name.
- Infinite scroll + stagger animations preserved.
- New useDebouncedValue hook.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: Phone smoke + merge

- [ ] **Step 10.1: Start backend + Metro**

Run backend in one shell:
```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project BomPriceApproval.API --urls "http://0.0.0.0:7300"
```
Verify: `curl -sS -o /dev/null -w "%{http_code}\n" http://localhost:7300/swagger/index.html` → `200`.

Run Metro in another shell:
```bash
cd bom-mobile && npx expo start --clear
```
Wait for `Bundled` line to appear.

- [ ] **Step 10.2: Phone smoke — MD list**

Connect phone to Expo Go → `exp://192.168.1.216:8081`. Login as MD (any seeded MD account). Verify:
- Header: "MANAGING DIRECTOR / Requisitions" + count + bell + Log out
- Search input visible, 6 chips below
- Default chip `MD review` active; list shows only MdReview items
- Tap each chip → list updates; `All` shows everything
- Type a customer-name fragment in search → debounces, filters after ~300 ms
- Type a RefNo fragment → also matches
- Clear search → all back
- Infinite scroll still works
- Pull-to-refresh still works

- [ ] **Step 10.3: Phone smoke — approved prices**

Using the MD account (or a SalesPerson whose branch includes an Approved req):
- Open any Approved requisition → item card shows `Price / kg` + `Line total`
- Open any non-Approved requisition → no price block (unchanged)
- Open a Rejected requisition → no price block, rose rejection box still shows
- Cross-check AED math: `expectedQty × pricePerKg` matches line total

- [ ] **Step 10.4: Fast-forward merge**

Run:
```bash
git checkout master
git merge --ff-only feature/mobile-v2-md-list-prices
git log --oneline master -12
```
Verify: master now contains all V2.0 commits.

- [ ] **Step 10.5: Delete feature branch**

Run:
```bash
git branch -d feature/mobile-v2-md-list-prices
git worktree list
git branch -v | head -5
```
Verify: branch deleted, no orphan worktree.

- [ ] **Step 10.6: No push (security blocker)**

Per `project_repo_push_status.md`: PG password still present in pre-hardening commit. Do **not** push to any remote until security hardening (Phase 6) completes.

---

## Final verification

- [ ] Backend: `dotnet test --nologo` → all green (pre-existing flaky auth-timing may retry)
- [ ] Mobile: `npx tsc --noEmit` + `npx jest --silent` → clean + all green
- [ ] Git: master has all 9 feature commits; feature branch deleted; no worktrees beyond main
- [ ] Phone: all smoke checklist items pass

---

## Self-review notes

- **Spec coverage:** Each spec section has at least one task — API list extension (Task 1), API detail extension (Task 2), TypeScript types (Task 3), currency helper (Task 4), ItemPriceBlock (Tasks 5-7), MD list UI (Tasks 8-9), phone smoke + merge (Task 10).
- **Placeholders:** None — every step has concrete code or commands.
- **Type consistency:** `ApprovalItemPrice` is used consistently (backend record + TS interface). `CHIP_TO_STATUSES` is exported from Task 8 and imported in Task 9. `ChipLabel` type flows from Task 8 → Task 9.
- **Role visibility:** Preserved — backend DTO never exposes Margin/MaterialCostPct/OtherCostPct to any mobile consumer.

---

## Out of scope (deferred per spec)

- Date-range filter, chip live counts, sort customization — future polish.
- Accountant mobile stack — V2.1 separate spec/plan.
- Android EAS deploy (Plan 3b) — executed after backend production hardening.
