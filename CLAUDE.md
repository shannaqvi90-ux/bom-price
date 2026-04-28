# Claude Code Rules

> **Note:** This file is auto-loaded into every Claude Code session. It is the source of truth for project conventions, architecture, and user preferences. Keep it accurate — outdated information here directly causes hallucinations.

---

## User Preferences (Shan)

- **Language:** Respond in Karachi dialect Urdu (Roman script). Use English only for code, commands, file paths, and technical terms.
- **Step-by-step confirmation:** For any major action (refactors, database changes, installing packages, destructive operations), stop and ask for approval first. Proceed only after explicit "haan" / "yes" / "aage badho".
- **Command explanation:** When running complex or unfamiliar commands, briefly explain what the command does before or after executing. For routine commands (`ls`, `cd`, `git status`), skip the explanation.
- **Learning mode:** User is transitioning from accountant to developer. Prefer clarity over brevity. Flag risky operations clearly.
- **No hallucinations:** If uncertain about a file path, API, or convention — ask or verify via file reading. Never guess.

---

## Git Conventions

Claude is permitted to run git commands autonomously as part of Superpowers workflows (which include git worktrees, feature branches, and commits). However, the user previously experienced a major git chaos (1447 pending changes, 9 orphaned worktrees, files accidentally deleted across branches) that took hours to clean up. The rules below exist to prevent recurrence while preserving Superpowers' ability to function.

### ✅ Allowed — Claude may do these autonomously

- `git status`, `git diff`, `git log`, `git branch -v`, `git worktree list` — read-only inspection.
- `git add <specific paths>` or `git add -u` — when staging a logical unit of work.
- `git commit -m "<conventional-commit message>"` — after showing diff + message and receiving user approval (see Safety Procedure below).
- `git checkout <existing branch>` — switching between existing branches.
- `git worktree add <path> <branch>` — **only when Superpowers explicitly invokes this** as part of `using-git-worktrees` skill at the start of a feature (not ad-hoc).
- `git worktree remove <path>` and `git worktree prune` — cleanup after feature completion.
- `git pull` — only when user explicitly requests it.

### ❌ Forbidden — NEVER do these without explicit user approval each time

- **`git push`** — NEVER push autonomously. Push only happens when user explicitly says "push karo" / "push to GitHub".
- **`git reset --hard`** — destructive; wipes uncommitted work.
- **`git push --force` / `git push -f`** — rewrites remote history; dangerous.
- **`git branch -D`** — force-deletes unmerged branches.
- **`git rm -rf`** or mass file deletions via scripts.
- **PR creation** via `gh` CLI or API — NEVER. PRs are opened by user on GitHub manually.

### 🔒 Mandatory safety procedure — follow for every commit

Before running `git commit`, Claude MUST:

1. **Show the diff summary first:**
   ```bash
   git diff --stat
   ```
2. **Propose the commit message** (conventional commit format: `feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`).
3. **Wait for user approval** before running `git commit`. Acceptable approval: "haan", "yes", "commit karo", "ok".
4. **Never batch multiple unrelated changes** into one commit. If the diff spans unrelated concerns, propose splitting into multiple commits and ask which order to do them.

### 🌳 Worktree discipline — mandatory hygiene

Because worktrees accumulate and cause confusion when left uncleaned, Claude MUST follow these rules:

1. **Always announce worktree creation:** When Superpowers is about to create a worktree, Claude must tell the user BEFORE running `git worktree add`:
   > ⚠️ Superpowers is creating a worktree at `.claude/worktrees/<name>` on branch `<branch>` for this feature. This is part of the standard workflow. Proceed?

2. **Verify before creating a new worktree:** Always run `git worktree list` first. If there are 3+ existing worktrees, STOP and ask the user:
   > ⚠️ 3+ worktrees already exist: [list]. Please review whether to clean up old ones before creating a new one.

3. **Cleanup after feature completion:** When a feature is merged into master (or abandoned), Claude MUST:
   - Run `git worktree remove <path>`
   - Run `git worktree prune`
   - Confirm with `git worktree list` that the cleanup succeeded
   - Update the user on the current worktree state

4. **Weekly reminder (session-level):** At the start of a session, if `git worktree list` shows worktrees older than 7 days, Claude must flag them:
   > ⚠️ Yeh worktrees 7+ din puraani hain: [list]. In ko review karna chahiye — merge karni hai ya discard?

5. **Never orphan a worktree:** If a worktree's branch has been deleted or the folder has been manually removed, Claude must run `git worktree prune` to clean up git's internal state.

### 🛑 Mandatory pause points

- After every **5 commits** in a single session, stop and say:
  > ⚠️ 5 commits ban gaye is session mein. Review karna chahte ho? Yeh commits list hain: [list]. Aage badhoon ya ruk jaoon?
  - **Exception:** when Auto Mode is active (user has explicitly opted into continuous autonomous execution), skip this 5-commit pause — only include a one-line running commit summary in the next status update and keep going. The per-commit CLAUDE.md safety procedure (show `git diff --stat` + propose commit message) still applies.
- When working tree has **more than 50 changed files**, STOP before running `git add` (applies even in Auto Mode — this is a safety rule, not a cadence rule). Ask user to review and confirm staging strategy.
- If `git status` shows unexpected branch state (wrong branch checked out, uncommitted changes that user didn't make) — STOP and report to user. Do not auto-fix. (Applies even in Auto Mode.)

### 📝 Commit message conventions

Use [Conventional Commits](https://www.conventionalcommits.org/) format:

- `feat(module): <short description>` — new feature
- `fix(module): <short description>` — bug fix
- `chore: <short description>` — tooling, cleanup, non-feature
- `docs: <short description>` — documentation only
- `refactor(module): <short description>` — code restructure, no behavior change
- `test(module): <short description>` — test additions/fixes

Examples:
- `feat(bom): add per-item wastage validation`
- `fix(auth): populate BranchId claim from refresh token`
- `chore: update gitignore for test artifacts`
- `docs: update CLAUDE.md based on reality audit`

---

## Testing

- Before running integration tests, check if the backend is running:
  ```bash
  curl -s http://localhost:7300/swagger/index.html >/dev/null || echo 'Backend not running - start it first'
  ```
  If it is not running, start it (`dotnet run --project BomPriceApproval.API`) and wait for it to be ready before running tests. Never run integration tests against a stopped backend — they will fail and waste cycles.
- After every file edit, prefer running `dotnet build --nologo -v q` to catch compile errors immediately rather than batching fixes.
- Integration tests use `WebApplicationFactory<Program>` and inherit the API's runtime config — **they hit the real localhost PostgreSQL** at the connection string in user-secrets. (`Testcontainers.PostgreSql` is a stale dependency in `BomPriceApproval.Tests.csproj` but is not actually used anywhere — do not assume containerized DB.) Tests do not mock the database.
- Tests reuse one `WebApplicationFactory` instance per class via `IClassFixture<WebApplicationFactory<Program>>`. Seeded users live across tests — use Guid-isolated throwaway records (V23b lesson).

---

## Project Overview

**BOM & Price Approval** is a quotation workflow system for Fujairah Plastic Factory. Sales staff submit quotation requests; BOM creators build Bills of Materials; accountants enter costing data; the Managing Director approves and dispatches PDF quotations via email.

The repository contains:
- **ASP.NET Core 8 backend** (`BomPriceApproval.API/`)
- **React 19 + Vite web frontend** (`bom-web/`)
- **React Native (Expo) mobile app** (`bom-mobile/`) — V1 shipped 2026-04-27 via EAS preview-channel APK; deploy runbook at `bom-mobile/docs/DEPLOY.md`.

---

## Commands

```bash
# Build
dotnet build

# Run the API (listens on http://localhost:7300)
dotnet run --project BomPriceApproval.API

# Run all tests
dotnet test

# Run a specific test project
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj

# Run a single test class
dotnet test --filter "FullyQualifiedName~AuthTests"

# Run a single test method
dotnet test --filter "FullyQualifiedName~AuthTests.Login_ValidCredentials_ReturnsTokens"

# Apply EF Core migrations
dotnet ef database update --project BomPriceApproval.API
```

Swagger UI is available at `http://localhost:7300/swagger` when the API is running.

---

## Architecture

### Solution Layout

```
BomPriceApproval.API/
  Domain/
    Entities/        # Pure C# entity classes (RequisitionItem, ApprovalItem, AdminAuditLog, …)
    Enums/           # UserRole, RequisitionStatus, ItemType, LandedCostType, AdminActionType, NotificationType
  Infrastructure/
    Data/
      AppDbContext.cs
      Migrations/
    Services/        # TokenService, EmailService, NotificationService, PdfService,
                     # ItemImportService, CustomerImportService, PurchaseLedgerService,
                     # AdminAuditLogger, RevokedJtiCleanupService (HostedService)
  Features/          # Admin, Approvals, Auth, Bom, Branches, Costing, Customers,
                     # ExchangeRates, Groups, Items, Notifications, Processes,
                     # Requisitions, Stats, Users — each a self-contained controller + DTOs slice

BomPriceApproval.Tests/
  Admin/             # AdminAuditLogTests, AdminDeleteRequisitionTests, AdminReassignSpTests,
                     # AdminResetPasswordTests, AdminRollbackStatusTests,
                     # AdminUnlockBomTests, AdminUnlockCostingTests
  Approvals/         # ApprovalGetReviewTests, ApprovalHistoricalReadTests, ApprovalValidationTests, NotificationResilienceTests
  Auth/              # AuthTests, LoginLockoutTests, LoginMustChangePasswordTests, RefreshTokenRaceTests
  Authorization/     # AdminOverrideAuthorizationHelperTests, BranchAuthorizationHelperTests,
                     # SalesAuthorizationHelperTests, SalesGroupAccessTests
  Bom/               # BomTests, BomSaveLinesTests, BomWithCostTests, BomHistoricalReadTests, NotificationResilienceTests
  Branches/          # BranchesAdminCrudTests, UserBranchesEntityTests
  Costing/           # CostingTests, CostingTestFixture (shared helper), CostingLastItemTransitionTests, NotificationResilienceTests
  Customers/         # CustomersCrudTests, CustomerImportTests, CustomersListGroupScopingTests
  Groups/            # GroupsAdminCrudTests
  Infrastructure/    # PasswordGeneratorTests
  Items/             # ItemEditTests, ItemCreateDuplicateTests, ItemsListBranchAndTypeTests, PurchaseLedgerImportTests
  Notifications/     # NotificationCascadeOnBranchChangeTests, SalesGroupNotificationRoutingTests
  Requisitions/      # RequisitionWorkflowTests, ValidationTests, ResubmitTests, ListDateFilterTests,
                     # BranchHistoryReadTests, ChangeBranchTests, ChangeCustomerTests,
                     # RequisitionsCreateBranchPickerTests, RequisitionsListBranchScopingTests, RequisitionsListGroupScopingTests
  Stats/             # AccountantDashboardTests
  Users/             # UserCreateTests, UserBranchesAdminTests, UserGroupAdminTests, UserTokenRevocationTests
  Shared/            # TestDtos, ThrowingNotificationFactory

bom-web/             # React 19 + Vite + TanStack Query + Tailwind CSS
  src/
    features/        # admin, approvals, auth, bom, costing, customers, dashboard,
                     # exchange-rates, items, notifications, requisitions, users
    types/api.ts     # Shared TypeScript types
    api/             # Axios instance + typed query hooks
    components/      # Shared UI primitives (Dialog, Button, OwnedByBadge, BranchPicker, …)
    store/           # Zustand auth store (mustChangePassword flag, etc.)

bom-mobile/          # React Native (Expo Router) + TanStack Query + Reanimated/Moti + Haptics
  app/               # File-based routes: (sales)/, (accountant)/, (md)/, login, profile, notifications
  src/
    api/             # Axios + auth/branches/groups/lookups query hooks
    components/      # OwnedByBadge, BranchSwapSheet, SearchablePicker, StatusChipRow, …
    auth/            # AuthGuard / role-based redirects
    signalr/         # SignalR hub client (?access_token query auth)
    theme/           # corporate-blue palette tokens
  docs/DEPLOY.md     # EAS Android deploy runbook
  eas.json           # development / preview / production profiles
```

### Feature-Slice Controllers

Each feature folder under `Features/` is self-contained: a controller and its DTOs live together. There is no application/service layer — controllers call EF Core directly (with branch-isolation queries) or delegate to an infrastructure service.

### Branch Isolation

Every data-access query must scope results to the user's `BranchId` extracted from JWT claims. Admins, MDs, and Accountants (via `UserBranches` M:N — see V2.3-A) have cross-branch visibility; non-Accountant `null` BranchId roles see all. **SalesPerson visibility is no longer simple "own-branch only"** post-V2.3-A/B — see those sections below for the current per-req branch picker + sales-group peer pool model. Centralized helpers: `BranchAuthorization.UserAuthorizedForBranch` and `SalesAuthorization.VisibleSalesPersonIds`.

### V2.3-A Branch model (post-2026-04-26)

`User.BranchId` semantics now depend on role:

- **SalesPerson**: "default pre-fill hint" for the new-requisition branch picker. SP picks the branch per-req (UI dropdown). Backend accepts `BranchId` in `POST /api/requisitions` payload, with a 1-release transition fallback to `User.BranchId` when payload omits it (logged warning).
- **BomCreator**: binding constraint (single branch — unchanged).
- **Accountant**: ignored — source of truth is `UserBranches` table (M:N join). One Accountant can be assigned to multiple branches via admin `PUT /api/users/{id}/branches`.
- **ManagingDirector / Admin**: cross-branch (unchanged).

Branch authorization is centralized in `BranchAuthorization.UserAuthorizedForBranch(user, branchId, db)`.

Branch reassignment: Accountant + Admin can call `PATCH /api/requisitions/{id}/branch` for reqs in BomPending / BomInProgress / CostingPending. Items must already belong to the new branch (strict block — caller removes mismatched items first).

Branches admin CRUD via `POST/PUT/DELETE /api/branches` (Admin only). DELETE soft-deletes (`IsActive=false`) and 409s if branch is in use.

### V2.3-B Sales Groups (post-2026-04-26)

SalesPersons can be grouped into flat peer "sales groups". All members of a group share full visibility + edit/create rights on each other's customers and requisitions. Groups are branch-agnostic.

- **`User.GroupId`** (nullable FK to `SalesGroups`): only meaningful for SalesPerson role. Other roles are unaffected by group membership.
- **Visibility computation:** `SalesAuthorization.VisibleSalesPersonIds(user, db)` returns either `[user.Id]` (solo SP) or all SP members of the user's group. Used in `RequisitionsController.GetAll`/`Count`/`CanAccess`/`Create`, `CustomersController.GetAll`/`Get`/`Update`.
- **Group management:** Admin + Accountant roles via `POST/PUT/DELETE /api/groups` (soft-delete with in-use guard returning 409 Conflict) and `PUT /api/users/{id}/group` (SP-only target; non-SP target rejected 400).
- **Notifications stay routed by original `SalesPersonId`** — group peers do not receive notifs about each other's reqs (Q8).
- **Q11 clean cut on remove:** clearing `User.GroupId` immediately revokes group visibility in both directions.
- **OwnedByBadge** (web + mobile): non-self requisitions and customers display "by/owned by <SalesPersonName>" subtle text so SPs can attribute peer-pool items at a glance.

> On-device smoke pending — user runs spec §12 11-item checklist when phone tunnel ready.

### V2.3-C P1 Admin Override (post-2026-04-26)

Admin role gets 7 corrective operations contextually surfaced where each entity lives, all writing to a unified `AdminAuditLog` table.

- **C1 Hard-delete requisition** — `DELETE /api/admin/requisitions/{id}`. Cascades children (verified: BomHeader → BomLine + BomCost + BomCostLine + CostingDraft chain). Notif to SP + branch staff + MDs.
- **C2 Status rollback** — `POST /api/admin/requisitions/{id}/rollback-status`. Whitelist transitions only (`Approved→MdReview`, `MdReview→CostingPending`, `CostingInProgress→CostingPending`, `CostingPending→BomInProgress`, `BomInProgress→BomPending`). Forward jumps + Rejected blocked.
- **C3 Reassign salesperson** — `POST /api/admin/requisitions/{id}/reassign-sp`. Full replace; old SP captured in audit. Target must be active SalesPerson role.
- **C4 Unlock BOM** — `POST /api/admin/requisitions/{id}/unlock-bom`. Status → `BomInProgress`. Allowed from `CostingPending` / `CostingInProgress` / `MdReview`. BOM data preserved.
- **C5 Unlock costing** — `POST /api/admin/requisitions/{id}/unlock-costing`. Status → `CostingInProgress`. Allowed from `MdReview` only. Costing data preserved.
- **C7 Reset password** — `POST /api/admin/users/{id}/reset-password`. Returns one-shot 12-char temp (`PasswordGenerator` excludes l/I/o/O/0/1); sets `User.MustChangePassword=true`; clears `FailedLoginAttempts`+`LockedUntil`; revokes all refresh tokens. **TempPassword never written to logs/audit** (verified by test).
- **C9 Audit log** — `GET /api/admin/audit-log` paginated/filtered (actionType, adminUserId, entityType, entityId, from, to). Page at `/admin/audit-log`. Reads only `AdminAuditLog` (legacy `BranchChangeHistory` + `CustomerChangeHistory` viewers stay on RequisitionDetail).

All endpoints require `reason: string` (≥ 5 chars). All gated `[Authorize(Roles="Admin")]`. Helper: `AdminOverrideAuthorization` centralizes the C2/C4/C5 status guards (35 unit tests).

`User.MustChangePassword` extends `LoginResponse`; web `<ForceChangePasswordGuard>` redirects flagged users to `/change-password` until cleared. ChangePassword endpoint blocks `NewPassword == CurrentPassword` (closes silent-bypass).

Web UI: `<AdminActionsCard>` collapsible card on `RequisitionDetailPage` (Admin-only, conditional buttons per status), `ResetPasswordModal` row action on `UsersPage`, `/admin/audit-log` page with filters/pagination/Diff expand.

`AdminAuditLog.ActionType` is stored as **string** for forensic readability (deviates from project convention of int-stored enums; documented inline in `AppDbContext.cs`).

`AdminAuditLogger.Log()` adds row to DbContext but does NOT save — caller owns the transaction (single SaveChanges with the entity mutation).

Web only — no mobile UI in P1. C6 (override approved prices) and C8 (hard-delete customer) deferred to Phase 2.

### V2.3-C P2 Admin Override — Phase 2 (post-2026-04-27)

Phase 2 adds the two deferred operations + finishes structural cleanup of the admin namespace.

- **C6 Override approved prices** — `POST /api/admin/requisitions/{id}/override-prices`. Allowed only from `Status=Approved`. Validates item set is unchanged (D5 frozen — no add/remove), prices ≥ 0, percent fields sum to 100 ± 0.01, non-AED requires foreign price. **Re-snaps exchange rate (D7)** to today's active rate for the original currency. Marks current `QuotationApproval` as `IsSuperseded=true`+`SupersededAt`, creates a new approval with admin's prices and the new `RateSnapshot`. Notes prefixed `[Override]`. Best-effort PDF + email to **SP only (D6 broad — never customer)**; PDF/email failure does not roll back the approval supersession. Notifies SP + original-MD + branch Accountants.
- **C8 Hard-delete customer** — `DELETE /api/admin/customers/{id}`. **Anonymize-in-place (D11)**: `Customer.IsDeleted=true`, `Code` rewritten to `[deleted-{id}]` (Code has unique index → empty would collide), Name→`[Deleted YYYY-MM-DD]`, Email/Phone/Address blanked, `SalesPersonId` cleared. **409 Conflict (D13)** if the customer has any req in active workflow status (BomPending..MdReview); Approved/Rejected reqs do NOT block. Audit BeforeJson includes the full Customer row + array of all referencing req-IDs (D15). Notifies the customer's old SP + their V23b group peers + active Accountants in branches with reqs for this customer.
- **GET current-approval (admin only)** — `GET /api/admin/requisitions/{id}/current-approval`. Returns the current non-superseded approval with per-item prices, used by the C6 modal to pre-fill the editor.

**New schema (V23c P2 migrations):**
- `Customer.IsDeleted` (bool, indexed) + `DeletedAt` (DateTime?) + `DeletedByUserId` (FK Users, Restrict)
- `QuotationApproval.RateSnapshot` (decimal?(18,6)) — per-approval exchange rate (D7 re-snap)
- New `AdminActionType` enum values: `OverridePrices`, `HardDeleteCustomer`
- New `NotificationType` enum values: `PricesOverridden`, `CustomerDeleted`

**Listing filter (E2):** `GET /api/customers` and `GET /api/customers/{id}` filter on `!c.IsDeleted` (no opt-in flag for non-admin). Historical req detail still resolves the customer via FK navigation (anonymize-in-place preserves the row + PK).

**R4 broad email policy (D6):** `ApprovalsController.Approve` already emailed SP (existing behaviour). Body now augmented with customer Name + Email + Phone for SP to forward. `(no contact on file)` fallback if customer fields blank. SP-no-email guard logs warning instead of failing. C6 endpoint follows the same pattern.

**R1 N+1 fan-out fix:** `NotificationService.SendToUsersAsync(IEnumerable<int>, ...)` does a single `SaveChangesAsync` for the recipient set, then per-user SignalR push. Existing `SendAsync` unchanged. Migrated callers: `AdminRequisitionsController` (5 sites), `RequisitionsController` (4 sites), `BomController`, `CostingController`, `ApprovalsController`. Single-recipient `SendAsync` calls left as-is.

**R2 controller split:** `AdminController.cs` deleted. The 7 P1 endpoints + 2 P2 endpoints + 1 GET now live in three thin controllers (all `[Route("api/admin")] [Authorize(Roles="Admin")]`):
- `AdminRequisitionsController` — Delete/Rollback/ReassignSp/UnlockBom/UnlockCosting (P1) + GetCurrentApproval/OverridePrices (P2). 5 + 2 endpoints.
- `AdminCustomersController` — HardDeleteCustomer (P2). 1 endpoint.
- `AdminUsersController` — ResetPassword (P1). 1 endpoint.
- `AdminAuditLogController` — GetAuditLog (P1). 1 endpoint.

**Web UI (P2):** `<OverridePricesModal>` (most complex modal — 5-field-per-item editor, percent-sum validation, currency-aware foreign price field, pre-fills from `GET /api/admin/requisitions/{id}/current-approval`); `<DeleteCustomerModal>` (reason + confirmation checkbox + 409-blocking-reqs panel); `<AdminActionsCard>` extended with "Override Prices" button (visible only when `req.status === "Approved"`); `CustomersPage` row action (Admin-only) to open `<DeleteCustomerModal>`; `<AuditLogPage>` filter UI extended with `Admin User` dropdown (filtered+sorted from `useUsers()`) + `Entity ID` number input + new `OverridePrices`/`HardDeleteCustomer` action labels + `Customer` entity type. Web only — no mobile UI in P2 (D17).

### Multi-Item Requisition Model

A `QuotationRequest` contains multiple `RequisitionItem` entries (each with an `Item` + `ExpectedQty`). BOM and costing are tracked per-item via `BomHeader.RequisitionItemId`. Approval uses `ApprovalItem` (per-item price/margin on `QuotationApproval`).

### Requisition Workflow State Machine

```
BomPending → BomInProgress → CostingPending → CostingInProgress → MdReview → Approved | Rejected
```

Status transitions are role-gated:
- **SalesPerson** creates (→ BomPending), can add/remove items
- **BomCreator** starts/saves/submits BOM per item (BomPending → BomInProgress → CostingPending)
- **Accountant** starts/drafts/submits costing per item (CostingPending → CostingInProgress → MdReview)
- **ManagingDirector** approves/rejects with per-item prices (MdReview → Approved | Rejected)

### Authentication

- JWT access tokens (15 min) + refresh tokens (7 days, stored in DB, revokable)
- Claims: `UserId`, `Email`, `Role`, `BranchId`, `Name`, `jti` (per-token unique id)
- `LoginResponse` includes a `mustChangePassword` flag (set after Admin C7 reset; cleared by `/api/auth/change-password`)
- Passwords hashed with BCrypt; `PasswordValidator` enforces 4-class composition; `PasswordGenerator` is the temp-password helper used by C7
- jti revocation: `RevokedJti` table is checked on every JWT validation (`Program.cs OnTokenValidated`); admin override C7, role change, and logout add to it. `RevokedJtiCleanupService` (HostedService) prunes expired entries.
- SignalR hub at `/hubs/notifications` authenticates via `?access_token=` query param (intercepted in `OnMessageReceived`)
- Rate limiting: `login` policy (20/15min) and `imports` policy (10/hr) — disabled outside Production so dev + tests run freely

### Real-time Notifications

`NotificationService` sends SignalR messages to role-based groups **and** persists a record to the `Notifications` table. Hub is at `/hubs/notifications`.

### Key Infrastructure Services

| Service | Purpose |
|---|---|
| `TokenService` | Generates JWT + refresh tokens |
| `EmailService` | SMTP via MailKit; supports PDF attachments |
| `PdfService` | Generates quotation PDFs using QuestPDF |
| `NotificationService` | SignalR real-time + DB persistence |
| `ItemImportService` | Excel (ClosedXML) and CSV (CsvHelper) import for items |
| `CustomerImportService` | Excel/CSV import for customers |
| `PurchaseLedgerService` | Excel/CSV import for purchase-ledger snapshots (last-purchase-price source) |
| `AdminAuditLogger` | Snapshots Admin override actions to `AdminAuditLog` (caller owns the SaveChanges transaction; serializes enums as **strings**, not ints, for forensic readability — forever-decision) |
| `RevokedJtiCleanupService` | HostedService that prunes expired `RevokedJti` rows so the table stays small |

### Database

PostgreSQL via EF Core 8 (Npgsql). All financial columns use `HasPrecision(18, 4)` or `(18, 6)`. The `RefNo` column on `QuotationRequest` is a PostgreSQL computed column that formats as `REQ-0001`.

`appsettings.json` and `appsettings.Development.json` ship with **empty** `ConnectionStrings:DefaultConnection` — the actual connection string lives in `dotnet user-secrets` (UserSecretsId `bom-price-approval-secrets`). Convention: `Host=localhost;Port=5433;Database=bom_price_approval;Username=postgres;Password=…`. PG password was last rotated 2026-04-27 (history pre-rotation password is now a dead value).

### Logging

Serilog structured logging via `UseSerilog`. Development: human-readable console. Production: `CompactJsonFormatter` (one JSON event per line, ready for Loki/Elastic/Datadog). Microsoft.AspNetCore + EFCore loggers minimum is `Warning` to keep request-cycle noise out. **Never log request/response bodies** — would leak temp passwords from C7 reset (verified-by-test invariant).

### Mobile architecture (`bom-mobile/`)

- **Routing:** Expo Router (file-based). Role-gated layouts under `app/(sales)/`, `app/(accountant)/`, `app/(md)/`. Auth/role redirect handled by `<AuthGuard>` in `src/auth/`.
- **State + data:** TanStack Query for server state; Zustand for auth store. SignalR client at `src/signalr/` mirrors web's notification hub via `?access_token=` query auth.
- **UI primitives:** native `<View>`/`<Text>` + Reanimated/Moti for motion + Haptics on interactive feedback. Corporate-blue palette `#1e40af`. `<OwnedByBadge>` shows on group-peer reqs/customers (V23b).
- **Dev backend access:** emulator uses `adb reverse tcp:7300 tcp:7300` to reach host's API. Cleartext traffic enabled for non-production EAS profiles via `expo-build-properties` plugin (gated by `EAS_BUILD_PROFILE !== "production"` in `app.config.ts`).
- **EAS:** `@shan_naqvi/fpf-quotations` (project ID `4d550ebf-6917-4811-8d0c-db0aa90e559f`). Profiles: `development` (dev client), `preview` (APK for internal QA), `production` (AAB for Play Store). EAS Update channel wired to `preview`. Runbook at `bom-mobile/docs/DEPLOY.md`.
- **Defensive coding:** filter `b.isActive ?? true` (not just `b.isActive`) when consuming branch lists — guards against API drift if `isActive` is ever omitted.

### PWA architecture (`bom-web` post-2026-04-28, P1)

`bom-web` is now an installable PWA. iOS / iPadOS staff install via Safari "Add to Home Screen" → home screen icon → standalone fullscreen launch. Free distribution path; no Apple Developer account.

- **Plugin:** `vite-plugin-pwa@^1.2.0` with Vite 8 (installed via `--legacy-peer-deps` — peer says Vite 3-7 but works with 8 in `injectManifest` mode; build verified emitting `dist/sw.js` + 10-entry precache).
- **Manifest** at `bom-web/public/manifest.webmanifest` (name: `FPF Quotations`, theme: `#1e40af`, blue splash, standalone display).
- **Icons** at `bom-web/public/pwa-{64,192,512}x{...}.png`, `apple-touch-icon-180x180.png`, `maskable-icon-512x512.png`. Generated by `@vite-pwa/assets-generator` from `public/icon-source.png` (copied from `bom-mobile/assets/icon.png`).
- **Service worker source** at `bom-web/src/sw.ts` (Workbox `injectManifest` mode). NetworkFirst (5s timeout, 24h TTL) for `/api/(requisitions|customers|items|branches|users|groups)` lists + corresponding detail patterns. NetworkOnly (passthrough) for `/api/auth/*`, `/api/notifications`, mutations, SignalR `/hubs/*`. Skip-waiting via `postMessage({ type: "SKIP_WAITING" })`.
- **Platform detection** at `src/utils/platform.ts` — `isIOSorIPadOS()` (handles modern iPadOS Mac-disguised UA via `navigator.maxTouchPoints > 1`), `isSafari()`, `isStandalone()`, `isAndroidChrome()`.
- **Hooks** at `src/hooks/`:
  - `usePwaInstall` — captures `beforeinstallprompt` for Android Chrome, exposes platform-aware install state + 30-day localStorage dismiss for iOS modal
  - `useServiceWorker` — Workbox lifecycle wrap; exposes `updateAvailable` + `applyUpdate()` (postMessage SKIP_WAITING + reload on `controllerchange`)
- **UX components** at `src/components/pwa/`:
  - `<InstallModal>` — fullscreen iOS/iPadOS Safari modal with step-by-step Share→Add-to-Home-Screen instructions
  - `<InstallBanner>` — Android Chrome top-right banner with native install button
  - `<UpdateToast>` — Sonner toast on new SW waiting; "Refresh now" / "Later"
  - `<OfflineBanner>` — top sticky red banner on `navigator.onLine === false`
- **Logout cache clear** — `clearPwaApiCaches()` in `src/utils/pwaCaches.ts` deletes `bom-api-list-cache` + `bom-api-detail-cache` before token clear (prevents next user on same device seeing prior user's data).
- **Dev** — `vite-plugin-pwa` `devOptions.enabled = false`. Local `npm run dev` does NOT register SW; debugging clean. Production build emits `sw.js` only.
- **Web Push backend (P2 post-2026-04-28):**
  - `WebPush@1.0.13` NuGet wrapping VAPID + RFC 8030 push protocol
  - `WebPushService` (`Infrastructure/Services/WebPushService.cs`) — singleton DI; `IsConfigured=false` when VAPID keys missing → all `SendAsync` calls become no-ops with one warning log at startup
  - VAPID config: `WebPush:VapidPublicKey/VapidPrivateKey/Subject` in user-secrets (dev) / Fly secrets (prod). `appsettings.json` ships placeholders.
  - `PushSubscription` table — `(UserId, Endpoint UNIQUE, P256dh, Auth, UserAgent?, CreatedAt, LastUsedAt?)`. FK Users with Cascade delete. Indexes: `(UserId)`, `(Endpoint)` unique.
  - `POST/DELETE /api/notifications/push-subscribe` — own-user-only DELETE, idempotent (404→204), POST upserts by Endpoint. Both `[Authorize]` (any role).
  - `NotificationService.SendAsync` + `SendToUsersAsync` extended with `FanOutWebPushAsync` — additive; failure NEVER breaks SignalR + DB. 410 Gone / 404 NotFound auto-deletes dead subscription. Title is hard-coded `"FPF Quotations"`; body is the existing notification message string.
- **Web Push frontend** — P3 pending separate PR (spec `docs/superpowers/specs/2026-04-28-pwa-conversion-design.md`).

---

## Configuration Checklist (fresh environment)

Secrets live in **`dotnet user-secrets`** (NOT in `appsettings.json`, which ships empty for those keys). UserSecretsId: `bom-price-approval-secrets`.

```bash
# 1. Connection string (PostgreSQL on port 5433)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5433;Database=bom_price_approval;Username=postgres;Password=…" \
  --project BomPriceApproval.API

# 2. JWT signing key (32+ chars)
dotnet user-secrets set "Jwt:Key" "<random-32-char-key>" --project BomPriceApproval.API

# 3. SMTP (optional — only needed for outbound email/PDF dispatch)
dotnet user-secrets set "Email:Username" "<gmail-or-smtp-user>" --project BomPriceApproval.API
dotnet user-secrets set "Email:Password" "<smtp-app-password>" --project BomPriceApproval.API

# 4. List current secrets to verify
dotnet user-secrets list --project BomPriceApproval.API

# 5. Apply EF Core migrations
dotnet ef database update --project BomPriceApproval.API
```

---

## Ports

| Service | Port |
|---|---|
| API (HTTP) | 7300 |
| API (HTTPS) | 7301 |
| React web (`bom-web`) | 5300 |
| React Native / Expo Metro (`bom-mobile`) | 8081 |

CORS allowlist in `Program.cs` permits origins `http://localhost:5300` (web) and `http://localhost:8081` (mobile dev).

---

# Global Workflow Instructions

## Model Strategy

- **Planning, brainstorming, specs, architecture:** use `opus`
- **Coding, implementation, debugging, fixes:** use `sonnet`
- **Quick questions, small edits:** use `sonnet`

Always confirm which phase we are in before starting work.

## Workflow Rules (Superpowers)

Always follow this order — no exceptions:

1. `/model opus` → `/superpowers:brainstorm`
2. `/superpowers:write-plan` → show me the plan, wait for my approval
3. `/model sonnet` → `/superpowers:execute-plan`
4. Never start coding without an approved plan
5. Never skip the brainstorm phase — even for small features

**Subagent-driven-development (V2.3-C P1 lesson):** for foundation work (new tables + auditing + multi-endpoint feature slices), `/superpowers:subagent-driven-development` with strict spec-reviewer + code-reviewer dual-review caught real bugs every 1-2 tasks (cascade gaps, silent bypasses, audit serialization issues). High quality but slow (~5h / 25-task plan). Worth choosing it over streamlined alternatives when the foundation is load-bearing. For follow-up work that copies an established pattern, single-reviewer dispatch saves ~30% with no quality regression.

## Context Window Rules

- After every 3–4 completed tasks, stop and say exactly this:
  > ⚠️ We've completed X tasks. Please check context % on Claude HUD and confirm if I should continue.
- Do not proceed until I reply `continue`.
- Never proceed if I say `compact` — wait for me to run `/compact` and confirm done.

## Coding Rules

- Never rewrite working code unless explicitly told.
- Never fix unreported bugs.
- Always confirm before any refactoring.
- Write minimal, clean code — no over-engineering.
- No unnecessary comments — only where logic is non-obvious.
- One problem at a time — never fix multiple things together.

## Communication Rules

- If something is unclear: ask before doing.
- Always show plan first, code second.
- If you find a bug while coding: report it, don't fix it silently.
- Be direct — no unnecessary explanations.
- If a task will take long: estimate and confirm with me first.

## Before Starting Any Task — Checklist

- [ ] Is the plan approved?
- [ ] Is the correct model selected?
- [ ] Is CLAUDE.md up to date?
- [ ] Have I waited for explicit approval from the user?
- [ ] Is the worktree state clean (run `git worktree list` first)?

---

## Maintenance

This file drifts over time as the codebase evolves. Re-audit it every ~2 months by running a "reality audit" session (compare each claim against the actual codebase). The last update was **2026-04-27** (post-V2.3-C P2: C6 + C8 + R1 fan-out + R2 admin split).
