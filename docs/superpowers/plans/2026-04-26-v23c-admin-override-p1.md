# V2.3-C Phase 1 — Admin Override Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement 7 admin-only corrective operations (delete req, status rollback, reassign SP, unlock BOM, unlock costing, reset password, audit log viewer) with a unified `AdminAuditLog` table and forced-password-change UX.

**Architecture:** Routes live under `/api/admin/...`. Each mutation writes its before/after snapshot to `AdminAuditLog` in the same transaction as the entity change. Web UI surfaces ops contextually (`AdminActionsCard` collapsible on `RequisitionDetailPage`; row action on `UsersPage`); audit log gets a dedicated `/admin/audit-log` page. Mobile gets nothing in P1.

**Tech Stack:** ASP.NET Core 8 + EF Core 8 + Npgsql + BCrypt.Net (backend); React 19 + Vite + TanStack Query + vitest + RTL (web).

**Spec:** [docs/superpowers/specs/2026-04-26-v23c-admin-override-design.md](../specs/2026-04-26-v23c-admin-override-design.md) @ commit `acf9cd8`.

**Branch:** `feat/v23c-admin-override-p1` (off `feat/v23b-sales-groups`). Create at start of execution: `git checkout -b feat/v23c-admin-override-p1 feat/v23b-sales-groups`.

**Operational notes** (carry forward from V2.3-A/B sessions):
- API DLL lock workaround: build/test/migrate with `--configuration Release` to avoid Debug-output locks if local API process is running.
- Test isolation: use Guid-isolated throwaway entities (`Email = $"sp-{Guid.NewGuid():N}@test.com"`) — never mutate seeded users (`ali@`, `sara@`, etc.).
- Per-test scope leak fix: use `using var scope = factory.Services.CreateScope();` (not just `using var db = NewDb()`).

---

## File Structure

### Backend — `BomPriceApproval.API/`

**Created:**
- `Domain/Entities/AdminAuditLog.cs` — entity (Id, AdminUserId, ActionType, EntityType, EntityId, Reason, BeforeJson, AfterJson, CreatedAt)
- `Domain/Enums/AdminActionType.cs` — enum (DeleteRequisition, RollbackStatus, ReassignSp, UnlockBom, UnlockCosting, ResetPassword)
- `Infrastructure/Authorization/AdminOverrideAuthorization.cs` — helper (CanRollback, CanUnlockBom, CanUnlockCosting + whitelist)
- `Infrastructure/Services/AdminAuditLogger.cs` — snapshot/serialize service
- `Infrastructure/Services/PasswordGenerator.cs` — random temp generator
- `Infrastructure/Data/Migrations/<TIMESTAMP>_V23c_AdminAuditLog.cs` — EF migration
- `Features/Admin/AdminController.cs` — all 7 endpoints
- `Features/Admin/AdminDtos.cs` — request/response records

**Modified:**
- `Domain/Entities/User.cs` — add `MustChangePassword` bool
- `Domain/Enums/NotificationType.cs` (or wherever notification types live) — add 5 new types
- `Infrastructure/Data/AppDbContext.cs` — add `DbSet<AdminAuditLog>`, OnModelCreating config
- `Features/Auth/AuthDtos.cs` — extend `LoginResponse` record with `MustChangePassword: bool`
- `Features/Auth/AuthController.cs` — populate flag in `Login`/`Refresh`/`ChangePassword` returns; `ChangePassword` clears the flag
- `Infrastructure/Services/NotificationService.cs` (or equivalent) — extend if needed for new fan-outs

### Backend tests — `BomPriceApproval.Tests/`

**Created:**
- `Authorization/AdminOverrideAuthorizationHelperTests.cs`
- `Infrastructure/PasswordGeneratorTests.cs`
- `Admin/AdminDeleteRequisitionTests.cs`
- `Admin/AdminRollbackStatusTests.cs`
- `Admin/AdminReassignSpTests.cs`
- `Admin/AdminUnlockBomTests.cs`
- `Admin/AdminUnlockCostingTests.cs`
- `Admin/AdminResetPasswordTests.cs`
- `Admin/AdminAuditLogTests.cs`
- `Auth/LoginMustChangePasswordTests.cs`

### Web — `bom-web/`

**Created:**
- `src/api/admin.ts` — typed hooks for all 7 admin ops + `useAuditLog`
- `src/features/admin/adminOverrideAuthorization.ts` — TS mirror of backend whitelist + predicates
- `src/features/admin/AdminActionsCard.tsx` — collapsible 5-button card
- `src/features/admin/AdminActionsCard.test.tsx`
- `src/features/admin/modals/DeleteRequisitionModal.tsx` (+ test)
- `src/features/admin/modals/RollbackStatusModal.tsx` (+ test)
- `src/features/admin/modals/ReassignSpModal.tsx` (+ test)
- `src/features/admin/modals/UnlockBomModal.tsx` (+ test)
- `src/features/admin/modals/UnlockCostingModal.tsx` (+ test)
- `src/features/admin/users/ResetPasswordModal.tsx` (+ test)
- `src/features/admin/audit-log/AuditLogPage.tsx` (+ test)
- `src/features/admin/audit-log/DiffPanel.tsx`
- `src/features/auth/ForceChangePasswordGuard.tsx` (+ test)

**Modified:**
- `src/features/requisitions/RequisitionDetailPage.tsx` — render `<AdminActionsCard>` for Admin
- `src/features/admin/users/UsersPage.tsx` — row action "Reset password"
- `src/features/admin/users/UsersPage.test.tsx` — extend
- `src/api/auth.ts` (existing) — extend `LoginResponse` type with `mustChangePassword`
- `src/AppShell.tsx` (or sidebar component) — add "Audit Log" admin nav link
- `src/App.tsx` (or router) — add `/admin/audit-log` route, wrap protected routes with `<ForceChangePasswordGuard>`

### Docs

**Modified:**
- `CLAUDE.md` — document V2.3-C P1 admin override

---

## Task 1: Add `AdminAuditLog` entity + `AdminActionType` enum + EF migration

**Files:**
- Create: `BomPriceApproval.API/Domain/Entities/AdminAuditLog.cs`
- Create: `BomPriceApproval.API/Domain/Enums/AdminActionType.cs`
- Modify: `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs`
- Create: `BomPriceApproval.API/Infrastructure/Data/Migrations/<TIMESTAMP>_V23c_AdminAuditLog.cs` (auto-generated)

- [ ] **Step 1: Create AdminActionType enum**

Create `Domain/Enums/AdminActionType.cs`:

```csharp
namespace BomPriceApproval.API.Domain.Enums;

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

- [ ] **Step 2: Create AdminAuditLog entity**

Create `Domain/Entities/AdminAuditLog.cs`:

```csharp
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Domain.Entities;

public class AdminAuditLog
{
    public int Id { get; set; }
    public int AdminUserId { get; set; }
    public User AdminUser { get; set; } = null!;
    public AdminActionType ActionType { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string BeforeJson { get; set; } = string.Empty;
    public string? AfterJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 3: Wire DbSet + OnModelCreating config in AppDbContext**

In `Infrastructure/Data/AppDbContext.cs`, add near other DbSets:

```csharp
public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
```

Add inside `OnModelCreating(ModelBuilder modelBuilder)`:

```csharp
modelBuilder.Entity<AdminAuditLog>(e =>
{
    e.Property(x => x.EntityType).HasMaxLength(50).IsRequired();
    e.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
    e.Property(x => x.BeforeJson).HasColumnType("jsonb").IsRequired();
    e.Property(x => x.AfterJson).HasColumnType("jsonb");
    e.Property(x => x.ActionType).HasConversion<string>().HasMaxLength(50);

    e.HasOne(x => x.AdminUser)
        .WithMany()
        .HasForeignKey(x => x.AdminUserId)
        .OnDelete(DeleteBehavior.Restrict);

    e.HasIndex(x => new { x.EntityType, x.EntityId });
    e.HasIndex(x => new { x.AdminUserId, x.CreatedAt }).IsDescending(false, true);
    e.HasIndex(x => x.CreatedAt).IsDescending();
});
```

- [ ] **Step 4: Generate migration**

Run (from repo root, with API NOT running):

```bash
dotnet ef migrations add V23c_AdminAuditLog --project BomPriceApproval.API --configuration Release
```

If the API is running and you can't stop it, use `--no-build` after a separate `dotnet build --configuration Release`.

Expected: a new migration file under `Infrastructure/Data/Migrations/` named `<timestamp>_V23c_AdminAuditLog.cs`. Open it and verify it creates a `AdminAuditLogs` table with the columns + indexes above.

- [ ] **Step 5: Apply migration**

```bash
dotnet ef database update --project BomPriceApproval.API --configuration Release
```

Expected: "Done." with no errors. Verify the table exists in PG: `\dt admin_audit_logs` should show it.

- [ ] **Step 6: Verify build green**

```bash
dotnet build --configuration Release --nologo -v q
```

Expected: "Build succeeded." Zero errors.

- [ ] **Step 7: Commit**

```bash
git add BomPriceApproval.API/Domain/Entities/AdminAuditLog.cs \
        BomPriceApproval.API/Domain/Enums/AdminActionType.cs \
        BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs \
        BomPriceApproval.API/Infrastructure/Data/Migrations/
git commit -m "feat(api): add AdminAuditLog entity + AdminActionType enum migration (V23c)"
```

---

## Task 2: Add `User.MustChangePassword` column + `LoginResponse` extension

**Files:**
- Modify: `BomPriceApproval.API/Domain/Entities/User.cs`
- Modify: `BomPriceApproval.API/Features/Auth/AuthDtos.cs`
- Modify: `BomPriceApproval.API/Features/Auth/AuthController.cs`
- Create: `BomPriceApproval.API/Infrastructure/Data/Migrations/<TIMESTAMP>_V23c_UserMustChangePassword.cs`
- Create: `BomPriceApproval.Tests/Auth/LoginMustChangePasswordTests.cs`

- [ ] **Step 1: Add MustChangePassword to User entity**

In `Domain/Entities/User.cs`, add after `IsActive`:

```csharp
public bool MustChangePassword { get; set; } = false;
```

- [ ] **Step 2: Generate migration for the new column**

```bash
dotnet ef migrations add V23c_UserMustChangePassword --project BomPriceApproval.API --configuration Release
```

Open the generated migration. Verify it adds a non-nullable bool column with `defaultValue: false` so existing rows backfill correctly.

- [ ] **Step 3: Apply migration**

```bash
dotnet ef database update --project BomPriceApproval.API --configuration Release
```

Expected: column added, all existing users have `false`.

- [ ] **Step 4: Extend LoginResponse record**

In `Features/Auth/AuthDtos.cs` line 12, change:

```csharp
public record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
```

to:

```csharp
public record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId, bool MustChangePassword);
```

- [ ] **Step 5: Update LoginResponse callers in AuthController**

In `Features/Auth/AuthController.cs`, every `new LoginResponse(...)` call must now pass the `MustChangePassword` flag from the user record. There are calls at line 81 (Login) and line 120 (Refresh) — verify both. Pattern:

```csharp
return Ok(new LoginResponse(accessToken, refreshTokenValue, user.Role.ToString(), user.Id, user.Name, user.BranchId, user.MustChangePassword));
```

For the Refresh endpoint, fetch user.MustChangePassword from DB (the existing code likely already loads the user — confirm).

- [ ] **Step 6: Make ChangePassword clear the flag**

In the existing `ChangePassword` endpoint (in `AuthController.cs`), inside the same transaction that updates `PasswordHash`, set `user.MustChangePassword = false;` before `SaveChangesAsync()`.

- [ ] **Step 7: Write failing tests for LoginResponse + ChangePassword flag**

Create `BomPriceApproval.Tests/Auth/LoginMustChangePasswordTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using BCrypt.Net;

namespace BomPriceApproval.Tests.Auth;

public class LoginMustChangePasswordTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Login_NormalUser_ReturnsMustChangePasswordFalse()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { Email = "ali@test.com", Password = "ali123" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await login.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body!["mustChangePassword"].ToString().Should().Be("False");
    }

    [Fact]
    public async Task Login_FlaggedUser_ReturnsMustChangePasswordTrue()
    {
        // Create a throwaway user with the flag set
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = $"forced-{Guid.NewGuid():N}@test.com";
        var user = new User
        {
            Email = email,
            Name = "Forced User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("temp123!"),
            Role = UserRole.SalesPerson,
            BranchId = 1,
            IsActive = true,
            MustChangePassword = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = "temp123!" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await login.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body!["mustChangePassword"].ToString().Should().Be("True");

        // Cleanup
        db.Users.Remove(user);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ChangePassword_ClearsMustChangePasswordFlag()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = $"forced-{Guid.NewGuid():N}@test.com";
        var user = new User
        {
            Email = email,
            Name = "Forced User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("temp123!"),
            Role = UserRole.SalesPerson,
            BranchId = 1,
            IsActive = true,
            MustChangePassword = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = "temp123!" });
        var loginBody = await login.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var token = loginBody!["accessToken"].ToString();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var change = await client.PostAsJsonAsync("/api/auth/change-password", new { CurrentPassword = "temp123!", NewPassword = "newpass456!" });
        change.StatusCode.Should().Be(HttpStatusCode.OK);
        var changeBody = await change.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        changeBody!["mustChangePassword"].ToString().Should().Be("False");

        // Verify in DB
        var refreshed = await db.Users.FindAsync(user.Id);
        refreshed!.MustChangePassword.Should().BeFalse();

        // Cleanup
        db.Users.Remove(refreshed);
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 8: Run the new tests + verify all auth tests still pass**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~Auth"
```

Expected: 3 new tests + existing AuthTests all green.

- [ ] **Step 9: Commit**

```bash
git add BomPriceApproval.API/Domain/Entities/User.cs \
        BomPriceApproval.API/Features/Auth/AuthDtos.cs \
        BomPriceApproval.API/Features/Auth/AuthController.cs \
        BomPriceApproval.API/Infrastructure/Data/Migrations/ \
        BomPriceApproval.Tests/Auth/LoginMustChangePasswordTests.cs
git commit -m "feat(api): add User.MustChangePassword + extend LoginResponse (V23c)"
```

---

## Task 3: Create `AdminAuditLogger` service

**Files:**
- Create: `BomPriceApproval.API/Infrastructure/Services/AdminAuditLogger.cs`
- Modify: `BomPriceApproval.API/Program.cs` (DI registration)

- [ ] **Step 1: Write the service**

Create `Infrastructure/Services/AdminAuditLogger.cs`:

```csharp
using System.Text.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;

namespace BomPriceApproval.API.Infrastructure.Services;

public class AdminAuditLogger(AppDbContext db)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Adds a new AdminAuditLog row to the DbContext (NOT saved — caller is expected to call SaveChangesAsync in the same transaction as the entity mutation).
    /// </summary>
    public void Log<TBefore, TAfter>(int adminUserId, AdminActionType actionType, string entityType, int entityId, string reason, TBefore before, TAfter? after)
        where TBefore : class
        where TAfter : class
    {
        var row = new AdminAuditLog
        {
            AdminUserId = adminUserId,
            ActionType = actionType,
            EntityType = entityType,
            EntityId = entityId,
            Reason = reason,
            BeforeJson = JsonSerializer.Serialize(before, JsonOpts),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after, JsonOpts),
            CreatedAt = DateTime.UtcNow
        };
        db.AdminAuditLogs.Add(row);
    }
}
```

- [ ] **Step 2: Register in DI**

In `Program.cs`, add alongside other service registrations (look for `builder.Services.AddScoped<...>()` block):

```csharp
builder.Services.AddScoped<AdminAuditLogger>();
```

- [ ] **Step 3: Verify build**

```bash
dotnet build --configuration Release --nologo -v q
```

Expected: green.

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Services/AdminAuditLogger.cs \
        BomPriceApproval.API/Program.cs
git commit -m "feat(api): add AdminAuditLogger snapshot service (V23c)"
```

---

## Task 4: Create `AdminOverrideAuthorization` helper + unit tests

**Files:**
- Create: `BomPriceApproval.API/Infrastructure/Authorization/AdminOverrideAuthorization.cs`
- Create: `BomPriceApproval.Tests/Authorization/AdminOverrideAuthorizationHelperTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BomPriceApproval.Tests/Authorization/AdminOverrideAuthorizationHelperTests.cs`:

```csharp
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Authorization;
using FluentAssertions;

namespace BomPriceApproval.Tests.Authorization;

public class AdminOverrideAuthorizationHelperTests
{
    [Theory]
    [InlineData(RequisitionStatus.Approved, RequisitionStatus.MdReview, true)]
    [InlineData(RequisitionStatus.MdReview, RequisitionStatus.CostingPending, true)]
    [InlineData(RequisitionStatus.CostingInProgress, RequisitionStatus.CostingPending, true)]
    [InlineData(RequisitionStatus.CostingPending, RequisitionStatus.BomInProgress, true)]
    [InlineData(RequisitionStatus.BomInProgress, RequisitionStatus.BomPending, true)]
    public void CanRollback_AllowsWhitelistedTransitions(RequisitionStatus from, RequisitionStatus to, bool expected)
        => AdminOverrideAuthorization.CanRollback(from, to).Should().Be(expected);

    [Theory]
    [InlineData(RequisitionStatus.Approved, RequisitionStatus.BomPending)]   // skip-jump
    [InlineData(RequisitionStatus.BomPending, RequisitionStatus.Approved)]   // forward
    [InlineData(RequisitionStatus.MdReview, RequisitionStatus.Approved)]     // forward
    [InlineData(RequisitionStatus.Rejected, RequisitionStatus.MdReview)]     // from rejected
    [InlineData(RequisitionStatus.Rejected, RequisitionStatus.BomPending)]   // from rejected
    [InlineData(RequisitionStatus.Draft, RequisitionStatus.BomPending)]      // not in whitelist
    public void CanRollback_BlocksDisallowedTransitions(RequisitionStatus from, RequisitionStatus to)
        => AdminOverrideAuthorization.CanRollback(from, to).Should().BeFalse();

    [Theory]
    [InlineData(RequisitionStatus.CostingPending, true)]
    [InlineData(RequisitionStatus.CostingInProgress, true)]
    [InlineData(RequisitionStatus.MdReview, true)]
    [InlineData(RequisitionStatus.Approved, false)]
    [InlineData(RequisitionStatus.Rejected, false)]
    [InlineData(RequisitionStatus.BomPending, false)]
    [InlineData(RequisitionStatus.BomInProgress, false)]
    public void CanUnlockBom_OnlyDownstreamStatuses(RequisitionStatus current, bool expected)
        => AdminOverrideAuthorization.CanUnlockBom(current).Should().Be(expected);

    [Theory]
    [InlineData(RequisitionStatus.MdReview, true)]
    [InlineData(RequisitionStatus.Approved, false)]
    [InlineData(RequisitionStatus.Rejected, false)]
    [InlineData(RequisitionStatus.CostingInProgress, false)]
    [InlineData(RequisitionStatus.CostingPending, false)]
    public void CanUnlockCosting_OnlyMdReview(RequisitionStatus current, bool expected)
        => AdminOverrideAuthorization.CanUnlockCosting(current).Should().Be(expected);
}
```

- [ ] **Step 2: Run tests to confirm they fail (compile error — class doesn't exist)**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminOverrideAuthorization"
```

Expected: build error "AdminOverrideAuthorization does not exist".

- [ ] **Step 3: Create the helper**

Create `Infrastructure/Authorization/AdminOverrideAuthorization.cs`:

```csharp
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Infrastructure.Authorization;

public static class AdminOverrideAuthorization
{
    private static readonly Dictionary<RequisitionStatus, RequisitionStatus> RollbackWhitelist = new()
    {
        [RequisitionStatus.Approved] = RequisitionStatus.MdReview,
        [RequisitionStatus.MdReview] = RequisitionStatus.CostingPending,
        [RequisitionStatus.CostingInProgress] = RequisitionStatus.CostingPending,
        [RequisitionStatus.CostingPending] = RequisitionStatus.BomInProgress,
        [RequisitionStatus.BomInProgress] = RequisitionStatus.BomPending,
    };

    public static bool CanRollback(RequisitionStatus from, RequisitionStatus to)
        => RollbackWhitelist.TryGetValue(from, out var allowed) && allowed == to;

    public static bool CanUnlockBom(RequisitionStatus current)
        => current is RequisitionStatus.CostingPending
                    or RequisitionStatus.CostingInProgress
                    or RequisitionStatus.MdReview;

    public static bool CanUnlockCosting(RequisitionStatus current)
        => current is RequisitionStatus.MdReview;

    /// <summary>For UI/API to populate the rollback target dropdown.</summary>
    public static RequisitionStatus? RollbackTarget(RequisitionStatus from)
        => RollbackWhitelist.TryGetValue(from, out var target) ? target : null;
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminOverrideAuthorization"
```

Expected: all parameterized cases pass.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Authorization/AdminOverrideAuthorization.cs \
        BomPriceApproval.Tests/Authorization/AdminOverrideAuthorizationHelperTests.cs
git commit -m "feat(api): add AdminOverrideAuthorization helper + unit tests (V23c)"
```

---

## Task 5: Create `PasswordGenerator` helper + unit tests

**Files:**
- Create: `BomPriceApproval.API/Infrastructure/Services/PasswordGenerator.cs`
- Create: `BomPriceApproval.Tests/Infrastructure/PasswordGeneratorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BomPriceApproval.Tests/Infrastructure/PasswordGeneratorTests.cs`:

```csharp
using BomPriceApproval.API.Infrastructure.Services;
using FluentAssertions;

namespace BomPriceApproval.Tests.Infrastructure;

public class PasswordGeneratorTests
{
    [Fact]
    public void Generate_DefaultLength_Returns12Chars()
    {
        var pwd = PasswordGenerator.Generate();
        pwd.Length.Should().Be(12);
    }

    [Fact]
    public void Generate_AlwaysContainsAllCharClasses()
    {
        for (int i = 0; i < 100; i++)
        {
            var pwd = PasswordGenerator.Generate();
            pwd.Should().MatchRegex("[a-z]", "needs lowercase");
            pwd.Should().MatchRegex("[A-Z]", "needs uppercase");
            pwd.Should().MatchRegex("[0-9]", "needs digit");
            pwd.Should().MatchRegex("[!@#$%^&*]", "needs special");
        }
    }

    [Fact]
    public void Generate_ProducesDifferentValuesAcrossCalls()
    {
        var samples = Enumerable.Range(0, 50).Select(_ => PasswordGenerator.Generate()).ToHashSet();
        samples.Count.Should().BeGreaterThan(45, "should be near-collision-free at 12 chars");
    }
}
```

- [ ] **Step 2: Run to confirm fail (compile error)**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~PasswordGenerator"
```

Expected: build error.

- [ ] **Step 3: Implement PasswordGenerator**

Create `Infrastructure/Services/PasswordGenerator.cs`:

```csharp
using System.Security.Cryptography;

namespace BomPriceApproval.API.Infrastructure.Services;

public static class PasswordGenerator
{
    private const string Lowercase = "abcdefghijkmnpqrstuvwxyz";   // no l, o
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // no I, O
    private const string Digits = "23456789";                       // no 0, 1
    private const string Specials = "!@#$%^&*";

    public static string Generate(int length = 12)
    {
        if (length < 4) throw new ArgumentException("length >= 4 required for 4 char classes", nameof(length));
        var chars = new char[length];

        // Guarantee one of each class
        chars[0] = PickRandom(Lowercase);
        chars[1] = PickRandom(Uppercase);
        chars[2] = PickRandom(Digits);
        chars[3] = PickRandom(Specials);

        // Fill rest from combined pool
        var pool = Lowercase + Uppercase + Digits + Specials;
        for (int i = 4; i < length; i++) chars[i] = PickRandom(pool);

        // Shuffle (Fisher-Yates with crypto RNG)
        for (int i = length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }

    private static char PickRandom(string source)
        => source[RandomNumberGenerator.GetInt32(source.Length)];
}
```

- [ ] **Step 4: Run tests to confirm pass**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~PasswordGenerator"
```

Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Services/PasswordGenerator.cs \
        BomPriceApproval.Tests/Infrastructure/PasswordGeneratorTests.cs
git commit -m "feat(api): add PasswordGenerator helper + unit tests (V23c)"
```

---

## Task 6: Add 5 new `NotificationType` enum values

**Files:**
- Modify: `BomPriceApproval.API/Domain/Enums/NotificationType.cs` (or wherever the enum lives — find via grep first)

- [ ] **Step 1: Locate the NotificationType enum**

```bash
grep -r "enum NotificationType" BomPriceApproval.API/
```

Identify the file. If the system uses string types instead of an enum, locate the constants file. Adapt steps 2-3 below to that style.

- [ ] **Step 2: Add 5 new values**

Add to the enum (preserving existing values + their numeric positions — APPEND only):

```csharp
RequisitionDeleted,
StatusRolledBack,
SalesPersonReassigned,
BomUnlocked,
CostingUnlocked,
```

- [ ] **Step 3: Verify build green**

```bash
dotnet build --configuration Release --nologo -v q
```

Expected: green. If any switch statement on `NotificationType` somewhere in the codebase doesn't have a default case, the new values will trigger a compiler warning — handle it.

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.API/Domain/Enums/NotificationType.cs
git commit -m "feat(api): add 5 NotificationType values for V23c admin overrides"
```

---

## Task 7: C9 endpoint — `GET /api/admin/audit-log` + tests

(Built first so subsequent endpoint tests can verify rows landed in the log.)

**Files:**
- Create: `BomPriceApproval.API/Features/Admin/AdminController.cs` (initial skeleton with C9 only — extended in tasks 8-13)
- Create: `BomPriceApproval.API/Features/Admin/AdminDtos.cs`
- Create: `BomPriceApproval.Tests/Admin/AdminAuditLogTests.cs`

- [ ] **Step 1: Write the failing test**

Create `BomPriceApproval.Tests/Admin/AdminAuditLogTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Admin;

public class AdminAuditLogTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private async Task<HttpClient> AsAdmin()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { Email = "admin@test.com", Password = "admin123" });
        var body = await login.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var token = body!["accessToken"].ToString();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    [Fact]
    public async Task GetAuditLog_AsAdmin_ReturnsPagedList()
    {
        // Seed a throwaway audit row
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Users.FirstAsync(u => u.Role == UserRole.Admin);
        var row = new AdminAuditLog
        {
            AdminUserId = admin.Id,
            ActionType = AdminActionType.DeleteRequisition,
            EntityType = "Requisition",
            EntityId = 99999,
            Reason = $"test-{Guid.NewGuid():N}",
            BeforeJson = "{}",
            CreatedAt = DateTime.UtcNow
        };
        db.AdminAuditLogs.Add(row);
        await db.SaveChangesAsync();

        var client = await AsAdmin();
        var resp = await client.GetAsync("/api/admin/audit-log?page=1&pageSize=20");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body!.Should().ContainKey("items");
        body.Should().ContainKey("total");

        // Cleanup
        db.AdminAuditLogs.Remove(row);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAuditLog_AsNonAdmin_Returns403()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { Email = "ali@test.com", Password = "ali123" });
        var body = await login.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var token = body!["accessToken"].ToString();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var resp = await client.GetAsync("/api/admin/audit-log");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAuditLog_FilterByActionType_NarrowsResults()
    {
        var client = await AsAdmin();
        var resp = await client.GetAsync("/api/admin/audit-log?actionType=DeleteRequisition&page=1&pageSize=5");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        // We can't assert exact counts without a clean db; just verify shape.
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body!.Should().ContainKey("items");
    }
}
```

- [ ] **Step 2: Run to confirm fail**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminAuditLog"
```

Expected: 404 on the endpoint (endpoint doesn't exist yet).

- [ ] **Step 3: Create AdminDtos.cs**

Create `Features/Admin/AdminDtos.cs`:

```csharp
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Admin;

public record DeleteRequisitionRequest(string Reason);
public record RollbackStatusRequest(RequisitionStatus TargetStatus, string Reason);
public record ReassignSpRequest(int NewSalesPersonId, string Reason);
public record UnlockBomRequest(string Reason);
public record UnlockCostingRequest(string Reason);
public record ResetPasswordRequest(string Reason);
public record ResetPasswordResponse(string TempPassword);

public record AuditLogItemDto(
    int Id,
    int AdminUserId,
    string AdminUserName,
    string ActionType,
    string EntityType,
    int EntityId,
    string Reason,
    string BeforeJson,
    string? AfterJson,
    DateTime CreatedAt);

public record AuditLogPagedResponse(
    IReadOnlyList<AuditLogItemDto> Items,
    int Total,
    int Page,
    int PageSize);
```

- [ ] **Step 4: Create AdminController with C9 only**

Create `Features/Admin/AdminController.cs`:

```csharp
using System.Security.Claims;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Admin;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController(AppDbContext db) : ControllerBase
{
    [HttpGet("audit-log")]
    public async Task<IActionResult> GetAuditLog(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] AdminActionType? actionType = null,
        [FromQuery] int? adminUserId = null,
        [FromQuery] string? entityType = null,
        [FromQuery] int? entityId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var q = db.AdminAuditLogs.Include(x => x.AdminUser).AsQueryable();
        if (actionType.HasValue) q = q.Where(x => x.ActionType == actionType.Value);
        if (adminUserId.HasValue) q = q.Where(x => x.AdminUserId == adminUserId.Value);
        if (!string.IsNullOrEmpty(entityType)) q = q.Where(x => x.EntityType == entityType);
        if (entityId.HasValue) q = q.Where(x => x.EntityId == entityId.Value);
        if (from.HasValue) q = q.Where(x => x.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(x => x.CreatedAt <= to.Value);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AuditLogItemDto(
                x.Id,
                x.AdminUserId,
                x.AdminUser.Name,
                x.ActionType.ToString(),
                x.EntityType,
                x.EntityId,
                x.Reason,
                x.BeforeJson,
                x.AfterJson,
                x.CreatedAt))
            .ToListAsync();

        return Ok(new AuditLogPagedResponse(items, total, page, pageSize));
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
}
```

- [ ] **Step 5: Run tests to confirm pass**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminAuditLog"
```

Expected: 3 tests pass.

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Features/Admin/AdminController.cs \
        BomPriceApproval.API/Features/Admin/AdminDtos.cs \
        BomPriceApproval.Tests/Admin/AdminAuditLogTests.cs
git commit -m "feat(api): GET /api/admin/audit-log paginated viewer (V23c C9)"
```

---

## Task 8: C1 — `DELETE /api/admin/requisitions/{id}` + tests

**Files:**
- Modify: `BomPriceApproval.API/Features/Admin/AdminController.cs` (add C1 method)
- Create: `BomPriceApproval.Tests/Admin/AdminDeleteRequisitionTests.cs`

- [ ] **Step 1: Write the failing test**

Create `BomPriceApproval.Tests/Admin/AdminDeleteRequisitionTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Admin;

public class AdminDeleteRequisitionTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private async Task<HttpClient> AsAdmin()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { Email = "admin@test.com", Password = "admin123" });
        var body = await login.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", body!["accessToken"].ToString());
        return client;
    }

    [Fact]
    public async Task DeleteRequisition_HappyPath_Returns204AndCascades()
    {
        // Seed a throwaway requisition
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sp = await db.Users.FirstAsync(u => u.Role == UserRole.SalesPerson);
        var customer = await db.Customers.FirstAsync();
        var req = new QuotationRequest
        {
            CustomerId = customer.Id,
            SalesPersonId = sp.Id,
            BranchId = sp.BranchId ?? 1,
            Status = RequisitionStatus.BomPending,
            Notes = $"throwaway-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };
        db.QuotationRequests.Add(req);
        await db.SaveChangesAsync();
        var reqId = req.Id;

        var client = await AsAdmin();
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/requisitions/{reqId}")
        {
            Content = JsonContent.Create(new { Reason = "test cleanup" })
        });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stillExists = await db.QuotationRequests.AnyAsync(r => r.Id == reqId);
        stillExists.Should().BeFalse();

        var auditWritten = await db.AdminAuditLogs.AnyAsync(a => a.EntityType == "Requisition" && a.EntityId == reqId);
        auditWritten.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteRequisition_MissingReason_Returns400()
    {
        var client = await AsAdmin();
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/admin/requisitions/1")
        {
            Content = JsonContent.Create(new { Reason = "" })
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteRequisition_AsNonAdmin_Returns403()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { Email = "ali@test.com", Password = "ali123" });
        var body = await login.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", body!["accessToken"].ToString());

        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/admin/requisitions/1")
        {
            Content = JsonContent.Create(new { Reason = "test" })
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteRequisition_UnknownId_Returns404()
    {
        var client = await AsAdmin();
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/admin/requisitions/9999999")
        {
            Content = JsonContent.Create(new { Reason = "test" })
        });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 2: Run to confirm 404 on the endpoint (because endpoint not yet built)**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminDeleteRequisition"
```

Expected: tests fail (the happy-path test will see 404 instead of 204).

- [ ] **Step 3: Add C1 method to AdminController**

Inject `AdminAuditLogger` and `NotificationService` into AdminController constructor:

```csharp
public class AdminController(AppDbContext db, AdminAuditLogger audit, NotificationService notify) : ControllerBase
```

(Adjust `NotificationService` import path if it differs — grep `class NotificationService` to find it.)

Add new method:

```csharp
[HttpDelete("requisitions/{id}")]
public async Task<IActionResult> DeleteRequisition(int id, [FromBody] DeleteRequisitionRequest body)
{
    if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
        return BadRequest(new { error = "Reason is required (min 5 chars)" });

    var req = await db.QuotationRequests
        .Include(r => r.Items)
        .FirstOrDefaultAsync(r => r.Id == id);
    if (req is null) return NotFound();

    var snapshot = new
    {
        req.Id,
        req.RefNo,
        req.Status,
        req.SalesPersonId,
        req.BranchId,
        req.CustomerId,
        ItemCount = req.Items.Count,
        BomHeaderCount = await db.BomHeaders.CountAsync(b => b.RequisitionId == id),
        ApprovalCount = await db.QuotationApprovals.CountAsync(a => a.RequisitionId == id)
    };

    var spId = req.SalesPersonId;
    var branchId = req.BranchId;
    var refNo = req.RefNo;

    db.QuotationRequests.Remove(req);
    audit.Log(CurrentUserId, AdminActionType.DeleteRequisition, "Requisition", id, body.Reason, snapshot, after: (object?)null);
    await db.SaveChangesAsync();

    // Notify SP + branch staff + MDs
    var recipientIds = await db.Users
        .Where(u => (u.Id == spId)
            || (u.BranchId == branchId && (u.Role == UserRole.BomCreator || u.Role == UserRole.Accountant))
            || u.Role == UserRole.ManagingDirector)
        .Select(u => u.Id)
        .ToListAsync();
    await notify.SendToUsersAsync(recipientIds, NotificationType.RequisitionDeleted,
        $"Requisition {refNo} was deleted by Admin",
        new { requisitionId = id, refNo, reason = body.Reason });

    return NoContent();
}
```

> Note: if EF cascade isn't configured for all child types (BomHeader, BomLine, BomCost, BomCostLine, CostingDraft, QuotationApproval, ApprovalItem, BranchChangeHistory, CustomerChangeHistory), the SaveChanges will fail with FK constraint error. If that happens, add explicit `RemoveRange` calls before `Remove(req)`. Verify cascade in `AppDbContext.OnModelCreating` first.

- [ ] **Step 4: Run tests to confirm pass**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminDeleteRequisition"
```

Expected: 4 tests pass. If cascade fails, add explicit `RemoveRange` per the note above and re-run.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Admin/AdminController.cs \
        BomPriceApproval.Tests/Admin/AdminDeleteRequisitionTests.cs
git commit -m "feat(api): DELETE /api/admin/requisitions/{id} hard-delete with audit (V23c C1)"
```

---

## Task 9: C2 — `POST /api/admin/requisitions/{id}/rollback-status` + tests

**Files:**
- Modify: `BomPriceApproval.API/Features/Admin/AdminController.cs` (add C2 method)
- Create: `BomPriceApproval.Tests/Admin/AdminRollbackStatusTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BomPriceApproval.Tests/Admin/AdminRollbackStatusTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Admin;

public class AdminRollbackStatusTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private async Task<HttpClient> AsAdmin()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { Email = "admin@test.com", Password = "admin123" });
        var body = await login.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", body!["accessToken"].ToString());
        return client;
    }

    private async Task<int> SeedReq(RequisitionStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sp = await db.Users.FirstAsync(u => u.Role == UserRole.SalesPerson);
        var cust = await db.Customers.FirstAsync();
        var req = new QuotationRequest
        {
            CustomerId = cust.Id, SalesPersonId = sp.Id, BranchId = sp.BranchId ?? 1,
            Status = status, Notes = $"throwaway-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow
        };
        db.QuotationRequests.Add(req);
        await db.SaveChangesAsync();
        return req.Id;
    }

    [Theory]
    [InlineData(RequisitionStatus.Approved, RequisitionStatus.MdReview)]
    [InlineData(RequisitionStatus.MdReview, RequisitionStatus.CostingPending)]
    [InlineData(RequisitionStatus.CostingInProgress, RequisitionStatus.CostingPending)]
    [InlineData(RequisitionStatus.CostingPending, RequisitionStatus.BomInProgress)]
    [InlineData(RequisitionStatus.BomInProgress, RequisitionStatus.BomPending)]
    public async Task Rollback_AllowedTransition_Returns200AndFlipsStatus(RequisitionStatus from, RequisitionStatus to)
    {
        var id = await SeedReq(from);
        var client = await AsAdmin();
        var resp = await client.PostAsJsonAsync($"/api/admin/requisitions/{id}/rollback-status",
            new { TargetStatus = to.ToString(), Reason = "test rollback" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var refreshed = await db.QuotationRequests.FindAsync(id);
        refreshed!.Status.Should().Be(to);

        // cleanup
        db.QuotationRequests.Remove(refreshed);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Rollback_FromRejected_Returns400()
    {
        var id = await SeedReq(RequisitionStatus.Rejected);
        var client = await AsAdmin();
        var resp = await client.PostAsJsonAsync($"/api/admin/requisitions/{id}/rollback-status",
            new { TargetStatus = "MdReview", Reason = "test" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.QuotationRequests.Remove((await db.QuotationRequests.FindAsync(id))!);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Rollback_ForwardJump_Returns400()
    {
        var id = await SeedReq(RequisitionStatus.BomPending);
        var client = await AsAdmin();
        var resp = await client.PostAsJsonAsync($"/api/admin/requisitions/{id}/rollback-status",
            new { TargetStatus = "Approved", Reason = "test" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.QuotationRequests.Remove((await db.QuotationRequests.FindAsync(id))!);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Rollback_WritesAuditLog()
    {
        var id = await SeedReq(RequisitionStatus.Approved);
        var client = await AsAdmin();
        var reason = $"audit-test-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync($"/api/admin/requisitions/{id}/rollback-status",
            new { TargetStatus = "MdReview", Reason = reason });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditRow = await db.AdminAuditLogs.FirstOrDefaultAsync(a => a.Reason == reason);
        auditRow.Should().NotBeNull();
        auditRow!.ActionType.Should().Be(AdminActionType.RollbackStatus);
        auditRow.EntityType.Should().Be("Requisition");
        auditRow.EntityId.Should().Be(id);

        // cleanup
        db.AdminAuditLogs.Remove(auditRow);
        db.QuotationRequests.Remove((await db.QuotationRequests.FindAsync(id))!);
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 2: Run to confirm fail**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminRollbackStatus"
```

Expected: 404 from missing endpoint.

- [ ] **Step 3: Add C2 method to AdminController**

```csharp
[HttpPost("requisitions/{id}/rollback-status")]
public async Task<IActionResult> RollbackStatus(int id, [FromBody] RollbackStatusRequest body)
{
    if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
        return BadRequest(new { error = "Reason is required (min 5 chars)" });

    var req = await db.QuotationRequests.FindAsync(id);
    if (req is null) return NotFound();

    if (!Infrastructure.Authorization.AdminOverrideAuthorization.CanRollback(req.Status, body.TargetStatus))
        return BadRequest(new { error = $"Cannot rollback {req.Status} → {body.TargetStatus}" });

    var before = new { req.Id, req.Status };
    req.Status = body.TargetStatus;
    var after = new { req.Id, req.Status };

    audit.Log(CurrentUserId, AdminActionType.RollbackStatus, "Requisition", id, body.Reason, before, after);
    await db.SaveChangesAsync();

    var recipientIds = await db.Users
        .Where(u => u.Id == req.SalesPersonId
            || (u.BranchId == req.BranchId && (u.Role == UserRole.BomCreator || u.Role == UserRole.Accountant))
            || u.Role == UserRole.ManagingDirector)
        .Select(u => u.Id).ToListAsync();
    await notify.SendToUsersAsync(recipientIds, NotificationType.StatusRolledBack,
        $"Requisition {req.RefNo} status rolled back to {req.Status} by Admin",
        new { requisitionId = id, fromStatus = before.Status.ToString(), toStatus = after.Status.ToString(), reason = body.Reason });

    return Ok(new { req.Id, req.Status });
}
```

- [ ] **Step 4: Run tests to confirm pass**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminRollbackStatus"
```

Expected: 8 tests pass (5 theory + 3 facts).

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Admin/AdminController.cs \
        BomPriceApproval.Tests/Admin/AdminRollbackStatusTests.cs
git commit -m "feat(api): POST rollback-status with whitelist + audit (V23c C2)"
```

---

## Task 10: C3 — `POST /api/admin/requisitions/{id}/reassign-sp` + tests

**Files:**
- Modify: `BomPriceApproval.API/Features/Admin/AdminController.cs`
- Create: `BomPriceApproval.Tests/Admin/AdminReassignSpTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BomPriceApproval.Tests/Admin/AdminReassignSpTests.cs`. Pattern matches AdminRollbackStatusTests above. Cases:

```csharp
[Fact]
public async Task ReassignSp_HappyPath_Returns200AndUpdates()
{
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Seed two throwaway SPs + one req owned by SP1
    var sp1 = new User { Email = $"sp1-{Guid.NewGuid():N}@test.com", Name = "Sp1", PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw"), Role = UserRole.SalesPerson, BranchId = 1, IsActive = true };
    var sp2 = new User { Email = $"sp2-{Guid.NewGuid():N}@test.com", Name = "Sp2", PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw"), Role = UserRole.SalesPerson, BranchId = 1, IsActive = true };
    db.Users.AddRange(sp1, sp2);
    var cust = await db.Customers.FirstAsync();
    await db.SaveChangesAsync();
    var req = new QuotationRequest { CustomerId = cust.Id, SalesPersonId = sp1.Id, BranchId = 1, Status = RequisitionStatus.BomPending, Notes = $"t-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow };
    db.QuotationRequests.Add(req);
    await db.SaveChangesAsync();

    var client = await AsAdmin();
    var resp = await client.PostAsJsonAsync($"/api/admin/requisitions/{req.Id}/reassign-sp",
        new { NewSalesPersonId = sp2.Id, Reason = "sp1 left" });
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var refreshed = await db.QuotationRequests.FindAsync(req.Id);
    refreshed!.SalesPersonId.Should().Be(sp2.Id);

    // cleanup
    db.QuotationRequests.Remove(refreshed);
    db.Users.RemoveRange(sp1, sp2);
    await db.SaveChangesAsync();
}

[Fact]
public async Task ReassignSp_NonSpTarget_Returns400()
{
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var sp = new User { Email = $"sp-{Guid.NewGuid():N}@test.com", Name = "Sp", PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw"), Role = UserRole.SalesPerson, BranchId = 1, IsActive = true };
    var acct = await db.Users.FirstAsync(u => u.Role == UserRole.Accountant);
    var cust = await db.Customers.FirstAsync();
    db.Users.Add(sp);
    await db.SaveChangesAsync();
    var req = new QuotationRequest { CustomerId = cust.Id, SalesPersonId = sp.Id, BranchId = 1, Status = RequisitionStatus.BomPending, Notes = $"t-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow };
    db.QuotationRequests.Add(req);
    await db.SaveChangesAsync();

    var client = await AsAdmin();
    var resp = await client.PostAsJsonAsync($"/api/admin/requisitions/{req.Id}/reassign-sp",
        new { NewSalesPersonId = acct.Id, Reason = "wrong target type" });
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    db.QuotationRequests.Remove(req);
    db.Users.Remove(sp);
    await db.SaveChangesAsync();
}

[Fact]
public async Task ReassignSp_InactiveTarget_Returns400()
{
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var sp1 = new User { Email = $"sp1-{Guid.NewGuid():N}@test.com", Name = "Sp1", PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw"), Role = UserRole.SalesPerson, BranchId = 1, IsActive = true };
    var sp2 = new User { Email = $"sp2-{Guid.NewGuid():N}@test.com", Name = "Sp2 inactive", PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw"), Role = UserRole.SalesPerson, BranchId = 1, IsActive = false };
    db.Users.AddRange(sp1, sp2);
    var cust = await db.Customers.FirstAsync();
    await db.SaveChangesAsync();
    var req = new QuotationRequest { CustomerId = cust.Id, SalesPersonId = sp1.Id, BranchId = 1, Status = RequisitionStatus.BomPending, Notes = $"t-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow };
    db.QuotationRequests.Add(req);
    await db.SaveChangesAsync();

    var client = await AsAdmin();
    var resp = await client.PostAsJsonAsync($"/api/admin/requisitions/{req.Id}/reassign-sp",
        new { NewSalesPersonId = sp2.Id, Reason = "test inactive target" });
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    db.QuotationRequests.Remove(req);
    db.Users.RemoveRange(sp1, sp2);
    await db.SaveChangesAsync();
}

[Fact]
public async Task ReassignSp_WritesAuditWithOldSpId()
{
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var sp1 = new User { Email = $"sp1-{Guid.NewGuid():N}@test.com", Name = "Sp1", PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw"), Role = UserRole.SalesPerson, BranchId = 1, IsActive = true };
    var sp2 = new User { Email = $"sp2-{Guid.NewGuid():N}@test.com", Name = "Sp2", PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw"), Role = UserRole.SalesPerson, BranchId = 1, IsActive = true };
    db.Users.AddRange(sp1, sp2);
    var cust = await db.Customers.FirstAsync();
    await db.SaveChangesAsync();
    var req = new QuotationRequest { CustomerId = cust.Id, SalesPersonId = sp1.Id, BranchId = 1, Status = RequisitionStatus.BomPending, Notes = $"t-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow };
    db.QuotationRequests.Add(req);
    await db.SaveChangesAsync();

    var reason = $"audit-{Guid.NewGuid():N}";
    var client = await AsAdmin();
    await client.PostAsJsonAsync($"/api/admin/requisitions/{req.Id}/reassign-sp",
        new { NewSalesPersonId = sp2.Id, Reason = reason });

    var auditRow = await db.AdminAuditLogs.FirstOrDefaultAsync(a => a.Reason == reason);
    auditRow.Should().NotBeNull();
    auditRow!.ActionType.Should().Be(AdminActionType.ReassignSp);
    auditRow.BeforeJson.Should().Contain(sp1.Id.ToString());
    auditRow.AfterJson.Should().NotBeNull();
    auditRow.AfterJson!.Should().Contain(sp2.Id.ToString());

    db.AdminAuditLogs.Remove(auditRow);
    db.QuotationRequests.Remove(req);
    db.Users.RemoveRange(sp1, sp2);
    await db.SaveChangesAsync();
}
```

- [ ] **Step 2: Run to confirm fail**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminReassignSp"
```

- [ ] **Step 3: Add C3 method**

```csharp
[HttpPost("requisitions/{id}/reassign-sp")]
public async Task<IActionResult> ReassignSp(int id, [FromBody] ReassignSpRequest body)
{
    if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
        return BadRequest(new { error = "Reason is required (min 5 chars)" });

    var req = await db.QuotationRequests.FindAsync(id);
    if (req is null) return NotFound();

    var newSp = await db.Users.FindAsync(body.NewSalesPersonId);
    if (newSp is null || newSp.Role != UserRole.SalesPerson || !newSp.IsActive)
        return BadRequest(new { error = "Target user must be an active SalesPerson" });

    var oldSpId = req.SalesPersonId;
    var before = new { req.Id, OldSalesPersonId = oldSpId };
    req.SalesPersonId = newSp.Id;
    var after = new { req.Id, NewSalesPersonId = newSp.Id };

    audit.Log(CurrentUserId, AdminActionType.ReassignSp, "Requisition", id, body.Reason, before, after);
    await db.SaveChangesAsync();

    var recipientIds = await db.Users
        .Where(u => u.Id == oldSpId || u.Id == newSp.Id || u.Role == UserRole.ManagingDirector)
        .Select(u => u.Id).ToListAsync();
    await notify.SendToUsersAsync(recipientIds, NotificationType.SalesPersonReassigned,
        $"Requisition {req.RefNo} reassigned by Admin",
        new { requisitionId = id, oldSpId, newSpId = newSp.Id, reason = body.Reason });

    return Ok(new { req.Id, req.SalesPersonId });
}
```

- [ ] **Step 4: Run tests to confirm pass**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminReassignSp"
```

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Admin/AdminController.cs \
        BomPriceApproval.Tests/Admin/AdminReassignSpTests.cs
git commit -m "feat(api): POST reassign-sp with audit + notif (V23c C3)"
```

---

## Task 11: C4 — `POST /api/admin/requisitions/{id}/unlock-bom` + tests

**Files:**
- Modify: `BomPriceApproval.API/Features/Admin/AdminController.cs`
- Create: `BomPriceApproval.Tests/Admin/AdminUnlockBomTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BomPriceApproval.Tests/Admin/AdminUnlockBomTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Admin;

public class AdminUnlockBomTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private async Task<HttpClient> AsAdmin()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { Email = "admin@test.com", Password = "admin123" });
        var body = await login.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", body!["accessToken"].ToString());
        return client;
    }

    private async Task<int> SeedReq(RequisitionStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sp = await db.Users.FirstAsync(u => u.Role == UserRole.SalesPerson);
        var cust = await db.Customers.FirstAsync();
        var req = new QuotationRequest
        {
            CustomerId = cust.Id, SalesPersonId = sp.Id, BranchId = sp.BranchId ?? 1,
            Status = status, Notes = $"t-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow
        };
        db.QuotationRequests.Add(req);
        await db.SaveChangesAsync();
        return req.Id;
    }

    private async Task Cleanup(int reqId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var req = await db.QuotationRequests.FindAsync(reqId);
        if (req is not null)
        {
            db.QuotationRequests.Remove(req);
            await db.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData(RequisitionStatus.CostingPending)]
    [InlineData(RequisitionStatus.CostingInProgress)]
    [InlineData(RequisitionStatus.MdReview)]
    public async Task UnlockBom_FromAllowedStatus_FlipsToBomInProgress(RequisitionStatus from)
    {
        var id = await SeedReq(from);
        var client = await AsAdmin();
        var resp = await client.PostAsJsonAsync($"/api/admin/requisitions/{id}/unlock-bom", new { Reason = "BOM correction needed" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var refreshed = await db.QuotationRequests.FindAsync(id);
        refreshed!.Status.Should().Be(RequisitionStatus.BomInProgress);

        await Cleanup(id);
    }

    [Theory]
    [InlineData(RequisitionStatus.Approved)]
    [InlineData(RequisitionStatus.Rejected)]
    [InlineData(RequisitionStatus.BomPending)]
    [InlineData(RequisitionStatus.BomInProgress)]
    public async Task UnlockBom_FromBlockedStatus_Returns400(RequisitionStatus from)
    {
        var id = await SeedReq(from);
        var client = await AsAdmin();
        var resp = await client.PostAsJsonAsync($"/api/admin/requisitions/{id}/unlock-bom", new { Reason = "should fail" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await Cleanup(id);
    }

    [Fact]
    public async Task UnlockBom_PreservesExistingBomData()
    {
        var id = await SeedReq(RequisitionStatus.CostingPending);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Add a sentinel BomHeader (use minimal valid shape — adjust required fields if any)
        var ri = new RequisitionItem { RequisitionId = id, ItemId = (await db.Items.FirstAsync()).Id, ExpectedQty = 10 };
        db.RequisitionItems.Add(ri);
        await db.SaveChangesAsync();
        var bom = new BomHeader { RequisitionItemId = ri.Id };
        db.BomHeaders.Add(bom);
        await db.SaveChangesAsync();
        var bomId = bom.Id;

        var client = await AsAdmin();
        await client.PostAsJsonAsync($"/api/admin/requisitions/{id}/unlock-bom", new { Reason = "preserve test" });

        var stillExists = await db.BomHeaders.AnyAsync(b => b.Id == bomId);
        stillExists.Should().BeTrue("BOM data must be preserved on unlock");

        // cleanup
        db.BomHeaders.Remove(bom);
        db.RequisitionItems.Remove(ri);
        await db.SaveChangesAsync();
        await Cleanup(id);
    }

    [Fact]
    public async Task UnlockBom_WritesAuditLog()
    {
        var id = await SeedReq(RequisitionStatus.MdReview);
        var reason = $"unlock-audit-{Guid.NewGuid():N}";
        var client = await AsAdmin();
        await client.PostAsJsonAsync($"/api/admin/requisitions/{id}/unlock-bom", new { Reason = reason });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditRow = await db.AdminAuditLogs.FirstOrDefaultAsync(a => a.Reason == reason);
        auditRow.Should().NotBeNull();
        auditRow!.ActionType.Should().Be(AdminActionType.UnlockBom);

        db.AdminAuditLogs.Remove(auditRow);
        await db.SaveChangesAsync();
        await Cleanup(id);
    }

    [Fact]
    public async Task UnlockBom_MissingReason_Returns400()
    {
        var id = await SeedReq(RequisitionStatus.MdReview);
        var client = await AsAdmin();
        var resp = await client.PostAsJsonAsync($"/api/admin/requisitions/{id}/unlock-bom", new { Reason = "" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await Cleanup(id);
    }
}
```

- [ ] **Step 2: Run to confirm fail**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminUnlockBom"
```

- [ ] **Step 3: Add C4 method**

```csharp
[HttpPost("requisitions/{id}/unlock-bom")]
public async Task<IActionResult> UnlockBom(int id, [FromBody] UnlockBomRequest body)
{
    if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
        return BadRequest(new { error = "Reason is required (min 5 chars)" });

    var req = await db.QuotationRequests.FindAsync(id);
    if (req is null) return NotFound();

    if (!Infrastructure.Authorization.AdminOverrideAuthorization.CanUnlockBom(req.Status))
        return BadRequest(new { error = $"Cannot unlock BOM from status {req.Status}" });

    var before = new { req.Id, req.Status };
    req.Status = RequisitionStatus.BomInProgress;
    var after = new { req.Id, req.Status };

    audit.Log(CurrentUserId, AdminActionType.UnlockBom, "Requisition", id, body.Reason, before, after);
    await db.SaveChangesAsync();

    var recipientIds = await db.Users
        .Where(u => u.BranchId == req.BranchId && (u.Role == UserRole.BomCreator || u.Role == UserRole.Accountant))
        .Select(u => u.Id).ToListAsync();
    await notify.SendToUsersAsync(recipientIds, NotificationType.BomUnlocked,
        $"BOM for requisition {req.RefNo} has been unlocked by Admin",
        new { requisitionId = id, reason = body.Reason });

    return Ok(new { req.Id, req.Status });
}
```

- [ ] **Step 4: Run tests to confirm pass**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminUnlockBom"
```

Expected: 9 tests pass (3 theory allowed + 4 theory blocked + preserves data + audit + missing reason).

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Admin/AdminController.cs \
        BomPriceApproval.Tests/Admin/AdminUnlockBomTests.cs
git commit -m "feat(api): POST unlock-bom with audit + notif (V23c C4)"
```

---

## Task 12: C5 — `POST /api/admin/requisitions/{id}/unlock-costing` + tests

**Files:**
- Modify: `BomPriceApproval.API/Features/Admin/AdminController.cs`
- Create: `BomPriceApproval.Tests/Admin/AdminUnlockCostingTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BomPriceApproval.Tests/Admin/AdminUnlockCostingTests.cs` with the same `AsAdmin()` / `SeedReq()` / `Cleanup()` helpers as Task 11. Test bodies:

```csharp
[Fact]
public async Task UnlockCosting_FromMdReview_FlipsToCostingInProgress()
{
    var id = await SeedReq(RequisitionStatus.MdReview);
    var client = await AsAdmin();
    var resp = await client.PostAsJsonAsync($"/api/admin/requisitions/{id}/unlock-costing", new { Reason = "costing correction" });
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var refreshed = await db.QuotationRequests.FindAsync(id);
    refreshed!.Status.Should().Be(RequisitionStatus.CostingInProgress);

    await Cleanup(id);
}

[Theory]
[InlineData(RequisitionStatus.Approved)]
[InlineData(RequisitionStatus.Rejected)]
[InlineData(RequisitionStatus.CostingPending)]
[InlineData(RequisitionStatus.CostingInProgress)]
[InlineData(RequisitionStatus.BomInProgress)]
[InlineData(RequisitionStatus.BomPending)]
public async Task UnlockCosting_FromBlockedStatus_Returns400(RequisitionStatus from)
{
    var id = await SeedReq(from);
    var client = await AsAdmin();
    var resp = await client.PostAsJsonAsync($"/api/admin/requisitions/{id}/unlock-costing", new { Reason = "should fail" });
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    await Cleanup(id);
}

[Fact]
public async Task UnlockCosting_WritesAuditLog()
{
    var id = await SeedReq(RequisitionStatus.MdReview);
    var reason = $"unlock-cost-audit-{Guid.NewGuid():N}";
    var client = await AsAdmin();
    await client.PostAsJsonAsync($"/api/admin/requisitions/{id}/unlock-costing", new { Reason = reason });

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var auditRow = await db.AdminAuditLogs.FirstOrDefaultAsync(a => a.Reason == reason);
    auditRow.Should().NotBeNull();
    auditRow!.ActionType.Should().Be(AdminActionType.UnlockCosting);

    db.AdminAuditLogs.Remove(auditRow);
    await db.SaveChangesAsync();
    await Cleanup(id);
}

[Fact]
public async Task UnlockCosting_MissingReason_Returns400()
{
    var id = await SeedReq(RequisitionStatus.MdReview);
    var client = await AsAdmin();
    var resp = await client.PostAsJsonAsync($"/api/admin/requisitions/{id}/unlock-costing", new { Reason = "" });
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    await Cleanup(id);
}
```

- [ ] **Step 2: Run to confirm fail**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminUnlockCosting"
```

- [ ] **Step 3: Add C5 method**

Endpoint impl:

```csharp
[HttpPost("requisitions/{id}/unlock-costing")]
public async Task<IActionResult> UnlockCosting(int id, [FromBody] UnlockCostingRequest body)
{
    if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
        return BadRequest(new { error = "Reason is required (min 5 chars)" });

    var req = await db.QuotationRequests.FindAsync(id);
    if (req is null) return NotFound();

    if (!Infrastructure.Authorization.AdminOverrideAuthorization.CanUnlockCosting(req.Status))
        return BadRequest(new { error = $"Cannot unlock costing from status {req.Status}" });

    var before = new { req.Id, req.Status };
    req.Status = RequisitionStatus.CostingInProgress;
    var after = new { req.Id, req.Status };

    audit.Log(CurrentUserId, AdminActionType.UnlockCosting, "Requisition", id, body.Reason, before, after);
    await db.SaveChangesAsync();

    var recipientIds = await db.Users
        .Where(u => (u.BranchId == req.BranchId && u.Role == UserRole.Accountant) || u.Role == UserRole.ManagingDirector)
        .Select(u => u.Id).ToListAsync();
    await notify.SendToUsersAsync(recipientIds, NotificationType.CostingUnlocked,
        $"Costing for requisition {req.RefNo} has been unlocked by Admin",
        new { requisitionId = id, reason = body.Reason });

    return Ok(new { req.Id, req.Status });
}
```

- [ ] **Step 4: Run tests to confirm pass**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminUnlockCosting"
```

Expected: 9 tests pass (1 fact happy + 6 theory blocked + audit + missing reason).

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Admin/AdminController.cs \
        BomPriceApproval.Tests/Admin/AdminUnlockCostingTests.cs
git commit -m "feat(api): POST unlock-costing with audit + notif (V23c C5)"
```

---

## Task 13: C7 — `POST /api/admin/users/{id}/reset-password` + tests

**Files:**
- Modify: `BomPriceApproval.API/Features/Admin/AdminController.cs`
- Create: `BomPriceApproval.Tests/Admin/AdminResetPasswordTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public async Task ResetPassword_HappyPath_ReturnsTempPasswordAndFlagsUser()
{
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var user = new User
    {
        Email = $"reset-{Guid.NewGuid():N}@test.com",
        Name = "Reset Test", PasswordHash = BCrypt.Net.BCrypt.HashPassword("oldpass"),
        Role = UserRole.SalesPerson, BranchId = 1, IsActive = true,
        FailedLoginAttempts = 3, LockedUntil = DateTime.UtcNow.AddMinutes(10)
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();

    var client = await AsAdmin();
    var resp = await client.PostAsJsonAsync($"/api/admin/users/{user.Id}/reset-password",
        new { Reason = "user locked out" });
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var temp = body!["tempPassword"].ToString();
    temp.Should().NotBeNullOrEmpty();
    temp!.Length.Should().Be(12);

    var refreshed = await db.Users.FindAsync(user.Id);
    refreshed!.MustChangePassword.Should().BeTrue();
    refreshed.FailedLoginAttempts.Should().Be(0);
    refreshed.LockedUntil.Should().BeNull();
    BCrypt.Net.BCrypt.Verify(temp, refreshed.PasswordHash).Should().BeTrue();

    db.Users.Remove(refreshed);
    await db.SaveChangesAsync();
}

[Fact]
public async Task ResetPassword_RevokesAllRefreshTokens() { /* seed token, run reset, assert IsRevoked */ }

[Fact]
public async Task ResetPassword_AuditLogDoesNotContainTempPassword() { /* assert audit row's BeforeJson and AfterJson do NOT contain the temp string */ }

[Fact]
public async Task ResetPassword_AsNonAdmin_Returns403() { /* login as ali, expect 403 */ }
```

- [ ] **Step 2: Run to confirm fail**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminResetPassword"
```

- [ ] **Step 3: Add C7 method**

```csharp
[HttpPost("users/{id}/reset-password")]
public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordRequest body)
{
    if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
        return BadRequest(new { error = "Reason is required (min 5 chars)" });

    var user = await db.Users.Include(u => u.RefreshTokens).FirstOrDefaultAsync(u => u.Id == id);
    if (user is null) return NotFound();

    var before = new
    {
        user.Id,
        user.MustChangePassword,
        user.FailedLoginAttempts,
        user.LockedUntil,
        ActiveTokenCount = user.RefreshTokens.Count(t => !t.IsRevoked)
    };

    var temp = Infrastructure.Services.PasswordGenerator.Generate();
    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(temp);
    user.MustChangePassword = true;
    user.FailedLoginAttempts = 0;
    user.LockedUntil = null;
    foreach (var tok in user.RefreshTokens.Where(t => !t.IsRevoked))
        tok.IsRevoked = true;

    var after = new
    {
        user.Id,
        user.MustChangePassword,
        user.FailedLoginAttempts,
        user.LockedUntil,
        ActiveTokenCount = 0
    };

    audit.Log(CurrentUserId, AdminActionType.ResetPassword, "User", id, body.Reason, before, after);
    await db.SaveChangesAsync();

    return Ok(new ResetPasswordResponse(temp));
}
```

> Note: The `tempPassword` is in the response body only. Do NOT log it. If `Program.cs` has a request/response logging middleware, ensure the body of this endpoint is excluded — verify by grepping for `app.Use` or similar middleware registrations and inspecting any body-logging logic.

- [ ] **Step 4: Run tests to confirm pass**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --filter "FullyQualifiedName~AdminResetPassword"
```

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Admin/AdminController.cs \
        BomPriceApproval.Tests/Admin/AdminResetPasswordTests.cs
git commit -m "feat(api): POST reset-password with one-shot temp + audit (V23c C7)"
```

---

## Task 14: Web — `src/api/admin.ts` typed hooks

**Files:**
- Create: `bom-web/src/api/admin.ts`

- [ ] **Step 1: Create the admin hooks module**

Create `bom-web/src/api/admin.ts`:

```typescript
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./client"; // existing axios instance
import type { RequisitionStatus } from "@/types/api";

export type AdminActionType =
  | "DeleteRequisition"
  | "RollbackStatus"
  | "ReassignSp"
  | "UnlockBom"
  | "UnlockCosting"
  | "ResetPassword";

export interface AuditLogItem {
  id: number;
  adminUserId: number;
  adminUserName: string;
  actionType: AdminActionType;
  entityType: "Requisition" | "User";
  entityId: number;
  reason: string;
  beforeJson: string;
  afterJson?: string | null;
  createdAt: string;
}

export interface AuditLogPagedResponse {
  items: AuditLogItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface AuditLogFilters {
  page?: number;
  pageSize?: number;
  actionType?: AdminActionType;
  adminUserId?: number;
  entityType?: "Requisition" | "User";
  entityId?: number;
  from?: string; // ISO date
  to?: string;
}

export function useAuditLog(filters: AuditLogFilters) {
  return useQuery({
    queryKey: ["admin-audit-log", filters],
    queryFn: async () => {
      const { data } = await api.get<AuditLogPagedResponse>("/api/admin/audit-log", { params: filters });
      return data;
    }
  });
}

function invalidateReqAndAudit(qc: ReturnType<typeof useQueryClient>, reqId?: number) {
  qc.invalidateQueries({ queryKey: ["requisitions"] });
  if (reqId) qc.invalidateQueries({ queryKey: ["requisition", reqId] });
  qc.invalidateQueries({ queryKey: ["admin-audit-log"] });
}

export function useDeleteRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, reason }: { id: number; reason: string }) => {
      await api.delete(`/api/admin/requisitions/${id}`, { data: { reason } });
    },
    onSuccess: (_, vars) => invalidateReqAndAudit(qc, vars.id)
  });
}

export function useRollbackStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, targetStatus, reason }: { id: number; targetStatus: RequisitionStatus; reason: string }) => {
      const { data } = await api.post(`/api/admin/requisitions/${id}/rollback-status`, { targetStatus, reason });
      return data;
    },
    onSuccess: (_, vars) => invalidateReqAndAudit(qc, vars.id)
  });
}

export function useReassignSp() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, newSalesPersonId, reason }: { id: number; newSalesPersonId: number; reason: string }) => {
      const { data } = await api.post(`/api/admin/requisitions/${id}/reassign-sp`, { newSalesPersonId, reason });
      return data;
    },
    onSuccess: (_, vars) => invalidateReqAndAudit(qc, vars.id)
  });
}

export function useUnlockBom() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, reason }: { id: number; reason: string }) => {
      const { data } = await api.post(`/api/admin/requisitions/${id}/unlock-bom`, { reason });
      return data;
    },
    onSuccess: (_, vars) => invalidateReqAndAudit(qc, vars.id)
  });
}

export function useUnlockCosting() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, reason }: { id: number; reason: string }) => {
      const { data } = await api.post(`/api/admin/requisitions/${id}/unlock-costing`, { reason });
      return data;
    },
    onSuccess: (_, vars) => invalidateReqAndAudit(qc, vars.id)
  });
}

export function useResetPassword() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, reason }: { id: number; reason: string }) => {
      const { data } = await api.post<{ tempPassword: string }>(`/api/admin/users/${id}/reset-password`, { reason });
      return data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["users"] });
      qc.invalidateQueries({ queryKey: ["admin-audit-log"] });
    }
  });
}
```

> Verify the `api` axios import path matches the project's existing convention. Grep `from "./client"` or `from "../api/client"` in any feature file to confirm.

- [ ] **Step 2: Verify TypeScript compiles**

```bash
cd bom-web && npx tsc --noEmit && cd ..
```

Expected: zero errors.

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/api/admin.ts
git commit -m "feat(web): add typed admin override + audit log hooks (V23c)"
```

---

## Task 15: Web — `LoginResponse` type extension + `ForceChangePasswordGuard`

**Files:**
- Modify: `bom-web/src/api/auth.ts` (or wherever `LoginResponse` type lives)
- Create: `bom-web/src/features/auth/ForceChangePasswordGuard.tsx`
- Create: `bom-web/src/features/auth/ForceChangePasswordGuard.test.tsx`
- Modify: `bom-web/src/App.tsx` (or router) — wrap protected routes

- [ ] **Step 1: Find and extend LoginResponse type**

```bash
grep -rn "LoginResponse\|interface.*Login\|type.*Login" bom-web/src/
```

Add `mustChangePassword: boolean` to the type.

- [ ] **Step 2: Persist the flag in auth state**

If auth state is in a store (Zustand / Context), add `mustChangePassword` to the user object. Otherwise add it to the local-storage payload. Pattern follows existing `name` / `branchId` storage.

- [ ] **Step 3: Write failing test for the guard**

Create `bom-web/src/features/auth/ForceChangePasswordGuard.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { ForceChangePasswordGuard } from "./ForceChangePasswordGuard";
import { describe, it, expect, vi } from "vitest";

vi.mock("@/store/auth", () => ({
  useAuth: () => ({ user: { mustChangePassword: true } })
}));

describe("ForceChangePasswordGuard", () => {
  it("redirects to /change-password when flag set", () => {
    render(
      <MemoryRouter initialEntries={["/dashboard"]}>
        <Routes>
          <Route path="/dashboard" element={<ForceChangePasswordGuard><div>dashboard</div></ForceChangePasswordGuard>} />
          <Route path="/change-password" element={<div>change pw page</div>} />
        </Routes>
      </MemoryRouter>
    );
    expect(screen.getByText("change pw page")).toBeInTheDocument();
  });
});
```

> Adjust the auth store import path to match the project. If no store exists, mock the relevant context.

- [ ] **Step 4: Run to confirm fail**

```bash
cd bom-web && npx vitest run src/features/auth/ForceChangePasswordGuard.test.tsx && cd ..
```

- [ ] **Step 5: Implement the guard**

```tsx
// bom-web/src/features/auth/ForceChangePasswordGuard.tsx
import { Navigate, useLocation } from "react-router-dom";
import { useAuth } from "@/store/auth"; // adjust path

export function ForceChangePasswordGuard({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  const location = useLocation();
  if (user?.mustChangePassword && location.pathname !== "/change-password") {
    return <Navigate to="/change-password" replace />;
  }
  return <>{children}</>;
}
```

- [ ] **Step 6: Wrap protected routes in App.tsx**

In the router configuration, wrap the authenticated section in `<ForceChangePasswordGuard>`. Pattern depends on the existing structure — if there's already a `<RequireAuth>` wrapper, nest inside it.

- [ ] **Step 7: After password change, clear the flag client-side**

In `ChangePasswordPage` (or wherever change-password POST is handled), on success the response now carries `mustChangePassword: false`. Update auth state with the new value so the guard stops redirecting.

- [ ] **Step 8: Run tests + verify build**

```bash
cd bom-web && npx vitest run && npx tsc --noEmit && cd ..
```

Expected: existing 217 tests + new ones green.

- [ ] **Step 9: Commit**

```bash
git add bom-web/src/api/auth.ts \
        bom-web/src/features/auth/ForceChangePasswordGuard.tsx \
        bom-web/src/features/auth/ForceChangePasswordGuard.test.tsx \
        bom-web/src/App.tsx
git commit -m "feat(web): mustChangePassword flag + force-change route guard (V23c)"
```

---

## Task 16: Web — `AdminActionsCard` component + tests

**Files:**
- Create: `bom-web/src/features/admin/adminOverrideAuthorization.ts` (TS mirror of backend whitelist)
- Create: `bom-web/src/features/admin/AdminActionsCard.tsx`
- Create: `bom-web/src/features/admin/AdminActionsCard.test.tsx`

- [ ] **Step 1: Write the TS authorization mirror**

```typescript
// bom-web/src/features/admin/adminOverrideAuthorization.ts
import type { RequisitionStatus } from "@/types/api";

const rollbackWhitelist: Record<string, RequisitionStatus> = {
  Approved: "MdReview",
  MdReview: "CostingPending",
  CostingInProgress: "CostingPending",
  CostingPending: "BomInProgress",
  BomInProgress: "BomPending"
};

export function rollbackTarget(from: RequisitionStatus): RequisitionStatus | null {
  return rollbackWhitelist[from] ?? null;
}

export function canRollback(from: RequisitionStatus): boolean {
  return from in rollbackWhitelist;
}

export function canUnlockBom(current: RequisitionStatus): boolean {
  return ["CostingPending", "CostingInProgress", "MdReview"].includes(current);
}

export function canUnlockCosting(current: RequisitionStatus): boolean {
  return current === "MdReview";
}

export function canDelete(): boolean {
  return true;
}

export function canReassignSp(): boolean {
  return true;
}
```

- [ ] **Step 2: Write failing test**

```tsx
// bom-web/src/features/admin/AdminActionsCard.test.tsx
import { render, screen } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { AdminActionsCard } from "./AdminActionsCard";

vi.mock("@/store/auth", () => ({
  useAuth: () => ({ user: { role: "Admin" } })
}));

describe("AdminActionsCard", () => {
  const baseReq = { id: 1, refNo: "REQ-0001", status: "BomPending" as const };

  it("renders for Admin role", () => {
    render(<AdminActionsCard requisition={baseReq} />);
    expect(screen.getByRole("button", { name: /admin actions/i })).toBeInTheDocument();
  });

  it("hides Unlock BOM button when status is BomPending", async () => {
    const { container } = render(<AdminActionsCard requisition={baseReq} />);
    container.querySelector("button[aria-label='admin actions']")?.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    expect(screen.queryByRole("button", { name: /unlock bom/i })).not.toBeInTheDocument();
  });

  it("shows Unlock BOM when status is CostingPending", async () => {
    render(<AdminActionsCard requisition={{ ...baseReq, status: "CostingPending" }} />);
    expect(screen.getByRole("button", { name: /unlock bom/i })).toBeInTheDocument();
  });
});
```

- [ ] **Step 3: Confirm fail**

```bash
cd bom-web && npx vitest run src/features/admin/AdminActionsCard.test.tsx && cd ..
```

- [ ] **Step 4: Implement AdminActionsCard**

```tsx
// bom-web/src/features/admin/AdminActionsCard.tsx
import { useState } from "react";
import type { RequisitionStatus } from "@/types/api";
import { useAuth } from "@/store/auth";
import {
  canDelete, canReassignSp, canRollback,
  canUnlockBom, canUnlockCosting
} from "./adminOverrideAuthorization";
import { DeleteRequisitionModal } from "./modals/DeleteRequisitionModal";
import { RollbackStatusModal } from "./modals/RollbackStatusModal";
import { ReassignSpModal } from "./modals/ReassignSpModal";
import { UnlockBomModal } from "./modals/UnlockBomModal";
import { UnlockCostingModal } from "./modals/UnlockCostingModal";

interface Props {
  requisition: { id: number; refNo: string; status: RequisitionStatus };
}

type ModalKind = "delete" | "rollback" | "reassign" | "unlockBom" | "unlockCosting" | null;

export function AdminActionsCard({ requisition }: Props) {
  const { user } = useAuth();
  const [expanded, setExpanded] = useState(false);
  const [modal, setModal] = useState<ModalKind>(null);

  if (user?.role !== "Admin") return null;

  return (
    <div className="mt-6 border border-amber-300 bg-amber-50 rounded-lg">
      <button
        type="button"
        aria-label="admin actions"
        className="w-full text-left px-4 py-2 font-medium text-amber-900"
        onClick={() => setExpanded(e => !e)}
      >
        Admin actions {expanded ? "▾" : "▸"}
      </button>
      {expanded && (
        <div className="px-4 pb-4 flex flex-wrap gap-2">
          {canDelete() && (
            <button className="btn-danger" onClick={() => setModal("delete")}>Delete requisition</button>
          )}
          {canRollback(requisition.status) && (
            <button className="btn-warning" onClick={() => setModal("rollback")}>Rollback status</button>
          )}
          {canReassignSp() && (
            <button className="btn-warning" onClick={() => setModal("reassign")}>Reassign SP</button>
          )}
          {canUnlockBom(requisition.status) && (
            <button className="btn-warning" onClick={() => setModal("unlockBom")}>Unlock BOM</button>
          )}
          {canUnlockCosting(requisition.status) && (
            <button className="btn-warning" onClick={() => setModal("unlockCosting")}>Unlock Costing</button>
          )}
        </div>
      )}
      {modal === "delete" && <DeleteRequisitionModal requisition={requisition} onClose={() => setModal(null)} />}
      {modal === "rollback" && <RollbackStatusModal requisition={requisition} onClose={() => setModal(null)} />}
      {modal === "reassign" && <ReassignSpModal requisition={requisition} onClose={() => setModal(null)} />}
      {modal === "unlockBom" && <UnlockBomModal requisition={requisition} onClose={() => setModal(null)} />}
      {modal === "unlockCosting" && <UnlockCostingModal requisition={requisition} onClose={() => setModal(null)} />}
    </div>
  );
}
```

> The test is written assuming the card is collapsed by default and `Unlock BOM` is only conditionally rendered. Adjust the test if you change defaults. (Tailwind class names like `btn-danger` should match existing project utility class conventions; substitute with the project's actual button classes.)

- [ ] **Step 5: Note — modal imports point to files NOT yet created**

The 5 modal imports above will fail to compile until Tasks 17-19 land. To complete this task TDD-style without breaking the build for the team, **temporarily stub the 5 modals** as empty placeholders before running tests. Replace each with `() => null` in the same task:

```tsx
// bom-web/src/features/admin/modals/DeleteRequisitionModal.tsx (placeholder)
export function DeleteRequisitionModal(_: any) { return null; }
```

(Repeat for RollbackStatusModal, ReassignSpModal, UnlockBomModal, UnlockCostingModal.) Tasks 17-21 will replace each with the real implementation.

- [ ] **Step 6: Run tests + tsc**

```bash
cd bom-web && npx vitest run src/features/admin/AdminActionsCard.test.tsx && npx tsc --noEmit && cd ..
```

- [ ] **Step 7: Commit**

```bash
git add bom-web/src/features/admin/
git commit -m "feat(web): AdminActionsCard + TS authz mirror + modal stubs (V23c)"
```

---

## Task 17: Web — `DeleteRequisitionModal` + tests

**Files:**
- Modify: `bom-web/src/features/admin/modals/DeleteRequisitionModal.tsx` (replace stub)
- Create: `bom-web/src/features/admin/modals/DeleteRequisitionModal.test.tsx`

- [ ] **Step 1: Write failing test**

```tsx
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { DeleteRequisitionModal } from "./DeleteRequisitionModal";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const mockMutate = vi.fn();
vi.mock("@/api/admin", () => ({
  useDeleteRequisition: () => ({ mutateAsync: mockMutate, isPending: false })
}));

function wrap(ui: React.ReactElement) {
  return <QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>;
}

describe("DeleteRequisitionModal", () => {
  it("disables confirm when reason is too short", () => {
    render(wrap(<DeleteRequisitionModal requisition={{ id: 1, refNo: "REQ-1", status: "BomPending" }} onClose={() => {}} />));
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "abc" } });
    expect(screen.getByRole("button", { name: /delete/i })).toBeDisabled();
  });

  it("calls mutation with reason when confirmed", async () => {
    const onClose = vi.fn();
    render(wrap(<DeleteRequisitionModal requisition={{ id: 1, refNo: "REQ-1", status: "BomPending" }} onClose={onClose} />));
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "duplicate created by SP error" } });
    fireEvent.click(screen.getByRole("button", { name: /delete/i }));
    await waitFor(() => expect(mockMutate).toHaveBeenCalledWith({ id: 1, reason: "duplicate created by SP error" }));
  });
});
```

- [ ] **Step 2: Confirm fail**

```bash
cd bom-web && npx vitest run src/features/admin/modals/DeleteRequisitionModal.test.tsx && cd ..
```

- [ ] **Step 3: Implement modal**

```tsx
// bom-web/src/features/admin/modals/DeleteRequisitionModal.tsx
import { useState } from "react";
import { useDeleteRequisition } from "@/api/admin";
import type { RequisitionStatus } from "@/types/api";

interface Props {
  requisition: { id: number; refNo: string; status: RequisitionStatus };
  onClose: () => void;
}

export function DeleteRequisitionModal({ requisition, onClose }: Props) {
  const [reason, setReason] = useState("");
  const mutation = useDeleteRequisition();
  const valid = reason.trim().length >= 5;

  async function handleConfirm() {
    if (!valid) return;
    try {
      await mutation.mutateAsync({ id: requisition.id, reason: reason.trim() });
      onClose();
    } catch (e) {
      // surface error via toast or inline; matches project convention
    }
  }

  return (
    <div role="dialog" className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg p-6 w-full max-w-md">
        <h2 className="text-lg font-semibold mb-2">Delete requisition {requisition.refNo}?</h2>
        <p className="text-sm text-red-700 mb-4">
          This will permanently delete the requisition and all related BOM, costing, and approval data. This cannot be undone.
        </p>
        <label className="block">
          <span className="text-sm font-medium">Reason (required, min 5 chars)</span>
          <textarea
            value={reason}
            onChange={e => setReason(e.target.value)}
            className="mt-1 w-full border rounded p-2"
            rows={3}
          />
        </label>
        <div className="mt-4 flex justify-end gap-2">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button type="button" className="btn-danger" disabled={!valid || mutation.isPending} onClick={handleConfirm}>
            {mutation.isPending ? "Deleting..." : "Delete"}
          </button>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Run tests + commit**

```bash
cd bom-web && npx vitest run src/features/admin/modals/DeleteRequisitionModal.test.tsx && cd ..
git add bom-web/src/features/admin/modals/DeleteRequisitionModal.tsx \
        bom-web/src/features/admin/modals/DeleteRequisitionModal.test.tsx
git commit -m "feat(web): DeleteRequisitionModal with reason validation (V23c C1 UI)"
```

---

## Task 18: Web — `RollbackStatusModal` + tests

**Files:**
- Modify: `bom-web/src/features/admin/modals/RollbackStatusModal.tsx`
- Create: `bom-web/src/features/admin/modals/RollbackStatusModal.test.tsx`

- [ ] **Step 1: Write failing test**

```tsx
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { RollbackStatusModal } from "./RollbackStatusModal";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const mockMutate = vi.fn();
vi.mock("@/api/admin", () => ({
  useRollbackStatus: () => ({ mutateAsync: mockMutate, isPending: false })
}));

function wrap(ui: React.ReactElement) {
  return <QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>;
}

describe("RollbackStatusModal", () => {
  it("shows only the whitelist target for current status", () => {
    render(wrap(<RollbackStatusModal requisition={{ id: 1, refNo: "REQ-1", status: "Approved" }} onClose={() => {}} />));
    // Approved → MdReview is the only valid target
    expect(screen.getByText(/MdReview/)).toBeInTheDocument();
    expect(screen.queryByText(/BomPending/)).not.toBeInTheDocument();
  });

  it("submits rollback with target and reason", async () => {
    render(wrap(<RollbackStatusModal requisition={{ id: 1, refNo: "REQ-1", status: "Approved" }} onClose={() => {}} />));
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "MD approved by mistake" } });
    fireEvent.click(screen.getByRole("button", { name: /rollback/i }));
    await waitFor(() => expect(mockMutate).toHaveBeenCalledWith({ id: 1, targetStatus: "MdReview", reason: "MD approved by mistake" }));
  });
});
```

- [ ] **Step 2: Confirm fail**

```bash
cd bom-web && npx vitest run src/features/admin/modals/RollbackStatusModal.test.tsx && cd ..
```

- [ ] **Step 3: Implement modal**

```tsx
import { useState } from "react";
import { useRollbackStatus } from "@/api/admin";
import { rollbackTarget } from "../adminOverrideAuthorization";
import type { RequisitionStatus } from "@/types/api";

interface Props {
  requisition: { id: number; refNo: string; status: RequisitionStatus };
  onClose: () => void;
}

export function RollbackStatusModal({ requisition, onClose }: Props) {
  const target = rollbackTarget(requisition.status);
  const [reason, setReason] = useState("");
  const mutation = useRollbackStatus();
  const valid = !!target && reason.trim().length >= 5;

  if (!target) {
    return (
      <div role="dialog" className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
        <div className="bg-white rounded-lg p-6 max-w-md">
          <p>No rollback target available for status {requisition.status}.</p>
          <button className="btn mt-4" onClick={onClose}>Close</button>
        </div>
      </div>
    );
  }

  async function handleConfirm() {
    if (!valid) return;
    try {
      await mutation.mutateAsync({ id: requisition.id, targetStatus: target!, reason: reason.trim() });
      onClose();
    } catch {}
  }

  return (
    <div role="dialog" className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg p-6 w-full max-w-md">
        <h2 className="text-lg font-semibold mb-2">Rollback {requisition.refNo}</h2>
        <p className="text-sm mb-4">
          {requisition.status} → <strong>{target}</strong>
        </p>
        <label className="block">
          <span className="text-sm font-medium">Reason (required, min 5 chars)</span>
          <textarea value={reason} onChange={e => setReason(e.target.value)} className="mt-1 w-full border rounded p-2" rows={3} />
        </label>
        <div className="mt-4 flex justify-end gap-2">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button type="button" className="btn-warning" disabled={!valid || mutation.isPending} onClick={handleConfirm}>
            {mutation.isPending ? "Rolling back..." : "Rollback"}
          </button>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Run tests + commit**

```bash
cd bom-web && npx vitest run src/features/admin/modals/RollbackStatusModal.test.tsx && cd ..
git add bom-web/src/features/admin/modals/RollbackStatusModal.tsx \
        bom-web/src/features/admin/modals/RollbackStatusModal.test.tsx
git commit -m "feat(web): RollbackStatusModal with whitelist-filtered target (V23c C2 UI)"
```

---

## Task 19: Web — `ReassignSpModal` + tests

**Files:**
- Modify: `bom-web/src/features/admin/modals/ReassignSpModal.tsx`
- Create: `bom-web/src/features/admin/modals/ReassignSpModal.test.tsx`

- [ ] **Step 1: Locate the existing `useUsers` hook (or equivalent)**

```bash
grep -rn "useUsers\|useUserList" bom-web/src/api/
```

If no role-filtered hook exists, the modal will fetch all users and filter client-side.

- [ ] **Step 2: Write failing test**

```tsx
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { ReassignSpModal } from "./ReassignSpModal";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const mockMutate = vi.fn();
vi.mock("@/api/admin", () => ({
  useReassignSp: () => ({ mutateAsync: mockMutate, isPending: false })
}));
vi.mock("@/api/users", () => ({
  useUsers: () => ({
    data: [
      { id: 10, name: "Sp One", role: "SalesPerson", isActive: true },
      { id: 11, name: "Sp Two", role: "SalesPerson", isActive: true },
      { id: 12, name: "Acct", role: "Accountant", isActive: true },
      { id: 13, name: "Inactive Sp", role: "SalesPerson", isActive: false }
    ]
  })
}));

function wrap(ui: React.ReactElement) {
  return <QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>;
}

describe("ReassignSpModal", () => {
  it("only lists active SalesPersons in the dropdown", () => {
    render(wrap(<ReassignSpModal requisition={{ id: 1, refNo: "REQ-1", status: "BomPending" }} onClose={() => {}} />));
    expect(screen.getByText(/Sp One/)).toBeInTheDocument();
    expect(screen.getByText(/Sp Two/)).toBeInTheDocument();
    expect(screen.queryByText(/Acct/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Inactive Sp/)).not.toBeInTheDocument();
  });

  it("submits with selected SP id and reason", async () => {
    render(wrap(<ReassignSpModal requisition={{ id: 1, refNo: "REQ-1", status: "BomPending" }} onClose={() => {}} />));
    fireEvent.change(screen.getByLabelText(/new salesperson/i), { target: { value: "11" } });
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "Sp1 has left" } });
    fireEvent.click(screen.getByRole("button", { name: /reassign/i }));
    await waitFor(() => expect(mockMutate).toHaveBeenCalledWith({ id: 1, newSalesPersonId: 11, reason: "Sp1 has left" }));
  });
});
```

- [ ] **Step 3: Confirm fail**

- [ ] **Step 4: Implement modal**

```tsx
import { useState } from "react";
import { useReassignSp } from "@/api/admin";
import { useUsers } from "@/api/users"; // adjust if hook name differs

interface Props {
  requisition: { id: number; refNo: string; status: string };
  onClose: () => void;
}

export function ReassignSpModal({ requisition, onClose }: Props) {
  const [newSpId, setNewSpId] = useState<number | null>(null);
  const [reason, setReason] = useState("");
  const mutation = useReassignSp();
  const { data: users } = useUsers();
  const sps = (users ?? []).filter((u: any) => u.role === "SalesPerson" && u.isActive);
  const valid = newSpId !== null && reason.trim().length >= 5;

  async function handleConfirm() {
    if (!valid) return;
    try {
      await mutation.mutateAsync({ id: requisition.id, newSalesPersonId: newSpId!, reason: reason.trim() });
      onClose();
    } catch {}
  }

  return (
    <div role="dialog" className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg p-6 w-full max-w-md">
        <h2 className="text-lg font-semibold mb-2">Reassign salesperson — {requisition.refNo}</h2>
        <label className="block mb-2">
          <span className="text-sm font-medium">New salesperson</span>
          <select
            className="mt-1 w-full border rounded p-2"
            value={newSpId ?? ""}
            onChange={e => setNewSpId(e.target.value ? Number(e.target.value) : null)}
          >
            <option value="">— select —</option>
            {sps.map((sp: any) => (
              <option key={sp.id} value={sp.id}>{sp.name}</option>
            ))}
          </select>
        </label>
        <label className="block">
          <span className="text-sm font-medium">Reason (required, min 5 chars)</span>
          <textarea value={reason} onChange={e => setReason(e.target.value)} className="mt-1 w-full border rounded p-2" rows={3} />
        </label>
        <div className="mt-4 flex justify-end gap-2">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button type="button" className="btn-warning" disabled={!valid || mutation.isPending} onClick={handleConfirm}>
            {mutation.isPending ? "Reassigning..." : "Reassign"}
          </button>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 5: Run tests + commit**

```bash
cd bom-web && npx vitest run src/features/admin/modals/ReassignSpModal.test.tsx && cd ..
git add bom-web/src/features/admin/modals/ReassignSpModal.tsx \
        bom-web/src/features/admin/modals/ReassignSpModal.test.tsx
git commit -m "feat(web): ReassignSpModal with active-SP picker (V23c C3 UI)"
```

---

## Task 20: Web — `UnlockBomModal` + `UnlockCostingModal` + tests

**Files:**
- Modify: `bom-web/src/features/admin/modals/UnlockBomModal.tsx`
- Create: `bom-web/src/features/admin/modals/UnlockBomModal.test.tsx`
- Modify: `bom-web/src/features/admin/modals/UnlockCostingModal.tsx`
- Create: `bom-web/src/features/admin/modals/UnlockCostingModal.test.tsx`

(Combined task — both modals are nearly identical in shape: reason textarea + confirm.)

- [ ] **Step 1: Write failing tests for both**

Create `bom-web/src/features/admin/modals/UnlockBomModal.test.tsx`:

```tsx
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { UnlockBomModal } from "./UnlockBomModal";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const mockMutate = vi.fn();
vi.mock("@/api/admin", () => ({
  useUnlockBom: () => ({ mutateAsync: mockMutate, isPending: false })
}));

function wrap(ui: React.ReactElement) {
  return <QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>;
}

describe("UnlockBomModal", () => {
  it("disables Unlock button when reason is too short", () => {
    render(wrap(<UnlockBomModal requisition={{ id: 1, refNo: "REQ-1", status: "MdReview" }} onClose={() => {}} />));
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "abc" } });
    expect(screen.getByRole("button", { name: /unlock bom/i })).toBeDisabled();
  });

  it("submits mutation with reason when confirmed", async () => {
    const onClose = vi.fn();
    render(wrap(<UnlockBomModal requisition={{ id: 1, refNo: "REQ-1", status: "MdReview" }} onClose={onClose} />));
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "wastage corrected" } });
    fireEvent.click(screen.getByRole("button", { name: /unlock bom/i }));
    await waitFor(() => expect(mockMutate).toHaveBeenCalledWith({ id: 1, reason: "wastage corrected" }));
  });
});
```

Create `bom-web/src/features/admin/modals/UnlockCostingModal.test.tsx`:

```tsx
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { UnlockCostingModal } from "./UnlockCostingModal";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const mockMutate = vi.fn();
vi.mock("@/api/admin", () => ({
  useUnlockCosting: () => ({ mutateAsync: mockMutate, isPending: false })
}));

function wrap(ui: React.ReactElement) {
  return <QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>;
}

describe("UnlockCostingModal", () => {
  it("disables Unlock button when reason is too short", () => {
    render(wrap(<UnlockCostingModal requisition={{ id: 1, refNo: "REQ-1", status: "MdReview" }} onClose={() => {}} />));
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "abc" } });
    expect(screen.getByRole("button", { name: /unlock costing/i })).toBeDisabled();
  });

  it("submits mutation with reason when confirmed", async () => {
    const onClose = vi.fn();
    render(wrap(<UnlockCostingModal requisition={{ id: 1, refNo: "REQ-1", status: "MdReview" }} onClose={onClose} />));
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "exchange rate fix" } });
    fireEvent.click(screen.getByRole("button", { name: /unlock costing/i }));
    await waitFor(() => expect(mockMutate).toHaveBeenCalledWith({ id: 1, reason: "exchange rate fix" }));
  });
});
```

- [ ] **Step 2: Confirm fail**

```bash
cd bom-web && npx vitest run src/features/admin/modals/UnlockBomModal.test.tsx src/features/admin/modals/UnlockCostingModal.test.tsx && cd ..
```

- [ ] **Step 3: Implement both modals**

`UnlockBomModal.tsx`:

```tsx
import { useState } from "react";
import { useUnlockBom } from "@/api/admin";

interface Props {
  requisition: { id: number; refNo: string; status: string };
  onClose: () => void;
}

export function UnlockBomModal({ requisition, onClose }: Props) {
  const [reason, setReason] = useState("");
  const mutation = useUnlockBom();
  const valid = reason.trim().length >= 5;

  async function handleConfirm() {
    if (!valid) return;
    try {
      await mutation.mutateAsync({ id: requisition.id, reason: reason.trim() });
      onClose();
    } catch {}
  }

  return (
    <div role="dialog" className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg p-6 w-full max-w-md">
        <h2 className="text-lg font-semibold mb-2">Unlock BOM — {requisition.refNo}</h2>
        <p className="text-sm mb-4">Status will revert to <strong>BomInProgress</strong> so the BomCreator can re-edit. Existing BOM data is preserved.</p>
        <label className="block">
          <span className="text-sm font-medium">Reason (required, min 5 chars)</span>
          <textarea value={reason} onChange={e => setReason(e.target.value)} className="mt-1 w-full border rounded p-2" rows={3} />
        </label>
        <div className="mt-4 flex justify-end gap-2">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button type="button" className="btn-warning" disabled={!valid || mutation.isPending} onClick={handleConfirm}>
            {mutation.isPending ? "Unlocking..." : "Unlock BOM"}
          </button>
        </div>
      </div>
    </div>
  );
}
```

`UnlockCostingModal.tsx` — same shape, swap labels and `useUnlockBom` → `useUnlockCosting`, "BomInProgress" → "CostingInProgress".

- [ ] **Step 4: Run tests + commit**

```bash
cd bom-web && npx vitest run src/features/admin/modals/UnlockBomModal.test.tsx src/features/admin/modals/UnlockCostingModal.test.tsx && cd ..
git add bom-web/src/features/admin/modals/UnlockBomModal.tsx \
        bom-web/src/features/admin/modals/UnlockBomModal.test.tsx \
        bom-web/src/features/admin/modals/UnlockCostingModal.tsx \
        bom-web/src/features/admin/modals/UnlockCostingModal.test.tsx
git commit -m "feat(web): UnlockBomModal + UnlockCostingModal (V23c C4+C5 UI)"
```

---

## Task 21: Web — Wire `AdminActionsCard` into `RequisitionDetailPage`

**Files:**
- Modify: `bom-web/src/features/requisitions/RequisitionDetailPage.tsx`

- [ ] **Step 1: Locate the existing detail page**

```bash
ls bom-web/src/features/requisitions/RequisitionDetailPage.tsx
```

- [ ] **Step 2: Add the import + render at the bottom**

Find a logical spot at the bottom of the rendered detail content (after existing branch / customer change buttons). Add:

```tsx
import { AdminActionsCard } from "@/features/admin/AdminActionsCard";

// inside the component's JSX, after existing detail sections:
<AdminActionsCard requisition={{ id: req.id, refNo: req.refNo, status: req.status }} />
```

`AdminActionsCard` itself returns `null` for non-admin users, so no extra role check needed at this layer.

- [ ] **Step 3: Verify build + smoke run existing tests**

```bash
cd bom-web && npx tsc --noEmit && npx vitest run src/features/requisitions/ && cd ..
```

Expected: existing RequisitionDetailPage tests still pass; no compile errors.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/features/requisitions/RequisitionDetailPage.tsx
git commit -m "feat(web): wire AdminActionsCard on RequisitionDetailPage (V23c)"
```

---

## Task 22: Web — `UsersPage` "Reset password" row action + `ResetPasswordModal`

**Files:**
- Modify: `bom-web/src/features/admin/users/UsersPage.tsx`
- Modify: `bom-web/src/features/admin/users/UsersPage.test.tsx`
- Create: `bom-web/src/features/admin/users/ResetPasswordModal.tsx`
- Create: `bom-web/src/features/admin/users/ResetPasswordModal.test.tsx`

- [ ] **Step 1: Write failing test for ResetPasswordModal one-shot reveal**

```tsx
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { ResetPasswordModal } from "./ResetPasswordModal";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const mockMutate = vi.fn().mockResolvedValue({ tempPassword: "Xy7$kQ9pM2!w" });
vi.mock("@/api/admin", () => ({
  useResetPassword: () => ({ mutateAsync: mockMutate, isPending: false })
}));

function wrap(ui: React.ReactElement) {
  return <QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>;
}

describe("ResetPasswordModal", () => {
  it("shows reason form first; reveals temp password after success", async () => {
    render(wrap(<ResetPasswordModal user={{ id: 5, name: "Test User" }} onClose={() => {}} />));
    expect(screen.getByLabelText(/reason/i)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "user locked out" } });
    fireEvent.click(screen.getByRole("button", { name: /reset/i }));
    await waitFor(() => expect(screen.getByText(/Xy7\$kQ9pM2!w/)).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /copy/i })).toBeInTheDocument();
  });

  it("clears temp password from state when closed", async () => {
    const onClose = vi.fn();
    render(wrap(<ResetPasswordModal user={{ id: 5, name: "Test User" }} onClose={onClose} />));
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "user locked out" } });
    fireEvent.click(screen.getByRole("button", { name: /reset/i }));
    await waitFor(() => screen.getByText(/Xy7/));
    fireEvent.click(screen.getByRole("button", { name: /i've copied it/i }));
    expect(onClose).toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Confirm fail**

- [ ] **Step 3: Implement ResetPasswordModal**

```tsx
// bom-web/src/features/admin/users/ResetPasswordModal.tsx
import { useState } from "react";
import { useResetPassword } from "@/api/admin";

interface Props {
  user: { id: number; name: string };
  onClose: () => void;
}

export function ResetPasswordModal({ user, onClose }: Props) {
  const [reason, setReason] = useState("");
  const [tempPassword, setTempPassword] = useState<string | null>(null);
  const mutation = useResetPassword();
  const valid = reason.trim().length >= 5;

  async function handleConfirm() {
    if (!valid) return;
    try {
      const result = await mutation.mutateAsync({ id: user.id, reason: reason.trim() });
      setTempPassword(result.tempPassword);
    } catch {}
  }

  function handleCopy() {
    if (tempPassword) navigator.clipboard.writeText(tempPassword).catch(() => {});
  }

  function handleClose() {
    setTempPassword(null);
    onClose();
  }

  return (
    <div role="dialog" className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg p-6 w-full max-w-md">
        {tempPassword === null ? (
          <>
            <h2 className="text-lg font-semibold mb-2">Reset password — {user.name}</h2>
            <label className="block">
              <span className="text-sm font-medium">Reason (required, min 5 chars)</span>
              <textarea value={reason} onChange={e => setReason(e.target.value)} className="mt-1 w-full border rounded p-2" rows={3} />
            </label>
            <div className="mt-4 flex justify-end gap-2">
              <button type="button" className="btn" onClick={handleClose}>Cancel</button>
              <button type="button" className="btn-warning" disabled={!valid || mutation.isPending} onClick={handleConfirm}>
                {mutation.isPending ? "Resetting..." : "Reset"}
              </button>
            </div>
          </>
        ) : (
          <>
            <h2 className="text-lg font-semibold mb-2">Temporary password</h2>
            <p className="text-sm text-amber-700 mb-4">
              This password is shown <strong>once</strong>. Copy it now and hand it to the user. They will be required to change it on next login.
            </p>
            <div className="bg-gray-100 p-3 rounded font-mono text-lg break-all">{tempPassword}</div>
            <div className="mt-4 flex justify-end gap-2">
              <button type="button" className="btn" onClick={handleCopy}>Copy</button>
              <button type="button" className="btn-primary" onClick={handleClose}>I've copied it</button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Add row action to UsersPage**

In `UsersPage.tsx`, find the row actions area and add (visible only to Admin):

```tsx
{currentUser.role === "Admin" && (
  <button onClick={() => setResetTarget(user)}>Reset password</button>
)}
```

Add state for `resetTarget` and render:

```tsx
{resetTarget && <ResetPasswordModal user={resetTarget} onClose={() => setResetTarget(null)} />}
```

Update `UsersPage.test.tsx` to assert the button shows for Admin and not for others.

- [ ] **Step 5: Run tests + commit**

```bash
cd bom-web && npx vitest run src/features/admin/users/ && cd ..
git add bom-web/src/features/admin/users/UsersPage.tsx \
        bom-web/src/features/admin/users/UsersPage.test.tsx \
        bom-web/src/features/admin/users/ResetPasswordModal.tsx \
        bom-web/src/features/admin/users/ResetPasswordModal.test.tsx
git commit -m "feat(web): UsersPage Reset password row action + one-shot reveal modal (V23c C7 UI)"
```

---

## Task 23: Web — `AuditLogPage` + `DiffPanel` + route + sidebar nav

**Files:**
- Create: `bom-web/src/features/admin/audit-log/AuditLogPage.tsx`
- Create: `bom-web/src/features/admin/audit-log/AuditLogPage.test.tsx`
- Create: `bom-web/src/features/admin/audit-log/DiffPanel.tsx`
- Modify: `bom-web/src/AppShell.tsx` (or sidebar) — add nav link
- Modify: `bom-web/src/App.tsx` (or router) — add `/admin/audit-log` route

- [ ] **Step 1: Write failing test**

```tsx
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { AuditLogPage } from "./AuditLogPage";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";

vi.mock("@/api/admin", () => ({
  useAuditLog: (filters: any) => ({
    data: {
      items: [
        { id: 1, adminUserId: 1, adminUserName: "Admin", actionType: "DeleteRequisition", entityType: "Requisition", entityId: 42, reason: "duplicate", beforeJson: "{\"id\":42}", afterJson: null, createdAt: "2026-04-26T18:00:00Z" }
      ],
      total: 1, page: 1, pageSize: 20
    },
    isLoading: false
  })
}));

function wrap(ui: React.ReactElement) {
  return <MemoryRouter><QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider></MemoryRouter>;
}

describe("AuditLogPage", () => {
  it("renders rows with timestamp + admin + action + entity + reason", () => {
    render(wrap(<AuditLogPage />));
    expect(screen.getByText(/Admin/)).toBeInTheDocument();
    expect(screen.getByText(/DeleteRequisition/)).toBeInTheDocument();
    expect(screen.getByText(/Requisition #42/)).toBeInTheDocument();
    expect(screen.getByText(/duplicate/)).toBeInTheDocument();
  });

  it("expand reveals diff panel", () => {
    render(wrap(<AuditLogPage />));
    fireEvent.click(screen.getByRole("button", { name: /diff/i }));
    expect(screen.getByText(/{\"id\":42}/)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Confirm fail**

- [ ] **Step 3: Implement DiffPanel**

```tsx
// bom-web/src/features/admin/audit-log/DiffPanel.tsx
interface Props { before: string; after?: string | null; }

export function DiffPanel({ before, after }: Props) {
  function pretty(json: string) {
    try { return JSON.stringify(JSON.parse(json), null, 2); } catch { return json; }
  }
  return (
    <div className="grid grid-cols-2 gap-2 text-xs">
      <div>
        <div className="font-semibold mb-1">Before</div>
        <pre className="bg-red-50 border border-red-200 p-2 rounded overflow-auto max-h-64">{pretty(before)}</pre>
      </div>
      <div>
        <div className="font-semibold mb-1">After</div>
        <pre className="bg-green-50 border border-green-200 p-2 rounded overflow-auto max-h-64">{after ? pretty(after) : "(deleted)"}</pre>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Implement AuditLogPage**

```tsx
import { useState } from "react";
import { Link } from "react-router-dom";
import { useAuditLog, type AdminActionType } from "@/api/admin";
import { DiffPanel } from "./DiffPanel";

const ACTION_TYPES: AdminActionType[] = [
  "DeleteRequisition", "RollbackStatus", "ReassignSp", "UnlockBom", "UnlockCosting", "ResetPassword"
];

export function AuditLogPage() {
  const [page, setPage] = useState(1);
  const [actionType, setActionType] = useState<AdminActionType | "">("");
  const [entityType, setEntityType] = useState<"Requisition" | "User" | "">("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [expanded, setExpanded] = useState<Set<number>>(new Set());

  const { data, isLoading } = useAuditLog({
    page,
    pageSize: 20,
    actionType: (actionType || undefined) as AdminActionType | undefined,
    entityType: (entityType || undefined) as "Requisition" | "User" | undefined,
    from: from || undefined,
    to: to || undefined
  });

  function toggle(id: number) {
    setExpanded(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }

  return (
    <div className="p-4">
      <h1 className="text-2xl font-semibold mb-4">Admin Audit Log</h1>
      <div className="flex gap-2 mb-4">
        <select value={actionType} onChange={e => setActionType(e.target.value as any)} className="border rounded p-2">
          <option value="">All actions</option>
          {ACTION_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
        </select>
        <select value={entityType} onChange={e => setEntityType(e.target.value as any)} className="border rounded p-2">
          <option value="">All entities</option>
          <option value="Requisition">Requisition</option>
          <option value="User">User</option>
        </select>
        <input type="date" value={from} onChange={e => setFrom(e.target.value)} className="border rounded p-2" />
        <input type="date" value={to} onChange={e => setTo(e.target.value)} className="border rounded p-2" />
      </div>
      {isLoading ? <p>Loading…</p> : (
        <table className="w-full border-collapse">
          <thead>
            <tr className="border-b">
              <th className="text-left p-2">When</th>
              <th className="text-left p-2">Admin</th>
              <th className="text-left p-2">Action</th>
              <th className="text-left p-2">Entity</th>
              <th className="text-left p-2">Reason</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {data?.items.map(item => (
              <>
                <tr key={item.id} className="border-b">
                  <td className="p-2">{new Date(item.createdAt).toLocaleString()}</td>
                  <td className="p-2">{item.adminUserName}</td>
                  <td className="p-2">{item.actionType}</td>
                  <td className="p-2">
                    {item.entityType === "Requisition"
                      ? <Link to={`/requisitions/${item.entityId}`} className="text-blue-600 underline">Requisition #{item.entityId}</Link>
                      : `${item.entityType} #${item.entityId}`}
                  </td>
                  <td className="p-2">{item.reason}</td>
                  <td className="p-2">
                    <button className="btn-sm" onClick={() => toggle(item.id)}>Diff</button>
                  </td>
                </tr>
                {expanded.has(item.id) && (
                  <tr key={`${item.id}-diff`}>
                    <td colSpan={6} className="p-2 bg-gray-50">
                      <DiffPanel before={item.beforeJson} after={item.afterJson} />
                    </td>
                  </tr>
                )}
              </>
            ))}
          </tbody>
        </table>
      )}
      <div className="mt-4 flex justify-between">
        <button className="btn" disabled={page === 1} onClick={() => setPage(p => p - 1)}>Prev</button>
        <span>Page {page} of {data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1}</span>
        <button className="btn" disabled={!data || page * data.pageSize >= data.total} onClick={() => setPage(p => p + 1)}>Next</button>
      </div>
    </div>
  );
}
```

- [ ] **Step 5: Add route + sidebar nav**

In `App.tsx` (or router file), add the route inside the protected/admin section:

```tsx
<Route path="/admin/audit-log" element={<AuditLogPage />} />
```

In `AppShell.tsx` (or sidebar component), add the nav link below existing admin entries (Branches, Groups):

```tsx
{user?.role === "Admin" && (
  <NavLink to="/admin/audit-log" className="...">Audit Log</NavLink>
)}
```

- [ ] **Step 6: Run tests + tsc**

```bash
cd bom-web && npx vitest run src/features/admin/audit-log/ && npx tsc --noEmit && cd ..
```

- [ ] **Step 7: Commit**

```bash
git add bom-web/src/features/admin/audit-log/ \
        bom-web/src/AppShell.tsx \
        bom-web/src/App.tsx
git commit -m "feat(web): /admin/audit-log page with filters + diff (V23c C9 UI)"
```

---

## Task 24: Docs — Update `CLAUDE.md` to document V2.3-C P1

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add V2.3-C P1 section to CLAUDE.md**

After the existing "V2.3-B Sales Groups" section, insert:

```markdown
### V2.3-C P1 Admin Override (post-2026-04-26)

Admin role gets 7 corrective operations contextually surfaced where each entity lives, all writing to a unified `AdminAuditLog` table.

- **C1 Hard-delete requisition** — `DELETE /api/admin/requisitions/{id}`. Cascades children. Notif to SP + branch staff + MDs.
- **C2 Status rollback** — `POST /api/admin/requisitions/{id}/rollback-status`. Whitelist transitions only (`Approved→MdReview`, `MdReview→CostingPending`, `CostingInProgress→CostingPending`, `CostingPending→BomInProgress`, `BomInProgress→BomPending`). Forward jumps + Rejected blocked.
- **C3 Reassign salesperson** — `POST /api/admin/requisitions/{id}/reassign-sp`. Full replace; old SP captured in audit.
- **C4 Unlock BOM** — `POST /api/admin/requisitions/{id}/unlock-bom`. Status → `BomInProgress`. Allowed from `CostingPending` / `CostingInProgress` / `MdReview`.
- **C5 Unlock costing** — `POST /api/admin/requisitions/{id}/unlock-costing`. Status → `CostingInProgress`. Allowed from `MdReview` only.
- **C7 Reset password** — `POST /api/admin/users/{id}/reset-password`. Returns one-shot 12-char temp; sets `User.MustChangePassword=true`; revokes all refresh tokens.
- **C9 Audit log** — `GET /api/admin/audit-log` paginated/filtered. Page at `/admin/audit-log`. Reads only `AdminAuditLog` (legacy `BranchChangeHistory` + `CustomerChangeHistory` viewers stay on RequisitionDetail).

All endpoints require `reason: string` (≥ 5 chars). All gated `[Authorize(Roles="Admin")]`. Helper: `AdminOverrideAuthorization` centralizes the C2/C4/C5 status guards.

`User.MustChangePassword` extends login response; web `<ForceChangePasswordGuard>` redirects flagged users to `/change-password` until cleared.

Web only — no mobile UI in P1. C6 (override approved prices) and C8 (hard-delete customer) deferred to Phase 2.
```

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: document V2.3-C P1 admin override in CLAUDE.md"
```

---

## Task 25: Wrap-up — Full test suite green check

**Files:** none (verification only)

- [ ] **Step 1: Full backend test run**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --configuration Release --nologo
```

Expected: previous 188 tests + ~50 new tests (Tasks 4, 5, 7, 8, 9, 10, 11, 12, 13) all pass. The known timing flake from V2.3-B may still appear once — re-run if so.

- [ ] **Step 2: Full web test run**

```bash
cd bom-web && npx vitest run && cd ..
```

Expected: previous 217 tests + new tests from Tasks 15, 16, 17, 18, 19, 20, 22, 23 all pass.

- [ ] **Step 3: tsc clean check**

```bash
cd bom-web && npx tsc --noEmit && cd ..
```

Expected: zero errors.

- [ ] **Step 4: Print branch summary**

```bash
git log --oneline feat/v23b-sales-groups..HEAD
git diff --stat feat/v23b-sales-groups..HEAD
```

Note totals for the next-session memory update: tasks completed, commits, files touched.

- [ ] **Step 5: Verify no `tempPassword` leakage**

```bash
grep -rn "tempPassword" BomPriceApproval.API/ --include="*.cs"
```

Should appear ONLY in `AdminController.cs` (response shaping) and `AdminDtos.cs` (record). Should NOT appear in any logging middleware, AuditLogger, or other infrastructure code.

If any other `tempPassword` reference exists in logging code, remove it before declaring done.

- [ ] **Step 6: Final summary commit (if any docs changed during wrap-up)**

If wrap-up surfaced anything that needs documenting (e.g., a small fix to a doc), commit it. Otherwise no commit at this step.

---

## Plan Summary

- **25 tasks total**: 13 backend (foundation + 7 endpoints), 9 web (hooks + 5 modals + 2 wirings + audit log page + force-change guard), 2 docs/wrap.
- **Expected commits**: ~25 (one per task; some tasks have 2 commits if logically split).
- **Test deltas**: ~50 new backend tests, ~15 new web tests.
- **Migrations**: 2 new EF migrations (`V23c_AdminAuditLog`, `V23c_UserMustChangePassword`).
- **No mobile changes.**
- **Breaks**: `LoginResponse` record shape gains a field. Frontend types must be updated in lockstep (Task 15).

When complete, V2.3-C P1 ships:
- 7 admin-only corrective operations live in production-grade UI + API
- Unified audit log captures every override with admin + reason + before/after snapshot
- Forced-password-change flow for users with admin-issued temp passwords
- Backend + web test suites green; on-device smoke deferred to next session
