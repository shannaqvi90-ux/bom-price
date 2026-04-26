# V2.3-B — Sales Group / Team Feature

**Spec date:** 2026-04-26
**Branch (planned):** `feat/v23b-sales-groups` (off `master` after V2.3-A merges)
**Predecessor:** V2.3-A (branch model rework) @ `93e4096` on `feat/v23a-branch-model`
**Successor:** V2.3-C (admin override page) — separate future spec

---

## 1. Summary

Allow grouping SalesPersons into flat peer teams so that all members of a group share full visibility and full edit/create rights on each other's customers and requisitions. Groups are branch-agnostic and managed by Admin or Accountant roles.

A typical real-world scenario: four salespeople (Ranj, Ayesha, Isra, Bashar) sit in one team. When Ayesha is out of office and her customer calls in to place a new order, any peer can pick it up — they see Ayesha's customer in their list, can edit the customer record, and can create a new requisition against it. The new requisition's `SalesPersonId` is the creator's; existing requisitions stay attributed to their original creator.

---

## 2. Goal

Provide a "shared customer pool" semantic for sales teams without changing the per-requisition `SalesPersonId` ownership concept (which drives reporting, attribution, and notification routing).

**Out of scope** (deferred or in other specs):
- Hierarchical groups (lead → members → sub-team) — Q3 rejected
- Multi-group membership — Q4 rejected
- Branch-bound groups — Q5 rejected
- Group-level reporting/analytics dashboards
- Auto-grouping based on territory or branch
- Customer-level visibility ACLs (groups already gives blanket access; finer per-customer sharing is unneeded)

---

## 3. Locked Design Decisions

Recorded during the 2026-04-26 brainstorm. These pin the design's traceability for future audits.

| # | Question | Decision |
|---|---|---|
| Q1 | What does "sales group/team" mean? | Group-based shared customer ownership (one of "(c) shared customer" but at group level not pair level) |
| Q2 | Action scope on shared customers/reqs | (c) Full shared — view + edit + create for any group member |
| Q3 | Group hierarchy | (a) Flat peer group (no lead) |
| Q4 | Multi-group membership | (a) Single group per SP (User.GroupId nullable FK) |
| Q5 | Branch crossing | (a) Branch-agnostic — group spans branches |
| Q6 | Group management roles | Admin + Accountant (deviation from V2.3-A's Admin-only on Branches CRUD; Accountant added because user explicitly chose this) |
| Q7 | List visibility | (a) Full pool — group customer/req lists merge all members' data |
| Q8 | Notifications on group reqs | (a) Original SP only — avoids notification spam |
| Q9 | SP without group | (a) Sees own customers + reqs only (current single-SP behavior as fallback) |
| Q10 | Existing data migration | (a) No auto-grouping; admin manually configures post-deploy |
| Q11 | SP removed from group | (a) Clean cut — SP loses group visibility, group loses SP's visibility, immediately |

---

## 4. Architecture Overview

```
┌──────────────┐       ┌────────────┐       ┌──────────────────┐
│   Customers  │◄──┐   │   Users    │   ┌──►│ QuotationRequest │
│  (existing)  │   │   │ ─────────  │   │   │    (existing)    │
│  SalesPerson │───┴──►│ Id         │◄──┘   │  SalesPersonId   │
│  Id (FK)     │       │ Role       │       │  (FK)            │
└──────────────┘       │ BranchId   │       └──────────────────┘
                       │ GroupId(?) │ ◄─── new column (V2.3-B)
                       └─────┬──────┘
                             │
                             ▼ FK
                       ┌────────────┐
                       │SalesGroup  │ ◄─── new entity (V2.3-B)
                       │  Id        │
                       │  Name      │
                       │  IsActive  │
                       └────────────┘
```

**Key architectural choices:**

- `User.GroupId` is the single source of truth for membership. No denormalized `Customer.GroupId` or `QuotationRequest.GroupId`.
- Visibility is computed at query time via JOIN through `User.GroupId`. The cost of one JOIN per list query is negligible at expected scale (small factory, < 100 SPs total).
- `User.GroupId` is nullable to preserve "solo SP" semantics (Q9, Q11) without special casing.
- Group is enforced to be SalesPerson-only at the API layer (`PUT /api/users/{id}/group` rejects non-SP). The DB column is technically nullable for any role; the validation lives in the controller.

**Relationship to V2.3-A:**

Independent feature. V2.3-A's branch model is unaffected. Group membership crosses branches freely (Ayesha in branch-2 can act on Ranj's branch-1 customer). The branch-isolation rule for non-SP roles (BomCreator/Accountant) is unchanged — SP cross-branch via per-req picker (V2.3-A) plus group sharing (V2.3-B) compose cleanly.

---

## 5. Data Model

### 5.1 New entity — `SalesGroup`

```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class SalesGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public ICollection<User> Members { get; set; } = [];
}
```

### 5.2 Modified entity — `User`

```csharp
public int? GroupId { get; set; }       // nullable; only meaningful for SalesPerson role
public SalesGroup? Group { get; set; }
```

### 5.3 EF migration: `V23b_SalesGroups`

```csharp
migrationBuilder.CreateTable(
    name: "SalesGroups",
    columns: table => new
    {
        Id = table.Column<int>(nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
        Name = table.Column<string>(maxLength: 100, nullable: false),
        IsActive = table.Column<bool>(nullable: false, defaultValue: true),
    },
    constraints: table => table.PrimaryKey("PK_SalesGroups", x => x.Id));

migrationBuilder.AddColumn<int>(
    name: "GroupId",
    table: "Users",
    type: "integer",
    nullable: true);

migrationBuilder.CreateIndex(
    name: "IX_Users_GroupId",
    table: "Users",
    column: "GroupId");

migrationBuilder.AddForeignKey(
    name: "FK_Users_SalesGroups_GroupId",
    table: "Users",
    column: "GroupId",
    principalTable: "SalesGroups",
    principalColumn: "Id",
    onDelete: ReferentialAction.Restrict);  // group with members can't be deleted
```

No data step (per Q10).

---

## 6. API Endpoints

### 6.1 Groups CRUD (mirror V2.3-A `/api/branches` pattern)

| Endpoint | Auth | Body | Returns |
|---|---|---|---|
| `GET /api/groups` | any authenticated | — | `[{id, name, isActive}]` |
| `POST /api/groups` | Admin + Accountant | `{name}` | 201 `{id, name, isActive}` |
| `PUT /api/groups/{id}` | Admin + Accountant | `{name, isActive}` | 204 |
| `DELETE /api/groups/{id}` | Admin + Accountant | — | 204 (sets IsActive=false) or 409 if has active members |

### 6.2 User-group assignment

| Endpoint | Auth | Body | Returns |
|---|---|---|---|
| `GET /api/users/{id}/group` | Admin + Accountant | — | `{groupId, groupName}` or null |
| `PUT /api/users/{id}/group` | Admin + Accountant | `{groupId}` (nullable) | 204 |

`PUT` validates:
- Target user.Role == SalesPerson (otherwise 400)
- groupId references an active SalesGroup (otherwise 400)
- groupId can be null to clear membership

---

## 7. Permission / Visibility Rules

### 7.1 Helper

```csharp
// BomPriceApproval.API/Infrastructure/Authorization/SalesAuthorization.cs

public static class SalesAuthorization
{
    /// <summary>
    /// Returns the set of SalesPerson IDs whose customers + reqs are visible to <paramref name="user"/>.
    /// SP with a group: returns all SP members of that group. SP without a group: returns just self.
    /// Non-SP roles: empty (caller should not invoke this for them).
    /// </summary>
    public static int[] VisibleSalesPersonIds(User user, AppDbContext db)
    {
        if (user.Role != UserRole.SalesPerson) return [];
        if (user.GroupId == null) return [user.Id];
        return db.Users
            .Where(u => u.GroupId == user.GroupId && u.Role == UserRole.SalesPerson)
            .Select(u => u.Id)
            .ToArray();
    }
}
```

### 7.2 List query updates

**`RequisitionsController.GetAll`** (extends V2.3-A):
```csharp
else if (CurrentRole == "SalesPerson")
{
    var visibleIds = SalesAuthorization.VisibleSalesPersonIds(currentUser, db);
    query = query.Where(q => visibleIds.Contains(q.SalesPersonId));
}
```

**`CustomersController.GetAll`**: same pattern via `Customer.SalesPersonId IN visibleIds`.

### 7.3 Per-resource access

`CanAccess` helpers (existing in `RequisitionsController`, `CustomersController`) extend to:
- For SP role: allow access if `resource.SalesPersonId IN VisibleSalesPersonIds(self, db)`
- All other roles: unchanged

This unlocks:
- `GET /api/requisitions/{id}` for group-mate's req
- `GET /api/customers/{id}` for group-mate's customer
- `PATCH /api/customers/{id}` for group-mate's customer (full shared per Q2=c)
- `POST /api/requisitions` against group-mate's customer
- BOM/Costing/Approvals controllers don't change — they're not SP-facing

### 7.4 Notifications

Notifications stay routed by original SalesPersonId (Q8=a). Group members see status changes via list refresh; no spam. The customer-change notification fan-out (V2.1-P2) and branch-change fan-out (V2.3-A) are unchanged.

### 7.5 Audit history

The customer-change history (V2.1-P2) and branch-change history (V2.3-A) record `ChangedByUserId = currentUser.Id`. When Ayesha edits Ranj's customer, the history correctly attributes Ayesha. No code changes needed — already works because history records the actor, not the customer's owner.

---

## 8. UI Surfaces

### 8.1 Web (`bom-web`)

**New page:** `/admin/groups`
- Table: Name, IsActive toggle, Members count, Edit, Delete
- Add modal: Name field
- Edit modal: Name + IsActive toggle
- Delete: confirmation modal; surfaces 409 in-use error
- Visible in sidebar to Admin and Accountant roles only
- Mirrors `/admin/branches` page implementation pattern (V2.3-A Task 20)

**Modified `UsersPage`:**
- New Group column for SP rows; resolves to group name via `useUserGroup(u.id)` per row (or denormalized via groups list join)
- Non-SP rows show "—"

**Modified `EditUserModal`:**
- For SalesPerson role only, add a Group single-select dropdown ("None" option for solo SP)
- Hidden for non-SP roles
- On save: PUT user (existing flow) THEN PUT `/api/users/{id}/group`

**New API hooks:** `bom-web/src/api/groups.ts`
- `useGroups()` query
- `useCreateGroup` / `useUpdateGroup` / `useDeleteGroup` mutations
- `useUserGroup(userId)` query
- `useSetUserGroup(userId)` mutation

### 8.2 Mobile (`bom-mobile`)

**No new admin UI** (admin is web-only).

**SP req list** automatically shows group reqs (data shift only, no JSX change).

**SP customer list** automatically shows group customers (data shift only).

**Subtle ownership badge** on each card if `req.salesPersonId !== self.userId`:
- Small subtle text below the customer name: `"by <SalesPersonName>"` (the original SP's full `User.Name` from seed/profile, e.g., "by Sara Accounts")
- Color: muted slate-500
- Only shown when not your own; your own reqs don't get a badge
- Helps the SP know whose work this is at a glance
- Same treatment on customer cards: `"owned by <SalesPersonName>"` if not yours

The req detail / customer detail pages don't need an explicit badge (the existing UI already shows the SP attribution).

This badge mirrors the V2.1-P2 customer-changed badge style but is informational, not interactive.

### 8.3 No mobile admin

Group create / edit / member assign is web-only. Mobile SPs only see the data shift effect.

---

## 9. Edge Cases / Acceptance Criteria

| Case | Expected Behavior |
|---|---|
| New SP, no group assigned | Sees own customers + reqs only (Q9 fallback) |
| SP joins group with existing peers | Immediately sees group's customers + reqs (no migration step needed; live JOIN) |
| SP removed from group | Immediately loses group visibility; group loses visibility of their stuff (Q11 clean cut) |
| Two SPs in same group create reqs against same customer | Both reqs are in group's pool; original SalesPersonId preserved on each |
| Customer creator leaves group | Customer remains owned by them (SalesPersonId unchanged); group loses visibility (clean cut); creator keeps it as solo |
| Customer-edit by group member | Allowed; customer-change history records the editor as actor (existing V2.1-P2 infra) |
| Customer-edit history visibility | Group members see all entries; non-group SPs cannot access |
| Soft-delete group with any SP members assigned | 409 Conflict — must clear all `User.GroupId` references first (any user with `GroupId == this.Id`, regardless of `User.IsActive`, blocks delete) |
| Soft-delete group with no members | OK; sets IsActive=false |
| Notification routing on req status change | Original `SalesPersonId` only (Q8) |
| Group + branch interaction (V2.3-A) | Independent; group is branch-agnostic. Ayesha (branch-2 SP) can edit Ranj's branch-1 customer |
| Group assigned to non-SP user | API rejects with 400 (group is SP-only) |
| Group deletion endpoint behavior | DELETE soft-deletes (sets IsActive=false). Once IsActive=false, cannot assign new SPs |
| Inactive group with members | Existing members keep group visibility (the group still exists; IsActive only blocks new assignments). Admin must clear members manually if hard removal is desired |
| Reassigning SP between groups | Old group loses access; new group gains access; SP's own customers/reqs follow them to new group's pool |

---

## 10. Test Coverage Outline

Following V2.3-A's testing pattern, target ~9 backend test files + 2 web component test files + mobile tsc-only. All integration tests against real Postgres via existing `WebApplicationFactory<Program>` setup.

### Backend

| Test class | Cases |
|---|---|
| `Groups/SalesGroupAdminCrudTests` | POST/PUT/DELETE Admin+Accountant gate; non-Admin/non-Accountant 403; in-use 409; soft-delete sets IsActive=false |
| `Users/UserGroupAdminTests` | Set/clear group; non-SP target rejected 400; non-existent group rejected 400; inactive group rejected 400; non-Admin/non-Accountant 403 |
| `Requisitions/RequisitionsListGroupScopingTests` | Group member sees peers' reqs; solo SP sees own only; ex-member loses access after Q11 cut; cross-branch group member visibility (after V2.3-A) |
| `Customers/CustomersListGroupScopingTests` | Group member sees peers' customers; solo SP sees own only; ex-member loses access |
| `Authorization/SalesGroupAccessTests` | Group member can GET/EDIT/CREATE-req against peer's customer; non-group SP forbidden 403; cross-branch combinations |
| `Notifications/SalesGroupNotificationTests` | Notifications stay routed to original SalesPersonId regardless of group composition |
| `Groups/SalesGroupEntityTests` | TDD-red intent test asserting `GET /api/groups` returns 2+ entries after admin seeds via API (smoke baseline) |

### Web

| Test class | Cases |
|---|---|
| `features/admin/groups/GroupsPage.test.tsx` | Renders list; add modal; edit modal; delete confirm; 409 surfaces error |
| `features/users/EditUserModal.test.tsx` (extend) | Group dropdown visible for SP role; hidden for others; PUT user-group called on save |

### Mobile

- tsc-only smoke; no jest tests added (mirrors V2.3-A pattern)
- On-device manual smoke checklist at end of plan

---

## 11. Migration / Deployment Notes

**Pre-deploy:**
- V2.3-A must be merged to master first (this spec assumes V2.3-A endpoints + entities are present, especially `Customer.SalesPersonId` and the reqs branch-scoping refactor)
- DB backup before applying migration (standard practice)

**Migration apply:**
- `dotnet ef database update --project BomPriceApproval.API` runs `V23b_SalesGroups`
- Migration adds 1 table + 1 column + 1 index + 1 FK
- No data step (Q10 — admin manually configures post-deploy)

**Post-deploy admin tasks:**
1. Admin or Accountant logs in to web `/admin/groups`
2. Creates groups for each real-world team (e.g., "North Sales", "South Sales")
3. Edits each SP user via `/admin/users` and assigns to a group
4. Verify group members see each other's customers + reqs in their next list refresh

**Rollback strategy:**
- `Down()` removes FK, index, GroupId column, and SalesGroups table in reverse order
- No data loss for non-V2.3-B data (customer/req records untouched)
- Group membership data is permanently lost on rollback (not preserved in another structure) — acceptable since groups are admin-configured post-deploy

---

## 12. Smoke Checklist (post-deploy)

| # | Item | Pass criteria |
|---|---|---|
| 1 | Admin creates 2 groups (North, South) | Groups appear in `/admin/groups` list |
| 2 | Admin assigns Ayesha + Ranj to North; Isra + Bashar to South | UsersPage Group column shows correct names |
| 3 | Ayesha logs into mobile → req list | Sees Ranj's reqs alongside her own; "by Ranj" badge on his cards |
| 4 | Ayesha → customer list | Sees Ranj's customers; can tap to view |
| 5 | Ayesha edits Ranj's customer (e.g., update phone) | Save succeeds; customer-change history shows Ayesha as actor |
| 6 | Ayesha creates new req against Ranj's customer | Req succeeds; new req's SalesPersonId = Ayesha; visible in both their lists |
| 7 | Ayesha → MD-bound req → MD approves | Notification reaches Ayesha (creator), not Ranj or other group members |
| 8 | Admin removes Ayesha from North | Ayesha next refresh: only sees own; Ranj next refresh: doesn't see Ayesha's stuff |
| 9 | Admin tries to delete North while Ranj is still in it | 409 Conflict surfaces in UI |
| 10 | Admin clears Ranj's group, then deletes North | Soft-delete succeeds (IsActive=false); North no longer assignable |
| 11 | Cross-branch test: Ayesha (branch-2 SP) edits Ranj's (branch-1 SP) customer | Edit succeeds; cross-branch visibility works |

---

## 13. Open Items / Risks

- **Customer attribution staleness:** when SP moves between groups, their existing customers stay with them but the visibility audience shifts. Acceptable per Q11 clean-cut decision; just worth flagging in the user-facing UI (small banner on customer detail: "Visible to your current group: North Sales") — defer to V2.3-B implementation if user pushes back.
- **Performance on large groups:** the `VisibleSalesPersonIds` helper materializes member IDs in-memory. If a group ever exceeds ~50 members, switch to subquery-style EF expression. Not a concern at current factory scale (4-10 expected).
- **Future hierarchy:** if Q3 (flat) ever needs to flip to hierarchical, the migration is small (add `ParentGroupId` column to SalesGroups). YAGNI for now.
- **Future multi-group:** if Q4 (single) ever needs to flip to multi, migrate `User.GroupId` to a `UserGroups` M:N table (mirror V2.3-A `UserBranches`). YAGNI for now.

---

## 14. Acceptance Definition

V2.3-B is "done" when:
- All 11 smoke items pass on real device
- Backend test suite is green (existing 162/162 + new ~25 tests for V2.3-B = ~187 expected total)
- Web tests green + tsc clean
- Mobile tsc clean
- CLAUDE.md updated with V2.3-B group model summary
- Branch ready to merge to master (no unresolved merge conflicts with V2.3-A)
- Manual on-device verification of "by Ranj" badge styling matches V2.1-P2 customer-changed badge aesthetic
