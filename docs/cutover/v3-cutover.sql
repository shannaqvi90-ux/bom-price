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

-- ============================================================================
-- (sections 3-4 added in subsequent tasks)
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
