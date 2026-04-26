# V2.3-A — Branch model rework + branch-change endpoint + Branches admin UI

**Date:** 2026-04-26
**Scope:** Backend + Web + Mobile. First sub-project of the V2.3 expansion (V2.3-B sales group and V2.3-C admin override are separate specs).
**Status:** Spec, awaiting plan
**Parent vision (user, paraphrased):** Salesperson chooses which branch a quotation is for at creation time; each branch has its own Accountant who only sees that branch's work; if SP picks the wrong branch the receiving Accountant can reassign mid-process. SP only sees Finished Goods in the item picker — Raw Materials belong to BOM stage.

---

## 1. Problem

Today's branch model is rigid in the wrong places and missing where it should exist:

1. **SalesPerson is hard-bound to one branch.** `User.BranchId` is required for SP, and `RequisitionsController.cs:221` overrides any payload value with `CurrentBranchId.Value`. SP cannot service customers whose order should be made at the other branch.
2. **No branch picker UI.** Neither web (`NewRequisitionPage.tsx`) nor mobile has a branch dropdown on new-req creation.
3. **Items are branch-scoped (`Item.BranchId`)** but with the SP locked to one branch, they only ever see their own branch's items. With the SP cross-branch fix, the items dropdown must refilter on branch change.
4. **Accountant is cross-branch (`null` BranchId)** which means there is no per-branch isolation — Sara today sees every branch's costing work. The user wants branch-bound Accountants going forward, with explicit multi-branch assignment for users like Sara who currently span both.
5. **No branch-change endpoint.** Once a requisition is created, its branch is locked. If SP picks wrong, today's only fix is delete + recreate (which loses RefNo, history, and notifications already sent).
6. **SP sees Raw Materials in the items picker.** Raw Materials are BOM-stage inventory, not customer-facing products. Should be filtered out for SP.

---

## 2. Goals

1. **SP cross-branch + branch picker** — SP picks which branch (Fujairah / Alain / future) a new requisition targets, per-req. `User.BranchId` for SP becomes a "default pre-fill hint", not a binding constraint.
2. **Accountant branch-bound via M:N** — new `UserBranches` table; an Accountant is assigned to one or more branches; queries scope to their branch set. Sara is auto-assigned to both branches in migration.
3. **`PATCH /api/requisitions/{id}/branch` endpoint** — Accountant or Admin can change a req's branch in BomPending / BomInProgress / CostingPending only. Strict block if existing items don't belong to the new branch.
4. **Branches admin UI** — web admin can CRUD branches (DB table exists, UI missing) and manage Accountant ↔ branch assignments.
5. **SP item picker filtered to Finished Goods** — server-enforced for SP role (defense-in-depth).

### Non-goals

- Group / team feature (V2.3-B).
- Admin override page (V2.3-C).
- BomCreator multi-branch (stays single-branch).
- Mobile admin Branches CRUD (web only).
- Customer ↔ branch enforcement (customer dropdown stays unfiltered by branch — see §6 Q4).

---

## 3. Scope

### In scope

**Backend:**
- New entity: `UserBranches` (M:N for Accountants).
- New entity: `BranchChangeHistories` (audit log; mirrors `CustomerChangeHistories`).
- `POST /api/requisitions` — accept `BranchId` from payload (replaces JWT-claim override).
- `GET /api/items` — `?branchId=` filter + auto-exclude `RawMaterial` for `SalesPerson` role.
- New `PATCH /api/requisitions/{id}/branch` — Accountant + Admin; status guard.
- New `GET /api/requisitions/{id}/branch-history` — audit read.
- New `GET /api/branches` (verify if exists; expose for picker).
- New admin endpoints: Branches CRUD (`POST` / `PUT` / `DELETE`), `GET /api/users/{id}/branches`, `PUT /api/users/{id}/branches`.
- New helper `BranchAuthorization.UserAuthorizedForBranch(user, branchId, db)` — single source of truth, replaces inline `u.BranchId == req.BranchId || u.BranchId == null` checks across all controllers.

**Mobile:**
- Branch picker on SP new-req screen.
- Branch-swap sheet + change-history sheet for Accountant.
- Branch-changed badge mounted on Accountant `[id]` form path, MD review screen, and `HistoricalRequisitionScreen` (parallel to V2.1 P2 customer-changed badge).

**Web:**
- Branch picker on `NewRequisitionPage.tsx`.
- Branch-swap modal + history badge on `RequisitionDetailPage.tsx`.
- New `BranchesPage.tsx` (admin CRUD).
- `UsersPage.tsx` — add BranchId column; for Accountant rows show multi-branch list.
- `EditUserModal.tsx` — for Accountant role replace single-branch dropdown with multi-select calling `PUT /users/{id}/branches`.

### Out of scope (deferred to V2.3-B / V2.3-C / later)

- Sales group / team entity (V2.3-B).
- Admin override / mid-process edits / delete-requisition (V2.3-C).
- BomCreator multi-branch.
- Customer-branch enforcement.
- Mobile admin Branches CRUD UI.

---

## 4. Architecture

### 4.1 Data model + migrations

#### New entity — `UserBranches` (M:N — Accountant role only)

```
UserBranches
  UserId       INT  FK → Users.Id  (cascade delete)
  BranchId     INT  FK → Branches.Id  (cascade delete)
  AssignedAt   TIMESTAMPTZ  default NOW()
  PRIMARY KEY (UserId, BranchId)
```

Used **only for the Accountant role**. SP / BomCreator / MD / Admin do not have rows here.

#### New entity — `BranchChangeHistories` (audit; mirrors `CustomerChangeHistories`)

```
BranchChangeHistories
  Id              SERIAL PK
  RequisitionId   INT  FK → QuotationRequests.Id  (cascade)
  OldBranchId     INT  FK → Branches.Id
  NewBranchId     INT  FK → Branches.Id
  ChangedByUserId INT  FK → Users.Id
  ChangedAt       TIMESTAMPTZ
  Reason          TEXT NULL
```

#### Existing entity — `Users.BranchId` semantic update (no schema change)

Same nullable int column, but the meaning depends on role:

| Role | Meaning of `User.BranchId` | Source-of-truth for "which branches?" |
|---|---|---|
| SalesPerson | "Default pre-fill hint" for new-req branch picker | N/A (cross-branch) |
| BomCreator | Binding constraint (today's behavior) | `User.BranchId` |
| Accountant | Ignored | `UserBranches` rows |
| ManagingDirector | Ignored | N/A (cross-branch) |
| Admin | Ignored | N/A (cross-branch) |

This dual semantic must be documented in `CLAUDE.md` after the migration ships.

#### Permission helper (centralized)

```csharp
public static bool UserAuthorizedForBranch(User u, int reqBranchId, AppDbContext db) => u.Role switch {
    UserRole.SalesPerson      => true,  // SP scoping is by self via SalesPersonId, not branch
    UserRole.BomCreator       => u.BranchId == reqBranchId,
    UserRole.Accountant       => db.UserBranches.Any(ub => ub.UserId == u.Id && ub.BranchId == reqBranchId),
    UserRole.ManagingDirector => true,
    UserRole.Admin            => true,
    _                         => false
};
```

All existing branch-scoping queries in controllers (`u.BranchId == req.BranchId || u.BranchId == null`) get rewritten to call this helper.

### 4.2 Backend API changes

| Method | Path | Change |
|---|---|---|
| `POST /api/requisitions` | accept `BranchId` from payload (Required); remove `BranchId = CurrentBranchId.Value`. Validate items belong to picked branch (strict). |
| `GET /api/items` | new optional `?branchId=X` server-side filter; for SalesPerson role, ALWAYS auto-exclude `RawMaterial` regardless of `?type=` param. |
| `GET /api/requisitions` | rewrite scoping for Accountant — uses `UserBranches` (sees reqs whose `BranchId` is in their assigned set). |
| BOM-start / costing-start / etc. notification fan-out | replace `u.BranchId == req.BranchId || u.BranchId == null` with `UserAuthorizedForBranch` helper. |

**New endpoints:**

| Method | Path | Roles | Purpose |
|---|---|---|---|
| `PATCH /api/requisitions/{id}/branch` | Accountant + Admin | Body `{ branchId, reason? }`. Status guard: `BomPending` / `BomInProgress` / `CostingPending`. Strict block if any req item's `BranchId ≠ new branchId` (returns 400 with item list). On success: write `BranchChangeHistories` + notify SP + old/new branch's BomCreator+Accountant + all MDs. |
| `GET /api/requisitions/{id}/branch-history` | any logged-in role w/ access | Read-only audit log (mirrors `customer-history`). |
| `GET /api/branches` | any logged-in role | `[{id, code, name, isActive}]` for dropdowns. |
| `POST /api/branches` | Admin | Create branch. |
| `PUT /api/branches/{id}` | Admin | Edit name / IsActive flag. |
| `DELETE /api/branches/{id}` | Admin | Soft-delete (`IsActive = false`); blocks if any user/req/item references it. |
| `GET /api/users/{id}/branches` | Admin | List Accountant's assigned branches. |
| `PUT /api/users/{id}/branches` | Admin | Replace Accountant's branch set. Body `{ branchIds: [int] }`. |

### 4.3 Frontend — Web (`bom-web/`)

| Path | Change |
|---|---|
| `src/features/requisitions/NewRequisitionPage.tsx` | New `<BranchPicker>` dropdown above customer + items. Default = `useAuthStore().user?.branchId` (SP's pre-fill). Items query: `useItems({ branchId: pickedBranchId, type: "FinishedGood" })`. On branch change → invalidate items query → dropdown refetches. Submit payload includes `branchId`. |
| `src/features/requisitions/RequisitionDetailPage.tsx` | For Accountant + Admin viewing req with status ∈ {BomPending, BomInProgress, CostingPending}: render "Change branch" button next to existing "Change customer". On click → `<BranchSwapSheet>` modal (mirrors `CustomerSwapSheet`). On save → `PATCH /branch` mutation → invalidate detail + list queries → toast. Show "Branch changed (N)" badge if history > 0. |
| `src/features/admin/branches/BranchesPage.tsx` | NEW — table with Code, Name, IsActive, actions (Edit, Toggle Active). Add / Edit modals. List from `GET /api/branches`. |
| `src/features/users/UsersPage.tsx` | Add `BranchId` column (display branch name; "—" for null/Accountant). For Accountant rows: show "Branches: Fujairah, Alain" (multi-value from `UserBranches`). |
| `src/features/users/EditUserModal.tsx` | For Accountant role: replace single BranchId dropdown with multi-select branches list (calls `PUT /api/users/{id}/branches`). |
| `src/api/branches.ts`, `src/api/userBranches.ts` | New hooks: `useBranches`, `useCreateBranch`, `useUpdateBranch`, `useUserBranches`, `useSetUserBranches`. |
| `src/api/requisitions.ts` | New hooks: `useChangeBranch(requisitionId)`, `useBranchChangeHistory(requisitionId)`. |
| Sidebar / `AppShell.tsx` | New admin nav link: "Branches". |

### 4.4 Frontend — Mobile (`bom-mobile/`)

| Path | Change |
|---|---|
| Sales new-req screen | New `<BranchPicker>` (SearchablePicker pattern) above customer + items. Default = user's `BranchId`. |
| `app/(accountant)/[id].tsx` | Add "Change branch" button (parallel to existing "Change customer" added in V2.1 P2). On tap → `<BranchSwapSheet>`. |
| `src/components/BranchSwapSheet.tsx` | NEW — mirrors `CustomerSwapSheet`. SearchablePicker for branches + reason input + Save/Cancel. |
| `src/components/BranchChangeHistorySheet.tsx` | NEW — mirrors `CustomerChangeHistorySheet`. |
| `src/api/branches.ts` | NEW: `useBranches()` hook. |
| `src/api/requisitions.ts` | NEW: `useChangeBranch`, `useBranchChangeHistory`. |
| `src/api/items.ts` | Update to accept optional `branchId` filter param. |
| Branch-changed badge | Mount in `(accountant)/[id]` form path AND `HistoricalRequisitionScreen` AND `(md)/[id]` review screen (parallel to V2.1 P2 customer-changed badge wiring). |

---

## 5. Authorization matrix

| Action | Mobile / web permission check | Backend enforces |
|---|---|---|
| Create req with picked BranchId | role = SalesPerson; UI passes BranchId | `RequisitionsController.Create` validates SP role; items belong to picked branch |
| List reqs as Accountant | UI shows reqs returned by API | `GET /api/requisitions` scopes via `UserAuthorizedForBranch` |
| Change branch | role ∈ {Accountant, Admin} AND status ∈ {BomPending, BomInProgress, CostingPending} | `PATCH /branch` enforces both gates + items mismatch |
| Branch-change history view | any logged-in role with read access on req | `GET /branch-history` enforces existing `CanAccess` |
| Branches admin CRUD | role = Admin | `[Authorize(Roles="Admin")]` on endpoints |
| Set Accountant's branches | role = Admin | `PUT /api/users/{id}/branches` admin-gated; rejects non-Accountant target with 400 |
| Items list as SP | UI doesn't render RawMaterial | `GET /api/items` server-filters out RawMaterial for SP role |

UI gating is **defense in depth** — the source of truth is server enforcement.

---

## 6. Migration plan + rollout

### 6.1 Order of operations (single PR — per all-in-one scope decision)

1. **EF migration script:**
   - Create `UserBranches(UserId, BranchId, AssignedAt)` with composite PK + FK cascades.
   - Create `BranchChangeHistories(...)`.
   - Data step inside same migration:
     ```sql
     INSERT INTO "UserBranches" ("UserId", "BranchId", "AssignedAt")
     SELECT u."Id", b."Id", NOW()
     FROM "Users" u, "Branches" b
     WHERE u."Role" = 3  -- Accountant
       AND u."IsActive" = TRUE
       AND b."IsActive" = TRUE;
     ```
     Auto-assigns ALL active Accountants to ALL active branches — preserves Sara's current cross-branch behavior across the cutover.

2. **Backend deploy:**
   - All controller queries rewritten to use `UserAuthorizedForBranch` helper.
   - `POST /api/requisitions` requires `BranchId` in payload.
   - **Transition window (1 release):** accept missing `BranchId` and fall back to `CurrentBranchId.Value`, logging a warning. Strictness lands in next release after frontends are confirmed shipped.

3. **Frontend deploy (web + mobile, same release):**
   - Both clients ship `BranchPicker` on new-req, sending `branchId` in payload.

4. **Cleanup release (next sprint):**
   - Remove backend transition-window fallback.
   - Remove `User.BranchId` setter from `EditUserModal` for Accountant role.

### 6.2 Risks + mitigations

| Risk | Mitigation |
|---|---|
| Sara loses access during migration window | Auto-assign all-Accountants × all-Branches in same migration → Sara sees all branches |
| Existing SP / mobile clients hit backend with old payload (no branchId) | Transition-window fallback for 1 release |
| Tests break (Sara was assumed cross-branch via `BranchId == null`) | Update test fixtures: seed `UserBranches` row for Accountant fixtures |
| Existing in-flight reqs broken by new permission helper | Helper returns true for any branch the Accountant has UserBranches rows for → identical behaviour to legacy "BranchId null = sees all" pre-V2.3 |
| Items.Type filter breaks SP smoke that used RawMaterial in test data | Update test seeds: SP-create paths use FinishedGood items only |
| `BranchChangeHistories` populated post-deploy → empty pre-rollback, no issue | n/a |

### 6.3 Rollback strategy

- If issue surfaces post-deploy, revert backend release (single PR commit).
- `UserBranches` table can stay (additive only) — old code ignores it.
- `User.BranchId = NULL` for Sara was already the case → no data loss.

---

## 7. Testing strategy

### 7.1 Backend (xUnit + Testcontainers per CLAUDE.md)

| Test class | Tests |
|---|---|
| `UserBranchesTests` | Insert / list / replace; FK cascade on user delete or branch delete; composite PK blocks duplicates. |
| `RequisitionsCreateBranchPickerTests` | SP creates req with explicit `BranchId` (success); item validation rejects items from other branch; missing BranchId during transition window logs warning + falls back. |
| `BranchAuthorizationHelperTests` | `UserAuthorizedForBranch`: SP/BomCreator/Accountant/Admin/MD per branch — covers all 5 role × matching/mismatching/empty-set permutations. |
| `RequisitionsListBranchScopingTests` | Existing list test rewrite — Accountant assigned to branch 1 sees only branch-1 reqs; Sara assigned to both sees both; cross-branch leak forbidden. |
| `ChangeBranchTests` (mirrors `ChangeCustomerTests`) | Happy path (Accountant in CostingPending) → 204 + history entry; status guard tests (CostingInProgress → 400, Approved → 400); item mismatch → 400 with item list in body; role gate (SP → 403, BomCreator → 403); branch isolation (branch-1 Accountant cannot change branch-2 req → 403); notification fan-out asserted on test sink. |
| `BranchHistoryReadTests` | Empty case; after change shows entry with full details; ordering DESC. |
| `BranchesAdminCrudTests` | POST/PUT/DELETE admin-only; soft-delete blocks if branch in use; non-admin → 403. |
| `UserBranchesAdminTests` | Admin sets accountant's branch list (replace semantics); non-admin → 403; cannot assign branches to non-Accountant role (validation 400). |
| `ItemsListTests` (extension) | SP gets only `FinishedGood` regardless of `?type=` param; non-SP gets all types; `?branchId=` filter works for all roles. |
| `NotificationCascadeOnBranchChangeTests` | Old + new branch's BomCreator + Accountant + SP + all MDs all receive notif on branch change (count + recipients asserted). |

Target: ~25 new test methods. Existing ~4 customer-related tests get parallel branch-related counterparts.

### 7.2 Frontend (web — vitest + RTL)

| Test file | Tests |
|---|---|
| `NewRequisitionPage.test.tsx` (extension) | Branch picker visible + defaults to user's BranchId; changing branch refetches items; submit payload includes branchId. |
| `RequisitionDetailPage.test.tsx` (extension) | Accountant sees Change-branch button in BomPending/CostingPending; not in CostingInProgress; non-Accountant doesn't see button; history badge shows count + opens sheet. |
| `BranchesPage.test.tsx` | NEW — list, create, edit, soft-delete flows. |
| `EditUserModal.test.tsx` (extension) | Accountant role shows multi-branch select; saving calls `PUT /users/{id}/branches`. |

### 7.3 Mobile (manual on-device smoke — same pattern as V2.1 P2)

8-item checklist:

1. SP login → new-req → branch picker shows + defaults to own `BranchId` + change works.
2. SP picks Alain branch → items dropdown only shows Alain finished goods (no raw materials).
3. SP picks customer of any branch (no filter applied).
4. SP submits → req lands in correct branch → both branches' BomCreator+Accountant get notified per `UserAuthorizedForBranch`.
5. Accountant opens BomPending req from wrong branch → "Change branch" button visible → swap to correct branch → toast + history badge → SP + old + new branch staff all get notif.
6. Accountant tries to change branch in CostingInProgress → button absent (status gate).
7. Accountant from branch-1 only doesn't see branch-2 reqs in their list (Sara migration test: Sara IS in both branches, sees both).
8. Branch-change badge + sheet visible on both Accountant `[id]` form path AND MD review screen + historical screen.

### 7.4 Build gates (per CLAUDE.md)

- `dotnet build` clean before merge.
- `dotnet test` all green — all existing tests + ~25 new must stay green.
- `tsc --noEmit` clean for both web (`bom-web`) and mobile (`bom-mobile`).

---

## 8. Implementation order (suggested for the plan)

The writing-plans skill will produce the detailed ordered task list. Suggested high-level order:

1. Backend: add `UserBranches` + `BranchChangeHistories` entities, migration, data-migration step, helper class. Tests for the helper + UserBranches CRUD.
2. Backend: rewrite branch-scoping queries in all controllers to use the helper. Re-run all existing tests to confirm no regression.
3. Backend: add `POST /api/requisitions` BranchId payload acceptance + transition-window fallback. Test new flow + fallback.
4. Backend: add `GET /api/items?branchId=` filter + SP-role RawMaterial exclusion. Test.
5. Backend: add `PATCH /api/requisitions/{id}/branch` + `GET /branch-history` + notification fan-out. Tests.
6. Backend: add Branches admin CRUD endpoints + `UserBranches` admin endpoints. Tests.
7. Web: `BranchPicker` component + integration in `NewRequisitionPage`. Test.
8. Web: `BranchSwapSheet` + history badge + integration in `RequisitionDetailPage`. Test.
9. Web: `BranchesPage` admin CRUD + sidebar nav. Test.
10. Web: `UsersPage` BranchId column + `EditUserModal` multi-branch support for Accountant. Test.
11. Mobile: `BranchPicker` on SP new-req; `useBranches` hook; items hook update. Tsc + on-device.
12. Mobile: `BranchSwapSheet` + `BranchChangeHistorySheet` components. Wire on `(accountant)/[id]`, `HistoricalRequisitionScreen`, `(md)/[id]` review screen.
13. Full smoke pass against the 8-item checklist in §7.3.

Each step compiles (`dotnet build` for backend, `tsc --noEmit` for mobile) before moving on.

---

## 9. Open decisions (locked during brainstorm 2026-04-26)

For traceability, recording the multiple-choice decisions made during brainstorm so future-me sees the alternatives that were considered:

| Question | Locked answer | Alternatives considered |
|---|---|---|
| Existing Accountant Sara migration | (C) Multi-branch via `UserBranches` M:N — Sara assigned to both branches | (A) Hard migration to one branch + new hire for other; (B) Two-tier: keep Sara as supervisor with null BranchId |
| `UserBranches` scope | (A) Mixed model — only Accountant role uses M:N | (B) Unified M:N for all roles; (C) Hybrid primary + additional |
| Existing SP Ali migration | (B) Keep `User.BranchId` as default pre-fill hint, dual semantic | (A) Drop SP BranchId entirely; (C) New separate `DefaultBranchId` column |
| Customer × branch filter on new-req | (A) No filter — sub independent of branch | (B) Filter customers by selected branch; (C) Allow + warn on mismatch |
| Items refilter UX | (A) Server-side filter `?branchId=X` per branch change | (B) Client-side filter on initial all-load; (C) Hybrid cache by branch |
| Items mismatch on `PATCH /branch` | (A) Strict — block branch change if any item belongs to old branch only | (B) Permissive — leave items as-is; (C) Cascade-clear items + reset to BomPending; (D) Smart preview + per-item user confirm |
| Notification fan-out on branch change | (B) Workflow-aware — SP + old branch BomCreator/Accountant + new branch BomCreator/Accountant + all MDs | (A) Match customer-change (SP + MDs); (C) Minimum (SP + new branch only) |

No further open questions. All decisions locked. Ready for writing-plans.
