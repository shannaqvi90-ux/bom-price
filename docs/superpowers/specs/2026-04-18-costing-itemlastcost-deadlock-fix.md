# Costing Submit — `ItemLastCost` Deadlock Fix

**Status:** Approved for implementation
**Date:** 2026-04-18
**Scope:** Single-file backend fix + one regression test
**Origin:** Surfaced during review of the costing-submit concurrency fix (Task 2 of `docs/superpowers/plans/2026-04-17-costing-submit-concurrency-fix.md`). Latent pre-existing bug, not caused by that fix.

## Problem

In `BomPriceApproval.API/Features/Costing/CostingController.cs`, the `SubmitItem` method upserts `ItemLastCost` rows by iterating `newCostLines` in list (BOM-line) order (currently at lines ~294–316).

Two concurrent `/api/costing/{reqId}/items/{riId}/submit` calls on **different** requisitions that share one or more raw-material items can acquire row-level locks on the shared `ItemLastCost` rows in **different orders**, forming a cyclic wait-for graph. PostgreSQL detects this after `deadlock_timeout` (default 1s) and aborts one transaction with SQLSTATE `40P01` (`serialization_failure` / `deadlock_detected`), which EF Core surfaces as an unhandled exception → HTTP 500 to the client.

The `FOR UPDATE` row-lock on `QuotationRequest` added by the prior concurrency fix does not help here: the two transactions lock different requisition rows and so run in parallel into the `ItemLastCost` upserts where the conflict occurs.

## Fix

Order the upsert loop by `RawMaterialItemId` ascending so every submitter acquires `ItemLastCost` locks in the same global order, eliminating the cyclic wait.

**File:** `BomPriceApproval.API/Features/Costing/CostingController.cs`

**Change (line ~294):**

```csharp
foreach (var costLine in newCostLines
    .OrderBy(cl => bom.Lines.First(bl => bl.Id == cl.BomLineId).RawMaterialItemId))
{
    // existing body unchanged
}
```

No other code changes. The business semantics of the upsert are order-independent — only the lock-acquisition order changes.

## Alternatives considered

| Approach | Verdict |
|---|---|
| **Sort upsert order by RawMaterialItemId** (chosen) | Minimal, targeted, one line. No extra round-trip. |
| `SELECT … FOR UPDATE … ORDER BY ItemId` on `ItemLastCost` rows before the loop | Same effect, but adds a round-trip and couples the controller to raw SQL. |
| PostgreSQL advisory lock keyed on "costing-submit" | Overkill — serializes all submitters globally, destroying the parallelism the FOR UPDATE fix enabled. |

## Regression test

**File:** `BomPriceApproval.Tests/Costing/CostingTests.cs`
**Location:** Adjacent to the existing parallel-submit tests around lines 520–575.

**Shape:**

1. Arrange
   - Create raw materials: `rmShared`, `rmA`, `rmB`.
   - Create two independent requisitions `reqA`, `reqB`. Give each a BOM with **two** lines: `reqA` = `[rmA, rmShared]`, `reqB` = `[rmShared, rmB]`. The reversed positions of `rmShared` guarantee that the list-order of `ItemLastCost` upserts differs between the two transactions absent the fix — the precondition for a deadlock.
   - Submit each BOM so both reach `CostingPending`.
2. Act
   - Log in as the accountant on two independent `HttpClient` instances.
   - Start costing on both items (sequential is fine — the race is on submit).
   - Fire both submits concurrently via `Task.WhenAll`.
3. Assert
   - Both responses are `204 NoContent`. Neither is `500`.
   - Both requisitions transition to `MdReview` (optional sanity check; the two tests adjacent already exercise status).

**Note on reproducing without the fix:** deadlocks are probabilistic, but with two transactions each touching two shared/unshared items in opposite order the probability is high enough that a single unfixed run typically fails within the `deadlock_timeout` window. If flakiness appears, loop the act/assert a small number of times (e.g., 3) inside the test.

## Risk & rollback

- **Functional risk:** none. Upsert order does not affect the final state of `ItemLastCost` (each `ItemId` is touched at most once per transaction because of the `.Distinct()` projection into `rawItemIds`).
- **Performance:** negligible — `.OrderBy` on an in-memory list sized by BOM-line count (typically <20).
- **Migration:** none.
- **Rollback:** revert the single-line diff.

## Out of scope

- Refactoring `newCostLines` / `bom.Lines.First(...)` lookup into a dictionary (separate cleanup, not required for correctness).
- Retry-on-deadlock middleware (the sort makes retries unnecessary for this code path).
- Any change to the `QuotationRequest` `FOR UPDATE` lock from the prior fix.
