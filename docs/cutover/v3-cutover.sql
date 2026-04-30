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
-- NOTE: psql `:variable` substitution does NOT work inside DO $$ ... $$ blocks
-- (psql leaves dollar-quoted strings unparsed), so pre-flight checks use plain
-- SQL with \gset + \if for assertions.
SELECT COUNT(*) = 1 AS admin_ok FROM "Users"
 WHERE "Id" = :adminId AND "Role" = 0 AND "IsActive" = true \gset

\if :admin_ok
\else
  \echo 'ERROR: Pre-flight failed — admin user not found or not active'
  \quit
\endif

-- Alain branch exists with Name='Al Ain' and IsActive=true.
SELECT COUNT(*) = 1 AS alain_ok FROM "Branches"
 WHERE "Id" = :alainBranchId AND "Name" = 'Al Ain' AND "IsActive" = true \gset

\if :alain_ok
\else
  \echo 'ERROR: Pre-flight failed — Alain branch not found or inactive'
  \quit
\endif

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
INSERT INTO "AdminAuditLogs"
  ("AdminUserId", "ActionType", "EntityType", "EntityId", "Reason", "BeforeJson", "CreatedAt")
SELECT
  :adminId,
  'V3CutoverMigration',
  'Requisition',
  q."Id",
  '[V3 cutover ' || :'cutoverDateLabel' || '] Workflow simplified — original V2.3 status preserved in BeforeJson',
  jsonb_build_object(
    'id', q."Id",
    'refNo', q."RefNo",
    'status', q."Status",
    'customerId', q."CustomerId",
    'salesPersonId', q."SalesPersonId",
    'updatedAt', q."UpdatedAt"
  ),
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
INSERT INTO "AdminAuditLogs"
  ("AdminUserId", "ActionType", "EntityType", "EntityId", "Reason", "BeforeJson", "CreatedAt")
SELECT
  :adminId,
  'V3CutoverMigration',
  'User',
  u."Id",
  '[V3 cutover ' || :'cutoverDateLabel' || '] BomCreator role deprecated — user deactivated per V3 design Q12',
  jsonb_build_object(
    'id', u."Id",
    'name', u."Name",
    'email', u."Email",
    'role', u."Role",
    'isActive', u."IsActive"
  ),
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
   SET "IsRevoked" = true
 WHERE "IsRevoked" = false
   AND "UserId" IN (SELECT "Id" FROM "Users" WHERE "Role" = 2 AND "IsActive" = false);

\echo '2c. Revoked BomCreator refresh tokens:' :ROW_COUNT

-- ============================================================================
-- 3. SOFT-DELETE NON-ALAIN CUSTOMERS
-- ============================================================================
-- Sets IsDeleted=true on every Customer whose BranchId != alainBranchId.
-- GET /customers in V23c P2 already filters !IsDeleted, so the FE list will
-- automatically hide them. Historical reqs that reference the customer still
-- resolve via FK navigation (anonymize-in-place; row stays present).
-- ============================================================================

-- 3a. Audit rows
INSERT INTO "AdminAuditLogs"
  ("AdminUserId", "ActionType", "EntityType", "EntityId", "Reason", "BeforeJson", "CreatedAt")
SELECT
  :adminId,
  'V3CutoverMigration',
  'Customer',
  c."Id",
  '[V3 cutover ' || :'cutoverDateLabel' || '] Customer in non-Alain branch hidden per V3 design Q13',
  jsonb_build_object(
    'id', c."Id",
    'code', c."Code",
    'name', c."Name",
    'branchId', c."BranchId",
    'isDeleted', c."IsDeleted",
    'salesPersonId', c."SalesPersonId"
  ),
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

-- ============================================================================
-- 4. DEACTIVATE NON-ALAIN ITEMS
-- ============================================================================
-- Sets IsActive=false on every Item whose BranchId != alainBranchId.
-- GET /items filters by IsActive (when includeInactive flag is absent), so
-- FE lists will hide them. Historical BOM lines reference the item via FK;
-- read paths still resolve.
-- ============================================================================

-- 4a. Audit rows
INSERT INTO "AdminAuditLogs"
  ("AdminUserId", "ActionType", "EntityType", "EntityId", "Reason", "BeforeJson", "CreatedAt")
SELECT
  :adminId,
  'V3CutoverMigration',
  'Item',
  i."Id",
  '[V3 cutover ' || :'cutoverDateLabel' || '] Item in non-Alain branch deactivated per V3 design Q13',
  jsonb_build_object(
    'id', i."Id",
    'code', i."Code",
    'description', i."Description",
    'type', i."Type",
    'branchId', i."BranchId",
    'isActive', i."IsActive"
  ),
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
  (SELECT COUNT(*) FROM "AdminAuditLogs" WHERE "ActionType" = 'V3CutoverMigration' AND "CreatedAt" >= NOW() - INTERVAL '1 minute') AS new_audit_rows;

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
