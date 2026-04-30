-- ============================================================================
-- V3 Cutover Fixture — Pre-Cutover State Seeder
-- ============================================================================
-- Seeds representative data for testing the cutover SQL script.
-- Assumes EF migrations have already been applied (tables exist).
-- Idempotent within a single run via NOT EXISTS guards.
--
-- NOTE: QuotationRequests."RefNo" is a stored computed column
-- ('REQ-' || LPAD("Id"::text, 4, '0')) — it CANNOT be set manually.
-- The fixture uses the "Notes" field with a 'fixture-marker-...' sentinel
-- string for idempotency checks instead.
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
    INSERT INTO "RefreshTokens" ("UserId", "Token", "ExpiresAt", "IsRevoked")
      VALUES (bc_id, 'fixture-bc-rt', NOW() + INTERVAL '7 days', false);
  END IF;

  -- 6. Customer in Alain (preserved)
  IF NOT EXISTS (SELECT 1 FROM "Customers" WHERE "Code" = 'CUST-FIXTURE-ALAIN') THEN
    INSERT INTO "Customers" ("Code", "Name", "Address", "Email", "PhoneNumber", "BranchId", "SalesPersonId", "CreatedByUserId", "CreatedAt", "IsDeleted")
      VALUES ('CUST-FIXTURE-ALAIN', 'Fixture Alain Customer', 'Alain', 'alain@test.local', '+971-1', alain_id, sp_id, admin_id, NOW(), false);
  END IF;
  SELECT "Id" INTO cust_alain_id FROM "Customers" WHERE "Code" = 'CUST-FIXTURE-ALAIN';

  -- 7. Customer in Dubai (soft-deleted by cutover)
  IF NOT EXISTS (SELECT 1 FROM "Customers" WHERE "Code" = 'CUST-FIXTURE-DUBAI') THEN
    INSERT INTO "Customers" ("Code", "Name", "Address", "Email", "PhoneNumber", "BranchId", "SalesPersonId", "CreatedByUserId", "CreatedAt", "IsDeleted")
      VALUES ('CUST-FIXTURE-DUBAI', 'Fixture Dubai Customer', 'Dubai', 'dubai@test.local', '+971-2', dubai_id, sp_id, admin_id, NOW(), false);
  END IF;
  SELECT "Id" INTO cust_dubai_id FROM "Customers" WHERE "Code" = 'CUST-FIXTURE-DUBAI';

  -- 8. Item in Alain (preserved)
  IF NOT EXISTS (SELECT 1 FROM "Items" WHERE "Code" = 'FG-FIXTURE-ALAIN') THEN
    INSERT INTO "Items" ("Code", "Description", "Type", "BranchId", "IsActive", "CreatedAt")
      VALUES ('FG-FIXTURE-ALAIN', 'Alain FG fixture', 0, alain_id, true, NOW());
  END IF;

  -- 9. Item in Dubai (deactivated by cutover)
  IF NOT EXISTS (SELECT 1 FROM "Items" WHERE "Code" = 'FG-FIXTURE-DUBAI') THEN
    INSERT INTO "Items" ("Code", "Description", "Type", "BranchId", "IsActive", "CreatedAt")
      VALUES ('FG-FIXTURE-DUBAI', 'Dubai FG fixture', 0, dubai_id, true, NOW());
  END IF;

  -- 10. In-flight V2.3 req (status=BomPending=1, cancelled by cutover)
  -- Idempotency via Notes sentinel since RefNo is computed (cannot set manually).
  IF NOT EXISTS (SELECT 1 FROM "QuotationRequests" WHERE "Notes" = 'fixture-marker-inflight-1') THEN
    INSERT INTO "QuotationRequests" ("Status", "CustomerId", "SalesPersonId", "BranchId", "CurrencyCode", "Notes", "CreatedAt", "UpdatedAt")
      VALUES (1, cust_alain_id, sp_id, alain_id, 'AED', 'fixture-marker-inflight-1', NOW(), NOW());
  END IF;

  -- 11. In-flight V2.3 req at MdReview=5 (cancelled by cutover)
  IF NOT EXISTS (SELECT 1 FROM "QuotationRequests" WHERE "Notes" = 'fixture-marker-inflight-2') THEN
    INSERT INTO "QuotationRequests" ("Status", "CustomerId", "SalesPersonId", "BranchId", "CurrencyCode", "Notes", "CreatedAt", "UpdatedAt")
      VALUES (5, cust_alain_id, sp_id, alain_id, 'AED', 'fixture-marker-inflight-2', NOW(), NOW());
  END IF;

  -- 12. Approved V2.3 req (PRESERVED — terminal state)
  IF NOT EXISTS (SELECT 1 FROM "QuotationRequests" WHERE "Notes" = 'fixture-marker-approved') THEN
    INSERT INTO "QuotationRequests" ("Status", "CustomerId", "SalesPersonId", "BranchId", "CurrencyCode", "Notes", "CreatedAt", "UpdatedAt")
      VALUES (6, cust_alain_id, sp_id, alain_id, 'AED', 'fixture-marker-approved', NOW(), NOW());
  END IF;

  -- 13. Draft V2.3 req (PRESERVED — V3 has same Draft semantic)
  IF NOT EXISTS (SELECT 1 FROM "QuotationRequests" WHERE "Notes" = 'fixture-marker-draft') THEN
    INSERT INTO "QuotationRequests" ("Status", "CustomerId", "SalesPersonId", "BranchId", "CurrencyCode", "Notes", "CreatedAt", "UpdatedAt")
      VALUES (0, cust_alain_id, sp_id, alain_id, 'AED', 'fixture-marker-draft', NOW(), NOW());
  END IF;

  RAISE NOTICE 'Fixture seeded. admin_id=%, sp_id=%, bc_id=%, alain_id=%, dubai_id=%', admin_id, sp_id, bc_id, alain_id, dubai_id;
END $$;

COMMIT;
