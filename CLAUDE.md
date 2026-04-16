# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**BOM & Price Approval** is a quotation workflow system for Fujairah Plastic Factory. Sales staff submit quotation requests; BOM creators build Bills of Materials; accountants enter costing data; the Managing Director approves and dispatches PDF quotations via email.

The repository contains an **ASP.NET Core 8 backend** and a **React 19 + Vite web frontend** (`bom-web/`). A React Native mobile app is planned but not yet implemented.

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

### Tests

Integration tests using `WebApplicationFactory<Program>` with Testcontainers for PostgreSQL. Tests spin up a real database container — do not mock the database.

## Configuration Checklist (fresh environment)

1. Set a 32+ character `Jwt:Key` in `appsettings.json` (or user secrets)
2. Configure `Smtp:*` fields for email delivery
3. Ensure PostgreSQL is running on port 5433 (or update the connection string)
4. Run `dotnet ef database update` to apply migrations

## Ports

| Service | Port |
|---|---|
| API (HTTP) | 7300 |
| API (HTTPS) | 7301 |
| React web (`bom-web`) | 5300 |
| React Native / Expo (planned) | 5500 |

---

# Claude Code - Global Workflow Instructions

## Model Strategy
- Planning, brainstorming, specs, architecture: use opus
- Coding, implementation, debugging, fixes: use sonnet
- Quick questions, small edits: use sonnet

Always confirm which phase we are in before starting work.

## Workflow Rules (Superpowers)
Always follow this order — no exceptions:
1. /model opus → /superpowers:brainstorm
2. /superpowers:write-plan → show me plan, wait for my approval
3. /model sonnet → /superpowers:execute-plan
4. Never start coding without an approved plan
5. Never skip brainstorm phase even for small features

## "continue" Command
When the user types **"continue"** (alone, at the start of a session, or after a compact):
1. Check claude-mem for the most recent session/observations on this project
2. Identify where we left off (last completed task, next pending task)
3. Resume from there — no need to re-explain what was already done


## Memory Rules
- At session start: review CLAUDE.md and auto memory
- During session: if important decision is made, ask "Should I save this to CLAUDE.md?"
- At session end: update CLAUDE.md with progress, decisions, fixes
- Never repeat a fix already in "Recently Fixed"
- Never re-plan something already decided

## Coding Rules
- Never rewrite working code unless explicitly told
- Never fix unreported bugs
- Always confirm before any refactoring
- Write minimal, clean code — no over-engineering
- No unnecessary comments — only where logic is non-obvious
- One problem at a time — never fix multiple things together

## Communication Rules
- If something is unclear: ask before doing
- Always show plan first, code second
- If you find a bug while coding: report it, don't fix it silently
- Be direct — no unnecessary explanations
- If a task will take long: estimate and confirm with me first

## Before Starting Any Task — Checklist
- [ ] Is the plan approved?
- [ ] Is the correct model selected?
- [ ] Is CLAUDE.md up to date?
- [ ] Have I waited for explicit approval from user?
