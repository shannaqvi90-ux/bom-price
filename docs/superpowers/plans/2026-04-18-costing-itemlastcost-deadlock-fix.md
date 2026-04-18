# Costing `ItemLastCost` Concurrency Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the `23505` unique-constraint-violation that occurs when two concurrent `/api/costing/{reqId}/items/{riId}/submit` calls on different requisitions share a raw-material item whose `ItemLastCost` row does not yet exist.

**Architecture:** `CostingController.SubmitItem` currently upserts `ItemLastCost` via a read-then-insert-if-absent pattern (`existingLastCosts` dict + `db.Add`). This is a TOCTOU race against the unique index on `"ItemId"`. The fix replaces it with an atomic Postgres `INSERT … ON CONFLICT ("ItemId") DO UPDATE` executed inside the existing ambient transaction via EF Core's `ExecuteSqlInterpolatedAsync`.

**Tech Stack:** ASP.NET Core 8, EF Core 8 (Npgsql provider), xUnit + FluentAssertions + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-04-18-costing-itemlastcost-deadlock-fix.md` (revised 2026-04-18 after Task 1 TDD-red)

---

## Revision note

The initial plan proposed a `.OrderBy(RawMaterialItemId)` one-line fix targeting a hypothetical `40P01` deadlock. Task 1's red-phase test reliably reproduced SQLSTATE `23505` (unique_violation) instead — a TOCTOU race on first-INSERT rather than a row-lock cycle. The spec and Task 2 below are revised accordingly. Task 1 is complete and the failing test is retained verbatim as the regression test for the revised fix.

---

## File Structure

- **Modify:** `BomPriceApproval.API/Features/Costing/CostingController.cs` (replace the dictionary read + upsert loop at lines ~288–316)
- **Modify:** `BomPriceApproval.Tests/Costing/CostingTests.cs` (test already added at lines 588–735 in Task 1)

No new files.

---

## Task 1 — Write the regression test (TDD red) — ✅ COMPLETE

**Status:** Done. Method `SubmitItem_ConcurrentDifferentReqsSharingRawMaterial_NoDeadlock` was inserted at `BomPriceApproval.Tests/Costing/CostingTests.cs:588`. Test fails with SQLSTATE `23505` on unfixed code (deterministic across two runs). Uncommitted.

**Deviation from Task 1 template:** the `BOM /items/{riId}/start` endpoint returns `200 OK` (not `204 NoContent`), so the status assertion on that call was dropped inside the `BuildAndSubmitBom` local — matches the pattern already in use elsewhere in the same test class.

---

## Task 2 — Apply the `ON CONFLICT` fix, verify, commit

**Files:**
- Modify: `BomPriceApproval.API/Features/Costing/CostingController.cs` — replace the block at lines ~288–316.

### Step 2.1 — Replace the dictionary read + upsert loop

- [ ] Open `BomPriceApproval.API/Features/Costing/CostingController.cs`. Locate this exact block (runs from roughly line 288 to line 316 — grep for `var rawItemIds` to anchor):

**Remove (delete these 29 lines):**

```csharp
        var rawItemIds = newCostLines
            .Select(l => bom.Lines.First(bl => bl.Id == l.BomLineId).RawMaterialItemId)
            .Distinct().ToList();
        var existingLastCosts = await db.ItemLastCosts
            .Where(l => rawItemIds.Contains(l.ItemId)).ToDictionaryAsync(l => l.ItemId);

        foreach (var costLine in newCostLines)
        {
            var bomLine = bom.Lines.First(bl => bl.Id == costLine.BomLineId);
            var itemId = bomLine.RawMaterialItemId;
            if (existingLastCosts.TryGetValue(itemId, out var lc))
            {
                lc.CostPerKg = costLine.CostPerKg;
                lc.CurrencyCode = costLine.CurrencyCode;
                lc.UpdatedAt = DateTime.UtcNow;
                lc.UpdatedByUserId = CurrentUserId;
            }
            else
            {
                var newEntry = new ItemLastCost
                {
                    ItemId = itemId, CostPerKg = costLine.CostPerKg,
                    CurrencyCode = costLine.CurrencyCode,
                    UpdatedAt = DateTime.UtcNow, UpdatedByUserId = CurrentUserId
                };
                db.ItemLastCosts.Add(newEntry);
                existingLastCosts[itemId] = newEntry;
            }
        }
```

**Insert in its place:**

```csharp
        // Upsert ItemLastCost via Postgres INSERT … ON CONFLICT to avoid a TOCTOU
        // race on the unique index when concurrent submits share a raw material.
        var upsertsByItem = newCostLines
            .Select(cl => new
            {
                ItemId = bom.Lines.First(bl => bl.Id == cl.BomLineId).RawMaterialItemId,
                cl.CostPerKg,
                cl.CurrencyCode
            })
            .GroupBy(x => x.ItemId)
            .Select(g => g.Last())
            .ToList();

        var now = DateTime.UtcNow;
        foreach (var u in upsertsByItem)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""ItemLastCosts"" (""ItemId"", ""CostPerKg"", ""CurrencyCode"", ""UpdatedAt"", ""UpdatedByUserId"")
                VALUES ({u.ItemId}, {u.CostPerKg}, {u.CurrencyCode}, {now}, {CurrentUserId})
                ON CONFLICT (""ItemId"") DO UPDATE SET
                    ""CostPerKg"" = EXCLUDED.""CostPerKg"",
                    ""CurrencyCode"" = EXCLUDED.""CurrencyCode"",
                    ""UpdatedAt"" = EXCLUDED.""UpdatedAt"",
                    ""UpdatedByUserId"" = EXCLUDED.""UpdatedByUserId""");
        }
```

**Leave untouched:** all surrounding code — the earlier `newCostLines.Add(...)` loop that builds the cost lines, the `db.BomCosts.Add(cost)` call, the `req.Status = ...` / `req.UpdatedAt = ...` lines, the later `await db.SaveChangesAsync();` at ~line 324, and the commit at the end of the method.

### Step 2.2 — Build

- [ ] From the worktree root:

```bash
dotnet build
```

Expected: `Build succeeded` with 0 errors. If the compiler complains about `ExecuteSqlInterpolatedAsync` being undefined, verify `using Microsoft.EntityFrameworkCore;` is at the top of `CostingController.cs` (it already is — that namespace exposes the extension on `DatabaseFacade`).

### Step 2.3 — Run the Task-1 regression test, confirm it PASSES

- [ ] Run:

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~SubmitItem_ConcurrentDifferentReqsSharingRawMaterial_NoDeadlock"
```

Expected: **PASS.** Run a second time to confirm it's not a fluke.

### Step 2.4 — Run the full Costing suite (regression sweep)

- [ ] Run:

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~Costing"
```

Expected: **all tests pass**, including the existing `Submit_ConvertsCurrencyWritesLinesUpsertsLastCostAndMovesToMdReview` (verifies `ItemLastCost` is correctly written with the converted values) and `Recosting_NewRequisitionDoesNotModifyPreviousBomCostLines` (verifies cross-requisition isolation of BomCostLines). These two act as regression checks for the upsert rewrite.

### Step 2.5 — Run the whole test suite

- [ ] Run:

```bash
dotnet test
```

Expected: **all tests pass.**

### Step 2.6 — Commit (test + fix together)

- [ ] Run:

```bash
git add BomPriceApproval.API/Features/Costing/CostingController.cs BomPriceApproval.Tests/Costing/CostingTests.cs
git commit -m "$(cat <<'EOF'
fix(costing): atomic ItemLastCost upsert via INSERT…ON CONFLICT

Two concurrent costing submits on different requisitions that shared
a raw-material item whose ItemLastCost row did not yet exist could
race on the TOCTOU gap between the dictionary-read and the INSERT,
producing a 23505 unique_violation on IX_ItemLastCosts_ItemId that
surfaced as HTTP 500. The FOR UPDATE on QuotationRequest from the
prior concurrency fix did not serialize them because they targeted
different req rows.

Replace the read-then-insert-if-absent loop with an atomic Postgres
INSERT … ON CONFLICT ("ItemId") DO UPDATE per distinct raw material.
The statement runs inside the existing transaction and preserves
last-write-wins semantics.

Adds a parallel-submit regression test covering two reqs with a
shared raw material in reversed BOM positions.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Expected: commit created; `git status` clean.

---

## Self-Review Notes

- **Spec coverage:** Problem, Fix, and Regression-test sections of the revised spec are covered by Task 1 (complete) and Task 2.
- **Placeholder scan:** no TBDs. All steps have exact code blocks or commands with expected outputs.
- **Type consistency:** `ExecuteSqlInterpolatedAsync` is an extension on `DatabaseFacade` provided by `Microsoft.EntityFrameworkCore`, already imported in the file. Column types in the SQL (`"ItemId"` int, `"CostPerKg"` numeric(18,4), `"CurrencyCode"` text, `"UpdatedAt"` timestamptz, `"UpdatedByUserId"` int) match the migration at `20260414171405_CostingEntry.cs:68–95`. The unique index being exploited by `ON CONFLICT ("ItemId")` is declared at the same migration, line 113–117.
- **Transaction scoping:** `ExecuteSqlInterpolatedAsync` participates in the ambient transaction opened earlier in `SubmitItem` — verified by reading `Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions` behavior.
