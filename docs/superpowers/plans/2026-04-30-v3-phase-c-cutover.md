# V3 Phase C — Production Cutover Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the cutover SQL script + operational runbook that prepares production for V3 frontend deploy. Tested locally + ready for Neon dry-run on cutover day.

**Architecture:** Single idempotent `psql` SQL script (run via `psql -f`) with `\set dryRun true/false` flag. Pre-flight assertions, four data-op sections (cancel V2.3 reqs / deactivate BomCreator / soft-delete non-Alain customers / deactivate non-Alain items), per-entity audit logging, post-flight count verification, and conditional `COMMIT`/`ROLLBACK`. Operational runbook covers Neon snapshot strategy, dry-run procedure, prod-run procedure, and rollback playbook.

**Tech Stack:** PostgreSQL 14+ · `psql` client (for `\set` / `\if` / `:ROW_COUNT` directives) · Neon DB (for snapshots + dry-run branches) · Bash scripting for the runbook commands.

**Spec:** [`docs/superpowers/specs/2026-04-30-v3-phase-c-cutover-design.md`](../specs/2026-04-30-v3-phase-c-cutover-design.md) — read sections "Locked design decisions", "SQL script — structural sketch", "Operational steps", and "Rollback procedure" before starting.

---

## File Structure

### NEW files

| Path | Purpose |
|---|---|
| `docs/cutover/v3-cutover.sql` | The cutover script. Filename omits the date so it stays evergreen as a checked-in template; cutover-day operator copies + date-stamps just before run (`cp v3-cutover.sql 2026-05-XX-v3-cutover.sql`) so the dated copy lives alongside in git as the actual execution artifact. |
| `docs/cutover/README.md` | Operational runbook — pre-cutover prep, Neon snapshot, dry-run procedure, prod-run procedure, post-flight verification, rollback playbook. |
| `docs/cutover/v3-cutover-fixture.sql` | Test-data seed for local Postgres + Neon dry-run rehearsals. Inserts representative V2.3 reqs in flight + BomCreator users + non-Alain customers/items. |
| `docs/cutover/test-cutover-locally.sh` | Bash helper script — spins up a fresh local Postgres DB (or uses existing one with throwaway prefix), seeds fixture, runs cutover with `dryRun=true`, asserts post-flight counts, runs again with `dryRun=false`, asserts counts again, drops the test DB. |

### MODIFIED files

None. This plan is purely additive.

### Notes

- The SQL script is `psql`-specific (uses `\set`, `\if`, `:ROW_COUNT`). Cannot be pasted into Neon's web SQL Console directly. The runbook includes a fallback "split into two files" workaround for Neon Console runs (one file with `COMMIT`, one with `ROLLBACK`).
- All `psql` directives use the `\set var value` + `:var` substitution syntax. Quoted versions use `:'var'` for string literals (e.g., for `cutoverDateLabel`).
- The `cutover-day operator copies + date-stamps` rule is documented inline at the top of `v3-cutover.sql` so future-readers don't get confused why the file is undated.

---

## Worktree

Implementation runs in the existing `feat/v3-phase-c-cutover` branch in the main repo (no separate worktree needed — Phase C is 4 file additions, no merge conflicts likely with master). Phase B's worktree at `.claude/worktrees/v3-phase-b/` stays untouched.

```bash
# Verify branch state
cd "/d/shan projects/BOM_Price_Approval"
git branch --show-current   # expect: feat/v3-phase-c-cutover
git status                   # expect: clean apart from "New Requirements.docx" untracked
git log -1 --format="%h %s"  # expect: ba81706 docs(v3): add Phase C cutover design spec
```

---

## Task 1: Create the cutover SQL script skeleton + pre-flight + final commit/rollback

**Files:**
- Create: `docs/cutover/v3-cutover.sql`

- [ ] **Step 1: Verify the directory does not yet exist**

```bash
cd "/d/shan projects/BOM_Price_Approval"
ls docs/cutover/ 2>&1 || echo "directory_not_present"
```

Expected: `directory_not_present` (or empty listing).

- [ ] **Step 2: Create the SQL skeleton file**

Create `docs/cutover/v3-cutover.sql` with this exact content:

```sql
-- ============================================================================
-- V3 Cutover Migration — DATE-STAMPED COPY ON CUTOVER DAY
-- ============================================================================
-- This file lives in git undated as the canonical template. On cutover day,
-- the operator copies it to a date-stamped sibling for execution + record:
--
--     cp v3-cutover.sql 2026-MM-DD-v3-cutover.sql
--     git add 2026-MM-DD-v3-cutover.sql && git commit -m "docs(cutover): record V3 cutover ran YYYY-MM-DD"
--
-- Run as the admin user whose ID is in :adminId.
-- DRY RUN: set dryRun=true at top — transaction COMMITs as ROLLBACK.
-- PROD RUN: set dryRun=false — transaction COMMITs.
-- ============================================================================

\set adminId 1                -- ← Admin user ID running the cutover (verify via SELECT first)
\set alainBranchId 2          -- ← verified before run via pre-flight SELECT
\set dryRun true              -- ← flip to false on prod run
\set cutoverDateLabel '2026-MM-DD'

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

-- Admin user exists with Role='Admin' and IsActive=true.
-- UserRole enum: Admin=0, SalesPerson=1, BomCreator=2, Accountant=3, ManagingDirector=4
DO $$
DECLARE admin_count INT;
BEGIN
  SELECT COUNT(*) INTO admin_count FROM "Users"
   WHERE "Id" = :adminId AND "Role" = 0 AND "IsActive" = true;
  IF admin_count != 1 THEN
    RAISE EXCEPTION 'Pre-flight: admin user % not found or not active', :adminId;
  END IF;
END $$;

-- Alain branch exists with Name='Al Ain' and IsActive=true.
DO $$
DECLARE alain_count INT;
BEGIN
  SELECT COUNT(*) INTO alain_count FROM "Branches"
   WHERE "Id" = :alainBranchId AND "Name" = 'Al Ain' AND "IsActive" = true;
  IF alain_count != 1 THEN
    RAISE EXCEPTION 'Pre-flight: Alain branch id=% with Name=Al Ain not found or inactive', :alainBranchId;
  END IF;
END $$;

\echo '0. Pre-flight checks PASSED.'

-- ============================================================================
-- (sections 1-4 added in subsequent tasks)
-- ============================================================================

-- ============================================================================
-- 5. POST-FLIGHT VERIFICATION
-- ============================================================================

\echo ''
\echo '=== POST-FLIGHT COUNTS ==='
SELECT
  (SELECT COUNT(*) FROM "QuotationRequests" WHERE "Status" IN (1,2,3,4,5)) AS still_inflight_v2_reqs,
  (SELECT COUNT(*) FROM "QuotationRequests" WHERE "Status" = 13 AND "CancelReason" LIKE '%V3 cutover%') AS cutover_cancelled_reqs,
  (SELECT COUNT(*) FROM "Users" WHERE "Role" = 2 AND "IsActive" = true) AS still_active_bomcreator_users,
  (SELECT COUNT(*) FROM "Customers" WHERE "BranchId" != :alainBranchId AND "IsDeleted" = false) AS still_visible_nonalain_customers,
  (SELECT COUNT(*) FROM "Items" WHERE "BranchId" != :alainBranchId AND "IsActive" = true) AS still_active_nonalain_items,
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

- [ ] **Step 3: Commit**

```bash
git add docs/cutover/v3-cutover.sql
git commit -m "feat(v3-cutover): SQL script skeleton with pre-flight + post-flight + dry-run

Skeleton with parameter declarations, pre-flight assertion blocks for
admin user + Alain branch, post-flight count query, and conditional
COMMIT/ROLLBACK based on :dryRun. Sections 1-4 (data ops) added in
subsequent tasks.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Add Section 1 — Cancel in-flight V2.3 requisitions

**Files:**
- Modify: `docs/cutover/v3-cutover.sql` (add section 1 between pre-flight and section 5)

- [ ] **Step 1: Insert section 1 after the pre-flight `\echo '0. Pre-flight checks PASSED.'` line**

Add this block:

```sql
-- ============================================================================
-- 1. CANCEL IN-FLIGHT V2.3 REQUISITIONS (statuses 1-5)
-- ============================================================================
-- Statuses cancelled: BomPending=1, BomInProgress=2, CostingPending=3,
-- CostingInProgress=4, MdReview=5.
-- Statuses preserved: Draft=0 (V3 has same semantic), Approved=6 (terminal),
-- Rejected=7 (V3 reuses for MD-rejected-from-MdPricing).
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
WHERE q."Status" IN (1, 2, 3, 4, 5);

\echo '1a. Audit rows for in-flight reqs:' :ROW_COUNT

-- 1b. Cancel via direct UPDATE
UPDATE "QuotationRequests"
   SET "Status" = 13,  -- V3 Cancelled
       "CancelReason" = '[V3 cutover ' || :'cutoverDateLabel' || '] Workflow simplified — please re-create in V3 if still needed',
       "CancelledAt" = NOW(),
       "CancelledByUserId" = :adminId,
       "UpdatedAt" = NOW()
 WHERE "Status" IN (1, 2, 3, 4, 5);

\echo '1b. Cancelled in-flight V2.3 reqs:' :ROW_COUNT

```

- [ ] **Step 2: Commit**

```bash
git add docs/cutover/v3-cutover.sql
git commit -m "feat(v3-cutover): section 1 — cancel in-flight V2.3 reqs

Targets statuses 1-5 (BomPending..MdReview); preserves Draft=0,
Approved=6, Rejected=7. Captures BeforeJson per req in AdminAuditLog
before flipping Status=13 + populating CancelReason/At/ByUserId.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Add Section 2 — Deactivate BomCreator users

**Files:**
- Modify: `docs/cutover/v3-cutover.sql` (add section 2 after section 1)

- [ ] **Step 1: Insert section 2 after `\echo '1b. Cancelled in-flight V2.3 reqs:' :ROW_COUNT`**

Add this block:

```sql
-- ============================================================================
-- 2. DEACTIVATE BOMCREATOR USERS
-- ============================================================================
-- Sets IsActive=false + revokes all refresh tokens.
-- Live access tokens (≤15 min TTL) continue to work until expiry; this is
-- acceptable inside the maintenance window. Hard-revocation of access tokens
-- via JTI tracking is a separate enhancement (out of scope per spec).
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
WHERE u."Role" = 2  -- BomCreator
  AND u."IsActive" = true;

\echo '2a. Audit rows for BomCreator users:' :ROW_COUNT

-- 2b. Deactivate the users
UPDATE "Users"
   SET "IsActive" = false
 WHERE "Role" = 2 AND "IsActive" = true;

\echo '2b. Deactivated BomCreator users:' :ROW_COUNT

-- 2c. Revoke their refresh tokens
UPDATE "RefreshTokens"
   SET "RevokedAt" = NOW()
 WHERE "RevokedAt" IS NULL
   AND "UserId" IN (SELECT "Id" FROM "Users" WHERE "Role" = 2 AND "IsActive" = false);

\echo '2c. Revoked BomCreator refresh tokens:' :ROW_COUNT

```

- [ ] **Step 2: Commit**

```bash
git add docs/cutover/v3-cutover.sql
git commit -m "feat(v3-cutover): section 2 — deactivate BomCreator users + revoke tokens

Sets IsActive=false on BomCreator role users (Role=2) and revokes all
their non-revoked RefreshTokens. AdminAuditLog row per user. Access
tokens continue working until 15-min TTL expiry (within maintenance
window); refresh attempts immediately fail post-revocation.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Add Section 3 — Soft-delete non-Alain customers

**Files:**
- Modify: `docs/cutover/v3-cutover.sql` (add section 3 after section 2)

- [ ] **Step 1: Insert section 3 after `\echo '2c. Revoked BomCreator refresh tokens:' :ROW_COUNT`**

Add this block:

```sql
-- ============================================================================
-- 3. SOFT-DELETE NON-ALAIN CUSTOMERS
-- ============================================================================
-- Sets IsDeleted=true on every Customer whose BranchId != alainBranchId.
-- GET /customers in V23c P2 already filters !IsDeleted, so the FE list will
-- automatically hide them. Historical reqs that reference the customer still
-- resolve via FK navigation (anonymize-in-place; row stays present).
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

\echo '3a. Audit rows for non-Alain customers:' :ROW_COUNT

-- 3b. Soft-delete
UPDATE "Customers"
   SET "IsDeleted" = true,
       "DeletedAt" = NOW(),
       "DeletedByUserId" = :adminId
 WHERE "BranchId" != :alainBranchId
   AND "IsDeleted" = false;

\echo '3b. Soft-deleted non-Alain customers:' :ROW_COUNT

```

- [ ] **Step 2: Commit**

```bash
git add docs/cutover/v3-cutover.sql
git commit -m "feat(v3-cutover): section 3 — soft-delete non-Alain customers

Sets Customer.IsDeleted=true + populates DeletedAt/DeletedByUserId for
all customers whose BranchId != Alain. V23c P2 list filter (!IsDeleted)
already hides them in FE. Historical req detail still resolves customer
via FK navigation (anonymize-in-place; row preserved).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Add Section 4 — Deactivate non-Alain items

**Files:**
- Modify: `docs/cutover/v3-cutover.sql` (add section 4 after section 3)

- [ ] **Step 1: Insert section 4 after `\echo '3b. Soft-deleted non-Alain customers:' :ROW_COUNT`**

Add this block:

```sql
-- ============================================================================
-- 4. DEACTIVATE NON-ALAIN ITEMS
-- ============================================================================
-- Sets IsActive=false on every Item whose BranchId != alainBranchId.
-- GET /items filters by IsActive (when includeInactive flag is absent), so
-- FE lists will hide them. Historical BOM lines reference the item via FK;
-- read paths still resolve.
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

\echo '4a. Audit rows for non-Alain items:' :ROW_COUNT

-- 4b. Deactivate
UPDATE "Items"
   SET "IsActive" = false
 WHERE "BranchId" != :alainBranchId
   AND "IsActive" = true;

\echo '4b. Deactivated non-Alain items:' :ROW_COUNT

```

- [ ] **Step 2: Commit**

```bash
git add docs/cutover/v3-cutover.sql
git commit -m "feat(v3-cutover): section 4 — deactivate non-Alain items

Sets Item.IsActive=false for all items whose BranchId != Alain. FE list
filter (includeInactive=false) hides them. Historical BOM lines still
reference items via FK; read paths preserved.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Create the test fixture seed

**Files:**
- Create: `docs/cutover/v3-cutover-fixture.sql`

This fixture seeds a representative pre-cutover state on a fresh DB (or after running existing migrations). Used by the local test script (Task 8) and Neon dry-run rehearsals.

- [ ] **Step 1: Create the fixture file**

Create `docs/cutover/v3-cutover-fixture.sql` with this exact content:

```sql
-- ============================================================================
-- V3 Cutover Fixture — Pre-Cutover State Seeder
-- ============================================================================
-- Seeds representative data for testing the cutover SQL script.
-- Assumes EF migrations have already been applied (tables exist).
-- Idempotent within a single run via NOT EXISTS guards.
-- ============================================================================

BEGIN;

-- 1. Ensure 5 branches exist (Alain + 4 hidden post-cutover)
INSERT INTO "Branches" ("Name", "IsActive")
SELECT name, true
  FROM (VALUES ('Al Ain'), ('Dubai'), ('Sharjah'), ('Fujairah'), ('Abu Dhabi')) AS v(name)
 WHERE NOT EXISTS (SELECT 1 FROM "Branches" b WHERE b."Name" = v.name);

-- Capture Alain ID for the rest of seeding
DO $$
DECLARE
  alain_id INT;
  dubai_id INT;
  admin_id INT;
  sp_id INT;
  bc_id INT;
  cust_alain_id INT;
  cust_dubai_id INT;
BEGIN
  SELECT "Id" INTO alain_id FROM "Branches" WHERE "Name" = 'Al Ain';
  SELECT "Id" INTO dubai_id FROM "Branches" WHERE "Name" = 'Dubai';

  -- 2. Admin user (cutover operator)
  IF NOT EXISTS (SELECT 1 FROM "Users" WHERE "Email" = 'cutover-admin@test.local') THEN
    INSERT INTO "Users" ("Name", "Email", "PasswordHash", "Role", "IsActive", "CreatedAt", "MustChangePassword", "BranchId", "FailedLoginAttempts")
      VALUES ('Cutover Admin', 'cutover-admin@test.local', 'fixture-hash', 0, true, NOW(), false, NULL, 0);
  END IF;
  SELECT "Id" INTO admin_id FROM "Users" WHERE "Email" = 'cutover-admin@test.local';

  -- 3. SalesPerson (existing in V3 — kept active)
  IF NOT EXISTS (SELECT 1 FROM "Users" WHERE "Email" = 'cutover-sp@test.local') THEN
    INSERT INTO "Users" ("Name", "Email", "PasswordHash", "Role", "IsActive", "CreatedAt", "MustChangePassword", "BranchId", "FailedLoginAttempts")
      VALUES ('Cutover Sales', 'cutover-sp@test.local', 'fixture-hash', 1, true, NOW(), false, alain_id, 0);
  END IF;
  SELECT "Id" INTO sp_id FROM "Users" WHERE "Email" = 'cutover-sp@test.local';

  -- 4. BomCreator (deactivated by cutover)
  IF NOT EXISTS (SELECT 1 FROM "Users" WHERE "Email" = 'cutover-bc@test.local') THEN
    INSERT INTO "Users" ("Name", "Email", "PasswordHash", "Role", "IsActive", "CreatedAt", "MustChangePassword", "BranchId", "FailedLoginAttempts")
      VALUES ('Cutover BomCreator', 'cutover-bc@test.local', 'fixture-hash', 2, true, NOW(), false, alain_id, 0);
  END IF;
  SELECT "Id" INTO bc_id FROM "Users" WHERE "Email" = 'cutover-bc@test.local';

  -- 5. Refresh token for the BomCreator (revoked by cutover)
  IF NOT EXISTS (SELECT 1 FROM "RefreshTokens" WHERE "UserId" = bc_id AND "Token" = 'fixture-bc-rt') THEN
    INSERT INTO "RefreshTokens" ("UserId", "Token", "ExpiresAt", "CreatedAt", "RevokedAt")
      VALUES (bc_id, 'fixture-bc-rt', NOW() + INTERVAL '7 days', NOW(), NULL);
  END IF;

  -- 6. Customer in Alain (preserved)
  IF NOT EXISTS (SELECT 1 FROM "Customers" WHERE "Code" = 'CUST-FIXTURE-ALAIN') THEN
    INSERT INTO "Customers" ("Code", "Name", "Address", "Email", "PhoneNumber", "BranchId", "SalesPersonId", "CreatedByUserId", "IsDeleted")
      VALUES ('CUST-FIXTURE-ALAIN', 'Fixture Alain Customer', 'Alain', 'alain@test.local', '+971-1', alain_id, sp_id, admin_id, false);
  END IF;
  SELECT "Id" INTO cust_alain_id FROM "Customers" WHERE "Code" = 'CUST-FIXTURE-ALAIN';

  -- 7. Customer in Dubai (soft-deleted by cutover)
  IF NOT EXISTS (SELECT 1 FROM "Customers" WHERE "Code" = 'CUST-FIXTURE-DUBAI') THEN
    INSERT INTO "Customers" ("Code", "Name", "Address", "Email", "PhoneNumber", "BranchId", "SalesPersonId", "CreatedByUserId", "IsDeleted")
      VALUES ('CUST-FIXTURE-DUBAI', 'Fixture Dubai Customer', 'Dubai', 'dubai@test.local', '+971-2', dubai_id, sp_id, admin_id, false);
  END IF;
  SELECT "Id" INTO cust_dubai_id FROM "Customers" WHERE "Code" = 'CUST-FIXTURE-DUBAI';

  -- 8. Item in Alain (preserved)
  IF NOT EXISTS (SELECT 1 FROM "Items" WHERE "Code" = 'FG-FIXTURE-ALAIN') THEN
    INSERT INTO "Items" ("Code", "Description", "Type", "BranchId", "IsActive")
      VALUES ('FG-FIXTURE-ALAIN', 'Alain FG fixture', 0, alain_id, true);
  END IF;

  -- 9. Item in Dubai (deactivated by cutover)
  IF NOT EXISTS (SELECT 1 FROM "Items" WHERE "Code" = 'FG-FIXTURE-DUBAI') THEN
    INSERT INTO "Items" ("Code", "Description", "Type", "BranchId", "IsActive")
      VALUES ('FG-FIXTURE-DUBAI', 'Dubai FG fixture', 0, dubai_id, true);
  END IF;

  -- 10. In-flight V2.3 req (status=BomPending=1, cancelled by cutover)
  IF NOT EXISTS (SELECT 1 FROM "QuotationRequests" WHERE "RefNo" = 'REQ-FIXTURE-INFLIGHT-1') THEN
    INSERT INTO "QuotationRequests" ("Status", "CustomerId", "SalesPersonId", "BranchId", "CurrencyCode", "CreatedAt", "UpdatedAt")
      VALUES (1, cust_alain_id, sp_id, alain_id, 'AED', NOW(), NOW());
    -- RefNo is computed; if column not auto, manually set:
    UPDATE "QuotationRequests" SET "RefNo" = 'REQ-FIXTURE-INFLIGHT-1'
     WHERE "RefNo" IS NULL OR "RefNo" = '';
  END IF;

  -- 11. In-flight V2.3 req at MdReview=5 (cancelled by cutover)
  IF NOT EXISTS (SELECT 1 FROM "QuotationRequests" WHERE "RefNo" = 'REQ-FIXTURE-INFLIGHT-2') THEN
    INSERT INTO "QuotationRequests" ("Status", "CustomerId", "SalesPersonId", "BranchId", "CurrencyCode", "CreatedAt", "UpdatedAt")
      VALUES (5, cust_alain_id, sp_id, alain_id, 'AED', NOW(), NOW());
    UPDATE "QuotationRequests" SET "RefNo" = 'REQ-FIXTURE-INFLIGHT-2'
     WHERE ("RefNo" IS NULL OR "RefNo" = '') AND "Status" = 5;
  END IF;

  -- 12. Approved V2.3 req (PRESERVED — terminal state)
  IF NOT EXISTS (SELECT 1 FROM "QuotationRequests" WHERE "RefNo" = 'REQ-FIXTURE-APPROVED') THEN
    INSERT INTO "QuotationRequests" ("Status", "CustomerId", "SalesPersonId", "BranchId", "CurrencyCode", "CreatedAt", "UpdatedAt")
      VALUES (6, cust_alain_id, sp_id, alain_id, 'AED', NOW(), NOW());
    UPDATE "QuotationRequests" SET "RefNo" = 'REQ-FIXTURE-APPROVED'
     WHERE ("RefNo" IS NULL OR "RefNo" = '') AND "Status" = 6;
  END IF;

  -- 13. Draft V2.3 req (PRESERVED — V3 has same Draft semantic)
  IF NOT EXISTS (SELECT 1 FROM "QuotationRequests" WHERE "RefNo" = 'REQ-FIXTURE-DRAFT') THEN
    INSERT INTO "QuotationRequests" ("Status", "CustomerId", "SalesPersonId", "BranchId", "CurrencyCode", "CreatedAt", "UpdatedAt")
      VALUES (0, cust_alain_id, sp_id, alain_id, 'AED', NOW(), NOW());
    UPDATE "QuotationRequests" SET "RefNo" = 'REQ-FIXTURE-DRAFT'
     WHERE ("RefNo" IS NULL OR "RefNo" = '') AND "Status" = 0;
  END IF;

  RAISE NOTICE 'Fixture seeded. admin_id=%, sp_id=%, bc_id=%, alain_id=%, dubai_id=%', admin_id, sp_id, bc_id, alain_id, dubai_id;
END $$;

COMMIT;
```

> Note: the `RefNo` column on `QuotationRequest` is documented in CLAUDE.md as a PostgreSQL computed column that formats as `REQ-0001`. The fixture's manual UPDATE on `RefNo` may collide with that — the test script in Task 8 will detect and the fixture can be adapted to read the auto-generated RefNo back from the row instead. If `RefNo` is generated automatically, the `UPDATE ... SET "RefNo" = 'REQ-FIXTURE-...'` lines will fail with a "cannot update generated column" error; remove them and read the actual RefNo via `RETURNING` clause if needed for assertions.

- [ ] **Step 2: Commit**

```bash
git add docs/cutover/v3-cutover-fixture.sql
git commit -m "feat(v3-cutover): add fixture seed for local + Neon dry-run testing

Idempotent fixture seeds 5 branches (Alain + 4 hidden), 1 admin + 1 SP +
1 BomCreator user, 1 BomCreator refresh token, 2 customers (Alain +
Dubai), 2 items (Alain + Dubai), 4 reqs (Draft, BomPending in-flight,
MdReview in-flight, Approved). Used by test-cutover-locally.sh and
manual Neon dry-run rehearsals.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Create the operational README runbook

**Files:**
- Create: `docs/cutover/README.md`

- [ ] **Step 1: Create the runbook**

Create `docs/cutover/README.md` with this content:

```markdown
# V3 Cutover Operational Runbook

This runbook covers the end-to-end V3 cutover process — from pre-cutover prep through post-cutover verification.

**Spec:** [`../superpowers/specs/2026-04-30-v3-phase-c-cutover-design.md`](../superpowers/specs/2026-04-30-v3-phase-c-cutover-design.md)

## Cutover-day timeline

| Step | What | Who | Approx duration |
|------|------|-----|---------|
| 24h before | Send broadcast email to staff | User | — |
| T-1h | Snapshot Neon DB (`pre-v3-cutover-YYYY-MM-DD` branch) | User via Neon Console | 1 min |
| T-30m | Final dry-run on `cutover-dryrun-YYYY-MM-DD` Neon branch | User runs `psql` | 5 min |
| T-0 | Maintenance window banner up on web app | User | — |
| T+0 | Run cutover SQL on prod Neon DB | User | <1 min |
| T+1m | Verify post-flight counts on prod | User | 1 min |
| T+2m | Deploy V3 backend to Fly | User: `flyctl deploy --remote-only` | 3-5 min |
| T+7m | Remove `hold` label + merge PR #32 | User: `gh pr edit 32 --remove-label hold && gh pr merge 32 --squash` | 1 min |
| T+8m | Cloudflare Pages auto-deploys V3 frontend | (automatic) | 1-2 min |
| T+10m | E2E smoke test on prod | User | 5 min |
| T+15m | Maintenance banner down; tag `v3-cutover-YYYY-MM-DD` | User | 1 min |

Total estimated downtime: ~15 minutes.

## Pre-cutover (24h before)

Send a broadcast email/Slack to all staff:

> Subject: V3 quotation workflow goes live on YYYY-MM-DD
>
> The simplified V3 workflow ships on [date] at [time] [timezone]. The system will be unavailable for ~15 minutes during cutover.
>
> What changes:
> - Sales now creates the BOM at the same time as the requisition (no more BOM-creator handoff). The BomCreator role is being retired.
> - MD approval is split into two steps: set the price, then sign+lock after the customer confirms.
> - Only Al Ain branch is in use going forward. Other branches' customers and items will be hidden.
> - Any in-flight requisitions will be auto-cancelled on cutover. If you have an in-flight req that's still important, please re-create it after cutover.
>
> Action required:
> - Sales: complete and submit any in-flight reqs that you can finish before cutover. Anything not submitted will need to be re-created.
> - BomCreators: your role is being retired; your account will be deactivated. Reach out if you need access reassigned.
>
> Questions? Contact [admin email].

## Pre-cutover (T-1h: Neon snapshot)

Create the rollback snapshot:

```bash
# Via Neon Console:
# 1. Open the bom-fpf project
# 2. Navigate to Branches
# 3. Click "Create branch" off main (= prod)
# 4. Name it: pre-v3-cutover-YYYY-MM-DD
# 5. Copy the connection string for rollback access (save in a secure note)
```

This is your nuclear-option rollback. If anything goes catastrophically wrong post-COMMIT, promote this branch to be the new main.

## Pre-cutover (T-30m: Final dry-run)

Create a fresh dry-run branch:

```bash
# Via Neon Console:
# 1. Create branch off main: cutover-dryrun-YYYY-MM-DD
# 2. Copy the connection string into NEON_DRYRUN_URI env var

# Date-stamp the cutover SQL file (operator action — checked into git for history):
cd "/d/shan projects/BOM_Price_Approval"
cp docs/cutover/v3-cutover.sql "docs/cutover/$(date +%Y-%m-%d)-v3-cutover.sql"

# Edit the dated copy: set adminId + alainBranchId from the prod values:
#   - adminId: SELECT "Id" FROM "Users" WHERE "Email" = '<your-admin-email>' AND "Role" = 0
#   - alainBranchId: SELECT "Id" FROM "Branches" WHERE "Name" = 'Al Ain'
#   - cutoverDateLabel: 'YYYY-MM-DD'
#   - dryRun: true (still dry-running)
nano "docs/cutover/$(date +%Y-%m-%d)-v3-cutover.sql"  # or your editor

# Run the dated copy on the dry-run branch:
psql "$NEON_DRYRUN_URI" -f "docs/cutover/$(date +%Y-%m-%d)-v3-cutover.sql"
```

Verify post-flight counts in the output:
- `still_inflight_v2_reqs = 0` (no V2.3 reqs in BomPending..MdReview remain)
- `cutover_cancelled_reqs > 0` — should equal pre-cutover count of in-flight reqs
- `still_active_bomcreator_users = 0`
- `still_visible_nonalain_customers = 0`
- `still_active_nonalain_items = 0`
- `new_audit_rows >= sum of categories` (1 row per cancelled req + 1 per deactivated user + 1 per soft-deleted customer + 1 per deactivated item)

Note: dry-run rolled back, so the dry-run DB itself is unchanged. The output is informational.

If anything looks off, fix the SQL (or the parameter values) and re-run.

## Pre-cutover prod (T-30m to T-0): Flip dry-run to false

```bash
# In the dated copy, change dryRun=true to dryRun=false
sed -i 's/^\\set dryRun true.*$/\\set dryRun false/' "docs/cutover/$(date +%Y-%m-%d)-v3-cutover.sql"

# Verify the flip
grep "^\\\\set dryRun" "docs/cutover/$(date +%Y-%m-%d)-v3-cutover.sql"
# Expected: \set dryRun false

# Commit the dated copy with the operational state used:
git add "docs/cutover/$(date +%Y-%m-%d)-v3-cutover.sql"
git commit -m "docs(cutover): record V3 cutover SQL ran $(date +%Y-%m-%d)"
```

## Cutover (T+0): Run on prod

```bash
# Capture stdout + stderr to a log
psql "$NEON_PROD_URI" -f "docs/cutover/$(date +%Y-%m-%d)-v3-cutover.sql" 2>&1 | tee "docs/cutover/$(date +%Y-%m-%d)-cutover.log"

# Inspect the log:
grep -E "^(===|[0-9]\.|ERROR|EXCEPTION)" "docs/cutover/$(date +%Y-%m-%d)-cutover.log"
```

If you see `EXCEPTION` or `ERROR`, the transaction auto-rolls back. Investigate, fix, re-run.

## Verify (T+1m): Post-flight on prod

Re-run the post-flight count query as a smoke check (single-line; safe outside the transaction):

```bash
psql "$NEON_PROD_URI" <<'EOF'
SELECT
  (SELECT COUNT(*) FROM "QuotationRequests" WHERE "Status" IN (1,2,3,4,5)) AS still_inflight_v2_reqs,
  (SELECT COUNT(*) FROM "QuotationRequests" WHERE "Status" = 13 AND "CancelReason" LIKE '%V3 cutover%') AS cutover_cancelled_reqs,
  (SELECT COUNT(*) FROM "Users" WHERE "Role" = 2 AND "IsActive" = true) AS still_active_bomcreator_users,
  (SELECT COUNT(*) FROM "Customers" WHERE "BranchId" != (SELECT "Id" FROM "Branches" WHERE "Name" = 'Al Ain') AND "IsDeleted" = false) AS still_visible_nonalain_customers,
  (SELECT COUNT(*) FROM "Items" WHERE "BranchId" != (SELECT "Id" FROM "Branches" WHERE "Name" = 'Al Ain') AND "IsActive" = true) AS still_active_nonalain_items,
  (SELECT COUNT(*) FROM "AdminAuditLog" WHERE "ActionType" = 'V3CutoverMigration' AND "CreatedAt" >= NOW() - INTERVAL '5 minutes') AS recent_cutover_audit_rows;
EOF
```

Expected: first 5 columns are 0; last 2 are positive integers.

## Deploy V3 backend (T+2m)

```bash
cd "/d/shan projects/BOM_Price_Approval/BomPriceApproval.API"
flyctl deploy --remote-only

# Watch healthcheck:
flyctl status -a bom-fpf-api --watch
```

Wait until at least one machine reports `started + healthy`. Verify the API responds:

```bash
curl -s https://bom-fpf-api.fly.dev/swagger/index.html >/dev/null && echo "API LIVE" || echo "API DOWN"
```

## Merge PR #32 (T+7m)

```bash
# Verify CI on PR #32 is still green
gh pr checks 32

# Remove the hold label
gh pr edit 32 --remove-label hold

# Merge — squash, since the PR has 23 commits
gh pr merge 32 --squash
```

Cloudflare Pages will auto-deploy the V3 frontend to https://bom-fpf.shannaqvi90-ux.workers.dev (or your prod domain). Watch the deploy:

```bash
gh run list --limit 5  # if a deploy GH Action exists
# OR Cloudflare Pages dashboard: https://dash.cloudflare.com/.../pages/view/bom-fpf
```

Wait until the green-checkmark deploy.

## E2E smoke (T+10m)

Open prod in a fresh incognito window. Test:

1. Login as an Alain SalesPerson.
2. Navigate to "Customers" — verify only Alain customers visible.
3. Navigate to "Items" — verify only Alain items visible.
4. Navigate to "Requisitions" — verify in-flight V2.3 reqs show as **Cancelled** with the `[V3 cutover ...]` reason.
5. Click "+ New Requisition" → fill customer + currency + 1 FG card with 1 BOM line → Submit. Verify it transitions to **Costing**.
6. Logout, login as Accountant. Verify the new req is in your Pending Costing queue.
7. Click into the req → Edit BOM & Costing → Submit. Verify it transitions to **MdPricing**.
8. Logout, login as MD. Verify the req is in Awaiting Pricing queue.
9. Click "Set Margin" → enter a positive number → Submit. Verify it transitions to **CustomerConfirm**.
10. Logout, login as the original SalesPerson. Click "Confirm with Customer" → "Customer Accepted". Verify it transitions to **MdFinalSign**.
11. Logout, login as MD. Click "Sign Final" → type SIGN → Submit. Verify it transitions to **Signed**.
12. Verify "Download PDF" works.
13. Verify the Cancelled in-flight reqs from step 4 are still visible in the list view (just frozen at Cancelled).

If any step fails, see the **Rollback** section below.

## Tag (T+15m)

```bash
cd "/d/shan projects/BOM_Price_Approval"
git fetch origin
NEW_MASTER_SHA=$(git rev-parse origin/master)
git tag "v3-cutover-$(date +%Y-%m-%d)" "$NEW_MASTER_SHA" -m "V3 cutover ran $(date +%Y-%m-%d)"
git push origin "v3-cutover-$(date +%Y-%m-%d)"
```

## Rollback procedure

If anything goes catastrophically wrong post-COMMIT:

### Database rollback (Neon)

```bash
# Via Neon Console:
# 1. Open the bom-fpf project
# 2. Branches → pre-v3-cutover-YYYY-MM-DD
# 3. Click "Promote to main" (instant; takes <30 sec)
# 4. Verify the prod connection string now points to the promoted branch
```

After DB rollback, the prod state is exactly pre-cutover. V2.3 backend + V2.3 frontend will resume working.

### Backend rollback (Fly)

```bash
flyctl releases -a bom-fpf-api
# Identify the previous release ID (the one before the V3 deploy)
flyctl releases rollback <id> -a bom-fpf-api
```

### Frontend rollback (Cloudflare Pages)

Cloudflare Pages auto-rolls if you redeploy from the prior master commit:

```bash
# Find the merge commit for PR #32 in master
PR32_MERGE=$(git log master --grep "Phase B" --pretty=%H | head -1)

# The commit BEFORE that merge:
PRIOR=$(git log $PR32_MERGE^ --pretty=%H | head -1)

# Force Cloudflare to redeploy from PRIOR:
# Option A: Cloudflare Pages dashboard → Deployments → "Rollback to this deployment"
# Option B: git revert on master + push (creates a new commit + auto-deploy)
git revert -m 1 $PR32_MERGE
git push origin master  # creates a new master commit; Cloudflare deploys it
```

(Note: `git push origin master` is hook-blocked per CLAUDE.md; use `gh pr create` + auto-merge for the revert.)

### What if cutover SQL ran but PR #32 wasn't merged?

The DB has V3 statuses (Cancelled=13 etc.) which V2.3 backend doesn't know. V2.3 backend reading those rows will likely:
- Cast the int to enum (succeeds in C#); status `.ToString()` returns "13" string; FE renders as raw "13" or empty styling.
- Cancelled reqs become visually weird but reads don't crash.

This is acceptable for a brief window. To recover cleanly:
- Either complete the deploy (deploy V3 backend + merge PR #32) ASAP, OR
- Promote the Neon snapshot back to main (full rollback), then start over later.

## After cutover

- Monitor Sentry/logs for unexpected errors during the first hour.
- Check support email/Slack for user reports of broken behavior.
- Confirm the maintenance banner is removed.
- Update memory file `project_v3_phase_b_pr_open.md` to "MERGED + cutover complete".
```

- [ ] **Step 2: Commit**

```bash
git add docs/cutover/README.md
git commit -m "feat(v3-cutover): add operational runbook

Step-by-step cutover-day procedure: Neon snapshot, dry-run, prod run,
backend deploy, PR #32 merge, E2E smoke, tag, rollback playbook.
Estimated downtime ~15 minutes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: Create local test script

**Files:**
- Create: `docs/cutover/test-cutover-locally.sh`

This bash script lets us validate the cutover SQL end-to-end against a local Postgres instance before trusting it on Neon. It:

1. Creates a throwaway DB.
2. Runs all EF migrations against it (so tables exist with current schema).
3. Seeds the fixture.
4. Runs cutover with `dryRun=true` and asserts post-flight counts.
5. Runs cutover with `dryRun=false` and asserts post-flight counts.
6. Drops the DB.

- [ ] **Step 1: Create the script**

Create `docs/cutover/test-cutover-locally.sh` with this content:

```bash
#!/usr/bin/env bash
# ============================================================================
# V3 Cutover — Local Test Script
# ============================================================================
# Validates the cutover SQL against a fresh local Postgres DB.
#
# Prerequisites:
#   - Local Postgres running on port 5433 with superuser 'postgres'
#   - PG password in env var PG_PASSWORD
#   - dotnet-ef installed (dotnet tool install -g dotnet-ef)
#   - Run from repo root
#
# Usage:
#   PG_PASSWORD='your-password' ./docs/cutover/test-cutover-locally.sh
# ============================================================================

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DB_NAME="bom_cutover_test_$$"  # PID-suffixed; throwaway
PG_HOST="localhost"
PG_PORT="5433"
PG_USER="postgres"

if [[ -z "${PG_PASSWORD:-}" ]]; then
  echo "ERROR: set PG_PASSWORD env var" >&2
  exit 1
fi

export PGPASSWORD="$PG_PASSWORD"

trap 'cleanup' EXIT

cleanup() {
  echo ""
  echo "=== CLEANUP ==="
  psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d postgres -c "DROP DATABASE IF EXISTS \"$DB_NAME\" WITH (FORCE);" >/dev/null 2>&1 || true
  echo "Dropped DB: $DB_NAME"
}

echo "=== CREATE DB: $DB_NAME ==="
psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d postgres -c "CREATE DATABASE \"$DB_NAME\";"

CONN_STRING="Host=$PG_HOST;Port=$PG_PORT;Database=$DB_NAME;Username=$PG_USER;Password=$PG_PASSWORD"

echo ""
echo "=== APPLY EF MIGRATIONS ==="
cd "$REPO_ROOT"
dotnet ef database update \
  --project BomPriceApproval.API \
  --connection "$CONN_STRING" \
  --no-build \
  --verbose 2>&1 | tail -10

PSQL_DB="psql -h $PG_HOST -p $PG_PORT -U $PG_USER -d $DB_NAME"

echo ""
echo "=== SEED FIXTURE ==="
$PSQL_DB -f "$REPO_ROOT/docs/cutover/v3-cutover-fixture.sql"

echo ""
echo "=== ASSERT FIXTURE STATE ==="
PRE_INFLIGHT=$($PSQL_DB -t -c 'SELECT COUNT(*) FROM "QuotationRequests" WHERE "Status" IN (1,2,3,4,5);' | tr -d ' ')
PRE_BC=$($PSQL_DB -t -c 'SELECT COUNT(*) FROM "Users" WHERE "Role" = 2 AND "IsActive" = true;' | tr -d ' ')
PRE_NONALAIN_CUST=$($PSQL_DB -t -c 'SELECT COUNT(*) FROM "Customers" c JOIN "Branches" b ON c."BranchId" = b."Id" WHERE b."Name" != '"'"'Al Ain'"'"' AND c."IsDeleted" = false;' | tr -d ' ')
PRE_NONALAIN_ITEM=$($PSQL_DB -t -c 'SELECT COUNT(*) FROM "Items" i JOIN "Branches" b ON i."BranchId" = b."Id" WHERE b."Name" != '"'"'Al Ain'"'"' AND i."IsActive" = true;' | tr -d ' ')

echo "Pre-cutover counts:"
echo "  in-flight V2.3 reqs:        $PRE_INFLIGHT (expected >= 2 from fixture)"
echo "  active BomCreator users:    $PRE_BC (expected >= 1)"
echo "  visible non-Alain customers: $PRE_NONALAIN_CUST (expected >= 1)"
echo "  active non-Alain items:     $PRE_NONALAIN_ITEM (expected >= 1)"

if [[ "$PRE_INFLIGHT" -lt 2 || "$PRE_BC" -lt 1 || "$PRE_NONALAIN_CUST" -lt 1 || "$PRE_NONALAIN_ITEM" -lt 1 ]]; then
  echo "ERROR: fixture did not seed enough data" >&2
  exit 1
fi

ADMIN_ID=$($PSQL_DB -t -c "SELECT \"Id\" FROM \"Users\" WHERE \"Email\" = 'cutover-admin@test.local';" | tr -d ' ')
ALAIN_ID=$($PSQL_DB -t -c "SELECT \"Id\" FROM \"Branches\" WHERE \"Name\" = 'Al Ain';" | tr -d ' ')
echo "  admin user id:    $ADMIN_ID"
echo "  Alain branch id:  $ALAIN_ID"

echo ""
echo "=== DRY-RUN CUTOVER ==="
DRYRUN_SQL=$(mktemp)
sed \
  -e "s/^\\\\set adminId .*$/\\\\set adminId $ADMIN_ID/" \
  -e "s/^\\\\set alainBranchId .*$/\\\\set alainBranchId $ALAIN_ID/" \
  -e "s/^\\\\set dryRun .*$/\\\\set dryRun true/" \
  "$REPO_ROOT/docs/cutover/v3-cutover.sql" > "$DRYRUN_SQL"

$PSQL_DB -f "$DRYRUN_SQL" 2>&1 | tee /tmp/cutover-dryrun.log
rm -f "$DRYRUN_SQL"

# Verify dry-run rolled back: counts should match pre-cutover
POST_DRY_INFLIGHT=$($PSQL_DB -t -c 'SELECT COUNT(*) FROM "QuotationRequests" WHERE "Status" IN (1,2,3,4,5);' | tr -d ' ')
if [[ "$POST_DRY_INFLIGHT" != "$PRE_INFLIGHT" ]]; then
  echo "ERROR: dry-run did not roll back; in-flight count changed from $PRE_INFLIGHT to $POST_DRY_INFLIGHT" >&2
  exit 1
fi
echo "✅ Dry-run rolled back correctly."

echo ""
echo "=== PROD-RUN CUTOVER ==="
PRODRUN_SQL=$(mktemp)
sed \
  -e "s/^\\\\set adminId .*$/\\\\set adminId $ADMIN_ID/" \
  -e "s/^\\\\set alainBranchId .*$/\\\\set alainBranchId $ALAIN_ID/" \
  -e "s/^\\\\set dryRun .*$/\\\\set dryRun false/" \
  "$REPO_ROOT/docs/cutover/v3-cutover.sql" > "$PRODRUN_SQL"

$PSQL_DB -f "$PRODRUN_SQL" 2>&1 | tee /tmp/cutover-prodrun.log
rm -f "$PRODRUN_SQL"

echo ""
echo "=== ASSERT POST-CUTOVER STATE ==="
POST_INFLIGHT=$($PSQL_DB -t -c 'SELECT COUNT(*) FROM "QuotationRequests" WHERE "Status" IN (1,2,3,4,5);' | tr -d ' ')
POST_CANCELLED=$($PSQL_DB -t -c "SELECT COUNT(*) FROM \"QuotationRequests\" WHERE \"Status\" = 13 AND \"CancelReason\" LIKE '%V3 cutover%';" | tr -d ' ')
POST_BC=$($PSQL_DB -t -c 'SELECT COUNT(*) FROM "Users" WHERE "Role" = 2 AND "IsActive" = true;' | tr -d ' ')
POST_NONALAIN_CUST=$($PSQL_DB -t -c "SELECT COUNT(*) FROM \"Customers\" c JOIN \"Branches\" b ON c.\"BranchId\" = b.\"Id\" WHERE b.\"Name\" != 'Al Ain' AND c.\"IsDeleted\" = false;" | tr -d ' ')
POST_NONALAIN_ITEM=$($PSQL_DB -t -c "SELECT COUNT(*) FROM \"Items\" i JOIN \"Branches\" b ON i.\"BranchId\" = b.\"Id\" WHERE b.\"Name\" != 'Al Ain' AND i.\"IsActive\" = true;" | tr -d ' ')
POST_AUDIT=$($PSQL_DB -t -c "SELECT COUNT(*) FROM \"AdminAuditLog\" WHERE \"ActionType\" = 'V3CutoverMigration';" | tr -d ' ')

# Assertions:
# - inflight reqs == 0
# - cancelled reqs >= PRE_INFLIGHT
# - active BomCreator users == 0
# - visible non-Alain customers == 0
# - active non-Alain items == 0
# - audit rows >= PRE_INFLIGHT + PRE_BC + PRE_NONALAIN_CUST + PRE_NONALAIN_ITEM

EXPECTED_AUDIT=$((PRE_INFLIGHT + PRE_BC + PRE_NONALAIN_CUST + PRE_NONALAIN_ITEM))

declare -A CHECKS=(
  ["in-flight V2.3 reqs (expected 0)"]="$POST_INFLIGHT|0|eq"
  ["cancelled V3 reqs (expected >= $PRE_INFLIGHT)"]="$POST_CANCELLED|$PRE_INFLIGHT|ge"
  ["active BomCreator users (expected 0)"]="$POST_BC|0|eq"
  ["visible non-Alain customers (expected 0)"]="$POST_NONALAIN_CUST|0|eq"
  ["active non-Alain items (expected 0)"]="$POST_NONALAIN_ITEM|0|eq"
  ["audit rows (expected >= $EXPECTED_AUDIT)"]="$POST_AUDIT|$EXPECTED_AUDIT|ge"
)

FAILED=0
for label in "${!CHECKS[@]}"; do
  IFS='|' read -r actual expected op <<< "${CHECKS[$label]}"
  if [[ "$op" == "eq" ]]; then
    if [[ "$actual" -ne "$expected" ]]; then
      echo "❌ $label — got $actual"
      FAILED=$((FAILED + 1))
    else
      echo "✅ $label — got $actual"
    fi
  else  # ge
    if [[ "$actual" -lt "$expected" ]]; then
      echo "❌ $label — got $actual"
      FAILED=$((FAILED + 1))
    else
      echo "✅ $label — got $actual"
    fi
  fi
done

if [[ "$FAILED" -gt 0 ]]; then
  echo ""
  echo "=== $FAILED check(s) FAILED ==="
  exit 1
fi

echo ""
echo "=== ALL CHECKS PASSED ==="
exit 0
```

- [ ] **Step 2: Make the script executable**

```bash
chmod +x docs/cutover/test-cutover-locally.sh
```

- [ ] **Step 3: Commit**

```bash
git add docs/cutover/test-cutover-locally.sh
git commit -m "feat(v3-cutover): local end-to-end test script

Bash script creates a throwaway Postgres DB, applies EF migrations,
seeds fixture, runs cutover SQL with dryRun=true (asserts rollback),
runs again with dryRun=false (asserts post-flight state matches
expectations). Uses dynamic adminId/alainBranchId via sed substitution
into a temp copy of v3-cutover.sql.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: Run the local test

**Files:**
- None modified (verification only)

- [ ] **Step 1: Verify Postgres is running on port 5433**

```bash
cd "/d/shan projects/BOM_Price_Approval"
psql -h localhost -p 5433 -U postgres -d postgres -c "SELECT 1;" 2>&1 | tail -3
```

Expected: `(1 row)` indicating connectivity.

If it fails: start your local Postgres + retry. Local postgres password is in `dotnet user-secrets list --project BomPriceApproval.API` under `ConnectionStrings:DefaultConnection`.

- [ ] **Step 2: Run the test**

```bash
PG_PASSWORD='<password-from-user-secrets>' ./docs/cutover/test-cutover-locally.sh
```

Expected output ends with:

```
=== ALL CHECKS PASSED ===
```

If checks fail: read the output, identify which check failed, fix the SQL or fixture, re-run.

If you see a "cannot update generated column" error from the fixture (for the `RefNo` column), edit the fixture to remove the `UPDATE ... SET "RefNo" = ...` lines (the auto-generated `REQ-NNNN` is fine for the test).

- [ ] **Step 3: No commit (test-only)**

If the test made any source changes (e.g., fixing the fixture), commit those:

```bash
git add docs/cutover/v3-cutover-fixture.sql  # if changed
git commit -m "fix(v3-cutover): adjust fixture to match RefNo computed-column constraint

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

If no changes were needed, this task ends with no commit.

---

## Task 10: Push branch + open PR

**Files:**
- None modified

- [ ] **Step 1: Verify branch state**

```bash
cd "/d/shan projects/BOM_Price_Approval"
git log --oneline master..HEAD | head -15
```

Expected: 7-9 commits — Phase C spec doc + Tasks 1-8 commits + maybe a Task 9 fix.

- [ ] **Step 2: Push the branch**

```bash
git push -u origin feat/v3-phase-c-cutover
```

- [ ] **Step 3: Open the PR**

Write the PR body to `.pr-body-c.md` (don't try to inline a heredoc — bash is fragile with multi-line):

```bash
cat > .pr-body-c.md <<'EOF'
## Summary

V3 Phase C — production cutover SQL script + operational runbook + local test harness. Prepares production for V3 frontend deploy by cancelling in-flight V2.3 reqs, deactivating BomCreator users, and hiding non-Alain branches' customers/items.

**This PR does NOT execute on production.** It ships the *artifacts* needed for cutover day. The actual run happens on a scheduled date (TBD), executed by the user against Neon prod via the runbook in `docs/cutover/README.md`.

**Spec:** [`docs/superpowers/specs/2026-04-30-v3-phase-c-cutover-design.md`](docs/superpowers/specs/2026-04-30-v3-phase-c-cutover-design.md)
**Plan:** [`docs/superpowers/plans/2026-04-30-v3-phase-c-cutover.md`](docs/superpowers/plans/2026-04-30-v3-phase-c-cutover.md)

## What ships

- `docs/cutover/v3-cutover.sql` — idempotent cutover script with `\set dryRun` flag, pre-flight assertions (admin user + Alain branch), four data-op sections (cancel V2.3 reqs / deactivate BomCreator / soft-delete non-Alain customers / deactivate non-Alain items), per-entity AdminAuditLog rows, post-flight count verification, conditional COMMIT/ROLLBACK.
- `docs/cutover/v3-cutover-fixture.sql` — fixture seed for testing (5 branches, admin + sales + bomcreator users, refresh token, customers + items in Alain + Dubai, 4 reqs in different statuses).
- `docs/cutover/test-cutover-locally.sh` — bash test harness; spins up a throwaway Postgres DB, applies EF migrations, seeds fixture, runs cutover dry-run + prod-run, asserts post-flight state.
- `docs/cutover/README.md` — operational runbook with cutover-day timeline, Neon snapshot procedure, dry-run procedure, prod-run procedure, post-flight verification, E2E smoke checklist, rollback playbook.

## Locked design decisions (12 of them — see spec for full table)

1. Idempotent SQL script via `psql -f` (not EF migration, not admin endpoint, not C# tool)
2. In-flight cancellation: direct UPDATE `Status=13` (V3 Cancelled) + populate `CancelReason`/`At`/`ByUserId`. Targets statuses 1-5 (BomPending..MdReview). Preserves Draft=0, Approved=6, Rejected=7.
3. Non-Alain hiding: soft-delete customers + deactivate items. Branches table untouched.
4. BomCreator deactivation: `IsActive=false` + revoke refresh tokens. Live access tokens (≤15 min TTL) work until expiry — acceptable inside maintenance window.
5. Audit granularity: per-entity rows. ~150 rows total expected.
6. AdminUserId for audit: the human admin running the script.
7. V2.3 Approved + Rejected reqs: untouched.
8. Atomicity: single `BEGIN/COMMIT`.
9. Dry-run support: `\set dryRun true` flag swaps COMMIT for ROLLBACK.
10. Pre-cutover safety: Neon DB branch snapshot.
11. No per-entity notification fan-out — manual broadcast email/Slack 24h before cutover.
12. PWA cache invalidation: already done in Phase B (Task 21 bumped suffix to `-v3`).

## Test plan

- [x] `docs/cutover/test-cutover-locally.sh` runs end-to-end against local Postgres
- [x] Dry-run rolls back cleanly (verified by post-dry-run count = pre-fixture count)
- [x] Prod-run leaves expected state (in-flight=0, BomCreator=0, non-Alain customers=0, non-Alain items=0, audit rows >= sum of categories)
- [ ] Final dry-run on Neon DB branch (cutover day, in runbook)
- [ ] Prod run on Neon main (cutover day, in runbook)

## Out of scope

- Phase B PR #32 has a `hold` label and merges only on cutover day in lockstep.
- Phase D mobile cutover preserved as separate work.
- V2.3 schema cleanup (legacy DTOs, V2.3 hooks, etc.) — future tech-debt PR.

## Merge timing

This PR can be merged anytime — it adds artifacts only, doesn't change runtime behavior. Suggest merging before cutover day so the date-stamped copy can be added on cutover day with a follow-up tiny PR.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF

gh pr create --base master --head feat/v3-phase-c-cutover \
  --title "feat(v3): Phase C cutover SQL + runbook + local test harness" \
  --body-file .pr-body-c.md

rm -f .pr-body-c.md
```

- [ ] **Step 4: Verify PR open**

```bash
gh pr view --web 2>&1 | tail -1   # opens browser to the new PR
gh pr view --json number,url,state,baseRefName,headRefName | head -5
```

Expected: state=OPEN, baseRefName=master, headRefName=feat/v3-phase-c-cutover.

- [ ] **Step 5: No commit needed (PR creation is the deliverable)**

---

## Self-review

After all 10 tasks complete, the implementer should:

1. **Spec coverage check:** every locked decision in the spec maps to a task:
   - Decision 1 (SQL script) → Tasks 1-5
   - Decision 2 (cancel statuses 1-5) → Task 2
   - Decision 3 (non-Alain hiding) → Tasks 4-5
   - Decision 4 (BomCreator deactivate) → Task 3
   - Decision 5 (per-entity audit) → Tasks 2-5 (each section adds audit rows)
   - Decision 6 (admin userId) → Task 1 (parameter)
   - Decision 7 (preserve Approved/Rejected) → Task 2 (statuses 1-5 only)
   - Decision 8 (atomic transaction) → Task 1 (BEGIN/COMMIT)
   - Decision 9 (dry-run flag) → Task 1
   - Decision 10 (Neon snapshot) → Task 7 (runbook)
   - Decision 11 (no notification fan-out) → Task 7 (runbook documents broadcast email)
   - Decision 12 (PWA cache already done) → out of scope

2. **Placeholder scan:** `grep -n "TBD\|TODO\|XXXX" docs/cutover/` should return only `2026-MM-DD` placeholders (intentional; filled cutover day) and the README's "TBD" for the cutover date.

3. **Type consistency:** the SQL script's column names match `BomPriceApproval.API/Domain/Entities/*.cs` PascalCase (e.g., `"Status"`, `"IsActive"`, `"BranchId"`).

---

## Post-execution offerings

After all 10 tasks pass:

- **PR #33 (this PR) ready for merge.** Suggest merging anytime; no production impact.
- **PR #32 stays on hold.** Removed only on cutover day.
- **Schedule cutover day.** Pick a date with the user; send broadcast email 24h before.
- **Update memory file** `project_v3_phase_b_pr_open.md` to note Phase C artifacts shipped.
