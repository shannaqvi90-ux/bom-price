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
- Integration tests use `WebApplicationFactory<Program>` + Testcontainers PostgreSQL. Tests spin up a real database container — **do not mock the database**.

---

## Project Overview

**BOM & Price Approval** is a quotation workflow system for Fujairah Plastic Factory. Sales staff submit quotation requests; BOM creators build Bills of Materials; accountants enter costing data; the Managing Director approves and dispatches PDF quotations via email.

The repository contains an **ASP.NET Core 8 backend** and a **React 19 + Vite web frontend** (`bom-web/`). A React Native mobile app is planned but not yet implemented.

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
    Entities/        # Pure C# entity classes (RequisitionItem, ApprovalItem, etc.)
    Enums/           # UserRole, RequisitionStatus, ItemType, LandedCostType
  Infrastructure/
    Data/
      AppDbContext.cs
      Migrations/
    Services/        # TokenService, EmailService, NotificationService, PdfService, ItemImportService
  Features/          # One folder per feature, each containing *Controller.cs + *Dtos.cs
BomPriceApproval.Tests/
  Auth/              # AuthTests.cs
  Bom/               # BomSaveLinesTests.cs, BomWithCostTests.cs, BomTests.cs
  Costing/           # CostingTests.cs
  Requisitions/      # RequisitionWorkflowTests.cs
bom-web/             # React 19 + Vite + TanStack Query + Tailwind CSS
  src/
    features/        # requisitions, bom, costing, approvals
    types/api.ts     # Shared TypeScript types
    api/             # Axios instance + lookup hooks
```

### Feature-Slice Controllers

Each feature folder under `Features/` is self-contained: a controller and its DTOs live together. There is no application/service layer — controllers call EF Core directly (with branch-isolation queries) or delegate to an infrastructure service.

### Branch Isolation

Every data-access query must scope results to the user's `BranchId` extracted from JWT claims. Admins, Accountants, and the MD have `null` BranchId and see all branches. SalesPersons are isolated to their own branch **and** to their own requisitions only.

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
- Claims: `UserId`, `Email`, `Role`, `BranchId`, `Name`
- Passwords hashed with BCrypt
- SignalR hub authenticates via `?access_token=` query param

### Real-time Notifications

`NotificationService` sends SignalR messages to role-based groups **and** persists a record to the `Notifications` table. Hub is at `/hubs/notifications`.

### Key Infrastructure Services

| Service | Purpose |
|---|---|
| `TokenService` | Generates JWT + refresh tokens |
| `EmailService` | SMTP via MailKit; supports PDF attachments |
| `PdfService` | Generates quotation PDFs using QuestPDF |
| `NotificationService` | SignalR real-time + DB persistence |
| `ItemImportService` | Excel (ClosedXML) and CSV (CsvHelper) import |

### Database

PostgreSQL via EF Core 8 (Npgsql). All financial columns use `HasPrecision(18, 4)` or `(18, 6)`. The `RefNo` column on `QuotationRequest` is a PostgreSQL computed column that formats as `REQ-0001`.

Default connection string targets `Host=localhost;Port=5433;Database=bom_price_approval`.

---

## Configuration Checklist (fresh environment)

1. Set a 32+ character `Jwt:Key` in `appsettings.json` (or user secrets)
2. Configure `Smtp:*` fields for email delivery
3. Ensure PostgreSQL is running on port 5433 (or update the connection string)
4. Run `dotnet ef database update` to apply migrations

---

## Ports

| Service | Port |
|---|---|
| API (HTTP) | 7300 |
| API (HTTPS) | 7301 |
| React web (`bom-web`) | 5300 |
| React Native / Expo (planned) | 5500 |

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

This file drifts over time as the codebase evolves. Re-audit it every ~2 months by running a "reality audit" session (compare each claim against the actual codebase). The last audit was **2026-04-26**.
