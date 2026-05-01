# V3 Mobile Phase D-2 (Accountant) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the Accountant surface of `bom-mobile` against the V3 backend contract — dashboard, 4-tab list, detail (active + read-only), cost-input drawer, customer-swap retention, branch-swap drop — replacing the V2.3 accountant code that was orphaned at the 2026-04-30 V3 cutover.

**Architecture:** V3-only purge of V2.3 accountant screens. Backend prereq PR first (stats reshape + costing-submit verify + customer-swap allowed-status). Then mobile: surgical rewrite of two V3-affected API hooks (`costing`, `stats`); leave V3-compatible rest. New components organized under `bom-mobile/src/features/accountant/{dashboard,list,detail,drawer,state}/`. Hybrid cost-input layout (main FG list + per-FG bottom-sheet drawer); save-on-close drawer commits to parent state which fires bulk PUT `/cost-data`; main "Submit to MD" calls `POST /submit` after final save. ReqCard promoted from D-1 SP to shared component; statusMap moved to shared utils.

**Tech Stack:** ASP.NET Core 8 (backend prereqs), React Native (Expo Router), TanStack Query, Zustand, Reanimated/Moti, Haptics, axios. Spec: `docs/superpowers/specs/2026-05-01-v3-mobile-phase-d-2-accountant-design.md`. Verification: `dotnet test` (backend prereqs), `tsc --noEmit` (mobile), Android emulator manual smoke, EAS OTA push, physical-device verify.

---

## Pre-flight

### Task 0: Branch + clean state

**Files:**
- None — environment setup only

- [ ] **Step 1: Verify on master + clean tree**

```bash
git status -sb
git branch -v
git log -1 --oneline
```
Expected: `master`, working tree clean, master at `88c5113` (PR #47 merge) or later.

- [ ] **Step 2: Create backend-prereq feature branch**

```bash
git checkout -b feat/v3-mobile-d2-backend-prereqs
```

- [ ] **Step 3: Verify backend baseline**

```bash
dotnet build --nologo -v q
```
Expected: 0 errors, 0 warnings (or no-worse than master baseline). Documents the pre-rewrite backend state.

- [ ] **Step 4: Verify mobile tooling baseline**

```bash
cd bom-mobile && npx tsc --noEmit; cd ..
```
Expected: ~26 errors (D-1 V2.3 cross-phase residuals). Documents the pre-rewrite mobile state. D-2's V2.3 accountant purge should reduce or eliminate these.

---

## Phase 0 — Backend prereqs (spec D-2.0)

A small backend-only PR. Three changes (B1+B2+B3). Lands on master before any mobile work begins.

### Task 1: Stats endpoint V3 reshape (B1)

**Files:**
- Modify: `BomPriceApproval.API/Features/Stats/StatsController.cs`
- Modify: `BomPriceApproval.API/Features/Stats/StatsDtos.cs`
- Test: `BomPriceApproval.Tests/Stats/AccountantDashboardTests.cs`

- [ ] **Step 1: Read existing endpoint**

```bash
grep -n "accountant-dashboard" BomPriceApproval.API/Features/Stats/StatsController.cs
```
Locate `GET /api/stats/accountant-dashboard` action + DTO file.

- [ ] **Step 2: Update DTO (V2.3 → V3 shape)**

Replace V2.3 DTO `AccountantDashboardStatsDto` with V3 shape in `StatsDtos.cs`:

```csharp
public sealed record AccountantDashboardV3Dto(
    int Costing,             // RequisitionStatus.Costing count
    int AwaitingMd,          // MdPricing + MdFinalSign
    int AwaitingCustomer,    // CustomerConfirm
    int SubmittedThisMonth   // accountant's own submissions this month — joined via AdminAuditLog or QuotationRequest.SubmittedToCostingByUserId
);
```

- [ ] **Step 3: Update controller action**

Replace the V2.3 LINQ query with V3 status counts. Only count branches the accountant has access to via `UserBranches` (per CLAUDE.md branch-isolation):

```csharp
[HttpGet("accountant-dashboard")]
[Authorize(Roles = "Accountant,Admin")]
public async Task<ActionResult<AccountantDashboardV3Dto>> GetAccountantDashboard()
{
    var userId = User.GetUserId();
    var allowedBranchIds = await _db.UserBranches
        .Where(ub => ub.UserId == userId)
        .Select(ub => ub.BranchId)
        .ToListAsync();

    var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    var byStatus = await _db.QuotationRequests
        .Where(r => allowedBranchIds.Contains(r.BranchId))
        .GroupBy(r => r.Status)
        .Select(g => new { Status = g.Key, Count = g.Count() })
        .ToListAsync();

    int countOf(RequisitionStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

    var awaitingMd = countOf(RequisitionStatus.MdPricing) + countOf(RequisitionStatus.MdFinalSign);
    var awaitingCustomer = countOf(RequisitionStatus.CustomerConfirm);
    var costing = countOf(RequisitionStatus.Costing);

    var submittedThisMonth = await _db.QuotationRequests
        .Where(r =>
            allowedBranchIds.Contains(r.BranchId)
            && r.CostingSubmittedAt != null
            && r.CostingSubmittedAt >= monthStart
            && r.CostingSubmittedByUserId == userId)
        .CountAsync();

    return Ok(new AccountantDashboardV3Dto(costing, awaitingMd, awaitingCustomer, submittedThisMonth));
}
```

> **NOTE:** `CostingSubmittedAt` and `CostingSubmittedByUserId` are V3 columns added in Phase A backend (PR #29). Verify they exist on `QuotationRequest` entity by reading `BomPriceApproval.API/Domain/Entities/QuotationRequest.cs`. If absent, add fallback: count via AdminAuditLog filter on `ActionType="SubmitCosting"` for this admin/accountant where applicable. This fallback is the implementation safety net — the column should exist post-V3 cutover.

- [ ] **Step 4: Update existing test class (rename + reshape assertions)**

Read existing `BomPriceApproval.Tests/Stats/AccountantDashboardTests.cs`. Update test seed data to use V3 statuses (`Costing`, `MdPricing`, `CustomerConfirm`, `MdFinalSign`). Replace V2.3 assertions on `pendingCosting`/`inProgress`/`awaitingMd`/`submittedThisMonth` with V3 shape:

```csharp
[Fact]
public async Task GetAccountantDashboard_ReturnsV3Counts_ScopedToUserBranches()
{
    // arrange — seed accountant + 2 branches
    var (accountantId, b1, b2) = await SeedAccountantWithTwoBranches();

    // seed 3 reqs in b1: 1 Costing + 1 MdPricing + 1 CustomerConfirm
    // seed 1 req in unrelated branch — should NOT count
    // seed 1 req in b2 status=Costing submitted-this-month by THIS accountant

    // act
    var resp = await _client.GetAsync("/api/stats/accountant-dashboard");

    // assert
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await resp.Content.ReadFromJsonAsync<AccountantDashboardV3Dto>();
    body!.Costing.Should().Be(2);             // 1 in b1 + 1 in b2
    body.AwaitingMd.Should().Be(1);           // 1 MdPricing in b1
    body.AwaitingCustomer.Should().Be(1);     // 1 CustomerConfirm in b1
    body.SubmittedThisMonth.Should().Be(1);   // the b2 req
}
```

Replace the existing fixture's seeding helpers as needed. Do NOT delete the old test class file — overwrite content.

- [ ] **Step 5: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~AccountantDashboardTests" --nologo -v normal
```
Expected: all assertions pass.

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Features/Stats/StatsController.cs BomPriceApproval.API/Features/Stats/StatsDtos.cs BomPriceApproval.Tests/Stats/AccountantDashboardTests.cs
git commit -m "feat(stats): V3 accountant dashboard endpoint reshape (B1)"
```

---

### Task 2: Verify costing-submit endpoint V3 behavior (B2)

**Files:**
- Read: `BomPriceApproval.API/Features/Costing/CostingController.cs`
- Test (potential add): `BomPriceApproval.Tests/Costing/CostingSubmitV3Tests.cs`

- [ ] **Step 1: Audit existing submit endpoint**

```bash
grep -n -A 50 "submit" BomPriceApproval.API/Features/Costing/CostingController.cs | head -80
```

Confirm `POST /api/costing/{id}/submit` exists and:
- (a) Validates `Status === RequisitionStatus.Costing` (returns 400/409 if other status).
- (b) Validates every `RequisitionItem` has a related `BomCost` row.
- (c) Returns 400 with field-level error path on missing data (e.g. `{"errors":{"finishedGoods[2].rawMaterialCosts[1].costPerKg":["required"]}}` or similar).
- (d) Transitions status `Costing → MdPricing`.
- (e) Notifies MD via `NotificationService`.

- [ ] **Step 2: If validation gaps exist — add the missing validation**

If endpoint accepts wrong status or skips per-FG cost-row check, add in same controller:

```csharp
if (req.Status != RequisitionStatus.Costing)
    return Conflict(new { error = $"Cannot submit costing — status is {req.Status}, expected Costing." });

var fgs = await _db.RequisitionItems
    .Where(ri => ri.RequisitionId == requisitionId)
    .Include(ri => ri.BomCost)
    .ToListAsync();

var missing = fgs.Where(fg => fg.BomCost == null).Select(fg => fg.Id).ToList();
if (missing.Count > 0)
    return BadRequest(new
    {
        error = "All FGs must have cost data before submit.",
        missingRequisitionItemIds = missing
    });
```

Skip Step 2 if validation already correct.

- [ ] **Step 3: Add test if validation was added or coverage missing**

If you added validation OR existing test class doesn't cover wrong-status / missing-cost cases, create `BomPriceApproval.Tests/Costing/CostingSubmitV3Tests.cs`:

```csharp
public class CostingSubmitV3Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public CostingSubmitV3Tests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Submit_FromNonCostingStatus_Returns409()
    {
        var (token, reqId) = await SeedReqInStatus(RequisitionStatus.Draft);
        _client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var resp = await _client.PostAsync($"/api/costing/{reqId}/submit", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Submit_FgWithoutBomCost_Returns400_WithMissingIds()
    {
        var (token, reqId, fgIdMissing) = await SeedCostingReqWith2FgsMissing1Cost();
        _client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var resp = await _client.PostAsync($"/api/costing/{reqId}/submit", null);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain(fgIdMissing.ToString());
    }

    [Fact]
    public async Task Submit_AllFgsCovered_Returns200_AndTransitionsToMdPricing()
    {
        var (token, reqId) = await SeedCostingReqFullyCosted();
        _client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var resp = await _client.PostAsync($"/api/costing/{reqId}/submit", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // ... reload + assert status == MdPricing
    }

    // helper SeedXxx methods follow project's existing test patterns:
    // login as Accountant via /api/auth/login, create FG/RM via admin payload, etc.
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~CostingSubmitV3Tests" --nologo -v normal
```

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Costing/CostingController.cs BomPriceApproval.Tests/Costing/CostingSubmitV3Tests.cs
git commit -m "test(costing): V3 submit endpoint validation coverage (B2)"
```

If neither code nor test changed (audit passed), skip the commit and add a note in PR body that B2 was a no-op verify.

---

### Task 3: Customer-swap allowed-status update (B3)

**Files:**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` (or wherever `PATCH /api/requisitions/{id}/customer` lives)
- Test (modify or add): `BomPriceApproval.Tests/Requisitions/ChangeCustomerTests.cs`

- [ ] **Step 1: Locate the endpoint**

```bash
grep -n "customer" BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs | head -20
```

Find the action. Confirm allowed-status check (V2.3 was `BomPending`/`BomInProgress`/`CostingPending`).

- [ ] **Step 2: Update allowed-status list to V3**

Replace the V2.3 status check with V3 set: `Draft` and `Costing`.

```csharp
private static readonly RequisitionStatus[] CustomerSwapAllowedStatuses =
{
    RequisitionStatus.Draft,
    RequisitionStatus.Costing,
};

// inside the action:
if (!CustomerSwapAllowedStatuses.Contains(req.Status))
    return Conflict(new { error = $"Customer swap not allowed in status {req.Status}. Allowed: Draft, Costing." });
```

- [ ] **Step 3: Update tests**

In `ChangeCustomerTests.cs`, ensure cases cover:
- Allowed: Draft, Costing → 200
- Disallowed: MdPricing, CustomerConfirm, MdFinalSign, Signed, Rejected, Cancelled → 409

Replace any V2.3-status assertions with V3 statuses.

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~ChangeCustomerTests" --nologo -v normal
```

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs BomPriceApproval.Tests/Requisitions/ChangeCustomerTests.cs
git commit -m "fix(requisitions): customer swap allowed-status set to V3 Draft+Costing (B3)"
```

---

### Task 4: Backend prereqs PR + merge

**Files:**
- None — git ops only

- [ ] **Step 1: Run full backend test suite**

```bash
dotnet test --nologo
```
Expected: 0 failures. Re-run if flaky timing test fails (per CLAUDE.md — known flake).

- [ ] **Step 2: Push branch + open PR**

```bash
git push -u origin feat/v3-mobile-d2-backend-prereqs
gh pr create --base master --head feat/v3-mobile-d2-backend-prereqs \
  --title "feat(backend): D-2 backend prereqs (B1 stats reshape, B2 submit verify, B3 customer-swap allowed-status)" \
  --body "$(cat <<'EOF'
## Summary

D-2 mobile rebuild prereqs per [`docs/superpowers/specs/2026-05-01-v3-mobile-phase-d-2-accountant-design.md`](docs/superpowers/specs/2026-05-01-v3-mobile-phase-d-2-accountant-design.md) §8.

- **B1** Stats endpoint V3 reshape (`{costing, awaitingMd, awaitingCustomer, submittedThisMonth}`)
- **B2** Costing-submit endpoint validation verified + tests added
- **B3** Customer-swap allowed-status updated to V3 (Draft + Costing only)

Web doesn't consume the stats endpoint. No web breakage expected.

## Test plan
- [x] dotnet test full suite green
- [x] B1: AccountantDashboardTests V3 reshape
- [x] B2: CostingSubmitV3Tests added (or no-op verify)
- [x] B3: ChangeCustomerTests V3 allowed-status

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 3: Wait for CI + auto-merge**

```bash
gh pr checks $(gh pr list --head feat/v3-mobile-d2-backend-prereqs --json number -q '.[0].number')
```

When all green:

```bash
PR=$(gh pr list --head feat/v3-mobile-d2-backend-prereqs --json number -q '.[0].number')
gh pr merge $PR --squash --delete-branch
```

- [ ] **Step 4: Sync master + verify**

```bash
git checkout master
git pull origin master
git log -1 --oneline
```

- [ ] **Step 5: Production deploy (optional but recommended for Fly)**

If you want backend prereqs LIVE before mobile work:

```bash
flyctl deploy --remote-only --config fly.toml -a bom-fpf-api
```

Mobile work below works locally regardless. But OTA preview channel hits production API, so to preview-test D-2 against live data you need backend deployed.

---

## Phase 1 — Mobile bootstrap (spec D-2.1.0)

### Task 5: Mobile feature branch

**Files:**
- None — git op only

- [ ] **Step 1: Create mobile feature branch**

```bash
git checkout -b feat/v3-mobile-phase-d-2-accountant
```

- [ ] **Step 2: Drift check**

```bash
git log mobile-shipped-vc1..HEAD --oneline -- bom-mobile/ | head
```
Expected: only D-1 commits (no native dep changes). If `app.config.ts` / `eas.json` / native deps in `package.json` changed since vc1 — note for Phase 9, but D-2 changes won't affect this either way.

---

## Phase 2 — Mobile API hook rewrites (spec D-2.1)

### Task 6: Rewrite costing.ts (V2.3 per-item → V3 bulk)

**Files:**
- Modify (overwrite): `bom-mobile/src/api/costing.ts`
- Modify: `bom-mobile/src/types/v3.ts` (add cost types)

- [ ] **Step 1: Add V3 cost type definitions to v3.ts**

Append to `bom-mobile/src/types/v3.ts`:

```typescript
export interface V3RawMaterialCostInput {
  bomLineId: number;
  costPerKg: number;
  currencyCode: string;
}

export interface V3FgCostInput {
  requisitionItemId: number;
  rawMaterialCosts: V3RawMaterialCostInput[];
  printingCostPerKg: number | null;
  printingCostCurrency: string | null;
  fohPerKg: number;
  transportPerKg: number;
  commissionPerKg: number;
}

export interface SaveV3CostDataPayload {
  finishedGoods: V3FgCostInput[];
}
```

- [ ] **Step 2: Overwrite costing.ts with V3 hooks**

```typescript
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import type { SaveV3CostDataPayload } from "@/types/v3";

export function useSaveV3CostData(requisitionId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (payload: SaveV3CostDataPayload) => {
      await api.put(`/api/costing/${requisitionId}/cost-data`, payload);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["requisition", requisitionId] });
    },
  });
}

export function useSubmitV3Costing(requisitionId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      await api.post(`/api/costing/${requisitionId}/submit`);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["requisition", requisitionId] });
      qc.invalidateQueries({ queryKey: ["requisitions"] });
      qc.invalidateQueries({ queryKey: ["stats", "accountantDashboardV3"] });
    },
  });
}
```

> Cost data is hydrated from the V3 requisition detail (`finishedGoods[].costs`), so no separate `useCostingReview` hook needed — D-1's `useRequisition` covers it.

- [ ] **Step 3: Verify nothing imports the deleted V2.3 hooks**

```bash
grep -rn "useCostingReview\|useStartCostingItem\|useSaveCostingItemDraft\|useSubmitCostingItem" bom-mobile/src bom-mobile/app
```
Expected: only matches inside `bom-mobile/app/(accountant)/` files (which are scheduled for deletion in Phase 8). Document the matches; they will be cleaned up by Phase 8 deletes.

- [ ] **Step 4: tsc check**

```bash
cd bom-mobile && npx tsc --noEmit; cd ..
```
Expected: existing V2.3 cross-phase residuals + new errors from V2.3 accountant files importing the deleted hooks. We tolerate this temporarily — Phase 8 will purge those files. Note the error count.

- [ ] **Step 5: Commit**

```bash
git add bom-mobile/src/api/costing.ts bom-mobile/src/types/v3.ts
git commit -m "refactor(mobile-d2): rewrite costing.ts for V3 bulk-upsert + submit"
```

---

### Task 7: Rewrite stats.ts for V3 dashboard shape

**Files:**
- Modify (overwrite): `bom-mobile/src/api/stats.ts`
- Modify: `bom-mobile/src/types/v3.ts` (add stats type)

- [ ] **Step 1: Add V3 dashboard type to v3.ts**

Append:

```typescript
export interface AccountantDashboardV3Stats {
  costing: number;
  awaitingMd: number;
  awaitingCustomer: number;
  submittedThisMonth: number;
}
```

- [ ] **Step 2: Overwrite stats.ts**

```typescript
import { useQuery } from "@tanstack/react-query";
import { api } from "./client";
import type { AccountantDashboardV3Stats } from "@/types/v3";

export function useAccountantDashboardV3() {
  return useQuery({
    queryKey: ["stats", "accountantDashboardV3"],
    queryFn: async () => {
      const res = await api.get<AccountantDashboardV3Stats>("/api/stats/accountant-dashboard");
      return res.data;
    },
    staleTime: 30_000,
  });
}
```

> Drop `useMdPendingCount` — was V2.3-only and unused by D-1. Verify with grep before deleting.

- [ ] **Step 3: Verify no remaining usages of dropped hook**

```bash
grep -rn "useMdPendingCount\|useAccountantDashboardStats" bom-mobile/src bom-mobile/app
```
Expected: only matches inside V2.3 accountant files (scheduled for deletion).

- [ ] **Step 4: tsc check**

```bash
cd bom-mobile && npx tsc --noEmit; cd ..
```
Expected: same pattern as Task 6 — V2.3 accountant residuals + new errors.

- [ ] **Step 5: Commit**

```bash
git add bom-mobile/src/api/stats.ts bom-mobile/src/types/v3.ts
git commit -m "refactor(mobile-d2): rewrite stats.ts for V3 accountant dashboard shape"
```

---

### Task 8a: Re-add useCustomerChangeHistory to requisitions.ts

**Files:**
- Modify: `bom-mobile/src/api/requisitions.ts`

D-2's customer-swap retention (D10) requires the existing `CustomerChangeHistorySheet` component, which imports `useCustomerChangeHistory` — a hook that was dropped during D-1 cleanup. Re-add the hook (NOT the branch-change variant — that's deleted with branch-swap UI).

- [ ] **Step 1: Add the hook to requisitions.ts**

```typescript
export function useCustomerChangeHistory(requisitionId: number, enabled = true) {
  return useQuery({
    queryKey: ["requisition", requisitionId, "customer-change-history"],
    queryFn: async () => {
      const res = await api.get<Array<{
        id: number;
        oldCustomerName: string;
        newCustomerName: string;
        changedByUserName: string;
        changedAt: string;
        reason?: string | null;
      }>>(`/api/requisitions/${requisitionId}/customer-change-history`);
      return res.data;
    },
    enabled: enabled && Number.isFinite(requisitionId) && requisitionId > 0,
    staleTime: 30_000,
  });
}
```

> Verify the endpoint URL matches the backend route. If backend uses a different URL, adjust. The endpoint should already exist — V2.3-A added it.

- [ ] **Step 2: Add `from?` query support to useRequisitions**

`useRequisitions` currently takes only `statuses?: string[]`. D-2's list page filters by month-start date too. Update:

```typescript
export function useRequisitions(opts?: { statuses?: string[]; from?: string }) {
  return useQuery({
    queryKey: ["requisitions", opts?.statuses, opts?.from],
    queryFn: async () => {
      const params = new URLSearchParams();
      (opts?.statuses ?? []).forEach((s) => params.append("status", s));
      if (opts?.from) params.append("from", opts.from);
      const res = await api.get<V3RequisitionListItem[]>(`/api/requisitions?${params.toString()}`);
      return res.data;
    },
    staleTime: 10_000,
  });
}
```

> Update D-1 callers of `useRequisitions(statusesArray)` to pass `{ statuses: statusesArray }` instead. Grep for callers:

```bash
grep -rn "useRequisitions(" bom-mobile/src bom-mobile/app
```

Update each call. The change is mechanical: `useRequisitions(arr)` → `useRequisitions({ statuses: arr })`.

- [ ] **Step 3: tsc check**

```bash
cd bom-mobile && npx tsc --noEmit; cd ..
```
Expected: D-1 callers updated, no new errors.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/src/api/requisitions.ts bom-mobile/src bom-mobile/app
git commit -m "feat(mobile-d2): re-add useCustomerChangeHistory + extend useRequisitions with from filter"
```

---

### Task 8: Promote ReqCard + statusMap to shared

**Files:**
- Create: `bom-mobile/src/utils/v3StatusMap.ts`
- Create: `bom-mobile/src/components/ReqCard.tsx`
- Delete (after import update): `bom-mobile/src/features/sales/utils/statusMap.ts`
- Delete (after import update): `bom-mobile/src/features/sales/list/ReqCard.tsx`
- Modify: every importer of either

- [ ] **Step 1: Create the shared statusMap**

Read `bom-mobile/src/features/sales/utils/statusMap.ts` and copy its content (verbatim) into `bom-mobile/src/utils/v3StatusMap.ts`.

```bash
cp bom-mobile/src/features/sales/utils/statusMap.ts bom-mobile/src/utils/v3StatusMap.ts
```

- [ ] **Step 2: Create the shared ReqCard**

Read `bom-mobile/src/features/sales/list/ReqCard.tsx` and copy into `bom-mobile/src/components/ReqCard.tsx`. Update its imports for the new location:

```typescript
import { Pressable, Text, View } from "react-native";
import * as Haptics from "expo-haptics";
import type { V3RequisitionListItem } from "@/types/v3";
import { STATUS_COLOR, STATUS_LABEL } from "@/utils/v3StatusMap";
import { OwnedByBadge } from "@/components/OwnedByBadge";
import { useAuth } from "@/auth/AuthContext";
// ...rest unchanged from sales/list/ReqCard.tsx body...
```

- [ ] **Step 3: Update D-1 SP imports to use shared paths**

```bash
grep -rln "@/features/sales/list/ReqCard\|sales/list/ReqCard\|features/sales/utils/statusMap\|sales/utils/statusMap" bom-mobile
```

For each match, replace imports:
- `from "...features/sales/list/ReqCard"` → `from "@/components/ReqCard"`
- `from "...features/sales/utils/statusMap"` → `from "@/utils/v3StatusMap"`

Use Edit tool, not sed — paths use Windows separators in some places.

- [ ] **Step 4: Delete the old files**

```bash
rm bom-mobile/src/features/sales/utils/statusMap.ts
rm bom-mobile/src/features/sales/list/ReqCard.tsx
rmdir bom-mobile/src/features/sales/utils 2>/dev/null
```

- [ ] **Step 5: tsc check**

```bash
cd bom-mobile && npx tsc --noEmit; cd ..
```
Expected: no NEW errors from the promotion (errors should be SAME as Task 7 baseline). If new errors → import update was missed somewhere.

- [ ] **Step 6: Commit**

```bash
git add bom-mobile/src/components/ReqCard.tsx bom-mobile/src/utils/v3StatusMap.ts bom-mobile/src/features/sales bom-mobile/app
git commit -m "refactor(mobile-d2): promote ReqCard + statusMap to shared (D-1 SP imports updated)"
```

---

## Phase 3 — Dashboard (spec D-2.2)

### Task 9: KpiHeroCard component

**Files:**
- Create: `bom-mobile/src/features/accountant/dashboard/KpiHeroCard.tsx`

- [ ] **Step 1: Create the component**

```typescript
import { Pressable, Text, View } from "react-native";
import { MotiView } from "moti";
import { Skeleton } from "@/components/Skeleton";

interface Props {
  count: number | undefined;
  loading: boolean;
  onPress: () => void;
}

export function KpiHeroCard({ count, loading, onPress }: Props) {
  return (
    <MotiView
      from={{ opacity: 0, translateY: 14 }}
      animate={{ opacity: 1, translateY: 0 }}
      transition={{ type: "spring", damping: 14, stiffness: 140, delay: 100 }}
    >
      <Pressable
        onPress={onPress}
        style={({ pressed }) => ({ transform: [{ scale: pressed ? 0.98 : 1 }] })}
      >
        <View style={{
          backgroundColor: "#1e40af",
          borderRadius: 16,
          padding: 20,
          marginBottom: 12,
          shadowColor: "#1e40af",
          shadowOffset: { width: 0, height: 6 },
          shadowOpacity: 0.3,
          shadowRadius: 12,
          elevation: 6,
        }}>
          <Text style={{ color: "#dbeafe", fontSize: 13, fontWeight: "600", letterSpacing: 0.5 }}>
            COSTING TO COMPLETE
          </Text>
          <View style={{ flexDirection: "row", alignItems: "flex-end", marginTop: 10 }}>
            {loading ? (
              <Skeleton width={80} height={44} radius={8} style={{ backgroundColor: "rgba(255,255,255,0.25)" }} />
            ) : (
              <Text style={{ color: "#ffffff", fontSize: 44, fontWeight: "800", letterSpacing: -1 }}>
                {count ?? 0}
              </Text>
            )}
            <Text style={{ color: "#dbeafe", fontSize: 15, marginLeft: 10, marginBottom: 8 }}>
              to review
            </Text>
          </View>
          <Text style={{ color: "#dbeafe", fontSize: 14, marginTop: 14 }}>
            Tap to open the queue →
          </Text>
        </View>
      </Pressable>
    </MotiView>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/accountant/dashboard/KpiHeroCard.tsx
git commit -m "feat(mobile-d2): KpiHeroCard component (Costing-to-complete hero)"
```

---

### Task 10: KpiRow component

**Files:**
- Create: `bom-mobile/src/features/accountant/dashboard/KpiRow.tsx`

- [ ] **Step 1: Create the component**

```typescript
import { Pressable, Text, View } from "react-native";
import { MotiView } from "moti";
import { Skeleton } from "@/components/Skeleton";

interface Props {
  label: string;
  value: number | undefined;
  loading: boolean;
  delay: number;
  onPress: () => void;
}

export function KpiRow({ label, value, loading, delay, onPress }: Props) {
  return (
    <MotiView
      from={{ opacity: 0, translateY: 14 }}
      animate={{ opacity: 1, translateY: 0 }}
      transition={{ type: "spring", damping: 14, stiffness: 140, delay }}
    >
      <Pressable
        onPress={onPress}
        style={({ pressed }) => ({ transform: [{ scale: pressed ? 0.98 : 1 }] })}
      >
        <View style={{
          backgroundColor: "#ffffff",
          borderWidth: 1,
          borderColor: "#e2e8f0",
          borderRadius: 14,
          padding: 14,
          marginBottom: 8,
          flexDirection: "row",
          alignItems: "center",
          justifyContent: "space-between",
        }}>
          <Text style={{ color: "#64748b", fontSize: 12, fontWeight: "700", letterSpacing: 0.5 }}>
            {label}
          </Text>
          {loading ? (
            <Skeleton width={36} height={26} />
          ) : (
            <Text style={{ color: "#0f172a", fontSize: 22, fontWeight: "800" }}>{value ?? 0}</Text>
          )}
        </View>
      </Pressable>
    </MotiView>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/accountant/dashboard/KpiRow.tsx
git commit -m "feat(mobile-d2): KpiRow component (compact KPI card)"
```

---

### Task 11: AccountantDashboard component + route

**Files:**
- Create: `bom-mobile/src/features/accountant/dashboard/AccountantDashboard.tsx`
- Create (overwrite Phase 8): keep `bom-mobile/app/(accountant)/index.tsx` for now — V2.3 file is being replaced; we'll switch it to import the new dashboard in Task 33's purge step. For now create dashboard alongside.

- [ ] **Step 1: Create the dashboard root component**

```typescript
import { useCallback, useState } from "react";
import { Pressable, RefreshControl, ScrollView, Text, View } from "react-native";
import { Stack, useRouter } from "expo-router";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useAuth } from "@/auth/AuthContext";
import { useAccountantDashboardV3 } from "@/api/stats";
import { useUnreadCount } from "@/api/notifications";
import { ScreenHeader } from "@/components/ScreenHeader";
import { Skeleton } from "@/components/Skeleton";
import { NotificationBell } from "@/components/NotificationBell";
import { KpiHeroCard } from "./KpiHeroCard";
import { KpiRow } from "./KpiRow";

function greet(): string {
  const h = new Date().getHours();
  if (h < 12) return "Good morning";
  if (h < 17) return "Good afternoon";
  if (h < 21) return "Good evening";
  return "Good night";
}

function startOfMonthIsoDate(): string {
  const now = new Date();
  return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1)).toISOString().slice(0, 10);
}

export function AccountantDashboard() {
  const router = useRouter();
  const { user, logout } = useAuth();
  const insets = useSafeAreaInsets();
  const statsQ = useAccountantDashboardV3();
  const unreadQ = useUnreadCount();
  const [refreshing, setRefreshing] = useState(false);

  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    try { await Promise.all([statsQ.refetch(), unreadQ.refetch()]); }
    finally { setRefreshing(false); }
  }, [statsQ, unreadQ]);

  const onLogout = async () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    await logout();
    router.replace("/login");
  };

  const firstName = (user?.name ?? "").split(" ")[0] || "there";
  const monthStart = startOfMonthIsoDate();

  const navTo = (path: string) => {
    Haptics.selectionAsync();
    router.push(path as Parameters<typeof router.push>[0]);
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

      <ScreenHeader label="ACCOUNTANT" title={`${greet()}, ${firstName} 👋`} right={HeaderRight} />

      <ScrollView
        contentContainerStyle={{ padding: 16, paddingTop: 4, paddingBottom: Math.max(insets.bottom, 16) + 16 }}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor="#1e40af" colors={["#1e40af"]} />
        }
      >
        <KpiHeroCard
          count={statsQ.data?.costing}
          loading={statsQ.isPending}
          onPress={() => navTo("/(accountant)/list?tab=queue")}
        />

        <KpiRow
          label="AWAITING MD"
          value={statsQ.data?.awaitingMd}
          loading={statsQ.isPending}
          delay={180}
          onPress={() => navTo("/(accountant)/list?tab=in-flight&filter=md")}
        />

        <KpiRow
          label="AWAITING CUSTOMER"
          value={statsQ.data?.awaitingCustomer}
          loading={statsQ.isPending}
          delay={260}
          onPress={() => navTo("/(accountant)/list?tab=in-flight&filter=customer")}
        />

        <KpiRow
          label="MD-BOUND THIS MONTH"
          value={statsQ.data?.submittedThisMonth}
          loading={statsQ.isPending}
          delay={340}
          onPress={() => navTo(`/(accountant)/list?tab=in-flight&from=${monthStart}`)}
        />

        <MotiView
          from={{ opacity: 0, translateY: 14 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "spring", damping: 14, stiffness: 140, delay: 420 }}
        >
          <Pressable
            onPress={() => navTo("/notifications")}
            style={({ pressed }) => ({ transform: [{ scale: pressed ? 0.98 : 1 }] })}
          >
            <View style={{
              backgroundColor: "#ffffff",
              borderWidth: 1, borderColor: "#e2e8f0",
              borderRadius: 14, padding: 16, marginTop: 4, marginBottom: 12,
              flexDirection: "row", alignItems: "center", justifyContent: "space-between",
            }}>
              <View style={{ flex: 1 }}>
                <Text style={{ color: "#64748b", fontSize: 13, fontWeight: "600", letterSpacing: 0.5 }}>NOTIFICATIONS</Text>
                <View style={{ flexDirection: "row", alignItems: "baseline", marginTop: 4 }}>
                  {unreadQ.isPending ? (
                    <Skeleton width={40} height={24} />
                  ) : (
                    <Text style={{ color: "#0f172a", fontSize: 22, fontWeight: "700" }}>
                      {unreadQ.data ?? 0}
                    </Text>
                  )}
                  <Text style={{ color: "#64748b", fontSize: 14, marginLeft: 8 }}>unread</Text>
                </View>
              </View>
              <Text style={{ fontSize: 28 }}>🔔</Text>
            </View>
          </Pressable>
        </MotiView>

        <MotiView
          from={{ opacity: 0, translateY: 14 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "spring", damping: 14, stiffness: 140, delay: 500 }}
        >
          <View style={{
            backgroundColor: "#ffffff",
            borderWidth: 1, borderColor: "#e2e8f0",
            borderRadius: 14, padding: 16,
          }}>
            <Text style={{ color: "#64748b", fontSize: 13, fontWeight: "600", letterSpacing: 0.5 }}>SIGNED IN AS</Text>
            <Text style={{ color: "#0f172a", fontSize: 17, fontWeight: "700", marginTop: 6 }}>{user?.name ?? "—"}</Text>
            <Text style={{ color: "#64748b", fontSize: 14, marginTop: 2 }}>Accountant</Text>
          </View>
        </MotiView>
      </ScrollView>
    </View>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/accountant/dashboard/AccountantDashboard.tsx
git commit -m "feat(mobile-d2): AccountantDashboard component (V3 KPIs)"
```

---

## Phase 4 — List page (spec D-2.3)

### Task 12: AccountantTabs component

**Files:**
- Create: `bom-mobile/src/features/accountant/list/AccountantTabs.tsx`

- [ ] **Step 1: Create 4-tab segmented control**

```typescript
import { Pressable, Text, View } from "react-native";
import * as Haptics from "expo-haptics";

export type AccountantTab = "queue" | "in-flight" | "done" | "closed";

const TAB_LABELS: Record<AccountantTab, string> = {
  queue: "Queue",
  "in-flight": "In Flight",
  done: "Done",
  closed: "Closed",
};

interface Props {
  active: AccountantTab;
  onChange: (tab: AccountantTab) => void;
}

export function AccountantTabs({ active, onChange }: Props) {
  return (
    <View style={{
      flexDirection: "row",
      backgroundColor: "#f1f5f9",
      borderRadius: 10,
      padding: 4,
      margin: 12,
    }}>
      {(Object.keys(TAB_LABELS) as AccountantTab[]).map((t) => {
        const isActive = active === t;
        return (
          <Pressable
            key={t}
            onPress={() => {
              Haptics.selectionAsync();
              onChange(t);
            }}
            style={{
              flex: 1,
              paddingVertical: 8,
              borderRadius: 8,
              backgroundColor: isActive ? "#ffffff" : "transparent",
              shadowColor: isActive ? "#0f172a" : "transparent",
              shadowOffset: { width: 0, height: 1 },
              shadowOpacity: 0.06,
              shadowRadius: 2,
              elevation: isActive ? 1 : 0,
            }}
          >
            <Text style={{
              textAlign: "center",
              fontSize: 13,
              fontWeight: isActive ? "700" : "500",
              color: isActive ? "#0f172a" : "#64748b",
            }}>
              {TAB_LABELS[t]}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/accountant/list/AccountantTabs.tsx
git commit -m "feat(mobile-d2): AccountantTabs (Queue/In Flight/Done/Closed segmented control)"
```

---

### Task 13: InFlightSubFilterChips component

**Files:**
- Create: `bom-mobile/src/features/accountant/list/InFlightSubFilterChips.tsx`

- [ ] **Step 1: Create sub-filter chip row**

```typescript
import { Pressable, Text, View } from "react-native";
import * as Haptics from "expo-haptics";

export type InFlightSubFilter = "all" | "md" | "customer";

const CHIP_LABELS: Record<InFlightSubFilter, string> = {
  all: "All",
  md: "MD",
  customer: "Customer",
};

interface Props {
  active: InFlightSubFilter;
  onChange: (chip: InFlightSubFilter) => void;
}

export function InFlightSubFilterChips({ active, onChange }: Props) {
  return (
    <View style={{ flexDirection: "row", gap: 8, paddingHorizontal: 12, paddingBottom: 8 }}>
      {(Object.keys(CHIP_LABELS) as InFlightSubFilter[]).map((c) => {
        const isActive = active === c;
        return (
          <Pressable
            key={c}
            onPress={() => {
              Haptics.selectionAsync();
              onChange(c);
            }}
            style={{
              paddingHorizontal: 12,
              paddingVertical: 6,
              borderRadius: 999,
              borderWidth: 1,
              borderColor: isActive ? "#1e40af" : "#cbd5e1",
              backgroundColor: isActive ? "#1e40af" : "#ffffff",
            }}
          >
            <Text style={{
              fontSize: 12,
              fontWeight: "600",
              color: isActive ? "#ffffff" : "#64748b",
            }}>
              {CHIP_LABELS[c]}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/accountant/list/InFlightSubFilterChips.tsx
git commit -m "feat(mobile-d2): InFlightSubFilterChips (All/MD/Customer)"
```

---

### Task 14: AccountantListScreen + statuses-for-tab helper

**Files:**
- Create: `bom-mobile/src/features/accountant/list/AccountantListScreen.tsx`
- Create: `bom-mobile/src/features/accountant/list/tabFilters.ts`

- [ ] **Step 1: Create the tab→statuses mapping helper**

```typescript
// bom-mobile/src/features/accountant/list/tabFilters.ts
import type { V3Status } from "@/types/v3";
import type { AccountantTab } from "./AccountantTabs";
import type { InFlightSubFilter } from "./InFlightSubFilterChips";

export function statusesForTab(tab: AccountantTab, sub: InFlightSubFilter = "all"): V3Status[] {
  switch (tab) {
    case "queue":
      return ["Costing"];
    case "done":
      return ["Signed"];
    case "closed":
      return ["Rejected", "Cancelled"];
    case "in-flight":
      if (sub === "md") return ["MdPricing", "MdFinalSign"];
      if (sub === "customer") return ["CustomerConfirm"];
      return ["MdPricing", "CustomerConfirm", "MdFinalSign"];
  }
}
```

- [ ] **Step 2: Create the list screen**

```typescript
import { useMemo, useState } from "react";
import { FlatList, Text, View, RefreshControl, Pressable } from "react-native";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { useRequisitions } from "@/api/requisitions";
import { ScreenHeader } from "@/components/ScreenHeader";
import { ReqCard } from "@/components/ReqCard";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";
import { EmptyState } from "@/components/EmptyState";
import { AccountantTabs, type AccountantTab } from "./AccountantTabs";
import { InFlightSubFilterChips, type InFlightSubFilter } from "./InFlightSubFilterChips";
import { statusesForTab } from "./tabFilters";

export function AccountantListScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ tab?: string; filter?: string; from?: string }>();

  const initialTab = (params.tab as AccountantTab | undefined) ?? "queue";
  const initialFilter = (params.filter as InFlightSubFilter | undefined) ?? "all";

  const [tab, setTab] = useState<AccountantTab>(initialTab);
  const [sub, setSub] = useState<InFlightSubFilter>(initialFilter);
  const [from, setFrom] = useState<string | undefined>(params.from);

  const statuses = useMemo(() => statusesForTab(tab, sub), [tab, sub]);
  const reqsQ = useRequisitions({ statuses, from });

  const HeaderRight = null;

  const onTabChange = (next: AccountantTab) => {
    setTab(next);
    setSub("all");
    if (next !== "in-flight") setFrom(undefined);
  };

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader title="Requisitions" back right={HeaderRight} />

      <AccountantTabs active={tab} onChange={onTabChange} />
      {tab === "in-flight" ? (
        <InFlightSubFilterChips active={sub} onChange={setSub} />
      ) : null}

      {from ? (
        <View style={{
          flexDirection: "row",
          alignItems: "center",
          paddingHorizontal: 12,
          marginBottom: 8,
        }}>
          <View style={{
            backgroundColor: "#fef3c7",
            paddingHorizontal: 10,
            paddingVertical: 6,
            borderRadius: 999,
            flexDirection: "row",
            alignItems: "center",
          }}>
            <Text style={{ fontSize: 12, color: "#92400e", fontWeight: "600" }}>
              From {from}
            </Text>
            <Pressable onPress={() => setFrom(undefined)} style={{ marginLeft: 8 }}>
              <Text style={{ fontSize: 12, color: "#92400e", fontWeight: "700" }}>×</Text>
            </Pressable>
          </View>
        </View>
      ) : null}

      {reqsQ.isPending ? (
        <LoadingView />
      ) : reqsQ.isError ? (
        <ErrorBanner message="Failed to load requisitions" onRetry={() => reqsQ.refetch()} />
      ) : (reqsQ.data ?? []).length === 0 ? (
        <EmptyState
          title={emptyTitleFor(tab, sub)}
          subtitle="Nothing to show here."
        />
      ) : (
        <FlatList
          data={reqsQ.data}
          keyExtractor={(r) => String(r.id)}
          renderItem={({ item }) => (
            <ReqCard req={item} onPress={(id) => router.push(`/(accountant)/${id}`)} />
          )}
          refreshControl={
            <RefreshControl
              refreshing={reqsQ.isFetching}
              onRefresh={() => reqsQ.refetch()}
              tintColor="#1e40af"
            />
          }
        />
      )}
    </View>
  );
}

function emptyTitleFor(tab: AccountantTab, sub: InFlightSubFilter): string {
  if (tab === "queue") return "Nothing in your queue";
  if (tab === "done") return "No signed quotes yet";
  if (tab === "closed") return "Nothing closed";
  if (sub === "md") return "Nothing awaiting MD";
  if (sub === "customer") return "Nothing awaiting customer";
  return "Nothing in flight";
}
```

> Note: `useRequisitions({ statuses, from })` is the D-1 list-fetch hook. Verify its signature accepts `statuses?: V3Status[]` and `from?: string` query params. If signatures differ, adapt: pass each status as a separate `?status=` query param (the existing V3 controller likely accepts comma-separated or repeated params).

- [ ] **Step 3: tsc check**

```bash
cd bom-mobile && npx tsc --noEmit; cd ..
```
Expected: error count same as Phase 2 baseline (V2.3 accountant residuals only). New screen compiles.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/src/features/accountant/list
git commit -m "feat(mobile-d2): AccountantListScreen with 4-tab + sub-filter"
```

---

## Phase 5 — Detail read-only path (spec D-2.4)

### Task 15: ReadonlyDetailView component

**Files:**
- Create: `bom-mobile/src/features/accountant/detail/ReadonlyDetailView.tsx`

- [ ] **Step 1: Create the component**

This component reuses D-1's `DetailHeader`, `FgReadCard`, `FinalPriceCard`, `StatusFooterCta` patterns. Cross-feature reuse is acceptable here because they're already V3-generic; if cleanliness demands, also promote them to `bom-mobile/src/components/v3/` in this task. For now, import from D-1's location:

```typescript
import { ScrollView, Text, View } from "react-native";
import type { V3Requisition } from "@/types/v3";
import { DetailHeader } from "@/features/sales/detail/DetailHeader";
import { FgReadCard } from "@/features/sales/detail/FgReadCard";
import { FinalPriceCard } from "@/features/sales/detail/FinalPriceCard";

interface Props {
  req: V3Requisition;
}

export function ReadonlyDetailView({ req }: Props) {
  return (
    <ScrollView contentContainerStyle={{ paddingBottom: 24 }}>
      <DetailHeader req={req} />

      {/* Per-status footer text */}
      {req.status === "MdPricing" ? (
        <FooterText text="Waiting on MD margin pricing" tone="info" />
      ) : null}
      {req.status === "CustomerConfirm" ? (
        <FooterText text="Waiting on SP customer-confirm" tone="info" />
      ) : null}
      {req.status === "MdFinalSign" ? (
        <FooterText text="Waiting on MD final sign" tone="info" />
      ) : null}
      {req.status === "Rejected" ? (
        <FooterText
          text={`Rejected: ${req.cancelReason ?? "(no reason recorded)"}`}
          tone="danger"
        />
      ) : null}
      {req.status === "Cancelled" ? (
        <FooterText
          text={`Cancelled${req.cancelledAt ? ` on ${new Date(req.cancelledAt).toLocaleDateString()}` : ""}: ${req.cancelReason ?? "(no reason)"}`}
          tone="muted"
        />
      ) : null}

      {req.finishedGoods.map((fg) => (
        <FgReadCard key={fg.id ?? fg.itemId} fg={fg} />
      ))}

      {req.status === "Signed" && req.finalPrice ? (
        <FinalPriceCard price={req.finalPrice} currency={req.currencyCode} />
      ) : null}
    </ScrollView>
  );
}

function FooterText({ text, tone }: { text: string; tone: "info" | "danger" | "muted" }) {
  const bg = tone === "info" ? "#eff6ff" : tone === "danger" ? "#fee2e2" : "#f1f5f9";
  const fg = tone === "info" ? "#1e40af" : tone === "danger" ? "#b91c1c" : "#475569";
  return (
    <View style={{
      backgroundColor: bg,
      marginHorizontal: 12, marginVertical: 8,
      padding: 12, borderRadius: 10,
    }}>
      <Text style={{ color: fg, fontSize: 14, fontWeight: "600" }}>{text}</Text>
    </View>
  );
}
```

> If `DetailHeader`/`FgReadCard`/`FinalPriceCard` props don't match `V3Requisition` directly, adjust the destructuring inside this component instead of editing D-1 components — keeps D-1 stable.

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/accountant/detail/ReadonlyDetailView.tsx
git commit -m "feat(mobile-d2): ReadonlyDetailView for non-Costing statuses"
```

---

## Phase 6 — Detail active path + cost-input drawer (spec D-2.5)

The biggest phase. State-management hook, pure readiness logic, drawer composition, parent screen.

### Task 16: fgReadiness pure function + tests

**Files:**
- Create: `bom-mobile/src/features/accountant/state/fgReadiness.ts`
- Create: `bom-mobile/src/features/accountant/state/fgReadiness.test.ts`

- [ ] **Step 1: Write the failing test**

```typescript
// fgReadiness.test.ts
import { describe, it, expect } from "vitest"; // or jest depending on bom-mobile config; check existing tests
import { fgReadiness, type FgDraftState } from "./fgReadiness";

describe("fgReadiness", () => {
  const empty: FgDraftState = {
    requisitionItemId: 1,
    hasPrinting: false,
    rawMaterialCosts: [{ bomLineId: 10, costPerKg: "", currencyCode: "" }],
    printingCostPerKg: "",
    printingCostCurrency: "",
    fohPerKg: "",
    transportPerKg: "",
    commissionPerKg: "",
  };

  it("returns 'not_started' when no fields touched", () => {
    expect(fgReadiness(empty)).toBe("not_started");
  });

  it("returns 'in_progress' when some RM cost set but others missing", () => {
    expect(fgReadiness({ ...empty, fohPerKg: "1.5" })).toBe("in_progress");
  });

  it("returns 'ready' when all RM costs > 0 + currencies set + FOH/Transport/Commission non-empty", () => {
    const state: FgDraftState = {
      ...empty,
      rawMaterialCosts: [{ bomLineId: 10, costPerKg: "2.5", currencyCode: "AED" }],
      fohPerKg: "1.0",
      transportPerKg: "0.5",
      commissionPerKg: "0",
    };
    expect(fgReadiness(state)).toBe("ready");
  });

  it("requires printing fields when hasPrinting=true", () => {
    const stateNoPrinting: FgDraftState = {
      ...empty,
      hasPrinting: true,
      rawMaterialCosts: [{ bomLineId: 10, costPerKg: "2.5", currencyCode: "AED" }],
      fohPerKg: "1.0",
      transportPerKg: "0.5",
      commissionPerKg: "0",
    };
    expect(fgReadiness(stateNoPrinting)).toBe("in_progress");

    const statePrinting: FgDraftState = {
      ...stateNoPrinting,
      printingCostPerKg: "0.8",
      printingCostCurrency: "AED",
    };
    expect(fgReadiness(statePrinting)).toBe("ready");
  });

  it("rejects RM cost = 0 as not-yet-ready", () => {
    const state: FgDraftState = {
      ...empty,
      rawMaterialCosts: [{ bomLineId: 10, costPerKg: "0", currencyCode: "AED" }],
      fohPerKg: "1.0",
      transportPerKg: "0.5",
      commissionPerKg: "0",
    };
    expect(fgReadiness(state)).toBe("in_progress");
  });
});
```

- [ ] **Step 2: Run test — verify it fails**

```bash
cd bom-mobile && npx vitest run src/features/accountant/state/fgReadiness.test.ts; cd ..
```
Expected: `fgReadiness is not a function` or similar import error. If `vitest` not configured, replace with whatever test runner the mobile project uses (check `bom-mobile/package.json` scripts). If no test runner exists, skip Step 2-3 — but DO write the test file anyway as documentation of intent.

- [ ] **Step 3: Implement fgReadiness**

```typescript
// fgReadiness.ts
export interface RawMaterialCostState {
  bomLineId: number;
  costPerKg: string;
  currencyCode: string;
}

export interface FgDraftState {
  requisitionItemId: number;
  hasPrinting: boolean;
  rawMaterialCosts: RawMaterialCostState[];
  printingCostPerKg: string;
  printingCostCurrency: string;
  fohPerKg: string;
  transportPerKg: string;
  commissionPerKg: string;
}

export type FgReadiness = "not_started" | "in_progress" | "ready";

function isPositive(s: string): boolean {
  if (s === "" || s === "-") return false;
  const n = parseFloat(s);
  return Number.isFinite(n) && n > 0;
}

function isNonEmpty(s: string): boolean {
  if (s === "" || s === "-") return false;
  const n = parseFloat(s);
  return Number.isFinite(n);
}

export function fgReadiness(s: FgDraftState): FgReadiness {
  const anyTouched =
    s.rawMaterialCosts.some((rc) => rc.costPerKg !== "" || rc.currencyCode !== "")
    || s.fohPerKg !== "" || s.transportPerKg !== "" || s.commissionPerKg !== ""
    || s.printingCostPerKg !== "" || s.printingCostCurrency !== "";
  if (!anyTouched) return "not_started";

  const allRmReady = s.rawMaterialCosts.every(
    (rc) => isPositive(rc.costPerKg) && rc.currencyCode !== ""
  );
  const printingReady = !s.hasPrinting
    || (isPositive(s.printingCostPerKg) && s.printingCostCurrency !== "");
  const otherReady = isNonEmpty(s.fohPerKg) && isNonEmpty(s.transportPerKg) && isNonEmpty(s.commissionPerKg);

  if (allRmReady && printingReady && otherReady) return "ready";
  return "in_progress";
}
```

- [ ] **Step 4: Run test — verify pass**

```bash
cd bom-mobile && npx vitest run src/features/accountant/state/fgReadiness.test.ts; cd ..
```
Expected: 5 passing.

- [ ] **Step 5: Commit**

```bash
git add bom-mobile/src/features/accountant/state/fgReadiness.ts bom-mobile/src/features/accountant/state/fgReadiness.test.ts
git commit -m "feat(mobile-d2): fgReadiness pure function + unit tests (D3 rules)"
```

---

### Task 17: useCostingDraftState hook

**Files:**
- Create: `bom-mobile/src/features/accountant/state/useCostingDraftState.ts`

- [ ] **Step 1: Implement the hook**

```typescript
import { useCallback, useMemo, useState } from "react";
import type { V3Requisition } from "@/types/v3";
import { fgReadiness, type FgDraftState, type FgReadiness } from "./fgReadiness";

function initFromReq(req: V3Requisition): FgDraftState[] {
  return req.finishedGoods.map((fg) => {
    const existing = fg.costs ?? null;
    return {
      requisitionItemId: fg.id ?? fg.itemId,
      hasPrinting: (fg as { hasPrinting?: boolean }).hasPrinting ?? false,
      rawMaterialCosts: (fg.bomLines ?? []).map((bl) => {
        const existingCost = (existing as unknown as { lines?: { bomLineId: number; purchaseValuePerKg?: number; purchaseCurrency?: string }[] } | null)
          ?.lines?.find((c) => c.bomLineId === (bl.id ?? -1));
        return {
          bomLineId: bl.id ?? -1,
          costPerKg: existingCost?.purchaseValuePerKg != null ? String(existingCost.purchaseValuePerKg) : "",
          currencyCode: existingCost?.purchaseCurrency ?? "AED",
        };
      }),
      printingCostPerKg: (existing as unknown as { printingCostPerKg?: number } | null)?.printingCostPerKg != null
        ? String((existing as unknown as { printingCostPerKg: number }).printingCostPerKg) : "",
      printingCostCurrency: (existing as unknown as { printingCostCurrency?: string } | null)?.printingCostCurrency ?? "AED",
      fohPerKg: existing?.foh != null ? String(existing.foh) : "",
      transportPerKg: existing?.transport != null ? String(existing.transport) : "",
      commissionPerKg: existing?.commission != null ? String(existing.commission) : "",
    };
  });
}

export interface UseCostingDraftState {
  drafts: FgDraftState[];
  readiness: FgReadiness[];
  allReady: boolean;
  setFg: (idx: number, partial: Partial<FgDraftState>) => void;
  setRmCost: (fgIdx: number, rmIdx: number, partial: Partial<FgDraftState["rawMaterialCosts"][number]>) => void;
  isDirtyVsBaseline: (idx: number, baseline: FgDraftState) => number; // count of diff fields
}

export function useCostingDraftState(req: V3Requisition): UseCostingDraftState {
  const [drafts, setDrafts] = useState<FgDraftState[]>(() => initFromReq(req));

  const readiness = useMemo(() => drafts.map(fgReadiness), [drafts]);
  const allReady = readiness.every((r) => r === "ready");

  const setFg = useCallback((idx: number, partial: Partial<FgDraftState>) => {
    setDrafts((prev) => {
      const next = [...prev];
      next[idx] = { ...next[idx], ...partial };
      return next;
    });
  }, []);

  const setRmCost = useCallback((fgIdx: number, rmIdx: number, partial: Partial<FgDraftState["rawMaterialCosts"][number]>) => {
    setDrafts((prev) => {
      const next = [...prev];
      next[fgIdx] = { ...next[fgIdx], rawMaterialCosts: [...next[fgIdx].rawMaterialCosts] };
      next[fgIdx].rawMaterialCosts[rmIdx] = { ...next[fgIdx].rawMaterialCosts[rmIdx], ...partial };
      return next;
    });
  }, []);

  const isDirtyVsBaseline = useCallback((idx: number, baseline: FgDraftState) => {
    const cur = drafts[idx];
    let diff = 0;
    if (cur.fohPerKg !== baseline.fohPerKg) diff++;
    if (cur.transportPerKg !== baseline.transportPerKg) diff++;
    if (cur.commissionPerKg !== baseline.commissionPerKg) diff++;
    if (cur.printingCostPerKg !== baseline.printingCostPerKg) diff++;
    if (cur.printingCostCurrency !== baseline.printingCostCurrency) diff++;
    cur.rawMaterialCosts.forEach((rc, i) => {
      const b = baseline.rawMaterialCosts[i];
      if (b && rc.costPerKg !== b.costPerKg) diff++;
      if (b && rc.currencyCode !== b.currencyCode) diff++;
    });
    return diff;
  }, [drafts]);

  return { drafts, readiness, allReady, setFg, setRmCost, isDirtyVsBaseline };
}
```

> The deep-typed `V3FinishedGood.costs` shape lives in `bom-mobile/src/types/v3.ts`. The current shape is `costs?: { foh?: number; transport?: number; commission?: number } | null` — too narrow for V3's actual costs payload. **Update** `v3.ts` to add `lines?` and `printingCostPerKg`/`printingCostCurrency` if missing. Skip the `as unknown as ...` casts above by widening the type. Do this inline in this task.

- [ ] **Step 2: Widen v3.ts cost shape**

In `bom-mobile/src/types/v3.ts`, replace the existing `V3FinishedGood.costs` declaration with:

```typescript
export interface V3CostLine {
  bomLineId: number;
  purchaseValuePerKg?: number | null;
  purchaseCurrency?: string | null;
}

export interface V3Costs {
  lines?: V3CostLine[];
  printingCostPerKg?: number | null;
  printingCostCurrency?: string | null;
  foh?: number | null;
  transport?: number | null;
  commission?: number | null;
  fohPerKg?: number | null;
  transportPerKg?: number | null;
  commissionPerKg?: number | null;
}

export interface V3FinishedGood {
  id?: number;
  itemId: number;
  code?: string;
  description?: string;
  expectedQty: number;
  hasPrinting?: boolean;
  bomLines: V3BomLine[];
  costs?: V3Costs | null;
}
```

Then simplify the casts in `useCostingDraftState.ts` to use these named types directly.

- [ ] **Step 3: tsc check**

```bash
cd bom-mobile && npx tsc --noEmit; cd ..
```
Expected: no new errors from the hook itself.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/src/features/accountant/state/useCostingDraftState.ts bom-mobile/src/types/v3.ts
git commit -m "feat(mobile-d2): useCostingDraftState hook + widened V3Costs type"
```

---

### Task 18: RmCostRow component

**Files:**
- Create: `bom-mobile/src/features/accountant/drawer/RmCostRow.tsx`

- [ ] **Step 1: Create row component (BOM line read-only + cost+currency editable)**

```typescript
import { Text, TextInput, View } from "react-native";
import { SearchablePicker } from "@/components/SearchablePicker";
import type { V3BomLine } from "@/types/v3";
import type { RawMaterialCostState } from "../state/fgReadiness";

const CURRENCIES = ["AED", "USD", "EUR", "GBP", "PKR", "INR", "CNY"];

interface Props {
  bom: V3BomLine;
  micron?: number | null;
  cost: RawMaterialCostState;
  onChange: (partial: Partial<RawMaterialCostState>) => void;
}

export function RmCostRow({ bom, micron, cost, onChange }: Props) {
  const costInvalid = cost.costPerKg !== "" && (parseFloat(cost.costPerKg) <= 0);

  return (
    <View style={{
      paddingVertical: 10, paddingHorizontal: 12,
      borderBottomWidth: 1, borderBottomColor: "#f1f5f9",
    }}>
      {/* Read-only row */}
      <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center" }}>
        <View style={{ flex: 1 }}>
          <Text style={{ fontSize: 14, fontWeight: "600", color: "#0f172a" }} numberOfLines={1}>
            {bom.rawMaterialDescription ?? "(unknown RM)"}
          </Text>
          <Text style={{ fontSize: 12, color: "#64748b", marginTop: 2 }}>
            Qty/KG: {bom.qtyPerKg.toFixed(2)} · Micron: {micron ?? "—"}
          </Text>
        </View>
      </View>

      {/* Editable row */}
      <View style={{ flexDirection: "row", alignItems: "center", marginTop: 8, gap: 8 }}>
        <View style={{ flex: 1 }}>
          <Text style={{ fontSize: 11, color: "#64748b", marginBottom: 4 }}>Cost/KG</Text>
          <TextInput
            value={cost.costPerKg}
            onChangeText={(v) => onChange({ costPerKg: v })}
            keyboardType="decimal-pad"
            placeholder="0.00"
            style={{
              borderWidth: 1,
              borderColor: costInvalid ? "#ef4444" : "#cbd5e1",
              borderRadius: 8, paddingHorizontal: 10, paddingVertical: 8,
              fontSize: 15, color: "#0f172a", backgroundColor: "#ffffff",
            }}
          />
          {costInvalid ? (
            <Text style={{ fontSize: 11, color: "#ef4444", marginTop: 4 }}>
              Must be greater than 0
            </Text>
          ) : null}
        </View>

        <View style={{ width: 110 }}>
          <Text style={{ fontSize: 11, color: "#64748b", marginBottom: 4 }}>Currency</Text>
          <SearchablePicker
            value={cost.currencyCode || null}
            options={CURRENCIES.map((c) => ({ value: c, label: c }))}
            onChange={(v) => onChange({ currencyCode: String(v ?? "") })}
            placeholder="—"
          />
        </View>
      </View>
    </View>
  );
}
```

> Verify `SearchablePicker` API matches by reading `bom-mobile/src/components/SearchablePicker.tsx`. If its props don't match the above (e.g. options shape differs), adjust the prop names — do not edit the component.

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/accountant/drawer/RmCostRow.tsx
git commit -m "feat(mobile-d2): RmCostRow (BOM line cost + currency editor)"
```

---

### Task 19: PrintingCostSection component

**Files:**
- Create: `bom-mobile/src/features/accountant/drawer/PrintingCostSection.tsx`

- [ ] **Step 1: Create section**

```typescript
import { Text, TextInput, View } from "react-native";
import { SearchablePicker } from "@/components/SearchablePicker";

const CURRENCIES = ["AED", "USD", "EUR", "GBP", "PKR", "INR", "CNY"];

interface Props {
  costPerKg: string;
  currency: string;
  onChange: (partial: { costPerKg?: string; currency?: string }) => void;
}

export function PrintingCostSection({ costPerKg, currency, onChange }: Props) {
  const invalid = costPerKg !== "" && parseFloat(costPerKg) <= 0;
  return (
    <View style={{ padding: 12, backgroundColor: "#fef9c3", marginVertical: 8, borderRadius: 10 }}>
      <Text style={{ fontSize: 13, fontWeight: "700", color: "#854d0e", marginBottom: 8 }}>
        Printing cost (this FG has printing)
      </Text>
      <View style={{ flexDirection: "row", gap: 8 }}>
        <View style={{ flex: 1 }}>
          <Text style={{ fontSize: 11, color: "#854d0e", marginBottom: 4 }}>Cost/KG</Text>
          <TextInput
            value={costPerKg}
            onChangeText={(v) => onChange({ costPerKg: v })}
            keyboardType="decimal-pad"
            placeholder="0.00"
            style={{
              borderWidth: 1, borderColor: invalid ? "#ef4444" : "#fde68a",
              borderRadius: 8, paddingHorizontal: 10, paddingVertical: 8,
              fontSize: 15, color: "#0f172a", backgroundColor: "#ffffff",
            }}
          />
          {invalid ? (
            <Text style={{ fontSize: 11, color: "#ef4444", marginTop: 4 }}>Must be &gt; 0</Text>
          ) : null}
        </View>
        <View style={{ width: 110 }}>
          <Text style={{ fontSize: 11, color: "#854d0e", marginBottom: 4 }}>Currency</Text>
          <SearchablePicker
            value={currency || null}
            options={CURRENCIES.map((c) => ({ value: c, label: c }))}
            onChange={(v) => onChange({ currency: String(v ?? "") })}
            placeholder="—"
          />
        </View>
      </View>
    </View>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/accountant/drawer/PrintingCostSection.tsx
git commit -m "feat(mobile-d2): PrintingCostSection (visible iff hasPrinting)"
```

---

### Task 20: OtherCostsSection component (FOH + Transport + Commission, AED-only)

**Files:**
- Create: `bom-mobile/src/features/accountant/drawer/OtherCostsSection.tsx`

- [ ] **Step 1: Create section**

```typescript
import { Text, TextInput, View } from "react-native";

interface Props {
  fohPerKg: string;
  transportPerKg: string;
  commissionPerKg: string;
  onChange: (partial: { fohPerKg?: string; transportPerKg?: string; commissionPerKg?: string }) => void;
}

function NumField({ label, value, onChange, suffix = "AED/KG" }: {
  label: string; value: string; onChange: (v: string) => void; suffix?: string;
}) {
  return (
    <View style={{ flex: 1 }}>
      <Text style={{ fontSize: 11, color: "#64748b", marginBottom: 4 }}>
        {label} <Text style={{ color: "#94a3b8" }}>({suffix})</Text>
      </Text>
      <TextInput
        value={value}
        onChangeText={onChange}
        keyboardType="decimal-pad"
        placeholder="0.00"
        style={{
          borderWidth: 1, borderColor: "#cbd5e1",
          borderRadius: 8, paddingHorizontal: 10, paddingVertical: 8,
          fontSize: 15, color: "#0f172a", backgroundColor: "#ffffff",
        }}
      />
    </View>
  );
}

export function OtherCostsSection({ fohPerKg, transportPerKg, commissionPerKg, onChange }: Props) {
  return (
    <View style={{ padding: 12, marginTop: 8 }}>
      <Text style={{ fontSize: 13, fontWeight: "700", color: "#0f172a", marginBottom: 8 }}>
        Other costs
      </Text>
      <View style={{ flexDirection: "row", gap: 8 }}>
        <NumField label="FOH" value={fohPerKg} onChange={(v) => onChange({ fohPerKg: v })} />
        <NumField label="Transport" value={transportPerKg} onChange={(v) => onChange({ transportPerKg: v })} />
        <NumField label="Commission" value={commissionPerKg} onChange={(v) => onChange({ commissionPerKg: v })} />
      </View>
    </View>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/accountant/drawer/OtherCostsSection.tsx
git commit -m "feat(mobile-d2): OtherCostsSection (FOH+Transport+Commission AED-only)"
```

---

### Task 21: DrawerFooter component (Cancel + Save & Close)

**Files:**
- Create: `bom-mobile/src/features/accountant/drawer/DrawerFooter.tsx`

- [ ] **Step 1: Create footer**

```typescript
import { Pressable, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";

interface Props {
  onCancel: () => void;
  onSave: () => void;
  saving: boolean;
}

export function DrawerFooter({ onCancel, onSave, saving }: Props) {
  const insets = useSafeAreaInsets();
  return (
    <View style={{
      flexDirection: "row", gap: 8,
      padding: 12, paddingBottom: Math.max(insets.bottom, 12),
      borderTopWidth: 1, borderTopColor: "#e2e8f0",
      backgroundColor: "#ffffff",
    }}>
      <Pressable
        onPress={onCancel}
        style={({ pressed }) => ({
          flex: 1,
          paddingVertical: 12, borderRadius: 10,
          borderWidth: 1, borderColor: "#cbd5e1",
          opacity: pressed ? 0.7 : 1,
          alignItems: "center",
        })}
      >
        <Text style={{ fontSize: 15, color: "#475569", fontWeight: "600" }}>Cancel</Text>
      </Pressable>
      <Pressable
        onPress={onSave}
        disabled={saving}
        style={({ pressed }) => ({
          flex: 2,
          paddingVertical: 12, borderRadius: 10,
          backgroundColor: saving ? "#93c5fd" : "#1e40af",
          opacity: pressed && !saving ? 0.85 : 1,
          alignItems: "center",
        })}
      >
        <Text style={{ fontSize: 15, color: "#ffffff", fontWeight: "700" }}>
          {saving ? "Saving…" : "Save & Close"}
        </Text>
      </Pressable>
    </View>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/accountant/drawer/DrawerFooter.tsx
git commit -m "feat(mobile-d2): DrawerFooter (Cancel + Save & Close)"
```

---

### Task 22: CostInputDrawer (compose all pieces)

**Files:**
- Create: `bom-mobile/src/features/accountant/drawer/CostInputDrawer.tsx`

- [ ] **Step 1: Create drawer**

```typescript
import { useEffect, useState } from "react";
import { Alert, KeyboardAvoidingView, Modal, Platform, Pressable, ScrollView, Text, View } from "react-native";
import * as Haptics from "expo-haptics";
import type { V3Requisition } from "@/types/v3";
import type { FgDraftState, RawMaterialCostState } from "../state/fgReadiness";
import { fgReadiness } from "../state/fgReadiness";
import { RmCostRow } from "./RmCostRow";
import { PrintingCostSection } from "./PrintingCostSection";
import { OtherCostsSection } from "./OtherCostsSection";
import { DrawerFooter } from "./DrawerFooter";

interface Props {
  visible: boolean;
  fgIdx: number; // index into req.finishedGoods
  req: V3Requisition;
  draft: FgDraftState;
  baseline: FgDraftState;
  saving: boolean;
  onClose: () => void;
  onSave: (next: FgDraftState) => void;
  onChangeRm: (rmIdx: number, partial: Partial<RawMaterialCostState>) => void;
  onChangeFg: (partial: Partial<FgDraftState>) => void;
  dirtyDiffCount: number;
}

export function CostInputDrawer({
  visible, fgIdx, req, draft, baseline, saving,
  onClose, onSave, onChangeRm, onChangeFg, dirtyDiffCount,
}: Props) {
  const fg = req.finishedGoods[fgIdx];
  const readiness = fgReadiness(draft);

  const handleClose = () => {
    if (dirtyDiffCount >= 3) {
      Alert.alert("Discard changes?", `You changed ${dirtyDiffCount} fields. Discard them?`, [
        { text: "Keep editing", style: "cancel" },
        { text: "Discard", style: "destructive", onPress: () => { Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium); onClose(); } },
      ]);
    } else {
      onClose();
    }
  };

  if (!fg) return null;

  return (
    <Modal visible={visible} animationType="slide" presentationStyle="pageSheet" onRequestClose={handleClose}>
      <KeyboardAvoidingView
        behavior={Platform.OS === "ios" ? "padding" : "height"}
        style={{ flex: 1, backgroundColor: "#f8fafc" }}
      >
        {/* Header */}
        <View style={{
          paddingTop: Platform.OS === "ios" ? 16 : 24,
          paddingHorizontal: 16, paddingBottom: 12,
          backgroundColor: "#ffffff",
          borderBottomWidth: 1, borderBottomColor: "#e2e8f0",
        }}>
          <View style={{ flexDirection: "row", alignItems: "center", justifyContent: "space-between" }}>
            <View style={{ flex: 1 }}>
              <Text style={{ fontSize: 11, color: "#64748b", letterSpacing: 0.5, fontWeight: "600" }}>
                COSTING · FG {fgIdx + 1} OF {req.finishedGoods.length}
              </Text>
              <Text style={{ fontSize: 18, fontWeight: "700", color: "#0f172a", marginTop: 2 }} numberOfLines={1}>
                {fg.description ?? `Item #${fg.itemId}`}
              </Text>
              <Text style={{ fontSize: 13, color: "#64748b", marginTop: 2 }}>
                {fg.code ?? ""} · {fg.expectedQty.toLocaleString()} KG
              </Text>
            </View>
            <Pressable
              onPress={handleClose}
              style={{ paddingHorizontal: 12, paddingVertical: 6 }}
            >
              <Text style={{ fontSize: 24, color: "#94a3b8" }}>×</Text>
            </Pressable>
          </View>
          <ReadinessChip readiness={readiness} />
        </View>

        <ScrollView style={{ flex: 1 }}>
          {/* RM rows */}
          <View style={{ backgroundColor: "#ffffff", marginTop: 12 }}>
            <Text style={{ paddingHorizontal: 12, paddingTop: 12, fontSize: 13, fontWeight: "700", color: "#0f172a" }}>
              Raw materials
            </Text>
            {(fg.bomLines ?? []).map((bl, blIdx) => {
              const cost = draft.rawMaterialCosts[blIdx];
              if (!cost) return null;
              return (
                <RmCostRow
                  key={bl.id ?? blIdx}
                  bom={bl}
                  micron={(bl as { micron?: number | null }).micron ?? null}
                  cost={cost}
                  onChange={(p) => onChangeRm(blIdx, p)}
                />
              );
            })}
          </View>

          {draft.hasPrinting ? (
            <PrintingCostSection
              costPerKg={draft.printingCostPerKg}
              currency={draft.printingCostCurrency}
              onChange={(p) => onChangeFg({
                printingCostPerKg: p.costPerKg ?? draft.printingCostPerKg,
                printingCostCurrency: p.currency ?? draft.printingCostCurrency,
              })}
            />
          ) : null}

          <OtherCostsSection
            fohPerKg={draft.fohPerKg}
            transportPerKg={draft.transportPerKg}
            commissionPerKg={draft.commissionPerKg}
            onChange={(p) => onChangeFg(p)}
          />
        </ScrollView>

        <DrawerFooter onCancel={handleClose} onSave={() => onSave(draft)} saving={saving} />
      </KeyboardAvoidingView>
    </Modal>
  );
}

function ReadinessChip({ readiness }: { readiness: ReturnType<typeof fgReadiness> }) {
  const color = readiness === "ready" ? "#10b981" : readiness === "in_progress" ? "#f59e0b" : "#94a3b8";
  const label = readiness === "ready" ? "🟢 Ready" : readiness === "in_progress" ? "🟡 In progress" : "⚪ Not started";
  return (
    <View style={{ alignSelf: "flex-start", marginTop: 8, paddingHorizontal: 10, paddingVertical: 4, borderRadius: 999, backgroundColor: `${color}22` }}>
      <Text style={{ fontSize: 12, fontWeight: "700", color }}>{label}</Text>
    </View>
  );
}
```

- [ ] **Step 2: tsc check**

```bash
cd bom-mobile && npx tsc --noEmit; cd ..
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/features/accountant/drawer/CostInputDrawer.tsx
git commit -m "feat(mobile-d2): CostInputDrawer (compose RM rows + printing + other costs + footer)"
```

---

### Task 23: FgCostingCard component

**Files:**
- Create: `bom-mobile/src/features/accountant/detail/FgCostingCard.tsx`

- [ ] **Step 1: Create card**

```typescript
import { Pressable, Text, View } from "react-native";
import * as Haptics from "expo-haptics";
import type { V3FinishedGood } from "@/types/v3";
import type { FgReadiness } from "../state/fgReadiness";

interface Props {
  fgIdx: number;
  fg: V3FinishedGood;
  readiness: FgReadiness;
  onPress: () => void;
}

export function FgCostingCard({ fgIdx, fg, readiness, onPress }: Props) {
  const dot = readiness === "ready" ? "🟢" : readiness === "in_progress" ? "🟡" : "⚪";
  const ringColor = readiness === "ready" ? "#10b981" : readiness === "in_progress" ? "#f59e0b" : "#cbd5e1";

  return (
    <Pressable
      onPress={() => { Haptics.selectionAsync(); onPress(); }}
      style={({ pressed }) => ({
        marginHorizontal: 12, marginVertical: 6,
        padding: 14, borderRadius: 12,
        backgroundColor: "#ffffff",
        borderWidth: 1.5, borderColor: ringColor,
        opacity: pressed ? 0.85 : 1,
      })}
    >
      <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center" }}>
        <Text style={{ fontSize: 12, color: "#64748b", fontWeight: "700", letterSpacing: 0.5 }}>
          FG {fgIdx + 1}
        </Text>
        <Text style={{ fontSize: 13, fontWeight: "700" }}>{dot}</Text>
      </View>
      <Text style={{ fontSize: 15, fontWeight: "600", color: "#0f172a", marginTop: 4 }} numberOfLines={2}>
        {fg.description ?? `Item #${fg.itemId}`}
      </Text>
      <Text style={{ fontSize: 13, color: "#64748b", marginTop: 4 }}>
        {fg.expectedQty.toLocaleString()} KG · {(fg.bomLines ?? []).length} BOM line{(fg.bomLines ?? []).length === 1 ? "" : "s"}
      </Text>
      <Text style={{ marginTop: 8, color: "#1e40af", fontSize: 13, fontWeight: "600" }}>
        Edit costs ▸
      </Text>
    </Pressable>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/accountant/detail/FgCostingCard.tsx
git commit -m "feat(mobile-d2): FgCostingCard (readiness-pill card on active path)"
```

---

### Task 24: SubmitAllFooter component

**Files:**
- Create: `bom-mobile/src/features/accountant/detail/SubmitAllFooter.tsx`

- [ ] **Step 1: Create footer**

```typescript
import { Pressable, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import * as Haptics from "expo-haptics";

interface Props {
  readyCount: number;
  totalCount: number;
  submitting: boolean;
  onSubmit: () => void;
}

export function SubmitAllFooter({ readyCount, totalCount, submitting, onSubmit }: Props) {
  const insets = useSafeAreaInsets();
  const enabled = !submitting && readyCount === totalCount && totalCount > 0;
  return (
    <View style={{
      borderTopWidth: 1, borderTopColor: "#e2e8f0",
      backgroundColor: "#ffffff",
      paddingHorizontal: 12, paddingTop: 10,
      paddingBottom: Math.max(insets.bottom, 12),
    }}>
      <Pressable
        onPress={() => { if (enabled) { Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium); onSubmit(); } }}
        disabled={!enabled}
        style={({ pressed }) => ({
          paddingVertical: 14, borderRadius: 12,
          backgroundColor: enabled ? "#1e40af" : "#cbd5e1",
          opacity: pressed && enabled ? 0.85 : 1,
          alignItems: "center",
        })}
      >
        <Text style={{ fontSize: 16, color: "#ffffff", fontWeight: "700" }}>
          {submitting ? "Submitting…" : enabled ? "Submit to MD" : `${readyCount} of ${totalCount} FGs ready`}
        </Text>
      </Pressable>
    </View>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/accountant/detail/SubmitAllFooter.tsx
git commit -m "feat(mobile-d2): SubmitAllFooter (sticky bottom CTA)"
```

---

### Task 25: ActiveCostingView (the big composer)

**Files:**
- Create: `bom-mobile/src/features/accountant/detail/ActiveCostingView.tsx`

- [ ] **Step 1: Create the active view**

```typescript
import { useState } from "react";
import { ScrollView, Text, View } from "react-native";
import { toast } from "sonner-native"; // verify which toast library mobile uses; otherwise import Toast helper
import type { V3Requisition } from "@/types/v3";
import { useSaveV3CostData, useSubmitV3Costing } from "@/api/costing";
import { useCostingDraftState } from "../state/useCostingDraftState";
import type { FgDraftState } from "../state/fgReadiness";
import { CustomerSwapSheet } from "@/components/CustomerSwapSheet";
import { CustomerChangeHistorySheet } from "@/components/CustomerChangeHistorySheet";
import { useCustomerChangeHistory } from "@/api/requisitions";
import { Pressable } from "react-native";
import { FgCostingCard } from "./FgCostingCard";
import { SubmitAllFooter } from "./SubmitAllFooter";
import { CostInputDrawer } from "../drawer/CostInputDrawer";

interface Props {
  req: V3Requisition;
}

export function ActiveCostingView({ req }: Props) {
  const draftState = useCostingDraftState(req);
  const [openFgIdx, setOpenFgIdx] = useState<number | null>(null);
  const [drawerBaseline, setDrawerBaseline] = useState<FgDraftState | null>(null);
  const [swapOpen, setSwapOpen] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);

  const saveMut = useSaveV3CostData(req.id);
  const submitMut = useSubmitV3Costing(req.id);

  const historyQ = useCustomerChangeHistory(req.id, true);
  const historyCount = historyQ.data?.length ?? 0;

  const openFor = (idx: number) => {
    setDrawerBaseline({ ...draftState.drafts[idx] });
    setOpenFgIdx(idx);
  };

  const closeDrawer = () => {
    setOpenFgIdx(null);
    setDrawerBaseline(null);
  };

  const saveDrawer = async () => {
    try {
      await saveMut.mutateAsync({
        finishedGoods: draftState.drafts.map((d) => ({
          requisitionItemId: d.requisitionItemId,
          rawMaterialCosts: d.rawMaterialCosts.map((rc) => ({
            bomLineId: rc.bomLineId,
            costPerKg: parseFloat(rc.costPerKg) || 0,
            currencyCode: rc.currencyCode || "AED",
          })),
          printingCostPerKg: d.hasPrinting ? (parseFloat(d.printingCostPerKg) || 0) : null,
          printingCostCurrency: d.hasPrinting ? (d.printingCostCurrency || "AED") : null,
          fohPerKg: parseFloat(d.fohPerKg) || 0,
          transportPerKg: parseFloat(d.transportPerKg) || 0,
          commissionPerKg: parseFloat(d.commissionPerKg) || 0,
        })),
      });
      closeDrawer();
    } catch (err) {
      const e = err as { response?: { data?: { error?: string } } };
      // toast.error if available, otherwise fall back to console.warn
      console.warn(e?.response?.data?.error ?? "Save failed");
    }
  };

  const submitAll = async () => {
    try {
      // Final save first (matches web pattern)
      await saveMut.mutateAsync({
        finishedGoods: draftState.drafts.map((d) => ({
          requisitionItemId: d.requisitionItemId,
          rawMaterialCosts: d.rawMaterialCosts.map((rc) => ({
            bomLineId: rc.bomLineId,
            costPerKg: parseFloat(rc.costPerKg) || 0,
            currencyCode: rc.currencyCode || "AED",
          })),
          printingCostPerKg: d.hasPrinting ? (parseFloat(d.printingCostPerKg) || 0) : null,
          printingCostCurrency: d.hasPrinting ? (d.printingCostCurrency || "AED") : null,
          fohPerKg: parseFloat(d.fohPerKg) || 0,
          transportPerKg: parseFloat(d.transportPerKg) || 0,
          commissionPerKg: parseFloat(d.commissionPerKg) || 0,
        })),
      });
      await submitMut.mutateAsync();
    } catch (err) {
      const e = err as { response?: { data?: { error?: string; missingRequisitionItemIds?: number[] } } };
      console.warn(e?.response?.data?.error ?? "Submit failed");
    }
  };

  const dirtyDiff = openFgIdx != null && drawerBaseline != null
    ? draftState.isDirtyVsBaseline(openFgIdx, drawerBaseline) : 0;

  return (
    <View style={{ flex: 1 }}>
      <ScrollView contentContainerStyle={{ paddingTop: 8, paddingBottom: 8 }}>
        {/* Customer card with swap action */}
        <View style={{ paddingHorizontal: 12, marginBottom: 8 }}>
          <Text style={{ fontSize: 13, color: "#64748b", fontWeight: "600", letterSpacing: 0.5 }}>CUSTOMER</Text>
          <Text style={{ fontSize: 16, fontWeight: "700", color: "#0f172a", marginTop: 4 }}>{req.customer.name}</Text>
          <View style={{ flexDirection: "row", gap: 8, marginTop: 8 }}>
            <Pressable
              onPress={() => setSwapOpen(true)}
              style={{
                paddingHorizontal: 12, paddingVertical: 8,
                borderRadius: 8, borderWidth: 1, borderColor: "#1e40af",
                backgroundColor: "#eff6ff",
              }}
            >
              <Text style={{ color: "#1e40af", fontWeight: "600", fontSize: 13 }}>Change customer</Text>
            </Pressable>
            {historyCount > 0 ? (
              <Pressable
                onPress={() => setHistoryOpen(true)}
                style={{
                  paddingHorizontal: 10, paddingVertical: 8,
                  borderRadius: 999, backgroundColor: "#fef3c7",
                }}
              >
                <Text style={{ color: "#92400e", fontSize: 12, fontWeight: "600" }}>
                  Customer changed ({historyCount})
                </Text>
              </Pressable>
            ) : null}
          </View>
        </View>

        {/* FG cards */}
        {req.finishedGoods.map((fg, idx) => (
          <FgCostingCard
            key={fg.id ?? fg.itemId}
            fgIdx={idx}
            fg={fg}
            readiness={draftState.readiness[idx]}
            onPress={() => openFor(idx)}
          />
        ))}
      </ScrollView>

      <SubmitAllFooter
        readyCount={draftState.readiness.filter((r) => r === "ready").length}
        totalCount={draftState.readiness.length}
        submitting={submitMut.isPending || saveMut.isPending}
        onSubmit={submitAll}
      />

      {openFgIdx != null && drawerBaseline != null ? (
        <CostInputDrawer
          visible={true}
          fgIdx={openFgIdx}
          req={req}
          draft={draftState.drafts[openFgIdx]}
          baseline={drawerBaseline}
          saving={saveMut.isPending}
          dirtyDiffCount={dirtyDiff}
          onClose={closeDrawer}
          onSave={saveDrawer}
          onChangeRm={(rmIdx, partial) => draftState.setRmCost(openFgIdx, rmIdx, partial)}
          onChangeFg={(partial) => draftState.setFg(openFgIdx, partial)}
        />
      ) : null}

      <CustomerSwapSheet
        requisitionId={req.id}
        currentCustomerId={req.customer.id}
        currentCustomerName={req.customer.name}
        open={swapOpen}
        onClose={() => setSwapOpen(false)}
      />
      <CustomerChangeHistorySheet
        requisitionId={req.id}
        open={historyOpen}
        onClose={() => setHistoryOpen(false)}
      />
    </View>
  );
}
```

> Verify:
> - `sonner-native` import — if mobile doesn't use it, remove the import and rely on `console.warn` for now (smoke will surface errors anyway). Mobile can add toast in a follow-up.
> - `useCustomerChangeHistory` — hook should exist in `bom-mobile/src/api/requisitions.ts` from D-1. If not, port from V2.3 by copying its query function.

- [ ] **Step 2: tsc check**

```bash
cd bom-mobile && npx tsc --noEmit; cd ..
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/features/accountant/detail/ActiveCostingView.tsx
git commit -m "feat(mobile-d2): ActiveCostingView (FG list + drawer + Submit-all + customer swap)"
```

---

### Task 26: AccountantDetailScreen (status branch)

**Files:**
- Create: `bom-mobile/src/features/accountant/detail/AccountantDetailScreen.tsx`

- [ ] **Step 1: Create the wrapper**

```typescript
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { Pressable, Text, View } from "react-native";
import * as Haptics from "expo-haptics";
import { useRequisition } from "@/api/requisitions";
import { useAuth } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";
import { ErrorBanner } from "@/components/ErrorBanner";
import { ScreenHeader } from "@/components/ScreenHeader";
import { NotificationBell } from "@/components/NotificationBell";
import { ActiveCostingView } from "./ActiveCostingView";
import { ReadonlyDetailView } from "./ReadonlyDetailView";

export function AccountantDetailScreen() {
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

  if (reqQ.isPending) return <LoadingView />;
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

  // Legacy V2.3 status fallback (D-1 parity)
  const isV3 = ["Draft","Costing","MdPricing","CustomerConfirm","MdFinalSign","Signed","Rejected","Cancelled"].includes(req.status);
  if (!isV3) {
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
      {req.status === "Costing" ? (
        <ActiveCostingView req={req} />
      ) : (
        <ReadonlyDetailView req={req} />
      )}
    </View>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-mobile/src/features/accountant/detail/AccountantDetailScreen.tsx
git commit -m "feat(mobile-d2): AccountantDetailScreen (status branch)"
```

---

## Phase 7 — Wire routes + V2.3 purge (spec D-2.7 first half)

### Task 27: Wire route files to new screens

**Files:**
- Modify (overwrite): `bom-mobile/app/(accountant)/index.tsx`
- Create: `bom-mobile/app/(accountant)/list.tsx` (overwrite if V2.3 exists)
- Modify (overwrite): `bom-mobile/app/(accountant)/[id].tsx`

- [ ] **Step 1: Wire dashboard route**

Replace `bom-mobile/app/(accountant)/index.tsx` content with:

```typescript
import { AccountantDashboard } from "@/features/accountant/dashboard/AccountantDashboard";

export default function AccountantIndex() {
  return <AccountantDashboard />;
}
```

- [ ] **Step 2: Wire list route**

Replace `bom-mobile/app/(accountant)/list.tsx`:

```typescript
import { AccountantListScreen } from "@/features/accountant/list/AccountantListScreen";

export default function AccountantList() {
  return <AccountantListScreen />;
}
```

- [ ] **Step 3: Wire detail route**

Replace `bom-mobile/app/(accountant)/[id].tsx`:

```typescript
import { AccountantDetailScreen } from "@/features/accountant/detail/AccountantDetailScreen";

export default function AccountantDetail() {
  return <AccountantDetailScreen />;
}
```

- [ ] **Step 4: tsc check**

```bash
cd bom-mobile && npx tsc --noEmit; cd ..
```
Expected: most V2.3 errors should now be related ONLY to `bom-mobile/app/(accountant)/item/` folder + lingering V2.3 component imports. Note error count.

- [ ] **Step 5: Commit**

```bash
git add bom-mobile/app/(accountant)
git commit -m "feat(mobile-d2): wire accountant routes to V3 screens"
```

---

### Task 28: Delete V2.3 accountant `item/` folder

**Files:**
- Delete: `bom-mobile/app/(accountant)/item/` (entire folder)

- [ ] **Step 1: Verify nothing in app routes navigates to `(accountant)/item/...` from V3 paths**

```bash
grep -rn "(accountant)/item" bom-mobile/src bom-mobile/app
```
Expected: only matches inside the `item/` folder itself or other V2.3 deletees.

- [ ] **Step 2: Delete the folder**

```bash
rm -rf bom-mobile/app/\(accountant\)/item
```

- [ ] **Step 3: tsc check**

```bash
cd bom-mobile && npx tsc --noEmit; cd ..
```
Expected: error count drops significantly.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/app/\(accountant\)
git commit -m "chore(mobile-d2): purge V2.3 accountant item/ folder"
```

---

### Task 29: Delete V2.3 components

**Files:**
- Delete: `bom-mobile/src/components/HistoricalRequisitionScreen.tsx`
- Delete: `bom-mobile/src/components/BranchSwapSheet.tsx`
- Delete: `bom-mobile/src/components/BranchChangeHistorySheet.tsx`
- Delete: `bom-mobile/src/components/RequisitionCard.tsx` (V2.3 — replaced by ReqCard)

- [ ] **Step 1: Verify no remaining importers**

```bash
grep -rn "HistoricalRequisitionScreen\|BranchSwapSheet\|BranchChangeHistorySheet\|RequisitionCard" bom-mobile/src bom-mobile/app
```
Expected: zero matches (or only matches inside the files themselves).

If matches exist outside the deletees, edit those files to remove imports + usage. Common case: `(sales)/[id].tsx` or other detail pages may have V2.3 RequisitionCard imports — replace with `ReqCard`.

- [ ] **Step 2: Delete files**

```bash
rm bom-mobile/src/components/HistoricalRequisitionScreen.tsx
rm bom-mobile/src/components/BranchSwapSheet.tsx
rm bom-mobile/src/components/BranchChangeHistorySheet.tsx
rm bom-mobile/src/components/RequisitionCard.tsx
```

- [ ] **Step 3: tsc check**

```bash
cd bom-mobile && npx tsc --noEmit; cd ..
```
Expected: 0 errors. If any remaining, they're V2.3 cross-phase residuals — fix or document.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/src/components
git commit -m "chore(mobile-d2): delete V2.3 components (HistoricalRequisitionScreen + Branch* + RequisitionCard)"
```

---

## Phase 8 — Smoke + ship (spec D-2.7 second half)

### Task 30: tsc clean baseline + lint

**Files:**
- None — verification only

- [ ] **Step 1: Final tsc check**

```bash
cd bom-mobile && npx tsc --noEmit; cd ..
```
Expected: 0 errors. If errors remain — fix or escalate.

- [ ] **Step 2: Optional lint (if mobile project has eslint configured)**

```bash
cd bom-mobile && npx eslint src app 2>&1 | tail -20; cd ..
```
Address any errors; warnings can be deferred.

- [ ] **Step 3: Commit fixes if any**

```bash
git status
# if any modifications:
git add bom-mobile
git commit -m "fix(mobile-d2): tsc/lint cleanup"
```

---

### Task 31: Manual emulator smoke checklist

**Files:**
- None — manual verification

- [ ] **Step 1: Start the API**

```bash
curl -s http://localhost:7300/swagger/index.html >/dev/null || dotnet run --project BomPriceApproval.API &
```
Wait until `/health` returns 200.

- [ ] **Step 2: Bridge the emulator's port to host**

```bash
adb reverse tcp:7300 tcp:7300
```

- [ ] **Step 3: Start expo + open emulator**

```bash
cd bom-mobile && npx expo start
```
Then press `a` to open Android emulator.

- [ ] **Step 4: Run smoke checklist (login as Sara — accountant)**

Walk through each:
- [ ] Dashboard renders all 4 KPI cards with non-empty counts
- [ ] Tap "Costing to complete" → list opens on Queue tab
- [ ] Tap In Flight tab → 3 sub-filter chips visible (All/MD/Customer)
- [ ] Tap MD chip → list filters to MdPricing+MdFinalSign reqs
- [ ] Tap Customer chip → list filters to CustomerConfirm reqs
- [ ] Tap Done tab → only Signed reqs
- [ ] Tap Closed tab → Rejected + Cancelled reqs only
- [ ] Open a Costing-status req → detail page enters active mode (FG cards visible)
- [ ] Tap FG card → drawer opens with header "COSTING · FG 1 OF N" + readiness chip
- [ ] Fill RM cost (2.5) + currency (AED) + FOH (1.0) + Transport (0.5) + Commission (0) — readiness chip flips to 🟢 ready
- [ ] Tap Save & Close → drawer closes, FG card border = green
- [ ] Repeat for all FGs → "Submit to MD" button enables
- [ ] Tap Submit to MD → req status flips to MdPricing → list updates
- [ ] Open a non-Costing req → ReadonlyDetailView with appropriate footer text
- [ ] Open a Signed req → final price card visible + Download PDF
- [ ] Customer-swap on Costing req works
- [ ] Branch-swap UI absent everywhere (no Change branch button on Costing detail)

- [ ] **Step 5: Document any issues**

If any checklist item fails, fix in a follow-up commit. If all pass, proceed to Task 32.

---

### Task 32: Push branch + open PR (with hold label)

**Files:**
- None — git op only

- [ ] **Step 1: Push branch**

```bash
git push -u origin feat/v3-mobile-phase-d-2-accountant
```

- [ ] **Step 2: Open PR — DO NOT AUTO-MERGE**

```bash
gh pr create --base master --head feat/v3-mobile-phase-d-2-accountant \
  --title "feat(mobile-d2): V3 Accountant rebuild" \
  --body "$(cat <<'EOF'
Implements [`docs/superpowers/specs/2026-05-01-v3-mobile-phase-d-2-accountant-design.md`](docs/superpowers/specs/2026-05-01-v3-mobile-phase-d-2-accountant-design.md).

## Locked decisions honored
D1-D12 from spec §4. Hybrid drawer pattern, save-on-close per drawer, strict client-side completion (⚪🟡🟢), 4-tab list, drop branch swap, keep customer swap, V3 status palette reuse.

## Backend prereqs
B1 (stats reshape) + B2 (submit verify) + B3 (customer-swap allowed-status) shipped via prior PR.

## Test plan
- [x] tsc clean
- [x] On-device emulator smoke checklist passed (Task 31)
- [ ] EAS OTA push to preview channel (next task post-merge)
- [ ] Verify on physical device after OTA

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)" \
  --label "hold"
```

The `hold` label prevents auto-merge until OTA verification on physical device passes (Task 33).

---

### Task 33: EAS OTA + physical-device verify

**Files:**
- None — release op

- [ ] **Step 1: Drift check pre-OTA**

```bash
git log mobile-shipped-vc1..HEAD --oneline -- bom-mobile/ | head
```
Expected: only D-1 + D-2 commits, no `app.config.ts` / `eas.json` / native dep changes. If anything native — abort OTA, switch to APK rebuild + tag bump (`mobile-shipped-vc2`).

- [ ] **Step 2: Push OTA**

```bash
cd bom-mobile && npx eas-cli update --branch preview --message "v3-mobile-d2-$(git rev-parse --short HEAD)"; cd ..
```

- [ ] **Step 3: Verify on physical device**

Open the existing APK on phone (the one installed from `mobile-shipped-vc1`). Pull-to-refresh in the app or restart it — Expo should detect the new bundle and apply. Then re-run a 5-step abbreviated smoke (login as Sara → dashboard → list-Queue tab → open Costing req → save 1 FG drawer + check pill flips to 🟢).

- [ ] **Step 4: Remove `hold` label and merge**

```bash
PR=$(gh pr list --head feat/v3-mobile-phase-d-2-accountant --json number -q '.[0].number')
gh pr edit $PR --remove-label "hold"
gh pr merge $PR --squash --delete-branch
```

- [ ] **Step 5: Sync master + verify**

```bash
git checkout master
git pull origin master
git log -1 --oneline
```

---

### Task 34: Memory + spec acceptance check

**Files:**
- Modify: `C:\Users\Administrator\.claude\projects\D--shan-projects-BOM-Price-Approval\memory\MEMORY.md`
- Create: `C:\Users\Administrator\.claude\projects\D--shan-projects-BOM-Price-Approval\memory\project_v3_mobile_d2_brainstorm.md`

- [ ] **Step 1: Walk through spec §11 acceptance criteria**

For each checkbox in [`docs/superpowers/specs/2026-05-01-v3-mobile-phase-d-2-accountant-design.md`](docs/superpowers/specs/2026-05-01-v3-mobile-phase-d-2-accountant-design.md) §11, verify it's done. Document any deferrals.

- [ ] **Step 2: Create memory entry**

Save `project_v3_mobile_d2_brainstorm.md`:

```markdown
---
name: V3 mobile Phase D-2 (Accountant) — SHIPPED YYYY-MM-DD
description: Master @ <SHA>; PR #<N> squash-merged; backend prereqs PR #<N>; EAS OTA pushed
type: project
---

# V3 Mobile Phase D-2 (Accountant)

**Status:** SHIPPED YYYY-MM-DD
**PRs:** #<backend-prereqs>, #<mobile>
**Master:** @ <SHA>
**OTA:** preview channel updated <YYYY-MM-DD>

## Decisions delivered
D1-D12 from spec §4. All shipped as designed.

## Acceptance criteria
[ ] / [x] for each item from spec §11.

## Deferrals
- (any deferred items)

## Open follow-ups
- D-3 (MD + signature pad) — separate brainstorm next
- (anything noted in implementation)

## Why notable
First mobile phase to complete the V3 accountant flow. Drawer pattern locked for D-3 reuse.
```

- [ ] **Step 3: Update MEMORY.md index**

Add new line to top of `MEMORY.md`:

```markdown
- [V3 mobile Phase D-2 — SHIPPED YYYY-MM-DD](project_v3_mobile_d2_brainstorm.md) — V3 accountant rebuild done; drawer pattern locked; D-3 (MD) next.
```

- [ ] **Step 4: Memory commit (no — memory is auto-managed; just save the files)**

Memory files are file-system writes; no git commit involved.

---

## Self-review (engineer reading this plan)

Spec → plan coverage:

| Spec section | Plan coverage |
|---|---|
| 4. Locked decisions D1-D12 | All threaded into tasks: D1/D8/D9 in 18-22; D2 in 22+25; D3 in 16-17+22-25; D4-D6 in 9-14; D7 in 26; D10 in 25; D11 (no UI work) ✓; D12 reuses D-1 palette ✓ |
| 5. State machine + status mapping | Tasks 14 (tabFilters) + 26 (legacy fallback) |
| 6.1 Dashboard | Tasks 9-11 |
| 6.2 List page | Tasks 12-14 |
| 6.3 Detail page | Tasks 15+25-26 |
| 6.4 Cost-input drawer | Tasks 16-22 |
| 6.5 Customer swap retention | Task 25 |
| 7.1 Purge list | Tasks 6-8 (API rewrites) + 27-29 (V2.3 file deletes) |
| 7.2 New routes | Task 27 |
| 7.3 New components | Tasks 9-26 |
| 7.4 API hook strategy | Tasks 6-7 |
| 7.5 Reused components | No new tasks (used in-place) |
| 8. Backend prereqs | Tasks 1-4 |
| 9. Deploy strategy | Task 33 |
| 10. Testing strategy | Tasks 30-31 |
| 11. Acceptance criteria | Task 34 step 1 |
| 12. Open questions / risks | R1 in Task 25 (error parsing); R2 in Task 22 (≥3-field confirm); R3 in Task 22 (KeyboardAvoidingView); R4 in Tasks 6-7 (cache invalidation); R5 in Task 1 (web grep before reshape); R6 (no work needed — backend handles) |
| 13. Implementation phasing | This whole plan |

Total: 34 tasks (0 + 1-34), ~80-150 commits estimated.

Frequency: 1 commit per task minimum, often 2-3.

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-05-01-v3-mobile-phase-d-2-accountant-implementation.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — Dispatch a fresh subagent per task, review between tasks, fast iteration. Best for a 34-task plan; avoids context exhaustion.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints. Good for short plans; this one is too long.

Recommendation: **option 1 — subagent-driven**, in a fresh session with `/model sonnet` (per CLAUDE.md model strategy).

**Which approach?**
