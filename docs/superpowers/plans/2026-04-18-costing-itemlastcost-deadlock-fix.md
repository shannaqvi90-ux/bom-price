# Costing `ItemLastCost` Deadlock Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the Postgres `40P01` deadlock that can occur when two concurrent `/api/costing/{reqId}/items/{riId}/submit` calls on different requisitions share one or more raw-material items.

**Architecture:** Inside `CostingController.SubmitItem`, the `newCostLines` list is iterated in BOM-line order to upsert `ItemLastCost` rows. Two concurrent submitters touching the same raw-material rows from different BOMs can acquire row locks in opposite orders, forming a cyclic wait-for graph. The fix is to sort the upsert loop by `RawMaterialItemId` ascending so every caller acquires locks in the same global order.

**Tech Stack:** ASP.NET Core 8 (EF Core + Npgsql), xUnit + FluentAssertions + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-04-18-costing-itemlastcost-deadlock-fix.md`

---

## File Structure

- **Modify:** `BomPriceApproval.API/Features/Costing/CostingController.cs` (the `foreach` at ~line 294)
- **Modify:** `BomPriceApproval.Tests/Costing/CostingTests.cs` (add one new `[Fact]` in the existing "Concurrency tests" region around lines 478–586; the file's existing DTO records and login helpers will be reused)

No new files.

---

## Task 1 — Write the regression test (TDD red)

**Files:**
- Modify: `BomPriceApproval.Tests/Costing/CostingTests.cs` — add a new method inside the existing `public class CostingTests(...)` class, just after `SubmitItem_MultipleConcurrent_NoDuplicatedCosts` (ends at line 586) and before the `// ── DTOs ──` comment at line 588.

### Step 1.1 — Add the failing test

- [ ] Insert the following method into `BomPriceApproval.Tests/Costing/CostingTests.cs` immediately after line 586 (after the closing `}` of `SubmitItem_MultipleConcurrent_NoDuplicatedCosts`) and before the `// ── DTOs ──` comment.

```csharp
[Fact]
public async Task SubmitItem_ConcurrentDifferentReqsSharingRawMaterial_NoDeadlock()
{
    // Two independent requisitions whose BOMs both reference a shared raw material,
    // but in OPPOSITE list positions so the ItemLastCost upsert order would differ
    // between the two transactions without the fix. The fix sorts the upsert loop
    // by RawMaterialItemId, making lock acquisition globally consistent.

    // ── Arrange: create shared and per-req raw materials + FG items ────────────
    var spClient = factory.CreateClient();
    var spToken = await LoginAsync(spClient, "ali@test.com", "Test@1234");
    spClient.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", spToken);

    async Task<int> CreateItem(string prefix, string type)
    {
        var code = $"{prefix}-{Guid.NewGuid():N}".Substring(0, 10);
        var resp = await spClient.PostAsJsonAsync("/api/items",
            new { Code = code, Description = $"{prefix} {code}", Type = type, LastPurchasePrice = (decimal?)null });
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        var item = await resp.Content.ReadFromJsonAsync<ItemDto>();
        return item!.Id;
    }

    var fgA = await CreateItem("FG", "FinishedGood");
    var fgB = await CreateItem("FG", "FinishedGood");
    var rmShared = await CreateItem("RM", "RawMaterial");
    var rmA = await CreateItem("RM", "RawMaterial");
    var rmB = await CreateItem("RM", "RawMaterial");

    var customers = await spClient.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
    var customerId = customers!.First().Id;

    async Task<(int ReqId, int RiId)> CreateReq(int fgId)
    {
        var resp = await spClient.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = fgId, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        var created = await resp.Content.ReadFromJsonAsync<CreatedRequisition>();
        var detail = await spClient.GetFromJsonAsync<RequisitionDetailDto>($"/api/requisitions/{created!.Id}");
        return (created.Id, detail!.Items[0].Id);
    }

    var (reqA, riA) = await CreateReq(fgA);
    var (reqB, riB) = await CreateReq(fgB);

    // ── Admin seeds a Process for BOM lines ──
    var adminClient = factory.CreateClient();
    var adminToken = await LoginAsync(adminClient, "admin@test.com", "Admin@1234");
    adminClient.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
    var procCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
    var procResp = await adminClient.PostAsJsonAsync("/api/processes", new { Name = procCode, DisplayOrder = 99 });
    procResp.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
    var process = await procResp.Content.ReadFromJsonAsync<ProcessDto>();

    // ── BomCreator builds TWO-line BOMs, reversed order of the shared RM ──
    //    reqA lines: [rmA, rmShared]   reqB lines: [rmShared, rmB]
    var bomClient = factory.CreateClient();
    var bomToken = await LoginAsync(bomClient, "bob@test.com", "Test@1234");
    bomClient.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bomToken);

    async Task BuildAndSubmitBom(int reqId, int riId, int firstRm, int secondRm)
    {
        (await bomClient.PostAsync($"/api/bom/{reqId}/items/{riId}/start", null))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
        var saveResp = await bomClient.PutAsJsonAsync($"/api/bom/{reqId}/items/{riId}/lines", new
        {
            Lines = new[]
            {
                new { ProcessId = process!.Id, RawMaterialItemId = firstRm,  QtyPerKg = 0.50m, WastagePct = 1.0m },
                new { ProcessId = process!.Id, RawMaterialItemId = secondRm, QtyPerKg = 0.50m, WastagePct = 1.0m }
            }
        });
        saveResp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
        (await bomClient.PostAsync($"/api/bom/{reqId}/submit", null))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
    }

    await BuildAndSubmitBom(reqA, riA, rmA, rmShared);
    await BuildAndSubmitBom(reqB, riB, rmShared, rmB);

    // ── Accountant starts costing on both, then submits concurrently ──
    async Task<(int RiId, List<CostingBomLineDto> Lines)> StartAndLoad(HttpClient c, int reqId, int riId)
    {
        (await c.PostAsync($"/api/costing/{reqId}/items/{riId}/start", null))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
        var review = await c.GetFromJsonAsync<CostingReviewDto>($"/api/costing/{reqId}");
        return (riId, review!.Items.First(x => x.RequisitionItemId == riId).BomLines);
    }

    var startClient = factory.CreateClient();
    var startToken = await LoginAsync(startClient, "sara@test.com", "Test@1234");
    startClient.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", startToken);

    var (_, linesA) = await StartAndLoad(startClient, reqA, riA);
    var (_, linesB) = await StartAndLoad(startClient, reqB, riB);

    object SubmitBody(List<CostingBomLineDto> lines) => new
    {
        RawMaterialCosts = lines
            .Select(l => new { BomLineId = l.BomLineId, CostPerKg = 3.0m, CurrencyCode = "AED" })
            .ToArray(),
        LandedCostType = "Percentage",
        LandedCostValue = 0m,
        FohAmount = 0m
    };

    // Fire the two submits concurrently. The FOR UPDATE lock on QuotationRequest
    // does NOT serialize them because they target different req rows, so both
    // transactions enter the ItemLastCost upsert loop at nearly the same time.
    // Pre-fix: high probability of a 40P01 deadlock on the shared rmShared row.
    // Post-fix: both transactions lock ItemLastCost rows in ascending RawMaterialItemId
    // order, so there is no cyclic wait and both complete.

    async Task<HttpResponseMessage> DoSubmit(int reqId, int riId, List<CostingBomLineDto> lines)
    {
        var c = factory.CreateClient();
        var t = await LoginAsync(c, "sara@test.com", "Test@1234");
        c.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", t);
        return await c.PostAsJsonAsync($"/api/costing/{reqId}/items/{riId}/submit", SubmitBody(lines));
    }

    var taskA = Task.Run(() => DoSubmit(reqA, riA, linesA));
    var taskB = Task.Run(() => DoSubmit(reqB, riB, linesB));
    var results = await Task.WhenAll(taskA, taskB);

    // ── Assert: both submits succeed, neither deadlocked into 500 ──
    foreach (var r in results)
        r.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

    // Both requisitions should be MdReview (single-item reqs, so one submit each = complete).
    var verifyClient = factory.CreateClient();
    var verifyToken = await LoginAsync(verifyClient, "ali@test.com", "Test@1234");
    verifyClient.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", verifyToken);
    (await verifyClient.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{reqA}"))!
        .Status.Should().Be("MdReview");
    (await verifyClient.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{reqB}"))!
        .Status.Should().Be("MdReview");
}
```

### Step 1.2 — Build the solution to confirm the test compiles

- [ ] Run:

```bash
dotnet build BomPriceApproval.Tests/BomPriceApproval.Tests.csproj
```

Expected: `Build succeeded` with 0 errors. If the build fails on an unresolved type (`ItemDto`, `CustomerDto`, `CreatedRequisition`, `RequisitionDetailDto`, `ProcessDto`, `CostingReviewDto`, `CostingBomLineDto`, `RequisitionDto`) confirm the method was pasted **inside** the `CostingTests` class, above the `// ── DTOs ──` block where those records live (line 588).

### Step 1.3 — Run the new test to verify it fails on the unfixed code

- [ ] Run:

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~SubmitItem_ConcurrentDifferentReqsSharingRawMaterial_NoDeadlock"
```

Expected: **FAIL** on the first run in most cases. Typical failure shapes (any of these confirms the bug):
- `Expected response.StatusCode to be NoContent, but found InternalServerError` with server log mentioning `40P01 deadlock detected` or `Microsoft.EntityFrameworkCore.DbUpdateException`.
- `System.Net.Http.HttpRequestException: ... 500 Internal Server Error`.

If the test PASSES on the first run, re-run up to 3 times — deadlocks are probabilistic. If it still passes, see "Flake fallback" below before concluding the test is ineffective.

**Flake fallback (only if test passes pre-fix after 3 runs):**
Replace the single `Task.WhenAll` block with a loop that rebuilds fresh BOM/costing state and re-fires the race, e.g.:

```csharp
var failures = 0;
for (var attempt = 0; attempt < 5; attempt++)
{
    // ... recreate 2 fresh reqs + BOMs with the shared RM (extract the arrange block
    //     into a local function first) and fire the race ...
    if (results.Any(r => r.StatusCode != System.Net.HttpStatusCode.NoContent)) failures++;
}
failures.Should().Be(0);
```

Do **not** add this loop unless the simple version fails to reproduce — extra iterations slow the test. The flake fallback is only a safety net.

### Step 1.4 — Do NOT commit yet

- [ ] Leave the failing test uncommitted. We commit the test and the fix together in Task 2 so the repo history has no known-broken intermediate state on `main`.

---

## Task 2 — Apply the fix, verify green, commit

**Files:**
- Modify: `BomPriceApproval.API/Features/Costing/CostingController.cs` — the `foreach (var costLine in newCostLines)` at line 294.

### Step 2.1 — Apply the one-line change

- [ ] In `BomPriceApproval.API/Features/Costing/CostingController.cs`, replace the `foreach` header on line 294:

**Before:**

```csharp
        foreach (var costLine in newCostLines)
        {
            var bomLine = bom.Lines.First(bl => bl.Id == costLine.BomLineId);
```

**After:**

```csharp
        foreach (var costLine in newCostLines
            .OrderBy(cl => bom.Lines.First(bl => bl.Id == cl.BomLineId).RawMaterialItemId))
        {
            var bomLine = bom.Lines.First(bl => bl.Id == costLine.BomLineId);
```

No other lines change.

### Step 2.2 — Build

- [ ] Run:

```bash
dotnet build
```

Expected: `Build succeeded` with 0 errors, 0 warnings introduced by this change.

### Step 2.3 — Run the new test and confirm it passes

- [ ] Run:

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~SubmitItem_ConcurrentDifferentReqsSharingRawMaterial_NoDeadlock"
```

Expected: **PASS**. Run it a second time to rule out luck — should still pass.

### Step 2.4 — Run the full Costing test suite to confirm no regression

- [ ] Run:

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~Costing"
```

Expected: **All tests pass**, including `SubmitItem_ConcurrentLastTwoItems_BothTriggerMdReview` and `SubmitItem_MultipleConcurrent_NoDuplicatedCosts`.

### Step 2.5 — Run the entire test suite

- [ ] Run:

```bash
dotnet test
```

Expected: **All tests pass.** If any unrelated test fails on your machine (e.g., a port binding issue), confirm it was failing before this change too — this fix only touches `SubmitItem`, so unrelated failures are pre-existing.

### Step 2.6 — Commit

- [ ] Run:

```bash
git add BomPriceApproval.API/Features/Costing/CostingController.cs BomPriceApproval.Tests/Costing/CostingTests.cs
git commit -m "$(cat <<'EOF'
fix(costing): deterministic ItemLastCost lock order to prevent deadlock

Two concurrent costing submits on different requisitions sharing one or
more raw-material items could acquire ItemLastCost row locks in opposite
orders, producing a Postgres 40P01 deadlock that surfaced as HTTP 500.

Sort the upsert loop by RawMaterialItemId so every submitter locks rows
in the same global order. Semantics of the upsert are unchanged because
each item id is touched at most once per transaction.

Adds a parallel-submit regression test covering two reqs with a shared
raw material in reversed BOM positions.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Expected: commit created; `git status` clean.

---

## Self-Review Notes (pre-handoff)

- **Spec coverage:** The spec's Problem/Fix/Test sections are covered by Tasks 1 and 2. The spec's "Out of scope" items (dictionary refactor, retry middleware, FOR UPDATE changes) are intentionally omitted.
- **Placeholder scan:** No TBDs. All steps have either exact code blocks or exact commands with expected outputs.
- **Type consistency:** The test reuses the record types defined at lines 588–609 of the existing `CostingTests.cs` (`ItemDto`, `CustomerDto`, `ProcessDto`, `CreatedRequisition`, `RequisitionDetailDto`, `RequisitionDto`, `CostingReviewDto`, `CostingBomLineDto`). The one-line code change in `SubmitItem` does not alter any public type.
- **Known caveat:** The test's effectiveness at reproducing a pre-fix deadlock is probabilistic. Step 1.3 includes an explicit flake fallback for the rare case that the simple version passes pre-fix.
