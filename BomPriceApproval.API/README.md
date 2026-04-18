# BomPriceApproval.API

ASP.NET Core 8 backend for the BOM & Price Approval quotation workflow system.

See repo root `CLAUDE.md` for full architecture notes. This README focuses on running the API locally.

## Prerequisites

- .NET 8 SDK
- PostgreSQL 16+ on `localhost:5433` (or override via the `DefaultConnection` user secret)
- (Optional) Docker — integration tests use Testcontainers PostgreSQL

## First-time setup

```bash
# Register the user-secrets store for this project (already done if you cloned post-session-1)
dotnet user-secrets init --project .

# Required secrets
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5433;Database=bom_price_approval;Username=postgres;Password=<your-password>" --project .
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)" --project .

# (Optional) SMTP for quotation PDFs — if empty, email is silently skipped
dotnet user-secrets set "Email:Host" "smtp.gmail.com" --project .
dotnet user-secrets set "Email:Port" "587" --project .
dotnet user-secrets set "Email:Username" "noreply@example.com" --project .
dotnet user-secrets set "Email:Password" "<app-password>" --project .

# Apply migrations (creates the DB schema and seeds default users)
dotnet ef database update
```

## Run

```bash
dotnet run
```

- HTTP: <http://localhost:7300>
- HTTPS: <https://localhost:7301>
- Swagger (development only): <http://localhost:7300/swagger>

## Test

```bash
# Whole suite (requires Docker running; spins up PostgreSQL via Testcontainers)
dotnet test

# Filter to one class or method
dotnet test --filter "FullyQualifiedName~AuthTests"
```

## Seeded Users (development)

| Email | Password | Role | Branch |
|---|---|---|---|
| `admin@test.com` | `Admin@1234` | Admin | — |
| `ali@test.com` | `Test@1234` | SalesPerson | Fujairah |
| `bob@test.com` | `Test@1234` | BomCreator | Fujairah |
| `sara@test.com` | `Test@1234` | Accountant | — |
| `md@test.com` | `Test@1234` | ManagingDirector | — |
| `eve@test.com` | `Test@1234` | BomCreator | Fujairah |
| `frank@test.com` | `Test@1234` | BomCreator | Al Ain |

These are development-only. Rotate or disable before deploying anywhere reachable from outside localhost.

## Security hardening (see commit history for details)

- Secrets live in user-secrets (dev) / environment variables (prod) — never in `appsettings.json`
- Swagger is wrapped in `IsDevelopment()` so the API schema isn't exposed in production
- JWT + refresh tokens: 15-min access, 7-day refresh, xmin concurrency token to prevent double-use races
- Role updates revoke all active refresh tokens for the affected user
- Admin `POST /api/users/{id}/revoke-sessions` force-logs-out all sessions for one user
- Login has per-IP rate limiting (prod only) + account lockout after 5 failed attempts
- Structured audit logs (`[Audit]` prefix) for login success/failure, role changes, approvals, rejections, session revocations

## Layout

See the root `CLAUDE.md` for Solution Layout, feature-slice controller convention, and workflow state machine.
