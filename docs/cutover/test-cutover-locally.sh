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
# Note: do NOT use --no-build. Stale DLLs silently skip newer migrations.
dotnet ef database update \
  --project BomPriceApproval.API \
  --connection "$CONN_STRING" 2>&1 | tail -10

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
POST_AUDIT=$($PSQL_DB -t -c "SELECT COUNT(*) FROM \"AdminAuditLogs\" WHERE \"ActionType\" = 'V3CutoverMigration';" | tr -d ' ')

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
