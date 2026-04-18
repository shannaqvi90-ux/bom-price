# Costing Submit — `ItemLastCost` Concurrency Fix

**Status:** Approved for implementation (revised 2026-04-18 after TDD red phase)
**Date:** 2026-04-18
**Scope:** `CostingController.SubmitItem` upsert rewrite + regression test
**Origin:** Surfaced during review of the costing-submit concurrency fix (Task 2 of `docs/superpowers/plans/2026-04-17-costing-submit-concurrency-fix.md`). Latent pre-existing bug, not caused by that fix.

## Revision note

Initial hypothesis was a Postgres `40P01` deadlock from opposite row-lock acquisition order during `ItemLastCost` UPDATEs. The regression test (written TDD-red) reliably reproduces a different failure mode instead:

- **SQLSTATE `23505` (unique_violation)** on `IX_ItemLastCosts_ItemId`
- Deterministic across runs — zero `40P01`s observed

The proposed `.OrderBy(RawMaterialItemId)` fix was therefore ineffective. This spec is revised to target the actual bug: a TOCTOU race on first-INSERT.

## Problem

`SubmitItem` upserts `ItemLastCost` rows with a **read-then-insert-if-absent** pattern:

1. `SELECT * FROM "ItemLastCosts" WHERE "ItemId" IN (...)` → build an in-memory dictionary (`existingLastCosts`)
2. For each new cost line: if the dict has the `ItemId`, UPDATE the tracked entity; otherwise `db.ItemLastCosts.Add(...)`
3. `SaveChangesAsync` flushes the pending inserts

When two submits on different requisitions share a raw-material item whose `ItemLastCost` row does not yet exist, both transactions see the dict as empty for that `ItemId`, both `Add(...)`, and the second commit hits the unique index on `"ItemId"` → `Npgsql.PostgresException 23505: duplicate key value violates unique constraint "IX_ItemLastCosts_ItemId"`, surfacing as HTTP 500.

The `FOR UPDATE` on `QuotationRequest` (from the prior concurrency fix) does not help: the two transactions lock different requisition rows so they run the upsert loop concurrently.

## Fix

Replace the EF-tracked read-then-insert pattern with an atomic Postgres **`INSERT … ON CONFLICT ("ItemId") DO UPDATE`** per distinct `ItemId`, executed inside the existing ambient transaction via `ExecuteSqlInterpolatedAsync`. This resolves the race at the storage engine level: concurrent inserters are serialized on the unique index, the loser sees its row already exists and silently transitions to UPDATE with the loser's values (preserving the existing "last-write-wins" semantics).

**File:** `BomPriceApproval.API/Features/Costing/CostingController.cs`

**Replacement for lines ~288–316** (the `rawItemIds` projection, `existingLastCosts` read, and the foreach-upsert loop):

```csharp
// De-duplicate by raw-material item id, keeping the last line's values
// (matches the existing "last BomLine wins" semantics of the old loop).
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

**Notes:**
- `ExecuteSqlInterpolatedAsync` parameterizes the interpolated values — no SQL injection.
- The statements run inside the transaction opened via `BeginTransactionAsync` earlier in `SubmitItem`, so they're atomic with the rest of the submit.
- `ItemLastCosts.Id` is an identity column; omitting it in the INSERT list is correct.
- The previously read `existingLastCosts` dictionary and `rawItemIds` projection become unused and are removed.

## Alternatives considered

| Approach | Verdict |
|---|---|
| **`INSERT … ON CONFLICT DO UPDATE`** (chosen) | Canonical Postgres upsert. Atomic. Preserves parallelism. No retry logic. |
| Sort loop by `RawMaterialItemId` (original proposal) | Does not address the INSERT race. Only affects hypothetical UPDATE-vs-UPDATE deadlocks, which the test never reproduced. |
| `pg_advisory_xact_lock` per raw-material id | Works, but adds a second locking mechanism alongside the `FOR UPDATE` on `QuotationRequest`. More complex than ON CONFLICT. |
| Retry `SaveChangesAsync` on 23505 | Papers over the race. Noisy under contention. Harder to reason about. |
| Global advisory lock for all costing submits | Kills parallelism the prior fix enabled. |

## Regression test

Already written in Task 1 (uncommitted): `SubmitItem_ConcurrentDifferentReqsSharingRawMaterial_NoDeadlock` in `BomPriceApproval.Tests/Costing/CostingTests.cs`.

Two requisitions with reversed-position shared raw material are submitted concurrently; both must return 204. Pre-fix: reliably fails with 23505 (3/3 and 3/3 across two runs). Post-fix: expected to pass deterministically.

The test name retains `NoDeadlock` for historical continuity; the assertion "both return 204" correctly covers both deadlock (40P01) and unique-violation (23505) manifestations.

## Risk & rollback

- **Functional risk:** low. The upsert's external contract is unchanged — one `ItemLastCost` row per `ItemId`, last-write-wins within a transaction. A concurrent submit that was previously the *second* arriver and crashed now silently overwrites — that is the existing semantics, just made reachable under concurrency.
- **Performance:** neutral. Same number of round-trips as before (N statements for N distinct raw-material ids, typically ≤ ~20).
- **Migration:** none. Unique index already exists.
- **Rollback:** revert the CostingController diff. Test can remain as a known-failing red test if rolled back.

## Out of scope

- Retry-on-deadlock middleware (not needed; ON CONFLICT eliminates the crash directly).
- Any change to the `QuotationRequest` `FOR UPDATE` lock from the prior fix.
- Converting other controllers' similar read-then-insert patterns (none identified in this session).
