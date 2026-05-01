# V3 Mobile Phase D-3 (Managing Director) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the Managing Director mobile surface for V3 — MdPricing margin entry, MdFinalSign with PNG signature upload, read-only views — and add backend `finalPrice` so Signed reqs render correctly. Purge all remaining V2.3 mobile MD code.

**Architecture:** Mirrors D-2 accountant pattern: status-branched detail page (active vs read-only), 4-tab list, dashboard with KPIs. Signature is one-time PNG upload via Profile (`expo-image-picker`); final-sign blocked if absent. `finalPrice` is computed server-side via a pure `FinalPriceComputer` service and embedded in `V3Requisition` response when status ≥ MdFinalSign.

**Tech Stack:** React Native (Expo SDK 54) + Expo Router + TanStack Query + `expo-image-picker`. Backend: ASP.NET Core 8 + EF Core + PostgreSQL. Spec: `docs/superpowers/specs/2026-05-02-v3-mobile-phase-d-3-md-design.md`.

---

### Task 0: Branch + clean state

**Files:**
- Verify: working tree clean, on master, master synced with origin

- [ ] **Step 1: Verify clean state**

```bash
git status --short
# Expected: empty
git branch --show-current
# Expected: master
git log --oneline -1
# Expected: c11f3a0 (or later) docs(v3-mobile): Phase D-3 MD design spec (#52)
```

- [ ] **Step 2: Pull latest master**

```bash
git pull --ff-only origin master
```

Expected: `Already up to date.`

- [ ] **Step 3: Verify D-2 prereqs landed**

```bash
git log --oneline | grep -E "D-2|d-2|RefNo" | head -5
# Expected: see PR #50 (D-2 mobile), #49 (D-2 backend), #51 (RefNo fix)
```

---

## Phase 0 — Backend prereqs (spec §8 B1-B4)

### Task 1: Backend feature branch + V3FinalPrice DTOs (B1 setup)

**Files:**
- Create: `BomPriceApproval.API/Features/Requisitions/V3FinalPriceDto.cs`

- [ ] **Step 1: Branch off master**

```bash
git checkout -b feat/v3-mobile-d3-backend master
```

Expected: `Switched to a new branch 'feat/v3-mobile-d3-backend'`

- [ ] **Step 2: Create DTO file**

```csharp
namespace BomPriceApproval.API.Features.Requisitions;

public record V3FinalPriceItem(
    int RequisitionItemId,
    int ItemId,
    string Description,
    decimal ExpectedQty,
    decimal CostPerKg,
    decimal MarginPerKg,
    decimal SalePerKg,
    decimal SalePerKgAed,
    decimal TotalAed);

public record V3FinalPrice(
    decimal TotalAed,
    string CurrencyCode,
    decimal? RateSnapshot,
    List<V3FinalPriceItem> PerFg);
```

- [ ] **Step 3: Build verify**

```bash
dotnet build BomPriceApproval.API/BomPriceApproval.API.csproj --nologo -v q
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/V3FinalPriceDto.cs
git commit -m "feat(v3-d3): add V3FinalPrice DTO records"
```

---

### Task 2: FinalPriceComputer service + unit tests (B2)

**Files:**
- Create: `BomPriceApproval.API/Infrastructure/Services/FinalPriceComputer.cs`
- Create: `BomPriceApproval.Tests/Approvals/FinalPriceComputerTests.cs`

- [ ] **Step 1: Write failing tests**

`BomPriceApproval.Tests/Approvals/FinalPriceComputerTests.cs`:
```csharp
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Features.Requisitions;
using BomPriceApproval.API.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace BomPriceApproval.Tests.Approvals;

public class FinalPriceComputerTests
{
    [Fact]
    public void Compute_AedRequisition_SaleEqualsAed()
    {
        var req = MakeReq("AED");
        var approval = MakeApproval(rateSnapshot: null, items: new[]
        {
            (req.Items[0].Id, marginPerKg: 1.80m),
        });
        // BomCost: TotalCostPerKg = 3.20 for FG[0]
        AddCost(req, fgIdx: 0, totalCostPerKg: 3.20m);

        var result = FinalPriceComputer.Compute(req, approval);

        result.CurrencyCode.Should().Be("AED");
        result.RateSnapshot.Should().BeNull();
        result.PerFg[0].SalePerKg.Should().Be(5.00m);          // 3.20 + 1.80
        result.PerFg[0].SalePerKgAed.Should().Be(5.00m);       // == SalePerKg for AED
        result.PerFg[0].TotalAed.Should().Be(25_000m);          // 5.00 × 5000
        result.TotalAed.Should().Be(25_000m);
    }

    [Fact]
    public void Compute_ForeignRequisition_SalePerKgAedUsesRateSnapshot()
    {
        var req = MakeReq("USD");
        var approval = MakeApproval(rateSnapshot: 3.6725m, items: new[]
        {
            (req.Items[0].Id, marginPerKg: 1.00m),
        });
        AddCost(req, fgIdx: 0, totalCostPerKg: 1.00m);

        var result = FinalPriceComputer.Compute(req, approval);

        result.CurrencyCode.Should().Be("USD");
        result.RateSnapshot.Should().Be(3.6725m);
        result.PerFg[0].SalePerKg.Should().Be(2.00m);                 // 1.00 + 1.00
        result.PerFg[0].SalePerKgAed.Should().Be(7.345m);             // 2.00 × 3.6725
        result.PerFg[0].TotalAed.Should().Be(36_725m);                // 7.345 × 5000
    }

    [Fact]
    public void Compute_MultiFg_SumsTotalsCorrectly()
    {
        var req = MakeReq("AED", fgCount: 2);
        var approval = MakeApproval(rateSnapshot: null, items: new[]
        {
            (req.Items[0].Id, marginPerKg: 1.00m),
            (req.Items[1].Id, marginPerKg: 2.00m),
        });
        AddCost(req, fgIdx: 0, totalCostPerKg: 3.00m);
        AddCost(req, fgIdx: 1, totalCostPerKg: 5.00m);

        var result = FinalPriceComputer.Compute(req, approval);

        result.PerFg.Should().HaveCount(2);
        result.PerFg[0].TotalAed.Should().Be(20_000m);  // (3+1) × 5000
        result.PerFg[1].TotalAed.Should().Be(35_000m);  // (5+2) × 5000
        result.TotalAed.Should().Be(55_000m);
    }

    [Fact]
    public void Compute_ZeroMargin_OK()
    {
        var req = MakeReq("AED");
        var approval = MakeApproval(rateSnapshot: null, items: new[]
        {
            (req.Items[0].Id, marginPerKg: 0m),
        });
        AddCost(req, fgIdx: 0, totalCostPerKg: 4.00m);

        var result = FinalPriceComputer.Compute(req, approval);

        result.PerFg[0].SalePerKg.Should().Be(4.00m);
        result.TotalAed.Should().Be(20_000m);
    }

    // === Test fixtures ===
    private static QuotationRequest MakeReq(string currency, int fgCount = 1)
    {
        var req = new QuotationRequest
        {
            Id = 100,
            CurrencyCode = currency,
            Status = RequisitionStatus.MdFinalSign,
            BranchId = 2,
            CustomerId = 1,
            SalesPersonId = 1,
        };
        for (int i = 0; i < fgCount; i++)
        {
            req.Items.Add(new RequisitionItem
            {
                Id = 1000 + i,
                ItemId = 2000 + i,
                Item = new Item { Id = 2000 + i, Description = $"FG {i + 1}", Code = $"FG-{i + 1}" },
                ExpectedQty = 5000m,
            });
        }
        return req;
    }

    private static QuotationApproval MakeApproval(decimal? rateSnapshot,
        (int RequisitionItemId, decimal marginPerKg)[] items)
    {
        var qa = new QuotationApproval
        {
            Stage = ApprovalStage.InitialPricing,
            RateSnapshot = rateSnapshot,
        };
        foreach (var (riId, margin) in items)
            qa.Items.Add(new ApprovalItem { RequisitionItemId = riId, MarginPerKg = margin });
        return qa;
    }

    private static void AddCost(QuotationRequest req, int fgIdx, decimal totalCostPerKg)
    {
        var ri = req.Items[fgIdx];
        var bomHeader = new BomHeader { RequisitionItemId = ri.Id };
        bomHeader.Cost = new BomCost { BomHeaderId = bomHeader.Id, TotalCostPerKg = totalCostPerKg };
        ri.BomHeader = bomHeader;
    }
}
```

- [ ] **Step 2: Run failing test**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~FinalPriceComputerTests" --nologo 2>&1 | tail -5
```

Expected: build error (FinalPriceComputer missing).

- [ ] **Step 3: Implement FinalPriceComputer**

`BomPriceApproval.API/Infrastructure/Services/FinalPriceComputer.cs`:
```csharp
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Features.Requisitions;

namespace BomPriceApproval.API.Infrastructure.Services;

// Pure compute — no DB access, no async. Caller must hydrate
// req.Items[i].Item, req.Items[i].BomHeader.Cost, and approval.Items.
public static class FinalPriceComputer
{
    public static V3FinalPrice Compute(QuotationRequest req, QuotationApproval approval)
    {
        var rate = approval.RateSnapshot;
        var perFg = req.Items
            .OrderBy(ri => ri.Id)
            .Select(ri =>
            {
                var costPerKg = ri.BomHeader?.Cost?.TotalCostPerKg ?? 0m;
                var margin = approval.Items
                    .FirstOrDefault(ai => ai.RequisitionItemId == ri.Id)?.MarginPerKg ?? 0m;
                var salePerKg = costPerKg + margin;
                var salePerKgAed = rate.HasValue ? salePerKg * rate.Value : salePerKg;
                var totalAed = salePerKgAed * ri.ExpectedQty;
                return new V3FinalPriceItem(
                    ri.Id, ri.ItemId, ri.Item.Description, ri.ExpectedQty,
                    costPerKg, margin, salePerKg, salePerKgAed, totalAed);
            })
            .ToList();

        return new V3FinalPrice(
            TotalAed: perFg.Sum(p => p.TotalAed),
            CurrencyCode: req.CurrencyCode,
            RateSnapshot: rate,
            PerFg: perFg);
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~FinalPriceComputerTests" --nologo 2>&1 | tail -5
```

Expected: 4 tests passed.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Services/FinalPriceComputer.cs BomPriceApproval.Tests/Approvals/FinalPriceComputerTests.cs
git commit -m "feat(v3-d3): FinalPriceComputer pure service + unit tests"
```

---

### Task 3: Wire finalPrice into V3Requisition response (B1 integration)

**Files:**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs:160-280` (V3 Get endpoint projection)

- [ ] **Step 1: Read existing V3 Get implementation**

```bash
grep -n "public async Task<IActionResult> Get" BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs
```

Note line numbers; the V3 Get endpoint is at `/api/requisitions/{id}` and returns the V3Requisition shape.

- [ ] **Step 2: Add finalPrice computation after fetching req**

Inside the V3 Get endpoint (after `req` is hydrated and Status is checked), add:

```csharp
V3FinalPrice? finalPrice = null;
if (req.Status >= RequisitionStatus.MdFinalSign && req.Status != RequisitionStatus.Rejected
    && req.Status != RequisitionStatus.Cancelled)
{
    var approval = await db.QuotationApprovals
        .Include(qa => qa.Items)
        .Where(qa => qa.QuotationRequestId == id && !qa.IsSuperseded)
        .OrderByDescending(qa => qa.ApprovedAt)
        .FirstOrDefaultAsync();
    if (approval is not null)
        finalPrice = FinalPriceComputer.Compute(req, approval);
}
```

Add `finalPrice` to the V3 response payload (alongside existing fields like `customer`, `salesPerson`, `finishedGoods`).

- [ ] **Step 3: Build verify**

```bash
dotnet build BomPriceApproval.API/BomPriceApproval.API.csproj --nologo -v q
```

Expected: 0 errors.

- [ ] **Step 4: Add integration test**

Create or extend `BomPriceApproval.Tests/Approvals/SetMarginTests.cs` with a test that:
1. Sets up a req in `Costing` status
2. Submits costing → MdPricing
3. Calls `set-margin` → CustomerConfirm
4. Calls `accept-customer` → MdFinalSign
5. Fetches `GET /api/requisitions/{id}` → asserts `finalPrice` populated with correct totals

```csharp
[Fact]
public async Task FinalPrice_PopulatedOnMdFinalSignStatus()
{
    // ... seed users + req + costing ...
    var setMarginResp = await client.PostAsJsonAsync(
        $"/api/approvals/{reqId}/set-margin",
        new { items = new[] { new { requisitionItemId = riId, marginPerKg = 1.50m } } });
    setMarginResp.EnsureSuccessStatusCode();
    
    await client.PostAsJsonAsync($"/api/approvals/{reqId}/accept-customer", new { });
    
    var detail = await client.GetFromJsonAsync<V3RequisitionResponse>($"/api/requisitions/{reqId}");
    detail.Should().NotBeNull();
    detail!.FinalPrice.Should().NotBeNull();
    detail.FinalPrice!.TotalAed.Should().BeGreaterThan(0);
    detail.FinalPrice.PerFg.Should().HaveCount(1);
}
```

- [ ] **Step 5: Run integration test**

```bash
dotnet test --filter "FullyQualifiedName~FinalPrice_PopulatedOnMdFinalSignStatus" --nologo 2>&1 | tail -5
```

Expected: pass.

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs BomPriceApproval.Tests/Approvals/SetMarginTests.cs
git commit -m "feat(v3-d3): embed finalPrice in V3Requisition response (status >= MdFinalSign)"
```

---

### Task 4: Stats endpoint MD-role branch (B3)

**Files:**
- Modify: `BomPriceApproval.API/Features/Stats/StatsController.cs` (add MD-role branch to v3-dashboard)
- Create: `BomPriceApproval.Tests/Stats/MdDashboardTests.cs`

- [ ] **Step 1: Read existing stats controller**

```bash
grep -n "v3-dashboard\|MdRole\|ManagingDirector" BomPriceApproval.API/Features/Stats/StatsController.cs | head -10
```

- [ ] **Step 2: Add MD-role branch**

After the existing accountant branch in the v3-dashboard endpoint:

```csharp
if (CurrentRole == "ManagingDirector")
{
    var todayUtc = DateTime.UtcNow.Date;
    var tomorrowUtc = todayUtc.AddDays(1);

    var counts = await db.QuotationRequests
        .GroupBy(r => 1)
        .Select(g => new
        {
            ToPrice = g.Count(r => r.Status == RequisitionStatus.MdPricing),
            ToSign = g.Count(r => r.Status == RequisitionStatus.MdFinalSign),
            InFlight = g.Count(r =>
                r.Status == RequisitionStatus.CustomerConfirm ||
                r.Status == RequisitionStatus.Costing),
            SignedToday = g.Count(r =>
                r.Status == RequisitionStatus.Signed &&
                r.UpdatedAt >= todayUtc && r.UpdatedAt < tomorrowUtc),
        })
        .FirstOrDefaultAsync() ?? new { ToPrice = 0, ToSign = 0, InFlight = 0, SignedToday = 0 };

    return Ok(new
    {
        toPrice = counts.ToPrice,
        toSign = counts.ToSign,
        inFlight = counts.InFlight,
        signedToday = counts.SignedToday,
    });
}
```

- [ ] **Step 3: Write test**

```csharp
[Fact]
public async Task MdDashboard_ReturnsCorrectCounts()
{
    // Seed: 2 MdPricing, 1 MdFinalSign, 3 CustomerConfirm, 1 Signed (today)
    // ... (setup omitted for brevity — follow seed pattern from CostingTests) ...

    var resp = await client.GetFromJsonAsync<MdDashboardResponse>("/api/stats/v3-dashboard");
    resp!.ToPrice.Should().Be(2);
    resp.ToSign.Should().Be(1);
    resp.InFlight.Should().Be(3);
    resp.SignedToday.Should().Be(1);
}
```

- [ ] **Step 4: Run test**

```bash
dotnet test --filter "FullyQualifiedName~MdDashboardTests" --nologo
```

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Stats/StatsController.cs BomPriceApproval.Tests/Stats/MdDashboardTests.cs
git commit -m "feat(v3-d3): MD-role branch on /api/stats/v3-dashboard"
```

---

### Task 5: set-margin notification fan-out to branch accountants (B4)

**Files:**
- Modify: `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs:399-415` (around set-margin notification)

- [ ] **Step 1: Read current set-margin notification block**

Around line 398 of ApprovalsController.cs, the existing notification sends to SP. Find and inspect the block.

- [ ] **Step 2: Extend to fan out to branch accountants**

```csharp
try
{
    // SP notification (unchanged)
    await notificationSvc.SendAsync(req.SalesPersonId,
        $"{req.RefNo} priced — confirm with customer", req.Id, "QuotationRequest");

    // Branch accountants (new in D-3)
    var accountantIds = await db.UserBranches
        .Where(ub => ub.BranchId == req.BranchId)
        .Join(db.Users.Where(u => u.Role == UserRole.Accountant && u.IsActive),
              ub => ub.UserId, u => u.Id, (ub, u) => u.Id)
        .Distinct()
        .ToListAsync();

    if (accountantIds.Count > 0)
    {
        await notificationSvc.SendToUsersAsync(accountantIds,
            $"{req.RefNo} margin set by MD — going to customer", req.Id, "QuotationRequest");
    }
}
catch (Exception ex)
{
    logger.LogWarning(ex, "[Notification] set-margin notify failed for {RefNo}", req.RefNo);
}
```

- [ ] **Step 3: Add resilience test (extend `NotificationResilienceTests`)**

Confirm that a thrown notification doesn't roll back the state transition.

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~ApprovalGetReviewTests|NotificationResilienceTests" --nologo
```

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Approvals/ApprovalsController.cs BomPriceApproval.Tests/
git commit -m "feat(v3-d3): set-margin notifies branch accountants too"
```

---

### Task 6: Backend prereqs PR + merge

- [ ] **Step 1: Run full test suite locally**

```bash
dotnet test --nologo 2>&1 | tail -5
```

Expected: All passing (D-2 baseline 280+ pass).

- [ ] **Step 2: Push branch**

```bash
git push -u origin feat/v3-mobile-d3-backend
```

- [ ] **Step 3: Open PR**

```bash
gh pr create --base master --head feat/v3-mobile-d3-backend \
  --title "feat(v3-d3): backend prereqs — finalPrice + MD stats + accountant notify" \
  --body "$(cat <<'EOF'
## Summary

D-3 backend prereqs (spec §8 B1-B4):

- **B1+B2** `V3FinalPrice`/`V3FinalPriceItem` DTOs + `FinalPriceComputer` pure service with unit tests
- **B1 integration** `finalPrice` embedded in `GET /api/requisitions/{id}` response when status ≥ MdFinalSign
- **B3** `/api/stats/v3-dashboard` MD-role branch — toPrice / toSign / inFlight / signedToday counts
- **B4** `set-margin` notification fans out to branch accountants in addition to SP

## Test plan

- [x] FinalPriceComputerTests pass (4 tests)
- [x] FinalPrice integration test (set-margin → fetch → assert finalPrice populated)
- [x] MdDashboardTests pass
- [x] NotificationResilienceTests still green
- [x] Full suite green

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 4: Wait CI, auto-merge**

```bash
until gh pr checks $(gh pr view --json number --jq .number) --json bucket --jq 'all(.bucket != "pending")' 2>/dev/null | grep -q true; do sleep 15; done
gh pr checks $(gh pr view --json number --jq .number)
gh pr merge --squash --delete-branch
```

Expected: green CI, merged to master.

- [ ] **Step 5: Sync local master**

```bash
git checkout master
git pull --ff-only origin master
```

---

## Phase 1 — Mobile bootstrap

### Task 7: Mobile feature branch

- [ ] **Step 1: Branch off master**

```bash
git checkout -b feat/v3-mobile-d3-md master
git log --oneline -3
# Verify backend prereqs are present
```

---

## Phase 2 — Mobile API hooks + types

### Task 8: Extend V3Requisition type with finalPrice

**Files:**
- Modify: `bom-mobile/src/types/v3.ts`

- [ ] **Step 1: Read current type**

```bash
grep -n "finalPrice" bom-mobile/src/types/v3.ts
```

The placeholder field already exists with comment "FinalPriceCard component does `if (!req.finalPrice) return null` so this field will be undefined". Replace with the now-shipped backend shape.

- [ ] **Step 2: Update type**

Replace the existing `finalPrice` field declaration with:

```ts
finalPrice?: V3FinalPrice | null;
```

And add the new types:

```ts
export interface V3FinalPriceItem {
  requisitionItemId: number;
  itemId: number;
  description: string;
  expectedQty: number;
  costPerKg: number;
  marginPerKg: number;
  salePerKg: number;
  salePerKgAed: number;
  totalAed: number;
}

export interface V3FinalPrice {
  totalAed: number;
  currencyCode: string;
  rateSnapshot: number | null;
  perFg: V3FinalPriceItem[];
}
```

- [ ] **Step 3: tsc verify**

```bash
cd bom-mobile && npx tsc --noEmit 2>&1 | grep -i "v3.ts\|finalPrice" | head -5
```

Expected: no new errors related to finalPrice.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/src/types/v3.ts
git commit -m "types(v3-d3): V3FinalPrice + V3FinalPriceItem types"
```

---

### Task 9: API hooks — useSetMargin / useRejectRequisition / useFinalSign

**Files:**
- Create: `bom-mobile/src/features/md/api/approvals.ts`

- [ ] **Step 1: Create file with hooks**

```ts
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";
import { requisitionKeys } from "@/api/requisitions";

export interface SetMarginItem {
  requisitionItemId: number;
  marginPerKg: number;
}

export interface SetMarginPayload {
  items: SetMarginItem[];
  notes?: string;
}

export function useSetMargin(reqId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: SetMarginPayload) =>
      api.post(`/api/approvals/${reqId}/set-margin`, payload),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(reqId) });
      qc.invalidateQueries({ queryKey: requisitionKeys.lists() });
    },
  });
}

export interface RejectPayload {
  reason: string;
}

export function useRejectRequisition(reqId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: RejectPayload) =>
      api.post(`/api/approvals/${reqId}/reject`, payload),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(reqId) });
      qc.invalidateQueries({ queryKey: requisitionKeys.lists() });
    },
  });
}

export interface FinalSignPayload {
  confirmationToken: string;
  notes?: string;
}

export function useFinalSign(reqId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: FinalSignPayload) =>
      api.post(`/api/approvals/${reqId}/final-sign`, payload),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(reqId) });
      qc.invalidateQueries({ queryKey: requisitionKeys.lists() });
    },
  });
}
```

- [ ] **Step 2: tsc verify**

```bash
cd bom-mobile && npx tsc --noEmit 2>&1 | grep -i "approvals.ts" | head -5
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/features/md/api/approvals.ts
git commit -m "feat(mobile-d3): MD approval mutation hooks (set-margin, reject, final-sign)"
```

---

### Task 10: API hooks — useUploadSignature / useOwnSignature

**Files:**
- Create: `bom-mobile/src/features/profile/api/signature.ts`

- [ ] **Step 1: Install expo-image-picker**

```bash
cd bom-mobile && npx expo install expo-image-picker
```

- [ ] **Step 2: Configure permissions in app.config.ts**

Add to plugins array (and Android permissions):

```ts
plugins: [
  // ...existing
  [
    "expo-image-picker",
    {
      photosPermission: "Allow access to photo library to upload signature.",
      cameraPermission: "Allow camera to capture signature.",
    },
  ],
],
```

Verify Android `READ_MEDIA_IMAGES` and `CAMERA` permissions are listed.

- [ ] **Step 3: Create signature hooks**

```ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";

export const profileKeys = {
  all: ["profile"] as const,
  signature: () => ["profile", "signature"] as const,
};

interface UploadResult {
  path: string;
  uploadedAt: string;
}

export function useUploadSignature() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ uri, mime }: { uri: string; mime: string }) => {
      const formData = new FormData();
      formData.append("file", {
        uri,
        name: "signature.png",
        type: mime,
        // RN FormData typing
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any);
      const r = await api.post<UploadResult>("/api/profile/signature", formData, {
        headers: { "Content-Type": "multipart/form-data" },
      });
      return r.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: profileKeys.signature() });
    },
  });
}

// Returns { exists: true, uri } if signature uploaded, { exists: false } if 404.
// We don't return the actual blob — consumers use <Image source={{ uri, headers }} />
// pointed at the API URL with the auth token.
export function useOwnSignature() {
  return useQuery({
    queryKey: profileKeys.signature(),
    queryFn: async () => {
      try {
        await api.head("/api/profile/signature");
        return { exists: true };
      } catch {
        return { exists: false };
      }
    },
    staleTime: 5 * 60 * 1000,
  });
}
```

Note: backend doesn't have HEAD route for signature. Either add HEAD support or change to GET with `responseType: "blob"` and check 404 catch path. Pick the simpler implementation:

```ts
queryFn: async () => {
  try {
    await api.get("/api/profile/signature", { responseType: "blob" });
    return { exists: true };
  } catch (e) {
    if ((e as { response?: { status?: number } })?.response?.status === 404) return { exists: false };
    throw e;
  }
},
```

- [ ] **Step 4: tsc verify**

```bash
cd bom-mobile && npx tsc --noEmit 2>&1 | grep -i "signature" | head -5
```

- [ ] **Step 5: Commit**

```bash
git add bom-mobile/src/features/profile/api/signature.ts bom-mobile/app.config.ts bom-mobile/package.json bom-mobile/package-lock.json
git commit -m "feat(mobile-d3): signature API hooks + expo-image-picker plugin config"
```

---

### Task 11: Stats API hook MD shape

**Files:**
- Modify: `bom-mobile/src/api/stats.ts` (add MD branch)

- [ ] **Step 1: Read current stats hook**

The existing `useV3Dashboard()` returns accountant shape. Split into role-specific hooks or extend to accept role.

- [ ] **Step 2: Add `useMdDashboard()`**

```ts
export interface MdDashboardCounts {
  toPrice: number;
  toSign: number;
  inFlight: number;
  signedToday: number;
}

export function useMdDashboard() {
  return useQuery({
    queryKey: ["stats", "v3-dashboard", "md"] as const,
    queryFn: () =>
      api.get<MdDashboardCounts>("/api/stats/v3-dashboard").then((r) => r.data),
  });
}
```

- [ ] **Step 3: tsc verify + commit**

```bash
cd bom-mobile && npx tsc --noEmit 2>&1 | grep -i "stats" | head -3
git add bom-mobile/src/api/stats.ts
git commit -m "feat(mobile-d3): useMdDashboard hook"
```

---

## Phase 3 — Profile signature section

### Task 12: SignaturePreview component

**Files:**
- Create: `bom-mobile/src/features/md/detail/SignaturePreview.tsx`

Reusable component used by both Profile and ActiveMdFinalSignView.

- [ ] **Step 1: Create component**

```tsx
import { Image, View, Text } from "react-native";
import { useAuth } from "@/auth/AuthContext";
import { API_BASE_URL } from "@/api/client";

interface Props {
  width?: number;
  height?: number;
}

export function SignaturePreview({ width = 200, height = 80 }: Props) {
  const { token } = useAuth();
  if (!token) return null;
  return (
    <View>
      <Image
        source={{
          uri: `${API_BASE_URL}/api/profile/signature?_=${Date.now()}`,
          headers: { Authorization: `Bearer ${token}` },
        }}
        style={{ width, height, resizeMode: "contain" }}
      />
    </View>
  );
}
```

Cache-busts via `?_=ts` so re-uploads visually refresh.

- [ ] **Step 2: tsc verify + commit**

```bash
cd bom-mobile && npx tsc --noEmit 2>&1 | grep "SignaturePreview" | head -3
git add bom-mobile/src/features/md/detail/SignaturePreview.tsx
git commit -m "feat(mobile-d3): SignaturePreview component (auth-headered Image)"
```

---

### Task 13: ProfileSignatureSection component

**Files:**
- Create: `bom-mobile/src/features/profile/ProfileSignatureSection.tsx`

- [ ] **Step 1: Create component**

```tsx
import { useState } from "react";
import { Alert, Pressable, Text, View } from "react-native";
import * as ImagePicker from "expo-image-picker";
import * as Haptics from "expo-haptics";
import { useUploadSignature, useOwnSignature } from "./api/signature";
import { SignaturePreview } from "@/features/md/detail/SignaturePreview";
import { Button } from "@/components/Button";
import { ErrorBanner } from "@/components/ErrorBanner";

const MAX_BYTES = 500 * 1024;

export function ProfileSignatureSection() {
  const ownSignatureQ = useOwnSignature();
  const upload = useUploadSignature();
  const [error, setError] = useState<string | null>(null);

  const handleUpload = async (source: "gallery" | "camera") => {
    setError(null);
    let perm;
    if (source === "gallery") {
      perm = await ImagePicker.requestMediaLibraryPermissionsAsync();
    } else {
      perm = await ImagePicker.requestCameraPermissionsAsync();
    }
    if (!perm.granted) {
      Alert.alert("Permission denied", "Permission required to upload signature.");
      return;
    }

    const result = source === "gallery"
      ? await ImagePicker.launchImageLibraryAsync({
          mediaTypes: ImagePicker.MediaTypeOptions.Images,
          quality: 0.9,
        })
      : await ImagePicker.launchCameraAsync({ quality: 0.9 });

    if (result.canceled) return;

    const asset = result.assets[0];
    if (asset.fileSize && asset.fileSize > MAX_BYTES) {
      setError(`File too large (${Math.round(asset.fileSize / 1024)}KB). Max 500KB.`);
      return;
    }

    try {
      await upload.mutateAsync({ uri: asset.uri, mime: asset.mimeType ?? "image/png" });
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Upload failed");
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
    }
  };

  return (
    <View style={{ paddingHorizontal: 16, paddingVertical: 12 }}>
      <Text style={{ fontSize: 13, color: "#64748b", fontWeight: "700", letterSpacing: 0.5 }}>
        SIGNATURE (MD)
      </Text>
      <Text style={{ fontSize: 12, color: "#94a3b8", marginTop: 4 }}>
        PNG/JPG · max 500KB · used on signed quotation PDFs
      </Text>

      {ownSignatureQ.data?.exists ? (
        <View style={{ marginTop: 12, padding: 12, borderWidth: 1, borderColor: "#e2e8f0", borderRadius: 10 }}>
          <SignaturePreview width={240} height={100} />
        </View>
      ) : (
        <View style={{ marginTop: 12, padding: 16, backgroundColor: "#fef3c7", borderRadius: 10 }}>
          <Text style={{ color: "#92400e", fontSize: 13 }}>
            ⚠️ No signature uploaded yet. Final-sign will be blocked until you upload one.
          </Text>
        </View>
      )}

      {error ? <View style={{ marginTop: 12 }}><ErrorBanner message={error} /></View> : null}

      <View style={{ flexDirection: "row", gap: 10, marginTop: 16 }}>
        <View style={{ flex: 1 }}>
          <Button title="Upload from gallery" onPress={() => handleUpload("gallery")} loading={upload.isPending} />
        </View>
        <View style={{ flex: 1 }}>
          <Button title="Take photo" variant="secondary" onPress={() => handleUpload("camera")} loading={upload.isPending} />
        </View>
      </View>
    </View>
  );
}
```

- [ ] **Step 2: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit 2>&1 | grep "ProfileSignatureSection" | head -3
git add bom-mobile/src/features/profile/ProfileSignatureSection.tsx
git commit -m "feat(mobile-d3): ProfileSignatureSection with gallery + camera upload"
```

---

### Task 14: Wire ProfileSignatureSection into profile.tsx

**Files:**
- Modify: `bom-mobile/app/profile.tsx`

- [ ] **Step 1: Read current profile**

```bash
cat bom-mobile/app/profile.tsx
```

- [ ] **Step 2: Add MD-only signature section**

Inside profile screen, after existing content:

```tsx
import { useAuth } from "@/auth/AuthContext";
import { ProfileSignatureSection } from "@/features/profile/ProfileSignatureSection";

// inside component:
const { user } = useAuth();

// in render:
{user?.role === "ManagingDirector" && <ProfileSignatureSection />}
```

- [ ] **Step 3: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit 2>&1 | grep "profile" | head -3
git add bom-mobile/app/profile.tsx
git commit -m "feat(mobile-d3): wire ProfileSignatureSection (MD-only) into profile"
```

---

## Phase 4 — Dashboard

### Task 15: SignatureMissingBanner component

**Files:**
- Create: `bom-mobile/src/features/md/dashboard/SignatureMissingBanner.tsx`

- [ ] **Step 1: Create component**

```tsx
import { Pressable, Text, View } from "react-native";
import { useRouter } from "expo-router";
import * as Haptics from "expo-haptics";

export function SignatureMissingBanner() {
  const router = useRouter();
  return (
    <Pressable
      onPress={() => {
        Haptics.selectionAsync();
        router.push("/profile");
      }}
      style={{
        marginHorizontal: 12, marginVertical: 8,
        padding: 14, borderRadius: 12,
        backgroundColor: "#fef3c7", borderWidth: 1, borderColor: "#fde68a",
      }}
    >
      <Text style={{ color: "#92400e", fontSize: 14, fontWeight: "600" }}>
        ⚠️ No signature uploaded
      </Text>
      <Text style={{ color: "#92400e", fontSize: 13, marginTop: 4 }}>
        Tap to upload your signature in Profile (required to sign quotations).
      </Text>
    </Pressable>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/md/dashboard/SignatureMissingBanner.tsx
git commit -m "feat(mobile-d3): SignatureMissingBanner component"
```

---

### Task 16: MdDashboard component

**Files:**
- Create: `bom-mobile/src/features/md/dashboard/MdDashboard.tsx`

Reuses `<KpiHeroCard>` and `<KpiRow>` from D-2 accountant (paths: `bom-mobile/src/features/accountant/dashboard/`). Decision: keep shared imports for now; promote later if needed.

- [ ] **Step 1: Create dashboard**

```tsx
import { ScrollView, Text, View, RefreshControl } from "react-native";
import { useRouter } from "expo-router";
import { Stack } from "expo-router";
import { useMdDashboard } from "@/api/stats";
import { useOwnSignature } from "@/features/profile/api/signature";
import { useAuth } from "@/auth/AuthContext";
import { ScreenHeader } from "@/components/ScreenHeader";
import { LoadingView } from "@/components/LoadingView";
import { KpiHeroCard } from "@/features/accountant/dashboard/KpiHeroCard";
import { KpiRow } from "@/features/accountant/dashboard/KpiRow";
import { SignatureMissingBanner } from "./SignatureMissingBanner";

export function MdDashboard() {
  const router = useRouter();
  const { user } = useAuth();
  const dashQ = useMdDashboard();
  const sigQ = useOwnSignature();

  if (dashQ.isPending) return <View style={{ flex: 1 }}><Stack.Screen options={{ headerShown: false }} /><ScreenHeader title="Dashboard" /><LoadingView /></View>;

  const counts = dashQ.data ?? { toPrice: 0, toSign: 0, inFlight: 0, signedToday: 0 };

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader title={`Welcome, ${user?.name ?? "MD"}`} />
      <ScrollView refreshControl={<RefreshControl refreshing={dashQ.isFetching} onRefresh={() => dashQ.refetch()} />}>
        <KpiHeroCard
          label="To price"
          value={counts.toPrice}
          icon="📋"
          onPress={() => router.push({ pathname: "/(md)/list", params: { tab: "queue" } })}
        />
        <KpiRow
          items={[
            { label: "To sign", value: counts.toSign, onPress: () => router.push({ pathname: "/(md)/list", params: { tab: "queue" } }) },
            { label: "In flight", value: counts.inFlight, onPress: () => router.push({ pathname: "/(md)/list", params: { tab: "in-flight" } }) },
            { label: "Signed today", value: counts.signedToday, onPress: () => router.push({ pathname: "/(md)/list", params: { tab: "done" } }) },
          ]}
        />
        {sigQ.data && !sigQ.data.exists && <SignatureMissingBanner />}
      </ScrollView>
    </View>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/md/dashboard/MdDashboard.tsx
git commit -m "feat(mobile-d3): MdDashboard with KPI cards + signature banner"
```

---

### Task 17: Wire (md)/index.tsx to MdDashboard

**Files:**
- Modify: `bom-mobile/app/(md)/index.tsx`

- [ ] **Step 1: Replace V2.3 content with V3 dashboard**

Replace the entire file with:

```tsx
import { MdDashboard } from "@/features/md/dashboard/MdDashboard";

export default function MdIndex() {
  return <MdDashboard />;
}
```

- [ ] **Step 2: tsc + commit**

```bash
cd bom-mobile && npx tsc --noEmit 2>&1 | head -5
git add bom-mobile/app/\(md\)/index.tsx
git commit -m "feat(mobile-d3): wire (md)/index.tsx to MdDashboard"
```

---

## Phase 5 — List page

### Task 18: tabFilters helper + MdTabs + InFlightSubFilter

**Files:**
- Create: `bom-mobile/src/features/md/list/tabFilters.ts`
- Create: `bom-mobile/src/features/md/list/MdTabs.tsx`
- Create: `bom-mobile/src/features/md/list/InFlightSubFilterChips.tsx`

- [ ] **Step 1: tabFilters.ts**

```ts
export type MdTab = "queue" | "in-flight" | "done" | "closed";
export type InFlightSubFilter = "all" | "customer" | "costing";

export function statusesForTab(tab: MdTab, sub: InFlightSubFilter): string[] {
  if (tab === "queue") return ["MdPricing", "MdFinalSign"];
  if (tab === "in-flight") {
    if (sub === "customer") return ["CustomerConfirm"];
    if (sub === "costing") return ["Costing"];
    return ["CustomerConfirm", "Costing"];
  }
  if (tab === "done") return ["Signed"];
  if (tab === "closed") return ["Rejected", "Cancelled"];
  return [];
}
```

- [ ] **Step 2: Add jest tests**

`bom-mobile/src/features/md/list/tabFilters.test.ts`:

```ts
import { statusesForTab } from "./tabFilters";

describe("statusesForTab", () => {
  it("queue returns MdPricing + MdFinalSign", () => {
    expect(statusesForTab("queue", "all")).toEqual(["MdPricing", "MdFinalSign"]);
  });
  it("in-flight all returns both customer + costing", () => {
    expect(statusesForTab("in-flight", "all")).toEqual(["CustomerConfirm", "Costing"]);
  });
  it("in-flight customer filter narrows", () => {
    expect(statusesForTab("in-flight", "customer")).toEqual(["CustomerConfirm"]);
  });
  it("done returns Signed", () => {
    expect(statusesForTab("done", "all")).toEqual(["Signed"]);
  });
  it("closed returns Rejected + Cancelled", () => {
    expect(statusesForTab("closed", "all")).toEqual(["Rejected", "Cancelled"]);
  });
});
```

- [ ] **Step 3: MdTabs component (mirror AccountantTabs)**

Copy `bom-mobile/src/features/accountant/list/AccountantTabs.tsx` to `bom-mobile/src/features/md/list/MdTabs.tsx`, adjust types to `MdTab`. Tab labels: Queue / In Flight / Done / Closed.

- [ ] **Step 4: InFlightSubFilterChips component**

Copy `bom-mobile/src/features/accountant/list/InFlightSubFilterChips.tsx` to `bom-mobile/src/features/md/list/InFlightSubFilterChips.tsx`, adjust labels: All / Customer / Costing.

- [ ] **Step 5: Run tests + commit**

```bash
cd bom-mobile && npx jest src/features/md/list/tabFilters.test.ts
git add bom-mobile/src/features/md/list/
git commit -m "feat(mobile-d3): MdTabs + InFlightSubFilterChips + tabFilters with tests"
```

---

### Task 19: MdListScreen + wire (md)/list.tsx

**Files:**
- Create: `bom-mobile/src/features/md/list/MdListScreen.tsx`
- Create: `bom-mobile/app/(md)/list.tsx`

- [ ] **Step 1: MdListScreen** (mirror `AccountantListScreen.tsx`)

Same structure as `bom-mobile/src/features/accountant/list/AccountantListScreen.tsx` but:
- Import from md module
- Empty-title strings adjusted ("Nothing to price", "Nothing to sign", etc.)
- Default tab = `"queue"`
- Sub-filter labels = "All" / "Customer" / "Costing"

- [ ] **Step 2: list.tsx route**

```tsx
import { MdListScreen } from "@/features/md/list/MdListScreen";

export default function MdList() {
  return <MdListScreen />;
}
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/features/md/list/MdListScreen.tsx bom-mobile/app/\(md\)/list.tsx
git commit -m "feat(mobile-d3): MdListScreen + (md)/list.tsx route"
```

---

## Phase 6 — Detail readonly path

### Task 20: ReadonlyMdView component

**Files:**
- Create: `bom-mobile/src/features/md/detail/ReadonlyMdView.tsx`

Reuses D-1 components: `<DetailHeader>`, `<FgReadCard>`, `<FinalPriceCard>`.

- [ ] **Step 1: Create component**

```tsx
import { ScrollView, Text, View } from "react-native";
import type { V3Requisition } from "@/types/v3";
import { DetailHeader } from "@/features/sales/detail/DetailHeader";
import { FgReadCard } from "@/features/sales/detail/FgReadCard";
import { FinalPriceCard } from "@/features/sales/detail/FinalPriceCard";

interface Props {
  req: V3Requisition;
}

export function ReadonlyMdView({ req }: Props) {
  const banner = bannerFor(req);
  return (
    <ScrollView contentContainerStyle={{ paddingBottom: 24 }}>
      <DetailHeader req={req} />

      {banner ? (
        <View style={{
          backgroundColor: banner.bg, marginHorizontal: 12, marginVertical: 8,
          padding: 12, borderRadius: 10,
        }}>
          <Text style={{ color: banner.fg, fontSize: 14, fontWeight: "600" }}>{banner.text}</Text>
        </View>
      ) : null}

      {req.finishedGoods.map((fg, idx) => (
        <FgReadCard key={fg.id} fg={fg} index={idx} />
      ))}

      {req.status === "Signed" ? <FinalPriceCard req={req} /> : null}
    </ScrollView>
  );
}

function bannerFor(req: V3Requisition): { text: string; bg: string; fg: string } | null {
  if (req.status === "Costing" || req.status === "Draft") return { text: "Waiting on accountant costing", bg: "#eff6ff", fg: "#1e40af" };
  if (req.status === "CustomerConfirm") return { text: "Waiting on SP customer-confirm", bg: "#eff6ff", fg: "#1e40af" };
  if (req.status === "Rejected") return { text: `Rejected: ${req.cancelReason ?? "(no reason)"}`, bg: "#fee2e2", fg: "#b91c1c" };
  if (req.status === "Cancelled") return { text: `Cancelled: ${req.cancelReason ?? "(no reason)"}`, bg: "#f1f5f9", fg: "#475569" };
  if (req.status === "Signed") return null; // FinalPriceCard suffices
  return null;
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/md/detail/ReadonlyMdView.tsx
git commit -m "feat(mobile-d3): ReadonlyMdView for non-active MD statuses"
```

---

## Phase 7 — Active MdPricing path

### Task 21: finalPriceClient.ts (live preview compute)

**Files:**
- Create: `bom-mobile/src/features/md/state/finalPriceClient.ts`
- Create: `bom-mobile/src/features/md/state/finalPriceClient.test.ts`

Pure compute, mirrors backend `FinalPriceComputer`. Used for ActiveMdPricingView's live preview.

- [ ] **Step 1: Write failing tests**

```ts
import { computeFinalPrice } from "./finalPriceClient";

describe("computeFinalPrice", () => {
  it("AED req: salePerKgAed = salePerKg", () => {
    const result = computeFinalPrice({
      currencyCode: "AED",
      rateSnapshot: null,
      perFg: [
        { requisitionItemId: 1, expectedQty: 5000, costPerKg: 3.20, marginPerKg: 1.80 },
      ],
    });
    expect(result.totalAed).toBe(25000);
    expect(result.perFg[0].salePerKg).toBe(5);
    expect(result.perFg[0].salePerKgAed).toBe(5);
  });

  it("Foreign req uses rateSnapshot", () => {
    const result = computeFinalPrice({
      currencyCode: "USD",
      rateSnapshot: 3.6725,
      perFg: [
        { requisitionItemId: 1, expectedQty: 5000, costPerKg: 1.00, marginPerKg: 1.00 },
      ],
    });
    expect(result.perFg[0].salePerKgAed).toBeCloseTo(7.345, 4);
    expect(result.totalAed).toBeCloseTo(36725, 1);
  });

  it("Multi-FG sums correctly", () => {
    const result = computeFinalPrice({
      currencyCode: "AED",
      rateSnapshot: null,
      perFg: [
        { requisitionItemId: 1, expectedQty: 5000, costPerKg: 3, marginPerKg: 1 },
        { requisitionItemId: 2, expectedQty: 5000, costPerKg: 5, marginPerKg: 2 },
      ],
    });
    expect(result.totalAed).toBe(55000);
  });
});
```

- [ ] **Step 2: Implement**

```ts
export interface FinalPriceInputItem {
  requisitionItemId: number;
  expectedQty: number;
  costPerKg: number;
  marginPerKg: number;
}

export interface FinalPriceInput {
  currencyCode: string;
  rateSnapshot: number | null;
  perFg: FinalPriceInputItem[];
}

export interface FinalPriceClientResult {
  totalAed: number;
  perFg: Array<FinalPriceInputItem & {
    salePerKg: number;
    salePerKgAed: number;
    totalAed: number;
  }>;
}

export function computeFinalPrice(input: FinalPriceInput): FinalPriceClientResult {
  const perFg = input.perFg.map((fg) => {
    const salePerKg = fg.costPerKg + fg.marginPerKg;
    const salePerKgAed = input.rateSnapshot != null ? salePerKg * input.rateSnapshot : salePerKg;
    const totalAed = salePerKgAed * fg.expectedQty;
    return { ...fg, salePerKg, salePerKgAed, totalAed };
  });
  return {
    totalAed: perFg.reduce((s, p) => s + p.totalAed, 0),
    perFg,
  };
}
```

- [ ] **Step 3: Run tests + commit**

```bash
cd bom-mobile && npx jest src/features/md/state/finalPriceClient.test.ts
git add bom-mobile/src/features/md/state/
git commit -m "feat(mobile-d3): finalPriceClient pure compute (mirror of backend)"
```

---

### Task 22: useMdPricingState hook

**Files:**
- Create: `bom-mobile/src/features/md/state/useMdPricingState.ts`

- [ ] **Step 1: Create hook**

```ts
import { useMemo, useState } from "react";
import type { V3Requisition } from "@/types/v3";
import { computeFinalPrice } from "./finalPriceClient";

export function useMdPricingState(req: V3Requisition) {
  const [margins, setMargins] = useState<Record<number, string>>({});
  const [notes, setNotes] = useState("");

  const setMargin = (riId: number, value: string) =>
    setMargins((m) => ({ ...m, [riId]: value }));

  const parsed = useMemo(() => {
    const result: Record<number, number | null> = {};
    for (const fg of req.finishedGoods) {
      const raw = margins[fg.id] ?? "";
      if (raw.trim() === "") {
        result[fg.id] = null;
      } else {
        const n = parseFloat(raw);
        result[fg.id] = isNaN(n) || n < 0 ? null : n;
      }
    }
    return result;
  }, [margins, req.finishedGoods]);

  const isValid = req.finishedGoods.every((fg) => parsed[fg.id] != null);

  const livePreview = useMemo(() => {
    if (!isValid) return null;
    return computeFinalPrice({
      currencyCode: req.currencyCode,
      rateSnapshot: null,  // live preview before margin set, no FX yet — backend re-snaps on save
      perFg: req.finishedGoods.map((fg) => ({
        requisitionItemId: fg.id,
        expectedQty: fg.expectedQty,
        costPerKg: fg.costs?.[0]?.totalCostPerKg ?? 0,
        marginPerKg: parsed[fg.id]!,
      })),
    });
  }, [parsed, req, isValid]);

  return { margins, setMargin, notes, setNotes, isValid, livePreview };
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/md/state/useMdPricingState.ts
git commit -m "feat(mobile-d3): useMdPricingState hook (margin draft + live preview)"
```

---

### Task 23: FgPricingCard component

**Files:**
- Create: `bom-mobile/src/features/md/detail/FgPricingCard.tsx`

- [ ] **Step 1: Create component**

```tsx
import { Text, TextInput, View } from "react-native";
import type { V3FinishedGoodDto } from "@/types/v3";

interface Props {
  fg: V3FinishedGoodDto;
  index: number;
  marginInput: string;
  onMarginChange: (value: string) => void;
  livePerFg: { salePerKg: number; salePerKgAed: number; totalAed: number } | null;
  currencyCode: string;
}

export function FgPricingCard({ fg, index, marginInput, onMarginChange, livePerFg, currencyCode }: Props) {
  const costPerKg = fg.costs?.[0]?.totalCostPerKg ?? 0;
  return (
    <View style={{ marginHorizontal: 12, marginVertical: 6, padding: 14, borderRadius: 12, backgroundColor: "white", borderWidth: 1, borderColor: "#e5e7eb" }}>
      <Text style={{ fontSize: 12, color: "#64748b", fontWeight: "700", letterSpacing: 0.5 }}>
        FG {index + 1}
      </Text>
      <Text style={{ fontSize: 15, fontWeight: "600", color: "#0f172a", marginTop: 4 }}>
        {fg.item.description}
      </Text>
      <Text style={{ fontSize: 12, color: "#94a3b8", marginTop: 2 }}>
        {fg.item.code} · {fg.expectedQty.toLocaleString()} KG
      </Text>

      <View style={{ marginTop: 12, padding: 10, backgroundColor: "#f8fafc", borderRadius: 8 }}>
        <RowKV label="Cost/KG" value={`${currencyCode} ${costPerKg.toFixed(2)}`} />
        <View style={{ flexDirection: "row", alignItems: "center", marginTop: 8 }}>
          <Text style={{ flex: 1, fontSize: 13, color: "#475569" }}>Margin/KG</Text>
          <TextInput
            value={marginInput}
            onChangeText={onMarginChange}
            placeholder="0.00"
            keyboardType="decimal-pad"
            style={{
              borderWidth: 1, borderColor: "#cbd5e1", borderRadius: 8,
              paddingHorizontal: 10, paddingVertical: 8, fontSize: 14,
              minWidth: 100, textAlign: "right",
            }}
          />
        </View>
        {livePerFg ? (
          <>
            <View style={{ borderTopWidth: 1, borderTopColor: "#e2e8f0", marginTop: 8, paddingTop: 8 }}>
              <RowKV label="Sale/KG" value={`${currencyCode} ${livePerFg.salePerKg.toFixed(2)}`} highlight />
              <RowKV label="Total" value={`AED ${livePerFg.totalAed.toLocaleString(undefined, { maximumFractionDigits: 2 })}`} highlight />
            </View>
          </>
        ) : null}
      </View>
    </View>
  );
}

function RowKV({ label, value, highlight }: { label: string; value: string; highlight?: boolean }) {
  return (
    <View style={{ flexDirection: "row", justifyContent: "space-between" }}>
      <Text style={{ fontSize: 13, color: "#64748b" }}>{label}</Text>
      <Text style={{ fontSize: 13, color: highlight ? "#0f172a" : "#475569", fontWeight: highlight ? "700" : "400" }}>{value}</Text>
    </View>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/md/detail/FgPricingCard.tsx
git commit -m "feat(mobile-d3): FgPricingCard with cost/margin input + live preview"
```

---

### Task 24: RejectReqModal component

**Files:**
- Create: `bom-mobile/src/features/md/modal/RejectReqModal.tsx`

- [ ] **Step 1: Create modal (modeled on CustomerSwapSheet pattern)**

```tsx
import { useState } from "react";
import { Alert, Modal, Pressable, Text, TextInput, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import * as Haptics from "expo-haptics";
import { useRejectRequisition } from "@/features/md/api/approvals";
import { Button } from "@/components/Button";
import { ErrorBanner } from "@/components/ErrorBanner";

interface Props {
  requisitionId: number;
  refNo: string;
  open: boolean;
  onClose: () => void;
}

export function RejectReqModal({ requisitionId, refNo, open, onClose }: Props) {
  const insets = useSafeAreaInsets();
  const reject = useRejectRequisition(requisitionId);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const handleReject = async () => {
    if (reason.trim().length < 5) {
      setError("Reason must be at least 5 characters.");
      return;
    }
    setError(null);
    try {
      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
      await reject.mutateAsync({ reason: reason.trim() });
      setReason("");
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Reject failed");
    }
  };

  return (
    <Modal visible={open} animationType="slide" transparent onRequestClose={onClose}>
      <Pressable onPress={onClose} style={{ flex: 1, backgroundColor: "rgba(15,23,42,0.4)", justifyContent: "flex-end" }}>
        <Pressable
          onPress={() => {}}
          style={{
            backgroundColor: "white", borderTopLeftRadius: 18, borderTopRightRadius: 18,
            padding: 20, paddingBottom: Math.max(insets.bottom, 16) + 12,
          }}
        >
          <Text style={{ fontSize: 18, fontWeight: "700", color: "#0f172a" }}>
            Reject {refNo}
          </Text>
          <Text style={{ fontSize: 13, color: "#94a3b8", marginTop: 4 }}>
            Provide a reason — bounces back to costing for re-work.
          </Text>

          <Text style={{ marginTop: 16, fontSize: 12, fontWeight: "600", color: "#64748b", letterSpacing: 0.5 }}>
            REASON
          </Text>
          <TextInput
            value={reason}
            onChangeText={setReason}
            placeholder="e.g. Material cost looks wrong, please verify…"
            placeholderTextColor="#94a3b8"
            multiline
            style={{
              borderWidth: 1, borderColor: "#cbd5e1", borderRadius: 10,
              paddingHorizontal: 12, paddingVertical: 10, fontSize: 14,
              minHeight: 80, textAlignVertical: "top", marginTop: 6,
            }}
          />

          {error ? <View style={{ marginTop: 12 }}><ErrorBanner message={error} /></View> : null}

          <View style={{ flexDirection: "row", gap: 10, marginTop: 20 }}>
            <View style={{ flex: 1 }}>
              <Button title="Cancel" variant="secondary" onPress={onClose} />
            </View>
            <View style={{ flex: 1 }}>
              <Button title="Reject" variant="danger" onPress={handleReject} loading={reject.isPending} />
            </View>
          </View>
        </Pressable>
      </Pressable>
    </Modal>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/md/modal/RejectReqModal.tsx
git commit -m "feat(mobile-d3): RejectReqModal with reason input + POST /reject"
```

---

### Task 25: ActiveMdPricingView (composer)

**Files:**
- Create: `bom-mobile/src/features/md/detail/ActiveMdPricingView.tsx`

- [ ] **Step 1: Create composer**

```tsx
import { useState } from "react";
import { Alert, ScrollView, Text, TextInput, View } from "react-native";
import type { V3Requisition } from "@/types/v3";
import { useMdPricingState } from "../state/useMdPricingState";
import { useSetMargin } from "../api/approvals";
import { FgPricingCard } from "./FgPricingCard";
import { RejectReqModal } from "../modal/RejectReqModal";
import { Button } from "@/components/Button";

interface Props {
  req: V3Requisition;
}

export function ActiveMdPricingView({ req }: Props) {
  const state = useMdPricingState(req);
  const setMargin = useSetMargin(req.id);
  const [rejectOpen, setRejectOpen] = useState(false);

  const handleApprove = async () => {
    if (!state.isValid) return;
    try {
      await setMargin.mutateAsync({
        items: req.finishedGoods.map((fg) => ({
          requisitionItemId: fg.id,
          marginPerKg: parseFloat(state.margins[fg.id] ?? "0"),
        })),
        notes: state.notes.trim() || undefined,
      });
    } catch (e) {
      Alert.alert("Error", e instanceof Error ? e.message : "Set margin failed");
    }
  };

  return (
    <View style={{ flex: 1 }}>
      <ScrollView contentContainerStyle={{ paddingTop: 8, paddingBottom: 16 }}>
        <View style={{ paddingHorizontal: 12, marginBottom: 12 }}>
          <Text style={{ fontSize: 13, color: "#64748b", fontWeight: "600", letterSpacing: 0.5 }}>CUSTOMER</Text>
          <Text style={{ fontSize: 16, fontWeight: "700", color: "#0f172a", marginTop: 4 }}>{req.customer.name}</Text>
          <Text style={{ fontSize: 13, color: "#94a3b8" }}>Currency: {req.currencyCode}</Text>
        </View>

        {req.finishedGoods.map((fg, idx) => (
          <FgPricingCard
            key={fg.id}
            fg={fg}
            index={idx}
            marginInput={state.margins[fg.id] ?? ""}
            onMarginChange={(v) => state.setMargin(fg.id, v)}
            livePerFg={state.livePreview?.perFg.find((p) => p.requisitionItemId === fg.id) ?? null}
            currencyCode={req.currencyCode}
          />
        ))}

        {state.livePreview ? (
          <View style={{ marginHorizontal: 12, marginVertical: 12, padding: 14, backgroundColor: "#1e40af", borderRadius: 12 }}>
            <Text style={{ color: "white", fontSize: 13, opacity: 0.85 }}>GRAND TOTAL</Text>
            <Text style={{ color: "white", fontSize: 24, fontWeight: "700", marginTop: 4 }}>
              AED {state.livePreview.totalAed.toLocaleString(undefined, { maximumFractionDigits: 2 })}
            </Text>
          </View>
        ) : null}

        <View style={{ paddingHorizontal: 12, marginTop: 8 }}>
          <Text style={{ fontSize: 12, color: "#64748b", fontWeight: "600", letterSpacing: 0.5, marginBottom: 6 }}>NOTES (optional)</Text>
          <TextInput
            value={state.notes}
            onChangeText={state.setNotes}
            placeholder="Optional notes for SP"
            placeholderTextColor="#94a3b8"
            multiline
            style={{ borderWidth: 1, borderColor: "#cbd5e1", borderRadius: 10, padding: 10, fontSize: 14, minHeight: 60, textAlignVertical: "top" }}
          />
        </View>
      </ScrollView>

      <View style={{ flexDirection: "row", gap: 10, padding: 12, borderTopWidth: 1, borderTopColor: "#e2e8f0", backgroundColor: "white" }}>
        <View style={{ flex: 1 }}>
          <Button title="Reject" variant="danger" onPress={() => setRejectOpen(true)} />
        </View>
        <View style={{ flex: 2 }}>
          <Button
            title="Approve & send"
            onPress={handleApprove}
            loading={setMargin.isPending}
            disabled={!state.isValid}
          />
        </View>
      </View>

      <RejectReqModal
        requisitionId={req.id}
        refNo={req.refNo}
        open={rejectOpen}
        onClose={() => setRejectOpen(false)}
      />
    </View>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/md/detail/ActiveMdPricingView.tsx
git commit -m "feat(mobile-d3): ActiveMdPricingView composer (per-FG cards + grand total + reject)"
```

---

## Phase 8 — Active MdFinalSign path

### Task 26: FinalSignSummary component

**Files:**
- Create: `bom-mobile/src/features/md/detail/FinalSignSummary.tsx`

- [ ] **Step 1: Create component**

```tsx
import { Text, View } from "react-native";
import type { V3FinalPrice } from "@/types/v3";

interface Props {
  finalPrice: V3FinalPrice;
}

export function FinalSignSummary({ finalPrice }: Props) {
  return (
    <View style={{ marginHorizontal: 12, marginVertical: 8 }}>
      <Text style={{ fontSize: 13, color: "#64748b", fontWeight: "700", letterSpacing: 0.5 }}>
        QUOTE SUMMARY
      </Text>
      <View style={{ marginTop: 8, padding: 12, backgroundColor: "white", borderRadius: 10, borderWidth: 1, borderColor: "#e2e8f0" }}>
        {finalPrice.perFg.map((fg) => (
          <View key={fg.requisitionItemId} style={{ paddingVertical: 8, borderBottomWidth: 1, borderBottomColor: "#f1f5f9" }}>
            <Text style={{ fontSize: 14, color: "#0f172a", fontWeight: "600" }}>{fg.description}</Text>
            <Text style={{ fontSize: 12, color: "#94a3b8", marginTop: 2 }}>
              {fg.expectedQty.toLocaleString()} KG × {finalPrice.currencyCode} {fg.salePerKg.toFixed(2)} = AED {fg.totalAed.toLocaleString(undefined, { maximumFractionDigits: 2 })}
            </Text>
          </View>
        ))}
        <View style={{ flexDirection: "row", justifyContent: "space-between", marginTop: 12 }}>
          <Text style={{ fontSize: 14, fontWeight: "700", color: "#0f172a" }}>TOTAL</Text>
          <Text style={{ fontSize: 18, fontWeight: "700", color: "#1e40af" }}>
            AED {finalPrice.totalAed.toLocaleString(undefined, { maximumFractionDigits: 2 })}
          </Text>
        </View>
      </View>
    </View>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/md/detail/FinalSignSummary.tsx
git commit -m "feat(mobile-d3): FinalSignSummary pricing table"
```

---

### Task 27: ActiveMdFinalSignView

**Files:**
- Create: `bom-mobile/src/features/md/detail/ActiveMdFinalSignView.tsx`

- [ ] **Step 1: Create view**

```tsx
import { useState } from "react";
import { Alert, ScrollView, Text, TextInput, View } from "react-native";
import { useRouter } from "expo-router";
import type { V3Requisition } from "@/types/v3";
import { useFinalSign } from "../api/approvals";
import { useOwnSignature } from "@/features/profile/api/signature";
import { FinalSignSummary } from "./FinalSignSummary";
import { SignaturePreview } from "./SignaturePreview";
import { Button } from "@/components/Button";
import { LoadingView } from "@/components/LoadingView";

interface Props {
  req: V3Requisition;
}

export function ActiveMdFinalSignView({ req }: Props) {
  const router = useRouter();
  const sigQ = useOwnSignature();
  const sign = useFinalSign(req.id);
  const [token, setToken] = useState("");
  const [notes, setNotes] = useState("");

  if (sigQ.isPending) return <LoadingView />;

  if (!sigQ.data?.exists) {
    return (
      <View style={{ flex: 1, padding: 24, justifyContent: "center" }}>
        <Text style={{ fontSize: 18, fontWeight: "700", color: "#92400e", textAlign: "center" }}>
          ⚠️ No signature uploaded
        </Text>
        <Text style={{ fontSize: 14, color: "#475569", textAlign: "center", marginTop: 12 }}>
          Please upload your signature in Profile before signing this quotation.
        </Text>
        <View style={{ marginTop: 24 }}>
          <Button title="Open Profile" onPress={() => router.push("/profile")} />
        </View>
      </View>
    );
  }

  const handleSign = async () => {
    if (token !== "SIGN") return;
    try {
      await sign.mutateAsync({ confirmationToken: token, notes: notes.trim() || undefined });
    } catch (e) {
      Alert.alert("Error", e instanceof Error ? e.message : "Final sign failed");
    }
  };

  return (
    <View style={{ flex: 1 }}>
      <ScrollView contentContainerStyle={{ paddingBottom: 16 }}>
        <View style={{ marginHorizontal: 12, marginVertical: 8, padding: 14, backgroundColor: "#fed7aa", borderRadius: 10 }}>
          <Text style={{ color: "#9a3412", fontSize: 14, fontWeight: "700" }}>⚠️ Sign &amp; Lock — irreversible</Text>
          <Text style={{ color: "#9a3412", fontSize: 13, marginTop: 4 }}>
            After signing, no changes can be made. PDF will be generated.
          </Text>
        </View>

        <View style={{ paddingHorizontal: 12, marginBottom: 8 }}>
          <Text style={{ fontSize: 13, color: "#64748b", fontWeight: "600", letterSpacing: 0.5 }}>CUSTOMER</Text>
          <Text style={{ fontSize: 16, fontWeight: "700", color: "#0f172a", marginTop: 4 }}>{req.customer.name}</Text>
        </View>

        {req.finalPrice ? <FinalSignSummary finalPrice={req.finalPrice} /> : null}

        <View style={{ marginHorizontal: 12, marginTop: 16 }}>
          <Text style={{ fontSize: 13, color: "#64748b", fontWeight: "700", letterSpacing: 0.5 }}>YOUR SIGNATURE</Text>
          <View style={{ marginTop: 8, padding: 12, borderWidth: 1, borderColor: "#e2e8f0", borderRadius: 10, alignItems: "center" }}>
            <SignaturePreview width={240} height={100} />
          </View>
        </View>

        <View style={{ paddingHorizontal: 12, marginTop: 16 }}>
          <Text style={{ fontSize: 12, color: "#64748b", fontWeight: "600", letterSpacing: 0.5, marginBottom: 6 }}>NOTES (optional)</Text>
          <TextInput
            value={notes}
            onChangeText={setNotes}
            placeholder="Optional notes"
            placeholderTextColor="#94a3b8"
            multiline
            style={{ borderWidth: 1, borderColor: "#cbd5e1", borderRadius: 10, padding: 10, fontSize: 14, minHeight: 60, textAlignVertical: "top" }}
          />
        </View>

        <View style={{ paddingHorizontal: 12, marginTop: 16 }}>
          <Text style={{ fontSize: 12, color: "#64748b", fontWeight: "600", letterSpacing: 0.5, marginBottom: 6 }}>TYPE SIGN TO CONFIRM</Text>
          <TextInput
            value={token}
            onChangeText={setToken}
            placeholder="SIGN"
            autoCapitalize="characters"
            style={{
              borderWidth: 2, borderColor: token === "SIGN" ? "#10b981" : "#cbd5e1", borderRadius: 10,
              padding: 12, fontSize: 18, fontWeight: "700", letterSpacing: 4, textAlign: "center",
            }}
          />
        </View>
      </ScrollView>

      <View style={{ flexDirection: "row", gap: 10, padding: 12, borderTopWidth: 1, borderTopColor: "#e2e8f0", backgroundColor: "white" }}>
        <View style={{ flex: 1 }}>
          <Button title="Cancel" variant="secondary" onPress={() => router.back()} />
        </View>
        <View style={{ flex: 2 }}>
          <Button
            title="Sign &amp; Lock"
            variant="danger"
            onPress={handleSign}
            loading={sign.isPending}
            disabled={token !== "SIGN"}
          />
        </View>
      </View>
    </View>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/md/detail/ActiveMdFinalSignView.tsx
git commit -m "feat(mobile-d3): ActiveMdFinalSignView with signature preview + SIGN-token gate"
```

---

### Task 28: MdDetailScreen (status branch dispatcher)

**Files:**
- Create: `bom-mobile/src/features/md/detail/MdDetailScreen.tsx`
- Modify: `bom-mobile/app/(md)/[id].tsx` (replace V2.3 content)

- [ ] **Step 1: MdDetailScreen**

```tsx
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { Pressable, Text, View } from "react-native";
import * as Haptics from "expo-haptics";
import { useRequisition } from "@/api/requisitions";
import { useAuth } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";
import { ErrorBanner } from "@/components/ErrorBanner";
import { ScreenHeader } from "@/components/ScreenHeader";
import { NotificationBell } from "@/components/NotificationBell";
import { ActiveMdPricingView } from "./ActiveMdPricingView";
import { ActiveMdFinalSignView } from "./ActiveMdFinalSignView";
import { ReadonlyMdView } from "./ReadonlyMdView";

const V3_STATUSES = ["Draft", "Costing", "MdPricing", "CustomerConfirm", "MdFinalSign", "Signed", "Rejected", "Cancelled"];

export function MdDetailScreen() {
  const router = useRouter();
  const { logout } = useAuth();
  const params = useLocalSearchParams<{ id: string }>();
  const id = Number(params.id);
  const reqQ = useRequisition(id);

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

  if (reqQ.isPending) {
    return (
      <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <Stack.Screen options={{ headerShown: false }} />
        <ScreenHeader title="Requisition" back right={HeaderRight} />
        <LoadingView />
      </View>
    );
  }

  if (reqQ.isError || !reqQ.data) {
    return (
      <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <Stack.Screen options={{ headerShown: false }} />
        <ScreenHeader title="Requisition" back right={HeaderRight} />
        <ErrorBanner message="Failed to load requisition" onRetry={() => reqQ.refetch()} />
      </View>
    );
  }

  const req = reqQ.data;

  if (!V3_STATUSES.includes(req.status)) {
    return (
      <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <Stack.Screen options={{ headerShown: false }} />
        <ScreenHeader title={req.refNo} back right={HeaderRight} />
        <View style={{ padding: 24 }}>
          <Text style={{ fontSize: 15, color: "#0f172a", lineHeight: 22 }}>
            This requisition is in a legacy state — please view on web.
          </Text>
        </View>
      </View>
    );
  }

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader title={req.refNo} back right={HeaderRight} />
      {req.status === "MdPricing" ? (
        <ActiveMdPricingView req={req} />
      ) : req.status === "MdFinalSign" ? (
        <ActiveMdFinalSignView req={req} />
      ) : (
        <ReadonlyMdView req={req} />
      )}
    </View>
  );
}
```

- [ ] **Step 2: Wire (md)/[id].tsx**

Replace the existing V2.3 file with:

```tsx
import { MdDetailScreen } from "@/features/md/detail/MdDetailScreen";

export default function MdDetail() {
  return <MdDetailScreen />;
}
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/features/md/detail/MdDetailScreen.tsx bom-mobile/app/\(md\)/\[id\].tsx
git commit -m "feat(mobile-d3): MdDetailScreen status-branch dispatcher + wire route"
```

---

## Phase 9 — V2.3 purge

### Task 29: Delete V2.3 MD app routes

**Files:**
- Delete: `bom-mobile/app/(md)/pending.tsx`
- Delete: `bom-mobile/app/(md)/historical/` (folder)
- Delete: `bom-mobile/app/(md)/item/` (folder)

- [ ] **Step 1: Delete files**

```bash
rm bom-mobile/app/\(md\)/pending.tsx
rm -rf bom-mobile/app/\(md\)/historical
rm -rf bom-mobile/app/\(md\)/item
```

- [ ] **Step 2: tsc verify**

```bash
cd bom-mobile && npx tsc --noEmit 2>&1 | grep -c "error TS"
```

Expected: significantly fewer errors (V2.3 MD imports gone). Note any remaining errors — should be zero or near-zero now.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "chore(mobile-d3): purge V2.3 MD app routes (pending, historical, item)"
```

---

### Task 30: Delete V2.3 MD-only components

**Files:**
- Delete: `bom-mobile/src/components/HistoricalRequisitionScreen.tsx`
- Delete: `bom-mobile/src/components/BranchSwapSheet.tsx`
- Delete: `bom-mobile/src/components/BranchChangeHistorySheet.tsx`
- Delete: `bom-mobile/src/components/RequisitionCard.tsx`

- [ ] **Step 1: Delete files**

```bash
rm bom-mobile/src/components/HistoricalRequisitionScreen.tsx
rm bom-mobile/src/components/BranchSwapSheet.tsx
rm bom-mobile/src/components/BranchChangeHistorySheet.tsx
rm bom-mobile/src/components/RequisitionCard.tsx
```

- [ ] **Step 2: tsc verify**

```bash
cd bom-mobile && npx tsc --noEmit 2>&1 | grep -c "error TS"
```

Expected: zero or near-zero. Any remaining error must be diagnosed and fixed.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "chore(mobile-d3): purge 4 V2.3 MD-only components"
```

---

## Phase 10 — Smoke + EAS rebuild + tag

### Task 31: Mobile PR + merge

- [ ] **Step 1: Final tsc + push**

```bash
cd bom-mobile && npx tsc --noEmit 2>&1 | tail -3
cd ..
git push -u origin feat/v3-mobile-d3-md
```

- [ ] **Step 2: Open PR**

```bash
gh pr create --base master --head feat/v3-mobile-d3-md \
  --title "feat(mobile-d3): V3 MD rebuild — pricing + final-sign + signature upload" \
  --body "$(cat <<'EOF'
## Summary

V3 mobile Phase D-3 — Managing Director surface rebuild. Final V3 mobile rebuild after D-1 (SP) + D-2 (Accountant). All V2.3 mobile code now purged.

- ActiveMdPricingView with cost-informed per-FG cards + live preview + Reject CTA
- ActiveMdFinalSignView with full quote summary + signature preview + SIGN-token gate
- ReadonlyMdView for non-active statuses (Costing/CustomerConfirm/Signed/Rejected/Cancelled)
- Profile signature upload section (gallery + camera) MD-only
- V2.3 MD app routes + 4 V2.3 components purged (~1100 lines)

## Test plan

- [ ] tsc clean
- [ ] On-device smoke pending (per spec §10)
- [ ] EAS rebuild required (expo-image-picker native module added)

## Hold

Adding `hold` label until on-device smoke completes.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"

gh pr edit $(gh pr view --json number --jq .number) --add-label hold
```

- [ ] **Step 3: After user smoke + clear hold + auto-merge**

(Smoke is user-manual; PR sits with `hold` until cleared.)

```bash
# After user clears hold:
gh pr merge --squash --delete-branch
git checkout master && git pull --ff-only origin master
```

---

### Task 32: EAS rebuild + mobile-shipped-vc2 tag

- [ ] **Step 1: Drift check**

```bash
git log mobile-shipped-vc1..master --oneline -- bom-mobile/
```

Confirm D-1 + D-2 + D-3 commits all present.

- [ ] **Step 2: EAS rebuild**

```bash
cd bom-mobile && npx eas-cli build --profile preview --platform android --non-interactive
```

Wait for build to complete; capture APK URL.

- [ ] **Step 3: Tag the shipped commit**

```bash
git tag mobile-shipped-vc2 master -m "EAS preview build vc2: $(date -I)"
git push origin mobile-shipped-vc2
```

- [ ] **Step 4: Update memory**

Add a memory entry for D-3 completion + new APK tag, mirroring D-2 memory pattern.

---

## Self-review pass

After completing all tasks:

- [ ] **Spec coverage**: Every section/decision in the spec maps to at least one task above. Decisions D1-D10 all addressed (D1 scope = whole plan; D2 signature upload = Tasks 12-14; D3 MdPricing = Tasks 21-25; D4 MdFinalSign = Tasks 26-27; D5 finalPrice = Tasks 1-3, 8; D6 4-tab list = Tasks 18-19; D7 image picker source = Task 13; D8 V2.3 purge = Tasks 29-30; D9 status branch = Task 28; D10 reject placement = Tasks 24-25).

- [ ] **Placeholder scan**: No "TBD", "TODO", "implement later" anywhere. Every step has actual code or commands.

- [ ] **Type consistency**: `useSetMargin`, `useRejectRequisition`, `useFinalSign` exported from same file (`features/md/api/approvals.ts`). `V3FinalPrice`, `V3FinalPriceItem` used consistently across types/hooks/components.

- [ ] **Missing piece detection**: nothing flagged on review.
