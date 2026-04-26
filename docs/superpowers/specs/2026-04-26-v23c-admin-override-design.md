# V2.3-C Phase 1 — Admin Override (Corrective Operations)

**Spec date:** 2026-04-26
**Branch (planned):** `feat/v23c-admin-override-p1` (off `feat/v23b-sales-groups` → `master` after V2.3-A + V2.3-B merge)
**Predecessor:** V2.3-B (sales groups) @ `6184233` on `feat/v23b-sales-groups`
**Successor:** V2.3-C Phase 2 — separate future spec covering C6 (override approved prices) + C8 (hard-delete customer)

---

## 1. Summary

Give the Admin role a set of contextual corrective operations to repair mistakes that are otherwise unrecoverable in the normal workflow: deleting bad requisitions, rolling status back to an earlier stage, transferring a requisition to a different SalesPerson, unlocking a frozen BOM or costing for re-edit, and resetting a user's password. Every operation requires a free-text reason and writes to a unified `AdminAuditLog` table that is viewable in a new `/admin/audit-log` page.

The admin does not get a single "console" — operations are surfaced where each entity already lives (button on `RequisitionDetailPage`, row action on `UsersPage`). This matches the existing convention established by V2.3-A (`BranchSwapModal`, `CustomerSwapModal`).

Web only. No mobile UI in V2.3-C P1.

---

## 2. Goal

Reduce the operational dependency on direct database edits when a workflow records gets into a wrong state. Today the only recourse for a wrongly-approved requisition, a frozen BOM that needs correction, or a locked-out user is a SQL update — risky, untracked, and gated on a developer being available.

**Out of scope** (deferred or in other specs):

- C6 — Override prices on an Approved requisition (V2.3-C Phase 2; needs PDF re-issue / void flow)
- C8 — Hard-delete a customer (V2.3-C Phase 2; FK cascade strategy needs design)
- Mobile UI for any V2.3-C operation
- Bulk operations (e.g., delete N requisitions at once)
- Email notification on password reset (admin hands tempPassword verbally per D7)
- Cross-source audit log unification (legacy `BranchChangeHistory` and `CustomerChangeHistory` viewers stay where they are on `RequisitionDetailPage`)
- Soft-delete-then-restore for requisitions (hard-delete only, per D10)
- Audit log retention / purge job
- Revoke RefreshTokens for the SalesPerson when their requisition is deleted (only the password-reset op revokes tokens — the user keeps their session)
- Restoring a deleted requisition (no undo)

---

## 3. Locked Design Decisions

Recorded during the 2026-04-26 brainstorm. These pin the design's traceability for future audits.

| # | Decision | Choice |
|---|---|---|
| D1 | Permission gate | Admin only |
| D2 | Reason field | Mandatory free-text on every C1-C5, C7 op (≥ 5 chars) |
| D3 | Audit log scope | New unified `AdminAuditLog` table; only V2.3-C P1 ops write to it (L1 scope); legacy `BranchChangeHistory` + `CustomerChangeHistory` are NOT retrofitted |
| D4 | C2 status rollback constraint | Whitelist of transitions only (see §6.2). Forward jumps blocked. Rollback FROM `Rejected` blocked entirely. |
| D5 | C3 reassign salesperson | Full replace `QuotationRequest.SalesPersonId`. Old SP captured in audit. New SP must be SalesPerson role; can be from any branch (no group constraint). |
| D6 | C4 / C5 unlock mechanism | Status flip back (no parallel "unlock" flag). C4 → `BomInProgress`. C5 → `CostingInProgress`. BomCreator / Accountant resumes naturally. |
| D7 | C7 reset password | System generates random 12-char temp password; admin sees it ONCE in modal (copy-to-clipboard); user must change on next login (`User.MustChangePassword=true`). |
| D8 | Mobile parity | Web only. Mobile gets nothing in V2.3-C P1. |
| D9 | Audit log viewer (C9) | `/admin/audit-log` page; reads ONLY `AdminAuditLog`. Filters: ActionType, AdminUser, EntityType, date range. Paginated. |
| D10 | Behaviour on `Rejected` requisitions | C1 (delete) yes. C2 (rollback FROM Rejected) blocked. C3-C5 N/A on Rejected (no editing path). |

---

## 4. Architecture Overview

```
┌─────────────────────────┐                ┌─────────────────────┐
│   RequisitionDetailPage │                │    UsersPage        │
│   ──────────────────── │                │ ─────────────────  │
│   AdminActionsCard      │                │  Row action menu:   │
│   (Admin-only collapse) │                │  "Reset password"   │
│   [Delete] [Rollback]   │                └──────────┬──────────┘
│   [Reassign SP]         │                           │
│   [Unlock BOM]          │                           │
│   [Unlock Costing]      │                           │
└────────────┬────────────┘                           │
             │ POST /api/admin/requisitions/...       │ POST /api/admin/users/{id}/reset-password
             │ DELETE /api/admin/requisitions/{id}    │
             ▼                                        ▼
┌──────────────────────────────────────────────────────────────────┐
│ AdminController (or per-feature controllers under /admin route)  │
│                                                                  │
│ AdminOverrideAuthorization helper:                               │
│   CanRollback(req, target)                                       │
│   CanUnlockBom(req)                                              │
│   CanUnlockCosting(req)                                          │
└────────────┬───────────────────────────────────────┬─────────────┘
             │                                       │
             ▼                                       ▼
┌──────────────────────────┐                 ┌──────────────────┐
│  Mutate target entity    │                 │ AdminAuditLogger │
│  (delete req, flip       │  same           │  (snapshot       │
│   status, reassign sp,   │  transaction    │   Before/After   │
│   reset password)        │ ───────────────►│   + write row)   │
└────────────┬─────────────┘                 └──────────────────┘
             │
             ▼
┌──────────────────────────────────────────────────────────────────┐
│ NotificationService.SendToUsersAsync(...) — fan-out per op       │
└──────────────────────────────────────────────────────────────────┘

                                 ┌────────────────┐
                                 │/admin/audit-log│ ◄── new page
                                 │  filterable    │      (D9)
                                 │  paginated     │
                                 └───────┬────────┘
                                         │
                                         ▼
                            GET /api/admin/audit-log
```

**Key architectural choices:**

- A single `AdminAuditLog` table with `BeforeJson` / `AfterJson` snapshots — keeps the audit shape uniform across all 6 mutation ops without per-op history tables.
- Mutation + audit write happen in the same `DbContext.SaveChangesAsync()` call, so they atomically succeed or fail together.
- Authorization helper centralizes the C2/C4/C5 status guards, mirroring the V2.3-A `BranchAuthorization` pattern.
- Notifications reuse `NotificationService` — no new SignalR hub.
- Routes live under `/api/admin/...` prefix to keep them visually separated from normal workflow endpoints. Each operation could live on its existing feature controller; placing them under `/admin` makes the audit / authorization scope obvious.

---

## 5. Data Model Changes

### 5.1 New entity `AdminAuditLog`

```csharp
public class AdminAuditLog
{
    public int Id { get; set; }
    public int AdminUserId { get; set; }
    public User AdminUser { get; set; } = null!;
    public AdminActionType ActionType { get; set; }   // enum below
    public string EntityType { get; set; } = string.Empty;  // "Requisition" | "User"
    public int EntityId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string BeforeJson { get; set; } = string.Empty;  // jsonb
    public string? AfterJson { get; set; }                  // jsonb, null on DELETE
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum AdminActionType
{
    DeleteRequisition,
    RollbackStatus,
    ReassignSp,
    UnlockBom,
    UnlockCosting,
    ResetPassword
}
```

Indexes:
- `IX_AdminAuditLog_EntityType_EntityId` — for "show me history of req X" queries (P2 may surface this on `RequisitionDetailPage`).
- `IX_AdminAuditLog_AdminUserId_CreatedAt` — descending; for "what did admin Y do recently" filter.
- `IX_AdminAuditLog_CreatedAt` — descending; default page sort.

`BeforeJson` / `AfterJson` use Postgres `jsonb`. EF Core column type: `"jsonb"`. Snapshot serialization uses `JsonSerializer.Serialize(entity, options)` with reference loop handling and `WriteIndented = false`.

### 5.2 New `User` column

```csharp
public bool MustChangePassword { get; set; } = false;
```

Migration: nullable column with default `false`, backfill existing rows to `false`.

### 5.3 Login flow extension

`POST /auth/login` response gains `mustChangePassword: bool` (always present). When `true`, the web client redirects to a forced `/change-password` screen on first protected route. The existing `POST /auth/change-password` endpoint clears the flag in the same transaction as the password update.

If `mustChangePassword=true`, the client's normal post-login navigation (dashboard) is suppressed until the password is changed. Refresh token issuance still happens at login — i.e., the user IS logged in, but the route guard forces them through `/change-password`.

### 5.4 What does NOT change

- `BranchChangeHistory` and `CustomerChangeHistory` are unchanged. They are NOT retrofitted to write to `AdminAuditLog`.
- `Notification` table — reuse existing. New `NotificationType` enum values added (see §9).
- No FK between `AdminAuditLog` and `QuotationRequest` / `User` on `EntityId`. EntityId is a polymorphic-ish reference; if the target row is deleted (e.g., C1 hard-delete), the audit row stays with `EntityId` pointing at a now-gone PK. This is desired — audit trail outlives the entity.

---

## 6. Backend — Endpoints

All endpoints are gated by `[Authorize(Roles = "Admin")]`. All request bodies require `reason: string` (validated min 5 chars; `400 Bad Request` with `{ error: "Reason is required (min 5 chars)" }` if absent / too short).

### 6.1 C1 — Hard-delete requisition

```
DELETE /api/admin/requisitions/{id}
Body: { "reason": "string" }
Response: 204 No Content
```

Behaviour:
- Loads req with all child collections (RequisitionItems, BomHeaders, BomLines, BomCosts, BomCostLines, CostingDrafts, QuotationApprovals, ApprovalItems, BranchChangeHistories, CustomerChangeHistory rows for this req).
- Snapshot entire aggregate to `BeforeJson` (top-level entity + counts of each child collection — full child serialization could exceed a reasonable row size).
- EF `Remove(req)` triggers cascade per existing FK config. Verify cascade is actually configured for all child types before plan execution; if any are `Restrict`, the plan must add explicit `RemoveRange` calls.
- Write `AdminAuditLog` row.
- `SaveChangesAsync()` (single transaction).
- Fire notifications (see §9).

`404` if req not found. `403` if non-admin.

### 6.2 C2 — Status rollback

```
POST /api/admin/requisitions/{id}/rollback-status
Body: { "targetStatus": "BomInProgress", "reason": "string" }
Response: 200 OK + RequisitionDetailDto
```

Allowed transitions (whitelist; everything else returns `400`):

| From | To |
|---|---|
| `Approved` | `MdReview` |
| `MdReview` | `CostingPending` |
| `CostingInProgress` | `CostingPending` |
| `CostingPending` | `BomInProgress` |
| `BomInProgress` | `BomPending` |

`Rejected` cannot rollback to anything (D10). To restart a Rejected req, admin should delete (C1) and have SP recreate. Forward jumps return `400`.

Side effect: status flip ONLY. BOM, costing, approval rows are preserved. The downstream actor (Accountant after rollback to CostingPending; BomCreator after rollback to BomPending; etc.) sees the existing data and re-edits / re-submits.

Notification fan-out (§9): SP + branch BomCreator + branch Accountant + all MDs.

### 6.3 C3 — Reassign salesperson

```
POST /api/admin/requisitions/{id}/reassign-sp
Body: { "newSalesPersonId": 42, "reason": "string" }
Response: 200 OK + RequisitionDetailDto
```

Validation:
- `newSalesPersonId` must reference a `User` with `Role = SalesPerson` and `IsActive = true`. Otherwise `400`.
- New SP can be from any branch, any group. No constraint that they share the req's branch (admin's call).
- `req.BranchId` is unchanged. If the branch is also wrong, admin runs C-A (V2.3-A `PATCH /api/requisitions/{id}/branch`) separately.

Side effect: `req.SalesPersonId = newSalesPersonId`. Old SP captured in audit `BeforeJson.SalesPersonId`.

Notification fan-out: old SP ("You no longer own req X") + new SP ("You have been assigned req X") + all MDs.

### 6.4 C4 — Unlock BOM

```
POST /api/admin/requisitions/{id}/unlock-bom
Body: { "reason": "string" }
Response: 200 OK + RequisitionDetailDto
```

Allowed when `req.Status ∈ { CostingPending, CostingInProgress, MdReview }`. Blocked on `Approved` (must rollback first via C2 chain) and on `Rejected` (D10) and on the BOM stages themselves (already editable). Returns `400` with current status if blocked.

Side effect: `req.Status = BomInProgress`. Existing `BomHeader` rows + `BomLines` + `BomCosts` preserved untouched (BomCreator can re-edit them naturally).

Notification fan-out: branch BomCreators + branch Accountants ("BOM for req X has been unlocked for re-edit by Admin").

### 6.5 C5 — Unlock costing

```
POST /api/admin/requisitions/{id}/unlock-costing
Body: { "reason": "string" }
Response: 200 OK + RequisitionDetailDto
```

Allowed when `req.Status ∈ { MdReview }`. Blocked on `Approved` (must rollback first via C2) and `Rejected` (D10) and on costing stages themselves.

Side effect: `req.Status = CostingInProgress`. Existing `CostingDrafts` and any submitted costing data preserved.

Notification fan-out: branch Accountants + all MDs ("Costing for req X has been unlocked for re-edit by Admin").

### 6.6 C7 — Reset user password

```
POST /api/admin/users/{id}/reset-password
Body: { "reason": "string" }
Response: 200 OK + { "tempPassword": "Xy7$kQ9pM2!w" }
```

Behaviour:
- Generate 12-char password using cryptographic RNG: at minimum 1 lowercase, 1 uppercase, 1 digit, 1 special. Implementation: helper `PasswordGenerator.Generate(length: 12)` in `Infrastructure/Services/`.
- `user.PasswordHash = BCrypt.HashPassword(tempPassword)`.
- `user.MustChangePassword = true`.
- `user.FailedLoginAttempts = 0`.
- `user.LockedUntil = null`.
- Revoke all `RefreshToken` rows for that user (set `IsRevoked = true`).
- Snapshot `BeforeJson` = `{ MustChangePassword: false, FailedLoginAttempts: N, LockedUntil: ... }` (NOT the password hash). `AfterJson` = `{ MustChangePassword: true, FailedLoginAttempts: 0, LockedUntil: null }`.
- Write `AdminAuditLog` row. **`tempPassword` is NEVER written to the audit log or any application log.**
- `SaveChangesAsync()`.
- Return `tempPassword` in response body. The web modal shows it once; on close it is gone from React state.

No email is sent. Admin hands the temp verbally / via secure channel of their choice.

### 6.7 C9 — Audit log viewer endpoint

```
GET /api/admin/audit-log
Query: page (default 1), pageSize (default 20, max 100),
       actionType?, adminUserId?, entityType?, entityId?, from? (ISO date), to? (ISO date)
Response: 200 OK
{
  "items": [
    {
      "id": 1,
      "adminUserId": 1,
      "adminUserName": "Admin",
      "actionType": "RollbackStatus",
      "entityType": "Requisition",
      "entityId": 42,
      "reason": "MD approved by mistake",
      "beforeJson": "{...}",
      "afterJson": "{...}",
      "createdAt": "2026-04-26T18:00:00Z"
    }
  ],
  "total": 137,
  "page": 1,
  "pageSize": 20
}
```

Sort: `CreatedAt DESC` (most recent first). No sort customization in v1.

---

## 7. Authorization Helper

New file `Infrastructure/Services/AdminOverrideAuthorization.cs`:

```csharp
public static class AdminOverrideAuthorization
{
    public static bool CanDelete(QuotationRequest req)
        => true; // Admin can delete any status

    public static bool CanRollback(RequisitionStatus from, RequisitionStatus to)
        => RollbackWhitelist.TryGetValue(from, out var allowed) && allowed == to;

    public static bool CanUnlockBom(RequisitionStatus current)
        => current is RequisitionStatus.CostingPending
                    or RequisitionStatus.CostingInProgress
                    or RequisitionStatus.MdReview;

    public static bool CanUnlockCosting(RequisitionStatus current)
        => current is RequisitionStatus.MdReview;

    private static readonly Dictionary<RequisitionStatus, RequisitionStatus> RollbackWhitelist = new()
    {
        [RequisitionStatus.Approved] = RequisitionStatus.MdReview,
        [RequisitionStatus.MdReview] = RequisitionStatus.CostingPending,
        [RequisitionStatus.CostingInProgress] = RequisitionStatus.CostingPending,
        [RequisitionStatus.CostingPending] = RequisitionStatus.BomInProgress,
        [RequisitionStatus.BomInProgress] = RequisitionStatus.BomPending,
    };
}
```

Mirrored on the web side: `src/features/admin/adminOverrideAuthorization.ts` exports the same whitelist + predicates so the rollback modal can populate its target-status `<select>` correctly and so admin buttons can hide when not applicable.

---

## 8. Web UI

### 8.1 Components / files

| File | Change |
|---|---|
| `src/features/requisitions/RequisitionDetailPage.tsx` | After existing detail sections, render `<AdminActionsCard requisition={...} />` (gated `if (currentUser.role === "Admin")`). |
| `src/features/admin/AdminActionsCard.tsx` | NEW. Collapsible card titled "Admin actions" (default collapsed). Shows 5 buttons; each opens its modal. Buttons hidden via `AdminOverrideAuthorization` predicates if not applicable to current status. |
| `src/features/admin/modals/DeleteRequisitionModal.tsx` | NEW. Reason textarea (required, min 5). Confirm button + warning ("This will permanently delete the requisition and all related BOM, costing, and approval data. This cannot be undone."). |
| `src/features/admin/modals/RollbackStatusModal.tsx` | NEW. Target-status `<select>` whitelist-filtered for current status. Reason textarea. Confirm. |
| `src/features/admin/modals/ReassignSpModal.tsx` | NEW. SalesPerson picker (uses `useUsers({ role: "SalesPerson", active: true })`). Reason textarea. Confirm. |
| `src/features/admin/modals/UnlockBomModal.tsx` | NEW. Reason textarea + confirm. |
| `src/features/admin/modals/UnlockCostingModal.tsx` | NEW. Reason textarea + confirm. |
| `src/features/admin/users/UsersPage.tsx` | Add "Reset password" row action (visible only to Admin) → opens `<ResetPasswordModal>`. |
| `src/features/admin/users/ResetPasswordModal.tsx` | NEW. Reason textarea + confirm. On success, modal swaps to a one-shot temp-password reveal panel: monospaced text + copy-to-clipboard button + "I've copied it" close. Closing clears the password from React state. |
| `src/features/admin/audit-log/AuditLogPage.tsx` | NEW. Route `/admin/audit-log`. Table columns: timestamp, admin name, action type, entity type + ID (clickable for Requisition → links to detail; for User → no link in v1), reason, "Diff" expand. Filters in toolbar: action-type select, admin-user select, entity-type select, date-range pickers. Pagination footer. |
| `src/features/admin/audit-log/DiffPanel.tsx` | NEW. Accordion content for "Diff" expand. Shows BeforeJson + AfterJson side-by-side with a basic syntax-highlighted `<pre>` (no fancy diff library in v1 — just two panels). |
| `src/api/admin.ts` | NEW. Typed hooks: `useDeleteRequisition`, `useRollbackStatus`, `useReassignSp`, `useUnlockBom`, `useUnlockCosting`, `useResetPassword`, `useAuditLog`. All write hooks invalidate the relevant queries on success (`requisitions`, `requisition/{id}`, `users`, `audit-log`). |
| `src/api/auth.ts` (existing) | Extend `LoginResponse` type with `mustChangePassword: bool`. |
| `src/AppShell.tsx` (or wherever sidebar lives) | New admin nav link "Audit Log" under existing Branches / Groups. |
| `src/App.tsx` (or router) | New route `/admin/audit-log` → `AuditLogPage`. |
| `src/features/auth/ChangePasswordPage.tsx` (existing or new) | If `mustChangePassword=true`, route guard forces redirect here on any protected route. After successful change, normal navigation resumes. |

### 8.2 Permissions in the UI

`AdminActionsCard` always rendered for Admin role; its child buttons hide individually when `AdminOverrideAuthorization` predicates say no. This way Admin sees the card but only the applicable actions.

`/admin/audit-log` route: Admin only. Non-admin gets 403 page (existing pattern).

---

## 9. Notifications

Reuse `NotificationService.SendToUsersAsync(...)` (existing). New `NotificationType` enum values:

| New value | Sent to | Payload |
|---|---|---|
| `RequisitionDeleted` | original SP + branch BomCreator(s) + branch Accountant(s) + all MDs | `{ requisitionId, refNo, deletedByAdminId, reason }` |
| `StatusRolledBack` | SP + branch BomCreator(s) + branch Accountant(s) + all MDs | `{ requisitionId, refNo, fromStatus, toStatus, reason }` |
| `SalesPersonReassigned` | old SP + new SP + all MDs | `{ requisitionId, refNo, oldSpId, newSpId, reason }` |
| `BomUnlocked` | branch BomCreator(s) + branch Accountant(s) | `{ requisitionId, refNo, reason }` |
| `CostingUnlocked` | branch Accountant(s) + all MDs | `{ requisitionId, refNo, reason }` |

Note: C7 `ResetPassword` does NOT send a notification (D7: admin hands temp verbally; user finds out by trying to log in, hitting the forced change-password flow).

---

## 10. Cascading Effects (Detail)

| Op | Affected rows |
|---|---|
| C1 delete req | EF cascade (or explicit RemoveRange if any FK is `Restrict`) deletes: `RequisitionItem`, `BomHeader`, `BomLine`, `BomCost`, `BomCostLine`, `CostingDraft`, `QuotationApproval`, `ApprovalItem`, `BranchChangeHistory` (rows for this req), `CustomerChangeHistory` (rows for this req if scoped per-req — verify in execution). `Notification` rows for this req: do NOT delete (preserve user notification history). |
| C2 rollback | `QuotationRequest.Status` flip only. No BOM/costing/approval data deletion. |
| C3 reassign | `QuotationRequest.SalesPersonId` flip only. `BranchId` unchanged. |
| C4 unlock BOM | `Status → BomInProgress`. Existing `BomHeader` / `BomLines` / `BomCosts` preserved. |
| C5 unlock costing | `Status → CostingInProgress`. Existing `CostingDraft` + costing data preserved. |
| C7 reset password | `User.PasswordHash` (new BCrypt hash), `MustChangePassword=true`, `FailedLoginAttempts=0`, `LockedUntil=null`. All `RefreshToken` rows for that user → `IsRevoked=true`. |

---

## 11. Testing

### 11.1 Backend integration tests (Tests/Admin/)

Use Guid-isolated throwaway entities per V2.3-B Task 15 lesson — NEVER mutate seeded users / reqs.

| File | Coverage |
|---|---|
| `AdminDeleteRequisitionTests` | 204 happy path, cascade verified (BOM + costing + approval rows gone), 403 non-admin, 400 missing reason, 404 unknown id, audit log row written with correct snapshot, notification fan-out fired. |
| `AdminRollbackStatusTests` | Each whitelist transition allowed (5 tests). All forward jumps blocked (sample 5). `Rejected → anything` blocked. 400 on illegal target. Audit log written. Notif fired. |
| `AdminReassignSpTests` | Happy path with valid SP, 400 on non-SP target, 400 on inactive user, audit log written, notif to old + new + MDs. |
| `AdminUnlockBomTests` | Happy path from each allowed status (3 tests). 400 from Approved (chain required). 400 from BomPending / BomInProgress (already editable). 400 from Rejected. BOM data preserved post-unlock. Notif fired. |
| `AdminUnlockCostingTests` | Happy path from MdReview. 400 from Approved / Rejected / earlier statuses. Costing data preserved. Notif fired. |
| `AdminResetPasswordTests` | Happy path returns temp password, BCrypt hash matches new password, `MustChangePassword=true`, `FailedLoginAttempts=0`, `LockedUntil=null`, all refresh tokens revoked, audit log written WITHOUT temp password content. 403 non-admin. 404 unknown id. |
| `AdminAuditLogTests` | Pagination (page, pageSize, total). Each filter parameter narrows results correctly. Sort is CreatedAt DESC. 403 non-admin. |
| `AdminOverrideAuthorizationHelperTests` | Unit tests for the helper (whitelist correctness, edge cases). |
| `LoginMustChangePasswordTests` | Login response includes `mustChangePassword` flag. Existing login tests stay green (flag defaults to false). Change-password endpoint clears the flag. |

Expected: ~50 tests across the suite.

### 11.2 Web tests (bom-web/src/**/__tests__)

| File | Coverage |
|---|---|
| `AdminActionsCard.test.tsx` | Renders for Admin, hidden for other roles, individual buttons hide per status. |
| `DeleteRequisitionModal.test.tsx` | Reason validation (min 5), confirm calls mutation, closes on success, error toast on failure. |
| `RollbackStatusModal.test.tsx` | Target-status `<select>` populated only with whitelist for current status. |
| `ReassignSpModal.test.tsx` | SP picker filtered to active SalesPersons. |
| `UnlockBomModal.test.tsx` / `UnlockCostingModal.test.tsx` | Reason validation, mutation called. |
| `ResetPasswordModal.test.tsx` | On success, swaps to temp-password panel; copy button works; closing clears state. |
| `AuditLogPage.test.tsx` | Filters apply, pagination works, diff expand reveals before/after. |
| `ChangePasswordPage.test.tsx` (extension) | Forced-change route guard kicks in when `mustChangePassword=true`. |

---

## 12. Risks / Open Seams

1. **`BeforeJson` / `AfterJson` size unbounded.** A requisition with 50+ items + full BOM + costing snapshots could push a single audit row toward ~50 KB. Postgres `jsonb` handles this without issue, but if audit volume grows large the table size could become significant. **Mitigation in v1:** for C1 (delete), snapshot only the top-level requisition row + counts of each child collection (e.g., `bomLineCount`, `costingDraftCount`) — do NOT serialize entire child trees. For C2-C5, `Before` = the small subset of fields that changed; `After` = same fields after. P2 candidate: structured-diff format.

2. **No retention / purge.** Audit log grows indefinitely. Acceptable in v1 for this user base (single factory); revisit if row count exceeds ~100k.

3. **`tempPassword` exposure surface.** The temp is in: response body (HTTPS-encrypted), web React state (until modal close), admin's clipboard if they copy. NOT in: audit log, application log, DB outside of its BCrypt hash. The `POST /api/admin/users/{id}/reset-password` endpoint must be excluded from any response-body logging middleware. **Mitigation:** verify `Program.cs` request/response logging middleware (if any) doesn't log this endpoint's body; if it does, add a filter. Plan task should call this out explicitly.

4. **No undo on C1.** Hard-deleting a requisition is irreversible. The reason field and audit log are the only protection. Acceptable per D10 (no soft-delete in v1).

5. **C2 / C4 / C5 do not cascade to dependent staff.** E.g., if Accountant has unsubmitted costing draft and admin C4-unlocks BOM, the draft remains but is now stale (the BOM may change). Acceptable per D6 — drafts are scratch, BomCreator's re-edit becomes the new source of truth and Accountant restarts.

6. **`MustChangePassword` enforcement is client-side.** A determined user could bypass the redirect by hitting API endpoints directly. Acceptable for an internal tool — the password is theirs to know, this is just a UX nudge to rotate it. If we ever care, add server-side enforcement: short-lived token + scope = "change-password-only" until the flag clears.

7. **Notifications use existing fan-out helpers.** If V2.3-A's branch fan-out helper has the wrong shape for one of the new notification types, the plan task may need to add a small variant. Verify during execution.

8. **C3 has no group-membership constraint.** Admin can reassign to any active SP, even one in a different group. This is intentional (D5: admin's call) but means group visibility might change for the req post-reassign. Acceptable; the reassign reason should explain.

9. **`RequisitionDetailPage` UI could become crowded** with the new admin section + existing branch / customer change buttons. Mitigation: the `AdminActionsCard` is a collapsed accordion by default — admin must explicitly expand it.

10. **`/admin/audit-log` page has no real-time stream.** Admin must refresh / re-filter to see new entries. Acceptable; SignalR push for audit log is overkill in v1.

---

## 13. Success Criteria

V2.3-C P1 ships when:

- [ ] All 7 endpoints (`DELETE /admin/requisitions/{id}`, 4 × `POST /admin/requisitions/{id}/{op}`, `POST /admin/users/{id}/reset-password`, `GET /admin/audit-log`) implemented and Admin-gated.
- [ ] `AdminAuditLog` table created via migration; every C1-C5 + C7 op writes a row in the same transaction as the mutation.
- [ ] `User.MustChangePassword` column added; login response carries the flag; change-password endpoint clears it; web client honors forced-change redirect.
- [ ] `AdminOverrideAuthorization` helper centralizes status / role guards.
- [ ] `AdminActionsCard` on `RequisitionDetailPage` (Admin only) with 5 buttons + 5 modals.
- [ ] `UsersPage` row action "Reset password" + modal with one-shot reveal.
- [ ] `/admin/audit-log` page with filters + pagination + diff expand.
- [ ] Notifications fire for all op types (5 new `NotificationType` values).
- [ ] Backend test suite green (~50 new tests); existing 188 backend tests still pass.
- [ ] Web test suite green; existing 217 web tests still pass.
- [ ] On-device smoke (web) walks all 7 ops on a throwaway requisition + throwaway user.
- [ ] Spec § 12 risks reviewed before merge to master.
