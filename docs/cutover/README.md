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
