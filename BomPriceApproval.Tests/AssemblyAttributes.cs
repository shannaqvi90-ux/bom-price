// Disable xUnit parallel test class execution.
//
// Why: tests share a single PostgreSQL database (the dev DB locally and the
// CI service container in GitHub Actions). They Guid-isolate USER EMAILS, but
// they share other state — push subscriptions, notifications, requisitions,
// processes — and modifying that shared state in parallel causes spurious
// failures (DbUpdateConcurrencyException, "expected 1 row, found 0", etc.).
//
// Per-test full isolation would require Guid-prefixing every entity in every
// test, which is a large refactor with marginal benefit (saves ~50s of CI
// time per run). Serial execution is the simpler trade-off and aligns the
// test architecture with the implicit "single-tenant DB" assumption.
//
// Trade-off: ~50s slower CI per run (≈ 40s parallel → 90s serial).
// In return: deterministic green CI, simpler mental model for future tests.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
