# V3 Phase C — Production Cutover Design

**Status:** approved 2026-04-30
**Goal:** prepare production database for V3 frontend deploy via a single idempotent SQL script.

V3 Phase A backend foundation is merged on master (`ea1a904`). V3 Phase B frontend is open as PR #32 with a `hold` label. This design covers Phase C — the data migration that runs on cutover day, immediately before V3 backend redeploy and PR #32 merge.

---

## Scope

The cutover script performs four data operations on production, all in a single PostgreSQL transaction:

1. **Cancel all in-flight V2.3 requisitions** — anything in `Status` 1-5 (`BomPending=1`, `BomInProgress=2`, `CostingPending=3`, `CostingInProgress=4`, `MdReview=5`) gets transitioned to V3 `Cancelled` (status 13) with a cutover-flagged reason. V2.3 `Draft` (status 0) reqs are left as-is — `Draft` carries identical semantics in V3 (sales hasn't submitted yet), so the workflow naturally continues.
2. **Deactivate all `BomCreator` users** — `IsActive=false` + revoke their refresh tokens for immediate logout.
3. **Soft-delete all customers in non-Alain branches** — `IsDeleted=true` on Dubai, Sharjah, Fujairah, Abu Dhabi customers.
4. **Deactivate all items in non-Alain branches** — `IsActive=false`.

Each operation logs one `AdminAuditLog` row per affected entity with `ActionType='V3CutoverMigration'`, capturing the original state in `BeforeJson` for forensic reversibility.

V2.3 `Approved` (status 6) and `Rejected` (status 7) requisitions are intentionally left untouched. `Approved` is terminal-historical (kept as-is); `Rejected` is also reused in V3 for MD-rejected-from-MdPricing reqs — both render correctly via Phase B's `V3StatusBadge`.

---

## Locked design decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | **Run mechanism: idempotent SQL script** committed to `docs/cutover/2026-XX-XX-v3-cutover.sql` | Atomic via single `BEGIN/COMMIT`; reviewable diff; runnable on Neon snapshot for dry-run; no chicken-egg with backend deploy |
| 2 | **In-flight cancellation: direct SQL UPDATE** to `Status=13` + populate `CancelReason` / `CancelledAt` / `CancelledByUserId`. Reason text: `[V3 cutover YYYY-MM-DD] Workflow simplified — please re-create in V3 if still needed.` | Uses V3 enum cleanly; UI's V3StatusBadge renders correctly; faster than per-req endpoint loop |
| 3 | **Non-Alain hiding: soft-delete customers + deactivate items only** (Branches table left untouched) | Existing backend filters (`!c.IsDeleted`, `i.IsActive`) already do the work; no FE changes needed; admin's branch admin pages keep functioning |
| 4 | **BomCreator deactivation: `IsActive=false` + revoke all refresh tokens** | Matches V23c P1 `AdminUsersController.ResetPassword` pattern; `IsActive=false` blocks login + refresh; refresh-token revocation forces re-login at next refresh attempt. **Note:** `Program.cs OnTokenValidated` only checks `RevokedJti` table, not `User.IsActive` — so live access tokens (≤15 min TTL) continue to work until expiry. Acceptable because cutover runs inside the 10-min maintenance window. Hard-revocation of access tokens via JTI tracking is a separate non-blocking enhancement (out of scope). |
| 5 | **Audit granularity: one `AdminAuditLog` row per affected entity** | Matches existing V23c P1 `AdminAuditLogger.Log()` per-entity pattern; queryable via `actionType=V3CutoverMigration` filter (already added to web filter UI in Phase B Task 19) |
| 6 | **`AdminUserId` for audit rows: the human Admin running the script** (passed via `\set adminId N` parameter at top of file) | Cutover is a deliberate human action; preserves accountability |
| 7 | **V2.3 `Approved` + `Rejected` reqs: leave alone** | Terminal states; Phase B's `V3StatusBadge` renders legacy V2 status names with proper colors; no action needed |
| 8 | **Atomicity: single `BEGIN/COMMIT` transaction** | All-or-nothing semantics; Postgres transaction lock is acceptable for ~150 row updates (estimated <2 sec on Neon) |
| 9 | **Dry-run support: `\set dryRun true` flag at top** swaps the final `COMMIT` for `ROLLBACK` | Lets us verify counts on a Neon DB branch before flipping prod |
| 10 | **Pre-cutover safety: Neon DB branch snapshot** taken immediately before prod run | Neon zero-cost branch off prod = instant rollback path if something unexpected surfaces post-COMMIT |
| 11 | **Notifications: no per-entity fan-out** — broadcast email/Slack to staff explaining cutover (one message, sent 24h before by user) | SQL script doesn't have access to `NotificationService`; per-req notifications would be ~10-100 SignalR pushes that nobody can act on; broadcast comm is the right granularity |
| 12 | **PWA cache invalidation: already handled in Phase B** (cache name suffix `-v3` from PR #32 Task 21) | No action needed in Phase C |

---

## File structure

| Path | Purpose |
|---|---|
| `docs/cutover/2026-XX-XX-v3-cutover.sql` | The cutover script. `XX-XX` filled in on cutover day. |
| `docs/cutover/README.md` | Operational runbook — pre-flight checks, dry-run procedure, prod-run procedure, post-flight verification, rollback procedure |
| `docs/cutover/v3-cutover.test.sql` (optional) | Helper SQL that seeds a "fake prod" state on a Neon DB branch — useful for dry-run rehearsals before cutover day |
| `docs/superpowers/specs/2026-04-30-v3-phase-c-cutover-design.md` | This document |
| `docs/superpowers/plans/2026-04-30-v3-phase-c-cutover.md` | Implementation plan (next step after this spec) |

---

## SQL script — structural sketch

```sql
-- ============================================================================
-- V3 Cutover Migration — 2026-XX-XX
-- ============================================================================
-- Run as the admin user whose ID is in :adminId.
-- DRY RUN: set dryRun=true at top; transaction COMMITs as ROLLBACK.
-- PROD RUN: set dryRun=false; transaction COMMITs.
-- ============================================================================

\set adminId 1               -- ← Admin user ID running the cutover (verify via SELECT first)
\set alainBranchId 2         -- ← verified before run via pre-flight SELECT
\set dryRun true             -- ← flip to false on prod run
\set cutoverDateLabel '2026-XX-XX'

\timing on
\echo '=== V3 CUTOVER MIGRATION ==='
\echo 'adminId:' :adminId
\echo 'alainBranchId:' :alainBranchId
\echo 'dryRun:' :dryRun
\echo 'cutoverDateLabel:' :cutoverDateLabel
\echo ''

BEGIN;

-- ============================================================================
-- 0. PRE-FLIGHT CHECKS — assert expected DB state
-- ============================================================================

-- Admin user exists with Role='Admin' and IsActive=true
-- UserRole enum: Admin=0, SalesPerson=1, BomCreator=2, Accountant=3, ManagingDirector=4
DO $$
DECLARE admin_count INT;
BEGIN
  SELECT COUNT(*) INTO admin_count FROM "Users"
   WHERE "Id" = :adminId AND "Role" = 0 AND "IsActive" = true;  -- 0 = Admin enum
  IF admin_count != 1 THEN
    RAISE EXCEPTION 'Pre-flight: admin user % not found or not active', :adminId;
  END IF;
END $$;

-- Alain branch exists with Name='Al Ain' and IsActive=true
DO $$
DECLARE alain_count INT;
BEGIN
  SELECT COUNT(*) INTO alain_count FROM "Branches"
   WHERE "Id" = :alainBranchId AND "Name" = 'Al Ain' AND "IsActive" = true;
  IF alain_count != 1 THEN
    RAISE EXCEPTION 'Pre-flight: Alain branch id=% with Name=Al Ain not found or inactive', :alainBranchId;
  END IF;
END $$;

-- ============================================================================
-- 1. CANCEL IN-FLIGHT V2.3 REQUISITIONS (statuses 0-4)
-- ============================================================================

-- 1a. Audit rows captured BEFORE update (BeforeJson reflects original state)
INSERT INTO "AdminAuditLog"
  ("AdminUserId", "ActionType", "EntityType", "EntityId", "Reason", "BeforeJson", "CreatedAt")
SELECT
  :adminId,
  'V3CutoverMigration',
  'Requisition',
  q."Id",
  '[V3 cutover ' || :'cutoverDateLabel' || '] Workflow simplified — original V2.3 status preserved in BeforeJson',
  json_build_object(
    'id', q."Id",
    'refNo', q."RefNo",
    'status', q."Status",
    'customerId', q."CustomerId",
    'salesPersonId', q."SalesPersonId",
    'updatedAt', q."UpdatedAt"
  )::text,
  NOW()
FROM "QuotationRequests" q
WHERE q."Status" IN (1, 2, 3, 4, 5);  -- BomPending=1, BomInProgress=2, CostingPending=3, CostingInProgress=4, MdReview=5

-- 1b. Cancel via direct UPDATE
UPDATE "QuotationRequests"
   SET "Status" = 13,  -- V3 Cancelled
       "CancelReason" = '[V3 cutover ' || :'cutoverDateLabel' || '] Workflow simplified — please re-create in V3 if still needed',
       "CancelledAt" = NOW(),
       "CancelledByUserId" = :adminId,
       "UpdatedAt" = NOW()
 WHERE "Status" IN (1, 2, 3, 4, 5);

\echo '1. Cancelled in-flight V2.3 reqs:' :ROW_COUNT

-- ============================================================================
-- 2. DEACTIVATE BOMCREATOR USERS
-- ============================================================================

-- 2a. Audit rows
INSERT INTO "AdminAuditLog"
  ("AdminUserId", "ActionType", "EntityType", "EntityId", "Reason", "BeforeJson", "CreatedAt")
SELECT
  :adminId,
  'V3CutoverMigration',
  'User',
  u."Id",
  '[V3 cutover ' || :'cutoverDateLabel' || '] BomCreator role deprecated — user deactivated per V3 design Q12',
  json_build_object(
    'id', u."Id",
    'name', u."Name",
    'email', u."Email",
    'role', u."Role",
    'isActive', u."IsActive"
  )::text,
  NOW()
FROM "Users" u
WHERE u."Role" = 2   -- BomCreator enum value (UserRole: Admin=0, SalesPerson=1, BomCreator=2, Accountant=3, ManagingDirector=4)
  AND u."IsActive" = true;

-- 2b. Deactivate
UPDATE "Users"
   SET "IsActive" = false
 WHERE "Role" = 2 AND "IsActive" = true;

\echo '2a. Deactivated BomCreator users:' :ROW_COUNT

-- 2c. Revoke their refresh tokens (Role=2 = BomCreator)
UPDATE "RefreshTokens"
   SET "RevokedAt" = NOW()
 WHERE "RevokedAt" IS NULL
   AND "UserId" IN (SELECT "Id" FROM "Users" WHERE "Role" = 2 AND "IsActive" = false);

\echo '2c. Revoked BomCreator refresh tokens:' :ROW_COUNT

-- ============================================================================
-- 3. SOFT-DELETE NON-ALAIN CUSTOMERS
-- ============================================================================

-- 3a. Audit rows
INSERT INTO "AdminAuditLog"
  ("AdminUserId", "ActionType", "EntityType", "EntityId", "Reason", "BeforeJson", "CreatedAt")
SELECT
  :adminId,
  'V3CutoverMigration',
  'Customer',
  c."Id",
  '[V3 cutover ' || :'cutoverDateLabel' || '] Customer in non-Alain branch hidden per V3 design Q13',
  json_build_object(
    'id', c."Id",
    'code', c."Code",
    'name', c."Name",
    'branchId', c."BranchId",
    'isDeleted', c."IsDeleted",
    'salesPersonId', c."SalesPersonId"
  )::text,
  NOW()
FROM "Customers" c
WHERE c."BranchId" != :alainBranchId
  AND c."IsDeleted" = false;

-- 3b. Soft-delete
UPDATE "Customers"
   SET "IsDeleted" = true,
       "DeletedAt" = NOW(),
       "DeletedByUserId" = :adminId
 WHERE "BranchId" != :alainBranchId
   AND "IsDeleted" = false;

\echo '3. Soft-deleted non-Alain customers:' :ROW_COUNT

-- ============================================================================
-- 4. DEACTIVATE NON-ALAIN ITEMS
-- ============================================================================

-- 4a. Audit rows
INSERT INTO "AdminAuditLog"
  ("AdminUserId", "ActionType", "EntityType", "EntityId", "Reason", "BeforeJson", "CreatedAt")
SELECT
  :adminId,
  'V3CutoverMigration',
  'Item',
  i."Id",
  '[V3 cutover ' || :'cutoverDateLabel' || '] Item in non-Alain branch deactivated per V3 design Q13',
  json_build_object(
    'id', i."Id",
    'code', i."Code",
    'description', i."Description",
    'type', i."Type",
    'branchId', i."BranchId",
    'isActive', i."IsActive"
  )::text,
  NOW()
FROM "Items" i
WHERE i."BranchId" != :alainBranchId
  AND i."IsActive" = true;

-- 4b. Deactivate
UPDATE "Items"
   SET "IsActive" = false
 WHERE "BranchId" != :alainBranchId
   AND "IsActive" = true;

\echo '4. Deactivated non-Alain items:' :ROW_COUNT

-- ============================================================================
-- 5. POST-FLIGHT VERIFICATION — counts the script affected; consumer reads them
-- ============================================================================

\echo ''
\echo '=== POST-FLIGHT COUNTS ==='
SELECT
  (SELECT COUNT(*) FROM "QuotationRequests" WHERE "Status" IN (1,2,3,4,5)) AS still_inflight_v2_reqs,  -- expect 0
  (SELECT COUNT(*) FROM "QuotationRequests" WHERE "Status" = 13 AND "CancelReason" LIKE '%V3 cutover%') AS cutover_cancelled_reqs,
  (SELECT COUNT(*) FROM "Users" WHERE "Role" = 2 AND "IsActive" = true) AS still_active_bomcreator_users,  -- expect 0
  (SELECT COUNT(*) FROM "Customers" WHERE "BranchId" != :alainBranchId AND "IsDeleted" = false) AS still_visible_nonalain_customers,  -- expect 0
  (SELECT COUNT(*) FROM "Items" WHERE "BranchId" != :alainBranchId AND "IsActive" = true) AS still_active_nonalain_items,  -- expect 0
  (SELECT COUNT(*) FROM "AdminAuditLog" WHERE "ActionType" = 'V3CutoverMigration' AND "CreatedAt" >= NOW() - INTERVAL '1 minute') AS new_audit_rows;

-- ============================================================================
-- 6. COMMIT or ROLLBACK
-- ============================================================================

\if :dryRun
  \echo ''
  \echo '*** DRY RUN — ROLLING BACK ***'
  ROLLBACK;
\else
  \echo ''
  \echo '*** PROD RUN — COMMITTING ***'
  COMMIT;
\endif
```

> The `\set` syntax + `\if` directive + `:ROW_COUNT` are `psql` client features (not pure SQL). The script must be run via `psql -f`, not pasted into the Neon SQL Console (which is closer to a generic Postgres client and doesn't support `\if`). For Neon Console runs, the conditional COMMIT/ROLLBACK can be split into two file variants. Plan covers both paths.

---

## Operational steps (cutover day)

1. **Pre-cutover prep (24h before)**
   - Send broadcast email to all staff: "V3 cutover scheduled for [date/time]; in-flight V2.3 reqs will be auto-cancelled and need to be re-created in V3 after cutover; BomCreator users will lose login access; non-Alain branches' data will be hidden."
   - Verify PR #32 still has `hold` label.

2. **Snapshot Neon DB**
   - Use Neon Console to create a branch off `main` (= prod) named `pre-v3-cutover-2026-XX-XX`. Free, instant, point-in-time.
   - Save the branch's connection string for rollback access.

3. **Dry-run on a fresh Neon branch**
   - Create another Neon branch off prod named `cutover-dryrun-2026-XX-XX`.
   - `psql <dryrun-branch-uri> -f docs/cutover/2026-XX-XX-v3-cutover.sql` with `dryRun=true`.
   - Verify post-flight counts match expectations:
     - `still_inflight_v2_reqs = 0`
     - `still_active_bomcreator_users = 0`
     - `still_visible_nonalain_customers = 0`
     - `still_active_nonalain_items = 0`
     - `new_audit_rows ≥ sum of categories` — but ROLLBACK will discard them, so this is informational only

4. **Re-run on dry-run branch with `dryRun=false`** — verify everything still works end-to-end (login, list reqs, list customers, etc.) on the snapshot DB. If broken, fix the script and go back to step 3.

5. **Maintenance window: 10 minutes** — post a banner on the web app or send a Slack ping that the system is briefly unavailable.

6. **Run on prod**
   - `psql <prod-uri> -f docs/cutover/2026-XX-XX-v3-cutover.sql` with `dryRun=false`.
   - Capture stdout (counts).
   - If it errors mid-transaction, the BEGIN auto-rolls back; nothing committed; investigate and re-run.

7. **Verify on prod immediately** — re-run the post-flight count query. All counts as expected.

8. **Deploy V3 backend**
   - `cd BomPriceApproval.API && flyctl deploy --remote-only`
   - Wait for healthcheck green.

9. **Merge PR #32**
   - `gh pr edit 32 --remove-label hold`
   - `gh pr merge 32 --squash`
   - Cloudflare Pages auto-deploys V3 frontend (~1-2 min).

10. **End-to-end smoke on prod**
    - Login as Sales → see only Alain customers + items
    - Create new V3 req → submit → verify Costing status
    - Login as Accountant → see V3 Costing queue
    - Login as MD → set margin → customer-confirm → final-sign
    - Verify PDF download

11. **Tag**
    - `git tag v3-cutover-2026-XX-XX <new-master-sha> -m "V3 cutover on YYYY-MM-DD"`
    - `git push origin v3-cutover-2026-XX-XX`

---

## Rollback procedure

If anything goes wrong post-COMMIT:

1. **Drop in the Neon snapshot** — promote the `pre-v3-cutover-2026-XX-XX` branch to be the new main. Neon supports this in seconds.
2. **Rollback V3 backend** — `flyctl releases` to find the prior release ID, `flyctl releases rollback <id>`. Or redeploy from the prior master commit.
3. **Revert PR #32 merge** — `git revert <merge-sha>` on master + push (will need PR + CI), OR force-redeploy Cloudflare Pages from the prior master commit (instant rollback of static frontend).

---

## Out of scope

- **Notification fan-out per affected entity** — covered by manual broadcast email instead.
- **Mobile app cutover** — V2.3 mobile preserved (read-only post-cutover); Phase D handles V3 mobile separately.
- **V2.3 schema cleanup** — old DTOs (`RequisitionDetail`, `ApprovalSummary`, etc. still in code), V2.3 controller endpoints (e.g., `POST /requisitions/{id}/approve` from V2.3), legacy `useRequisition`/`useCreateRequisition` web hooks. Phase D / future tech-debt PR.
- **`useCustomerChangeHistory`/`useBranchChangeHistory` hook cleanup** (flagged in PR #32 concerns) — Phase D.
- **Phase A backend-side cleanup** — `[Obsolete]`-marking the old DTOs flagged by the Task 2.5 code review.

---

## Risks + mitigations

| Risk | Mitigation |
|---|---|
| In-flight req cancellation surprises a salesperson mid-flow | Broadcast email 24h before cutover; Q11 already locked "hard cutover" |
| Audit log table grows by ~150 rows | Negligible (existing table likely <1000 rows) |
| BomCreator user can't log back in | Expected (Q12); admin can re-activate via Users admin panel if business need arises |
| Wrong `:alainBranchId` parameter | Pre-flight check fails the transaction before any UPDATE |
| Wrong `:adminId` parameter | Pre-flight check fails the transaction |
| Cutover takes too long → connection timeout | Estimated <2 sec for ~150 row updates; stale prod data unlikely to balloon |
| Neon snapshot doesn't capture state correctly | Take TWO snapshots — one via Neon Console branch, one via `pg_dump` to S3/local — defense in depth |
| Post-cutover, V3 backend deploy fails | Cutover SQL doesn't roll back automatically; admin re-deploys backend or rolls back via Fly releases. SQL state is forward-compatible with V2.3 backend (V2.3 backend just sees Cancelled reqs as future enum values it doesn't recognize — read-tolerant). |
| PR #32 CI fails on cutover day | `gh pr checks 32` before merge; if red, investigate and fix before pulling the trigger |
| Cloudflare Pages deploy lag (rare) | Cache busting via PWA `-v3` cache name handles client-side; CDN edge invalidation usually <2 min |

---

## Approval gates

- [x] Design approved by user 2026-04-30
- [ ] Spec reviewed (this doc)
- [ ] Implementation plan written + approved
- [ ] Cutover SQL implemented + tested on Neon dry-run branch
- [ ] Cutover SQL reviewed by user
- [ ] Cutover day scheduled with broadcast email sent
- [ ] Cutover executed end-to-end
- [ ] PR #32 merged + production smoke green
- [ ] `v3-cutover-YYYY-MM-DD` tag created
