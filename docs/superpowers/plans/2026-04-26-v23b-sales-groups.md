# V2.3-B — Sales Group / Team Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement flat peer "sales groups" so SP team members share full visibility + edit/create rights on each other's customers and requisitions, branch-agnostic, managed by Admin or Accountant roles.

**Architecture:** New `SalesGroup` entity + nullable `User.GroupId` FK. Visibility is computed at query time via JOIN through `User.GroupId` (single source of truth). New centralized helper `SalesAuthorization.VisibleSalesPersonIds(user, db)`. No denormalized columns on Customer/Requisition. Endpoint shape mirrors V2.3-A `/api/branches` admin CRUD.

**Tech Stack:** ASP.NET Core 8 + EF Core 8 + Npgsql (backend); React 19 + Vite + TanStack Query + vitest + RTL (web); React Native + Expo + TanStack Query (mobile).

**Spec:** [docs/superpowers/specs/2026-04-26-v23b-sales-group-design.md](../specs/2026-04-26-v23b-sales-group-design.md) @ commit `3db57f7`.

**Branch:** `feat/v23b-sales-groups` (branch off `master` if V2.3-A is merged, else off `feat/v23a-branch-model`).

---

## File Structure

### Backend — `BomPriceApproval.API/`

**Created:**
- `Domain/Entities/SalesGroup.cs` — entity (Id, Name, IsActive, Members)
- `Infrastructure/Authorization/SalesAuthorization.cs` — visibility helper
- `Infrastructure/Data/Migrations/<TIMESTAMP>_V23b_SalesGroups.cs` — EF migration
- `Features/Groups/GroupsController.cs` — CRUD endpoints
- `Features/Groups/GroupsDtos.cs` — admin DTOs (CreateGroupRequest, UpdateGroupRequest, GroupAdminResponse)

**Modified:**
- `Domain/Entities/User.cs` — add `GroupId` nullable FK + `Group` navigation
- `Infrastructure/Data/AppDbContext.cs` — add `DbSet<SalesGroup>`, OnModelCreating config
- `Features/Requisitions/RequisitionsController.cs` — list scoping uses helper for SP role; `CanAccess` extends to group-mates
- `Features/Customers/CustomersController.cs` — list scoping for SP role; access checks for group-mate edit
- `Features/Users/UsersController.cs` — add `GET /api/users/{id}/group` + `PUT /api/users/{id}/group`

### Backend tests — `BomPriceApproval.Tests/`

**Created:**
- `Authorization/SalesAuthorizationHelperTests.cs`
- `Groups/GroupsAdminCrudTests.cs`
- `Users/UserGroupAdminTests.cs`
- `Requisitions/RequisitionsListGroupScopingTests.cs`
- `Customers/CustomersListGroupScopingTests.cs`
- `Authorization/SalesGroupAccessTests.cs`
- `Notifications/SalesGroupNotificationRoutingTests.cs`

### Web — `bom-web/`

**Created:**
- `src/api/groups.ts`
- `src/api/userGroup.ts`
- `src/features/admin/groups/GroupsPage.tsx`
- `src/features/admin/groups/GroupsPage.test.tsx`
- `src/components/SalesGroupCell.tsx` (per-row resolver for UsersPage)

**Modified:**
- `src/features/users/UsersPage.tsx` — add Group column
- `src/features/users/UsersPage.test.tsx` — extend
- `src/features/users/EditUserModal.tsx` — add Group dropdown for SP role
- `src/features/users/EditUserModal.test.tsx` — extend
- `src/components/Sidebar.tsx` (or AppShell) — add Groups admin nav link
- `src/App.tsx` (or router) — add `/admin/groups` route

### Mobile — `bom-mobile/`

**Created:**
- `src/api/groups.ts` (read-only — useGroups query)
- `src/components/OwnedByBadge.tsx` (small text component)

**Modified:**
- `app/(sales)/index.tsx` (or wherever req cards render) — add OwnedByBadge per card
- `app/(sales)/customers.tsx` (or equivalent) — add OwnedByBadge on customer cards

### Docs

**Modified:**
- `CLAUDE.md` — document V2.3-B group model

---

## Task 1: Add `SalesGroup` entity + `User.GroupId` column + EF migration

**Files:**
- Create: `BomPriceApproval.API/Domain/Entities/SalesGroup.cs`
- Modify: `BomPriceApproval.API/Domain/Entities/User.cs`
- Modify: `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs`
- Create: `BomPriceApproval.API/Infrastructure/Data/Migrations/<TIMESTAMP>_V23b_SalesGroups.cs` (auto-generated)

- [ ] **Step 1: Create SalesGroup entity**

Create `Domain/Entities/SalesGroup.cs`:

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

- [ ] **Step 2: Add GroupId column to User entity**

Modify `Domain/Entities/User.cs` — add these two properties (location: alongside other navigation properties like `BranchId`):

```csharp
public int? GroupId { get; set; }
public SalesGroup? Group { get; set; }
```

- [ ] **Step 3: Wire DbSet + OnModelCreating config in AppDbContext**

Add to `Infrastructure/Data/AppDbContext.cs` (near other DbSets, ~line 28-30):

```csharp
public DbSet<SalesGroup> SalesGroups => Set<SalesGroup>();
```

Add inside `OnModelCreating(...)` after existing entity configs:

```csharp
mb.Entity<SalesGroup>(e =>
{
    e.HasKey(g => g.Id);
    e.Property(g => g.Name).HasMaxLength(100).IsRequired();
});

mb.Entity<User>()
    .HasOne(u => u.Group)
    .WithMany(g => g.Members)
    .HasForeignKey(u => u.GroupId)
    .OnDelete(DeleteBehavior.Restrict);  // group with members can't be deleted

mb.Entity<User>().HasIndex(u => u.GroupId);
```

- [ ] **Step 4: Stop the API process if running (DLL lock)**

If `dotnet ef migrations add` fails with file-lock errors related to `BomPriceApproval.API.exe`, stop the running API process first via Ctrl+C on its terminal or `taskkill /F /PID <pid>`. This was a recurring issue during V2.3-A.

- [ ] **Step 5: Generate migration**

Run: `dotnet ef migrations add V23b_SalesGroups --project BomPriceApproval.API`

Expected: Migration files created in `Infrastructure/Data/Migrations/`. Inspect the generated `<TIMESTAMP>_V23b_SalesGroups.cs` — should contain `CreateTable("SalesGroups", ...)`, `AddColumn("Users.GroupId")`, `CreateIndex("IX_Users_GroupId")`, `AddForeignKey` Restrict.

- [ ] **Step 6: Build to verify**

Run: `dotnet build BomPriceApproval.API --nologo -v q`
Expected: 0 errors. (~7-11 pre-existing warnings carried from V2.3-A; no new warnings.)

- [ ] **Step 7: Commit**

```bash
git add BomPriceApproval.API/Domain/Entities/SalesGroup.cs \
        BomPriceApproval.API/Domain/Entities/User.cs \
        BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs \
        BomPriceApproval.API/Infrastructure/Data/AppDbContextModelSnapshot.cs \
        "BomPriceApproval.API/Infrastructure/Data/Migrations/*V23b_SalesGroups*"
git commit -m "feat(api): add SalesGroup entity + User.GroupId FK migration (V23b)"
```

---

## Task 2: SalesAuthorization helper class + tests

**Files:**
- Create: `BomPriceApproval.API/Infrastructure/Authorization/SalesAuthorization.cs`
- Test: `BomPriceApproval.Tests/Authorization/SalesAuthorizationHelperTests.cs`

- [ ] **Step 1: Write failing helper tests**

Create `BomPriceApproval.Tests/Authorization/SalesAuthorizationHelperTests.cs`:

```csharp
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Authorization;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Authorization;

public class SalesAuthorizationHelperTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void NonSP_Role_ReturnsEmpty()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bom = new User { Role = UserRole.BomCreator, BranchId = 1 };
        SalesAuthorization.VisibleSalesPersonIds(bom, db).Should().BeEmpty();

        var acct = new User { Role = UserRole.Accountant };
        SalesAuthorization.VisibleSalesPersonIds(acct, db).Should().BeEmpty();
    }

    [Fact]
    public void SP_NoGroup_ReturnsSelfOnly()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sp = new User { Id = 12345, Role = UserRole.SalesPerson, BranchId = 1, GroupId = null };
        SalesAuthorization.VisibleSalesPersonIds(sp, db).Should().BeEquivalentTo(new[] { 12345 });
    }

    [Fact]
    public void SP_WithGroup_ReturnsAllSPMembersOfThatGroup()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var group = new SalesGroup { Name = $"TestGrp-{Guid.NewGuid():N}".Substring(0, 18), IsActive = true };
        db.SalesGroups.Add(group);
        db.SaveChanges();

        var sp1 = new User { Email = $"sp1-{Guid.NewGuid():N}@test.com", Name = "SP One", Role = UserRole.SalesPerson, BranchId = 1, GroupId = group.Id, IsActive = true };
        var sp2 = new User { Email = $"sp2-{Guid.NewGuid():N}@test.com", Name = "SP Two", Role = UserRole.SalesPerson, BranchId = 2, GroupId = group.Id, IsActive = true };
        var bomInGroup = new User { Email = $"bom-{Guid.NewGuid():N}@test.com", Name = "BOM In Grp", Role = UserRole.BomCreator, BranchId = 1, GroupId = group.Id, IsActive = true };
        db.Users.AddRange(sp1, sp2, bomInGroup);
        db.SaveChanges();

        var visible = SalesAuthorization.VisibleSalesPersonIds(sp1, db);
        visible.Should().BeEquivalentTo(new[] { sp1.Id, sp2.Id });
        visible.Should().NotContain(bomInGroup.Id, "non-SP members of the same group are excluded");
    }
}
```

- [ ] **Step 2: Run tests — expected BUILD FAIL ("SalesAuthorization not found")**

Run: `dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~SalesAuthorizationHelperTests" --nologo --configuration Release`
Expected: Build error `CS0234: namespace 'Authorization' does not exist`.

- [ ] **Step 3: Implement helper**

Create `BomPriceApproval.API/Infrastructure/Authorization/SalesAuthorization.cs`:

```csharp
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;

namespace BomPriceApproval.API.Infrastructure.Authorization;

public static class SalesAuthorization
{
    /// <summary>
    /// Returns the set of SalesPerson IDs whose customers + reqs are visible to <paramref name="user"/>.
    /// SP with a group: returns all SP members of that group.
    /// SP without a group: returns just self (Q9 fallback).
    /// Non-SP roles: empty array (caller should not use this helper for them).
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

- [ ] **Step 4: Run tests — expected PASS**

Run: `dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~SalesAuthorizationHelperTests" --nologo --configuration Release`
Expected: 3/3 pass.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Authorization/SalesAuthorization.cs \
        BomPriceApproval.Tests/Authorization/SalesAuthorizationHelperTests.cs
git commit -m "feat(api): add SalesAuthorization.VisibleSalesPersonIds helper + 3 unit tests (V23b)"
```

---

## Task 3: Groups admin CRUD endpoints + tests

**Files:**
- Create: `BomPriceApproval.API/Features/Groups/GroupsController.cs`
- Create: `BomPriceApproval.API/Features/Groups/GroupsDtos.cs`
- Test: `BomPriceApproval.Tests/Groups/GroupsAdminCrudTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BomPriceApproval.Tests/Groups/GroupsAdminCrudTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Groups;

public class GroupsAdminCrudTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> TokenAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    [Fact]
    public async Task Create_AsAdmin_Returns201()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
        var resp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"GrpA-{Guid.NewGuid():N}".Substring(0, 18) });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_AsAccountant_Returns201()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("sara@test.com", "Test@1234"));
        var resp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"GrpB-{Guid.NewGuid():N}".Substring(0, 18) });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_AsSP_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("ali@test.com", "Test@1234"));
        var resp = await _client.PostAsJsonAsync("/api/groups", new { Name = "Try" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_AsBomCreator_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("bob@test.com", "Test@1234"));
        var resp = await _client.PostAsJsonAsync("/api/groups", new { Name = "Try" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_AsAdmin_TogglesIsActive()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
        var create = await _client.PostAsJsonAsync("/api/groups", new { Name = $"Toggle-{Guid.NewGuid():N}".Substring(0, 18) });
        create.EnsureSuccessStatusCode();
        var created = (await create.Content.ReadFromJsonAsync<GroupAdminResponse>())!;

        var upd = await _client.PutAsJsonAsync($"/api/groups/{created.Id}", new { Name = created.Name, IsActive = false });
        upd.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResp = await _client.GetAsync("/api/groups");
        var list = (await listResp.Content.ReadFromJsonAsync<List<GroupAdminResponse>>())!;
        list.First(g => g.Id == created.Id).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_BlocksWhenInUse()
    {
        // Create group, assign Ali to it, attempt delete → 409
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
        var create = await _client.PostAsJsonAsync("/api/groups", new { Name = $"InUse-{Guid.NewGuid():N}".Substring(0, 18) });
        create.EnsureSuccessStatusCode();
        var grp = (await create.Content.ReadFromJsonAsync<GroupAdminResponse>())!;

        var users = (await _client.GetFromJsonAsync<List<UserShort>>("/api/users"))!;
        var ali = users.First(u => u.Email == "ali@test.com");
        await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = grp.Id });

        var delResp = await _client.DeleteAsync($"/api/groups/{grp.Id}");
        delResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Cleanup: clear Ali's group so other tests aren't polluted
        await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = (int?)null });
    }

    [Fact]
    public async Task Delete_NoMembers_SoftDeletes()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
        var create = await _client.PostAsJsonAsync("/api/groups", new { Name = $"Del-{Guid.NewGuid():N}".Substring(0, 18) });
        create.EnsureSuccessStatusCode();
        var grp = (await create.Content.ReadFromJsonAsync<GroupAdminResponse>())!;

        var del = await _client.DeleteAsync($"/api/groups/{grp.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = (await _client.GetFromJsonAsync<List<GroupAdminResponse>>("/api/groups"))!;
        list.First(g => g.Id == grp.Id).IsActive.Should().BeFalse();
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record GroupAdminResponse(int Id, string Name, bool IsActive);
    private record UserShort(int Id, string Email, string Name, string Role);
}
```

- [ ] **Step 2: Run tests — expected FAIL (404 on all endpoints)**

Run: `dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~GroupsAdminCrudTests" --nologo --configuration Release`

- [ ] **Step 3: Add DTOs**

Create `BomPriceApproval.API/Features/Groups/GroupsDtos.cs`:

```csharp
namespace BomPriceApproval.API.Features.Groups;

public record CreateGroupRequest(string Name);
public record UpdateGroupRequest(string Name, bool IsActive);
public record GroupAdminResponse(int Id, string Name, bool IsActive);
```

- [ ] **Step 4: Implement GroupsController**

Create `BomPriceApproval.API/Features/Groups/GroupsController.cs`:

```csharp
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Groups;

[ApiController]
[Route("api/groups")]
[Authorize]
public class GroupsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.SalesGroups
            .OrderBy(g => g.Id)
            .Select(g => new GroupAdminResponse(g.Id, g.Name, g.IsActive))
            .ToListAsync());

    [HttpPost]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<IActionResult> Create(CreateGroupRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Validation.Detail("Group name is required.").Field("Name", "Required.").Return();

        var entity = new SalesGroup { Name = req.Name.Trim(), IsActive = true };
        db.SalesGroups.Add(entity);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new GroupAdminResponse(entity.Id, entity.Name, entity.IsActive));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<IActionResult> Update(int id, UpdateGroupRequest req)
    {
        var g = await db.SalesGroups.FindAsync(id);
        if (g is null) return NotFound();
        g.Name = req.Name.Trim();
        g.IsActive = req.IsActive;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<IActionResult> Delete(int id)
    {
        var g = await db.SalesGroups.FindAsync(id);
        if (g is null) return NotFound();

        // Block soft-delete if any user references this group
        var inUse = await db.Users.AnyAsync(u => u.GroupId == id);
        if (inUse)
            return Conflict(new { message = $"Group {g.Name} has assigned users and cannot be deleted." });

        g.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
```

(Adapt `Validation.Detail(...).Field(...).Return()` to whatever pattern actually exists in this codebase — check `BranchesController.Create` from V2.3-A Task 12 as the reference.)

- [ ] **Step 5: Run tests — expected PASS**

Run: `dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~GroupsAdminCrudTests" --nologo --configuration Release`
Expected: 7/7 pass.

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Features/Groups/GroupsController.cs \
        BomPriceApproval.API/Features/Groups/GroupsDtos.cs \
        BomPriceApproval.Tests/Groups/GroupsAdminCrudTests.cs
git commit -m "feat(api): SalesGroups admin CRUD (Admin+Accountant) with in-use guard (V23b)"
```

---

## Task 4: GET / PUT `/api/users/{id}/group` admin endpoints + tests

**Files:**
- Modify: `BomPriceApproval.API/Features/Users/UsersController.cs`
- Test: `BomPriceApproval.Tests/Users/UserGroupAdminTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BomPriceApproval.Tests/Users/UserGroupAdminTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Users;

public class UserGroupAdminTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task<int> CreateGroupAsync(string namePrefix)
    {
        var resp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"{namePrefix}-{Guid.NewGuid():N}".Substring(0, 18) });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<GroupShort>())!.Id;
    }

    [Fact]
    public async Task SetGroup_AsAdmin_OnSP_Persists()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpId = await CreateGroupAsync("SetA");
        var users = (await _client.GetFromJsonAsync<List<UserShort>>("/api/users"))!;
        var ali = users.First(u => u.Email == "ali@test.com");

        var put = await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = grpId });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetFromJsonAsync<UserGroupResponse>($"/api/users/{ali.Id}/group");
        get!.GroupId.Should().Be(grpId);

        // Cleanup
        await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = (int?)null });
    }

    [Fact]
    public async Task SetGroup_AsAccountant_OnSP_Persists()
    {
        var sara = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sara.AccessToken);

        var grpId = await CreateGroupAsync("SetB");
        var users = (await _client.GetFromJsonAsync<List<UserShort>>("/api/users"))!;
        var ali = users.First(u => u.Email == "ali@test.com");

        var put = await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = grpId });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Cleanup
        await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = (int?)null });
    }

    [Fact]
    public async Task SetGroup_AsSP_Returns403()
    {
        var ali = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ali.AccessToken);

        var put = await _client.PutAsJsonAsync($"/api/users/999/group", new { GroupId = 1 });
        put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SetGroup_OnNonSPUser_Returns400()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpId = await CreateGroupAsync("Reject");
        var users = (await _client.GetFromJsonAsync<List<UserShort>>("/api/users"))!;
        var bob = users.First(u => u.Email == "bob@test.com");  // BomCreator

        var put = await _client.PutAsJsonAsync($"/api/users/{bob.Id}/group", new { GroupId = grpId });
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ClearGroup_PassNullGroupId_Persists()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpId = await CreateGroupAsync("Clear");
        var users = (await _client.GetFromJsonAsync<List<UserShort>>("/api/users"))!;
        var ali = users.First(u => u.Email == "ali@test.com");

        await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = grpId });
        var clear = await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = (int?)null });
        clear.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetFromJsonAsync<UserGroupResponse>($"/api/users/{ali.Id}/group");
        get!.GroupId.Should().BeNull();
    }

    [Fact]
    public async Task SetGroup_InactiveGroup_Returns400()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpId = await CreateGroupAsync("Inactive");
        // Soft-delete the group
        var del = await _client.DeleteAsync($"/api/groups/{grpId}");
        del.EnsureSuccessStatusCode();

        var users = (await _client.GetFromJsonAsync<List<UserShort>>("/api/users"))!;
        var ali = users.First(u => u.Email == "ali@test.com");

        var put = await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = grpId });
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record GroupShort(int Id, string Name, bool IsActive);
    private record UserShort(int Id, string Email, string Name, string Role);
    private record UserGroupResponse(int? GroupId, string? GroupName);
}
```

- [ ] **Step 2: Run tests — expected FAIL (404)**

Run: `dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~UserGroupAdminTests" --nologo --configuration Release`

- [ ] **Step 3: Implement endpoints in UsersController**

Add to `BomPriceApproval.API/Features/Users/UsersController.cs`:

```csharp
[HttpGet("{id}/group")]
[Authorize(Roles = "Admin,Accountant")]
public async Task<IActionResult> GetGroup(int id)
{
    var u = await db.Users
        .Where(x => x.Id == id)
        .Select(x => new { x.GroupId, GroupName = x.Group != null ? x.Group.Name : null })
        .FirstOrDefaultAsync();
    if (u is null) return NotFound();
    return Ok(new UserGroupResponse(u.GroupId, u.GroupName));
}

public record SetUserGroupRequest(int? GroupId);
public record UserGroupResponse(int? GroupId, string? GroupName);

[HttpPut("{id}/group")]
[Authorize(Roles = "Admin,Accountant")]
public async Task<IActionResult> SetGroup(int id, SetUserGroupRequest req)
{
    var u = await db.Users.FindAsync(id);
    if (u is null) return NotFound();
    if (u.Role != UserRole.SalesPerson)
        return Validation.Detail("Groups can only be set on SalesPersons.").Field("Role", "Must be SalesPerson.").Return();

    if (req.GroupId.HasValue)
    {
        var grp = await db.SalesGroups.FindAsync(req.GroupId.Value);
        if (grp is null || !grp.IsActive)
            return Validation.Detail("Group not found or inactive.").Field("GroupId", "Invalid group.").Return();
    }

    u.GroupId = req.GroupId;
    await db.SaveChangesAsync();
    return NoContent();
}
```

(Match the actual `Validation` builder in this codebase — see how UserBranches set/get was done in V2.3-A Task 13.)

- [ ] **Step 4: Run tests — expected PASS**

Run: `dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~UserGroupAdminTests" --nologo --configuration Release`
Expected: 6/6 pass.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Users/UsersController.cs \
        BomPriceApproval.Tests/Users/UserGroupAdminTests.cs
git commit -m "feat(api): admin GET/PUT /api/users/{id}/group for SP group assignment (V23b)"
```

---

## Task 5: Rewrite SP scoping in `RequisitionsController.GetAll` to use group helper

**Files:**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` — `GetAll`
- Test: `BomPriceApproval.Tests/Requisitions/RequisitionsListGroupScopingTests.cs`

- [ ] **Step 1: Write failing list-scoping tests**

Create `BomPriceApproval.Tests/Requisitions/RequisitionsListGroupScopingTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

public class RequisitionsListGroupScopingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task<(int spId, string email)> CreateSpAsync(string namePrefix, int branchId)
    {
        var email = $"{namePrefix}-{Guid.NewGuid():N}".Substring(0, 22) + "@test.com";
        var resp = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = $"SP {namePrefix}", Email = email, Password = "Test@1234", Role = 1, BranchId = branchId
        });
        resp.EnsureSuccessStatusCode();
        var u = (await resp.Content.ReadFromJsonAsync<UserShort>())!;
        return (u.Id, email);
    }

    [Fact]
    public async Task SP_NoGroup_OnlySeesOwnReqs()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var (spA_Id, spA_email) = await CreateSpAsync("noGrpA", 1);
        var (spB_Id, spB_email) = await CreateSpAsync("noGrpB", 1);

        // SP B creates a req
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1, CustomerId = customers.First().Id, CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        create.EnsureSuccessStatusCode();
        var spBReqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        // SP A logs in — should NOT see B's req
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var list = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions"))!;
        list.Any(r => r.Id == spBReqId).Should().BeFalse("solo SP only sees own");
    }

    [Fact]
    public async Task SP_InGroupWithPeer_SeesPeersReqs()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        // Create group
        var grpResp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"GrpScope-{Guid.NewGuid():N}".Substring(0, 18) });
        grpResp.EnsureSuccessStatusCode();
        var grpId = (await grpResp.Content.ReadFromJsonAsync<GroupShort>())!.Id;

        // Create 2 SPs and put both in group
        var (spA_Id, spA_email) = await CreateSpAsync("grpA", 1);
        var (spB_Id, spB_email) = await CreateSpAsync("grpB", 1);
        await _client.PutAsJsonAsync($"/api/users/{spA_Id}/group", new { GroupId = grpId });
        await _client.PutAsJsonAsync($"/api/users/{spB_Id}/group", new { GroupId = grpId });

        // SP B creates a req
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1, CustomerId = customers.First().Id, CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        create.EnsureSuccessStatusCode();
        var spBReqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        // SP A logs in — should now SEE B's req
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var list = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions"))!;
        list.Any(r => r.Id == spBReqId).Should().BeTrue("group peers share req visibility");
    }

    [Fact]
    public async Task SP_RemovedFromGroup_LosesPeerVisibility()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpResp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"GrpCut-{Guid.NewGuid():N}".Substring(0, 18) });
        grpResp.EnsureSuccessStatusCode();
        var grpId = (await grpResp.Content.ReadFromJsonAsync<GroupShort>())!.Id;

        var (spA_Id, spA_email) = await CreateSpAsync("cutA", 1);
        var (spB_Id, spB_email) = await CreateSpAsync("cutB", 1);
        await _client.PutAsJsonAsync($"/api/users/{spA_Id}/group", new { GroupId = grpId });
        await _client.PutAsJsonAsync($"/api/users/{spB_Id}/group", new { GroupId = grpId });

        // SP B creates a req
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1, CustomerId = customers.First().Id, CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        var spBReqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        // Remove A from group
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        await _client.PutAsJsonAsync($"/api/users/{spA_Id}/group", new { GroupId = (int?)null });

        // SP A re-checks list — no longer sees B
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var list = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions"))!;
        list.Any(r => r.Id == spBReqId).Should().BeFalse("Q11 clean cut");
    }

    [Fact]
    public async Task SP_InGroupCrossBranch_SeesCrossBranchPeerReqs()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpResp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"Cross-{Guid.NewGuid():N}".Substring(0, 18) });
        grpResp.EnsureSuccessStatusCode();
        var grpId = (await grpResp.Content.ReadFromJsonAsync<GroupShort>())!.Id;

        var (spB1_Id, spB1_email) = await CreateSpAsync("br1", 1);
        var (spB2_Id, spB2_email) = await CreateSpAsync("br2", 2);
        await _client.PutAsJsonAsync($"/api/users/{spB1_Id}/group", new { GroupId = grpId });
        await _client.PutAsJsonAsync($"/api/users/{spB2_Id}/group", new { GroupId = grpId });

        // SP-branch1 creates a branch-1 req
        var spB1 = await LoginAsync(spB1_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB1.AccessToken);
        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1, CustomerId = customers.First().Id, CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        var br1ReqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        // SP-branch2 logs in and should see branch-1 req via group
        var spB2 = await LoginAsync(spB2_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB2.AccessToken);
        var list = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions"))!;
        list.Any(r => r.Id == br1ReqId).Should().BeTrue("Q5 group is branch-agnostic");
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record UserShort(int Id, string Email, string Name, string Role);
    private record GroupShort(int Id, string Name, bool IsActive);
    private record CustomerShort(int Id, string Name);
    private record ItemShort(int Id, string Code, string Description, string Type, int BranchId);
    private record CreateResponse(int Id, string RefNo);
    private record ReqShort(int Id, string RefNo, string Status, int BranchId);
}
```

- [ ] **Step 2: Run tests — expected FAIL**

Run: `dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~RequisitionsListGroupScopingTests" --nologo --configuration Release`
Expected: All 4 fail (current scoping returns only `q.SalesPersonId == CurrentUserId`).

- [ ] **Step 3: Modify RequisitionsController.GetAll**

In `Features/Requisitions/RequisitionsController.cs`, find the SP branch of the scoping logic (V2.3-A left it as):
```csharp
else if (CurrentRole == "SalesPerson")
{
    query = query.Where(q => q.SalesPersonId == CurrentUserId);
}
```

Replace with:
```csharp
else if (CurrentRole == "SalesPerson")
{
    var currentUser = await db.Users.FirstAsync(u => u.Id == CurrentUserId);
    var visibleIds = SalesAuthorization.VisibleSalesPersonIds(currentUser, db);
    query = query.Where(q => visibleIds.Contains(q.SalesPersonId));
}
```

Add `using BomPriceApproval.API.Infrastructure.Authorization;` at top of file (likely already there from V2.3-A Task 7).

- [ ] **Step 4: Run tests — expected PASS**

Run: `dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~RequisitionsListGroupScopingTests" --nologo --configuration Release`
Expected: 4/4 pass.

- [ ] **Step 5: Run full backend suite — verify no regression**

Run: `dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --nologo --configuration Release`
Expected: All previously-green tests stay green. Solo SP visibility (existing assumption in many tests) preserved because of `if (user.GroupId == null) return [user.Id]` fallback.

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs \
        BomPriceApproval.Tests/Requisitions/RequisitionsListGroupScopingTests.cs
git commit -m "fix(api): scope SP requisitions list via group helper (V23b)"
```

---

## Task 6: Rewrite SP scoping in `CustomersController.GetAll` to use group helper

**Files:**
- Modify: `BomPriceApproval.API/Features/Customers/CustomersController.cs` — `GetAll`
- Test: `BomPriceApproval.Tests/Customers/CustomersListGroupScopingTests.cs`

- [ ] **Step 1: Write failing list-scoping tests**

Create `BomPriceApproval.Tests/Customers/CustomersListGroupScopingTests.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Customers;

public class CustomersListGroupScopingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task<(int spId, string email)> CreateSpAsync(string namePrefix)
    {
        var email = $"{namePrefix}-{Guid.NewGuid():N}".Substring(0, 22) + "@test.com";
        var resp = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = $"SP {namePrefix}", Email = email, Password = "Test@1234", Role = 1, BranchId = 1
        });
        resp.EnsureSuccessStatusCode();
        var u = (await resp.Content.ReadFromJsonAsync<UserShort>())!;
        return (u.Id, email);
    }

    [Fact]
    public async Task SP_InGroup_SeesPeerCreatedCustomers()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpResp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"CustGrp-{Guid.NewGuid():N}".Substring(0, 18) });
        var grpId = (await grpResp.Content.ReadFromJsonAsync<GroupShort>())!.Id;

        var (spA_Id, spA_email) = await CreateSpAsync("cgA");
        var (spB_Id, spB_email) = await CreateSpAsync("cgB");
        await _client.PutAsJsonAsync($"/api/users/{spA_Id}/group", new { GroupId = grpId });
        await _client.PutAsJsonAsync($"/api/users/{spB_Id}/group", new { GroupId = grpId });

        // SP B creates a customer
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var custResp = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = $"CUST-{Guid.NewGuid():N}".Substring(0, 12),
            Name = "Test Customer for Group",
            BranchId = 1
        });
        custResp.EnsureSuccessStatusCode();
        var custId = (await custResp.Content.ReadFromJsonAsync<CustShort>())!.Id;

        // SP A logs in — should see B's customer
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var list = (await _client.GetFromJsonAsync<List<CustShort>>("/api/customers"))!;
        list.Any(c => c.Id == custId).Should().BeTrue("group peer customers visible");
    }

    [Fact]
    public async Task SP_NoGroup_OnlySeesOwnCustomers()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var (spA_Id, spA_email) = await CreateSpAsync("scA");
        var (spB_Id, spB_email) = await CreateSpAsync("scB");

        // SP B creates a customer
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var custResp = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = $"CUST-{Guid.NewGuid():N}".Substring(0, 12),
            Name = "Solo Test Customer",
            BranchId = 1
        });
        var custId = (await custResp.Content.ReadFromJsonAsync<CustShort>())!.Id;

        // SP A — solo, doesn't see B's customer
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var list = (await _client.GetFromJsonAsync<List<CustShort>>("/api/customers"))!;
        list.Any(c => c.Id == custId).Should().BeFalse("solo SP only sees own customers");
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record UserShort(int Id, string Email, string Name, string Role);
    private record GroupShort(int Id, string Name, bool IsActive);
    private record CustShort(int Id, string Code, string Name);
}
```

- [ ] **Step 2: Run tests — expected FAIL**

Run: `dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~CustomersListGroupScopingTests" --nologo --configuration Release`

- [ ] **Step 3: Modify CustomersController.GetAll**

Find the SP scoping branch (likely `c.SalesPersonId == CurrentUserId`). Replace with:

```csharp
if (CurrentRole == "SalesPerson")
{
    var currentUser = await db.Users.FirstAsync(u => u.Id == CurrentUserId);
    var visibleIds = SalesAuthorization.VisibleSalesPersonIds(currentUser, db);
    query = query.Where(c => visibleIds.Contains(c.SalesPersonId));
}
```

Add the using import.

- [ ] **Step 4: Run tests — expected PASS**

Run: `dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~CustomersListGroupScopingTests" --nologo --configuration Release`
Expected: 2/2 pass.

- [ ] **Step 5: Run full suite**

Expected: green (preserved by GroupId-null fallback).

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Features/Customers/CustomersController.cs \
        BomPriceApproval.Tests/Customers/CustomersListGroupScopingTests.cs
git commit -m "fix(api): scope SP customers list via group helper (V23b)"
```

---

## Task 7: Extend `CanAccess` for group-mate access on req detail + customer edit + create-against-peer-customer

**Files:**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` — `CanAccess` method
- Modify: `BomPriceApproval.API/Features/Customers/CustomersController.cs` — `CanAccess` (or equivalent helper) + customer edit endpoint
- Test: `BomPriceApproval.Tests/Authorization/SalesGroupAccessTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BomPriceApproval.Tests/Authorization/SalesGroupAccessTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Authorization;

public class SalesGroupAccessTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task<(int spId, string email)> CreateSpAsync(string namePrefix)
    {
        var email = $"{namePrefix}-{Guid.NewGuid():N}".Substring(0, 22) + "@test.com";
        var resp = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = $"SP {namePrefix}", Email = email, Password = "Test@1234", Role = 1, BranchId = 1
        });
        resp.EnsureSuccessStatusCode();
        var u = (await resp.Content.ReadFromJsonAsync<UserShort>())!;
        return (u.Id, email);
    }

    private async Task<int> SetupGroupAsync(string namePrefix, int[] spIds)
    {
        var grpResp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"{namePrefix}-{Guid.NewGuid():N}".Substring(0, 18) });
        grpResp.EnsureSuccessStatusCode();
        var grpId = (await grpResp.Content.ReadFromJsonAsync<GroupShort>())!.Id;
        foreach (var id in spIds)
            await _client.PutAsJsonAsync($"/api/users/{id}/group", new { GroupId = grpId });
        return grpId;
    }

    [Fact]
    public async Task GroupMember_CanGet_PeerReqDetail()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var (spA_Id, spA_email) = await CreateSpAsync("acA");
        var (spB_Id, spB_email) = await CreateSpAsync("acB");
        await SetupGroupAsync("acGrp", new[] { spA_Id, spB_Id });

        // SP B creates a req
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1, CustomerId = customers.First().Id, CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        var spBReqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        // SP A GETs B's req detail — should succeed
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var get = await _client.GetAsync($"/api/requisitions/{spBReqId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NonGroupSP_CannotGet_OtherSPReqDetail_Returns403or404()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var (spA_Id, spA_email) = await CreateSpAsync("ngA");
        var (spB_Id, spB_email) = await CreateSpAsync("ngB");
        // No group — both solo

        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1, CustomerId = customers.First().Id, CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        var spBReqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var get = await _client.GetAsync($"/api/requisitions/{spBReqId}");
        get.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GroupMember_CanCreateReq_AgainstPeerCustomer()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var (spA_Id, spA_email) = await CreateSpAsync("crA");
        var (spB_Id, spB_email) = await CreateSpAsync("crB");
        await SetupGroupAsync("crGrp", new[] { spA_Id, spB_Id });

        // SP B creates a customer
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var custResp = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = $"CUST-{Guid.NewGuid():N}".Substring(0, 12),
            Name = "Peer Customer",
            BranchId = 1
        });
        var custId = (await custResp.Content.ReadFromJsonAsync<CustShort>())!.Id;

        // SP A creates a req against B's customer
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1, CustomerId = custId, CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        // The new req has SalesPersonId = SP A
        var reqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;
        var detail = (await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}"))!;
        detail.SalesPersonId.Should().Be(spA_Id);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record UserShort(int Id, string Email, string Name, string Role);
    private record GroupShort(int Id, string Name, bool IsActive);
    private record CustomerShort(int Id, string Name);
    private record CustShort(int Id, string Code, string Name);
    private record ItemShort(int Id, string Code, string Description, string Type, int BranchId);
    private record CreateResponse(int Id, string RefNo);
    private record ReqDetail(int Id, int SalesPersonId);
}
```

- [ ] **Step 2: Run tests — expected mixed (test 3 may already pass)**

Run: `dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~SalesGroupAccessTests" --nologo --configuration Release`
Test 1 likely fails (CanAccess too restrictive); test 2 should pass; test 3 may already pass (no SP-customer ownership check on create — verify).

- [ ] **Step 3: Modify `CanAccess` in RequisitionsController**

Find the existing `CanAccess(QuotationRequest q)` helper. The SP path likely reads:
```csharp
if (CurrentRole == "SalesPerson")
    return q.SalesPersonId == CurrentUserId;
```

Replace with:
```csharp
if (CurrentRole == "SalesPerson")
{
    var currentUser = db.Users.First(u => u.Id == CurrentUserId);
    var visibleIds = SalesAuthorization.VisibleSalesPersonIds(currentUser, db);
    return visibleIds.Contains(q.SalesPersonId);
}
```

(Note synchronous `.First` — `CanAccess` may be sync. If async, use `FirstAsync`. Match existing signature.)

- [ ] **Step 4: Verify customer-create permission check (POST /api/requisitions)**

In `RequisitionsController.Create`, the customer is referenced by `req.CustomerId`. Check if the controller verifies the SP "owns" the customer. If yes, that check needs to expand to group visibility. If not (i.e., customer pickability already comes from list which is now group-scoped from Task 6), no change needed.

The plan assumes the existing controller doesn't explicitly check `Customer.SalesPersonId == CurrentUserId` before creating a req — it just uses the customer ID provided. Test 3 verifies this empirically. If test 3 fails, add the group-aware check.

- [ ] **Step 5: Modify customer access checks if applicable**

If `CustomersController` has an explicit access check on PUT/PATCH endpoints (customer edit), wrap it the same way:
```csharp
if (CurrentRole == "SalesPerson")
{
    var currentUser = await db.Users.FirstAsync(u => u.Id == CurrentUserId);
    var visibleIds = SalesAuthorization.VisibleSalesPersonIds(currentUser, db);
    if (!visibleIds.Contains(customer.SalesPersonId)) return Forbid();
}
```

If the controller currently only allows the SP who created the customer to edit, expanding to group members is the change. If there's no explicit ownership check (only branch-isolation), the existing check is already permissive enough.

- [ ] **Step 6: Run tests — expected PASS**

Run: `dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~SalesGroupAccessTests" --nologo --configuration Release`
Expected: 3/3 pass.

- [ ] **Step 7: Run full suite**

Run: `dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --nologo --configuration Release`
Expected: green.

- [ ] **Step 8: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs \
        BomPriceApproval.API/Features/Customers/CustomersController.cs \
        BomPriceApproval.Tests/Authorization/SalesGroupAccessTests.cs
git commit -m "feat(api): allow group members access to peers' reqs + customers (V23b)"
```

---

## Task 8: Notification routing test — verify original SP only

**Files:**
- Test: `BomPriceApproval.Tests/Notifications/SalesGroupNotificationRoutingTests.cs`

- [ ] **Step 1: Add test asserting Q8 — only original SP gets notif on group reqs**

Create `BomPriceApproval.Tests/Notifications/SalesGroupNotificationRoutingTests.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Notifications;

public class SalesGroupNotificationRoutingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    [Fact]
    public async Task ReqProgression_NotifiesOnlyOriginalSP_NotPeerInGroup()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        // Create group with 2 SPs (use ali for one + a fresh second SP)
        var grpResp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"NotifGrp-{Guid.NewGuid():N}".Substring(0, 18) });
        var grpId = (await grpResp.Content.ReadFromJsonAsync<GroupShort>())!.Id;

        var users = (await _client.GetFromJsonAsync<List<UserShort>>("/api/users"))!;
        var ali = users.First(u => u.Email == "ali@test.com");
        await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = grpId });

        var peerEmail = $"peer-{Guid.NewGuid():N}".Substring(0, 22) + "@test.com";
        var peerResp = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "Peer SP", Email = peerEmail, Password = "Test@1234", Role = 1, BranchId = 1
        });
        var peer = (await peerResp.Content.ReadFromJsonAsync<UserShort>())!;
        await _client.PutAsJsonAsync($"/api/users/{peer.Id}/group", new { GroupId = grpId });

        // Ali creates a req
        var aliLogin = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliLogin.AccessToken);
        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1, CustomerId = customers.First().Id, CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        var reqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        // Status fan-out happens on req creation: BomCreators get notif. We're checking that the PEER SP doesn't get any req notif.
        // The peer should have ZERO notifs about this req.
        async Task<int> CountForReq(string email, string password)
        {
            var login = await LoginAsync(email, password);
            using var c = factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
            var notifs = (await c.GetFromJsonAsync<List<NotifShort>>("/api/notifications"))!;
            return notifs.Count(n => n.ReferenceType == "QuotationRequest" && n.ReferenceId == reqId);
        }

        (await CountForReq(peerEmail, "Test@1234")).Should().Be(0, "peer in same group should NOT receive req-progression notifs");

        // Cleanup
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = (int?)null });
        await _client.PutAsJsonAsync($"/api/users/{peer.Id}/group", new { GroupId = (int?)null });
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record GroupShort(int Id, string Name, bool IsActive);
    private record UserShort(int Id, string Email, string Name, string Role);
    private record CustomerShort(int Id, string Name);
    private record ItemShort(int Id, string Code, string Description, string Type, int BranchId);
    private record CreateResponse(int Id, string RefNo);
    private record NotifShort(int Id, string Message, string ReferenceType, int? ReferenceId);
}
```

- [ ] **Step 2: Run test — expected PASS**

Run: `dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~SalesGroupNotificationRoutingTests" --nologo --configuration Release`
Expected: PASS — current notification fan-out logic doesn't touch group members; this is a regression-prevention test.

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.Tests/Notifications/SalesGroupNotificationRoutingTests.cs
git commit -m "test(api): assert group-peer SP does not receive req notifs (Q8 routing)"
```

---

## Task 9: Web — `useGroups` + group CRUD hooks + user-group hooks

**Files:**
- Create: `bom-web/src/api/groups.ts`
- Create: `bom-web/src/api/userGroup.ts`

- [ ] **Step 1: Create useGroups + CRUD hooks**

Create `bom-web/src/api/groups.ts`:

```ts
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "./axios";

export interface SalesGroup {
  id: number;
  name: string;
  isActive: boolean;
}

export const groupKeys = {
  all: ["groups"] as const,
  list: () => [...groupKeys.all, "list"] as const,
};

export function useGroups() {
  return useQuery({
    queryKey: groupKeys.list(),
    queryFn: async () => (await api.get<SalesGroup[]>("/groups")).data,
    staleTime: 5 * 60_000,
  });
}

export function useCreateGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (req: { name: string }) =>
      (await api.post<SalesGroup>("/groups", req)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: groupKeys.list() }),
  });
}

export function useUpdateGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (req: { id: number; name: string; isActive: boolean }) => {
      await api.put(`/groups/${req.id}`, { name: req.name, isActive: req.isActive });
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: groupKeys.list() }),
  });
}

export function useDeleteGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      await api.delete(`/groups/${id}`);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: groupKeys.list() }),
  });
}
```

(Verify the axios baseURL strips `/api` — V2.3-A Task 17 implementer confirmed this. If it doesn't, prepend `/api/groups` instead.)

- [ ] **Step 2: Create user-group hooks**

Create `bom-web/src/api/userGroup.ts`:

```ts
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "./axios";

export interface UserGroupResponse {
  groupId: number | null;
  groupName: string | null;
}

export function useUserGroup(userId: number, enabled = true) {
  return useQuery({
    queryKey: ["users", userId, "group"],
    queryFn: async () => (await api.get<UserGroupResponse>(`/users/${userId}/group`)).data,
    enabled: enabled && userId > 0,
  });
}

export function useSetUserGroup(userId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (groupId: number | null) => {
      await api.put(`/users/${userId}/group`, { groupId });
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["users", userId, "group"] });
      qc.invalidateQueries({ queryKey: ["users"] });
    },
  });
}
```

- [ ] **Step 3: tsc**

Run: `cd bom-web && npx tsc --noEmit`
Expected: 0 new errors (only pre-existing baseUrl deprecation warning).

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/api/groups.ts bom-web/src/api/userGroup.ts
git commit -m "feat(web): add groups + user-group API hooks (V23b)"
```

---

## Task 10: Web — `/admin/groups` page + sidebar nav + route

**Files:**
- Create: `bom-web/src/features/admin/groups/GroupsPage.tsx`
- Create: `bom-web/src/features/admin/groups/GroupsPage.test.tsx`
- Modify: `bom-web/src/components/Sidebar.tsx` (or AppShell — check existing)
- Modify: `bom-web/src/App.tsx` (or router config)

- [ ] **Step 1: Write GroupsPage + tests**

Build off the V2.3-A Task 20 BranchesPage pattern. The structural shape is essentially identical: list table with Add/Edit modals + Delete confirm + 409 handling.

`GroupsPage.tsx` should mirror `BranchesPage.tsx` line-for-line, just substituting `Branch` types with `SalesGroup`, `useBranches` with `useGroups`, etc.

`GroupsPage.test.tsx` should cover:
- Renders list
- Add modal flow: input name, save → row appears
- Edit modal flow: change name + IsActive toggle
- Delete confirm: 409 surfaces error message in UI
- Admin + Accountant role visibility (mock auth in both states)

Both files should target ~14 tests like V2.3-A's BranchesPage.

- [ ] **Step 2: Add route + sidebar link**

In `bom-web/src/App.tsx` (or wherever routes are configured), add:
```tsx
<Route path="/admin/groups" element={
  <ProtectedRoute allow={["Admin", "Accountant"]}>
    <GroupsPage />
  </ProtectedRoute>
} />
```

In `bom-web/src/components/Sidebar.tsx`, add (alongside existing Branches link):
```tsx
{ to: "/admin/groups", label: "Groups", icon: Users2, roles: ["Admin", "Accountant"] }
```

(Use a sensible icon from `lucide-react` — `Users2` or similar.)

- [ ] **Step 3: Run tests + tsc**

Run:
```
cd bom-web && npm run test -- GroupsPage
cd bom-web && npx tsc --noEmit
```
Expected: green.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/features/admin/groups/ \
        bom-web/src/components/Sidebar.tsx \
        bom-web/src/App.tsx
git commit -m "feat(web): admin Groups CRUD page + sidebar nav (V23b)"
```

---

## Task 11: Web — `UsersPage` Group column + `EditUserModal` group dropdown

**Files:**
- Create: `bom-web/src/components/SalesGroupCell.tsx`
- Modify: `bom-web/src/features/users/UsersPage.tsx`
- Modify: `bom-web/src/features/users/UsersPage.test.tsx`
- Modify: `bom-web/src/features/users/EditUserModal.tsx`
- Modify: `bom-web/src/features/users/EditUserModal.test.tsx`

- [ ] **Step 1: Build SalesGroupCell component**

Create `bom-web/src/components/SalesGroupCell.tsx`:

```tsx
import { useUserGroup } from "@/api/userGroup";

interface Props {
  userId: number;
  role: string;
}

export function SalesGroupCell({ userId, role }: Props) {
  if (role !== "SalesPerson") return <span className="text-slate-400">—</span>;
  const { data } = useUserGroup(userId);
  return <span className="text-sm">{data?.groupName ?? <span className="text-slate-400">—</span>}</span>;
}
```

- [ ] **Step 2: Add Group column to UsersPage**

In `bom-web/src/features/users/UsersPage.tsx`, add a new `<th>Group</th>` column header and a `<td><SalesGroupCell userId={u.id} role={u.role} /></td>` cell in the row mapping. Place it after Branch column.

Update `UsersPage.test.tsx` with 2 new tests:
1. Group column header renders
2. SP rows show group name when assigned; "—" otherwise (mock `useUserGroup` to return both states)

- [ ] **Step 3: Add Group dropdown to EditUserModal**

In `bom-web/src/features/users/EditUserModal.tsx`:
- Import `useGroups` and `useSetUserGroup`
- For SP role (`role === "SalesPerson"`), render a Group `<select>`:
  ```tsx
  <select value={selectedGroupId ?? ""} onChange={(e) => setSelectedGroupId(e.target.value ? Number(e.target.value) : null)}>
    <option value="">None</option>
    {(groupsQ.data ?? []).filter(g => g.isActive).map(g => <option key={g.id} value={g.id}>{g.name}</option>)}
  </select>
  ```
- On save: PUT user (existing flow) THEN `setUserGroup.mutateAsync(selectedGroupId)`

Update `EditUserModal.test.tsx`:
1. Group dropdown visible only when role=SalesPerson
2. Group dropdown hidden for BomCreator/Accountant/MD/Admin roles
3. Save calls setUserGroup with selected groupId

- [ ] **Step 4: Run tests + tsc**

Run:
```
cd bom-web && npm run test -- UsersPage
cd bom-web && npm run test -- EditUserModal
cd bom-web && npx tsc --noEmit
```
Expected: green.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/components/SalesGroupCell.tsx \
        bom-web/src/features/users/UsersPage.tsx \
        bom-web/src/features/users/UsersPage.test.tsx \
        bom-web/src/features/users/EditUserModal.tsx \
        bom-web/src/features/users/EditUserModal.test.tsx
git commit -m "feat(web): UsersPage Group column + EditUserModal Group dropdown for SP (V23b)"
```

---

## Task 12: Mobile — `useGroups` hook (read-only) + types

**Files:**
- Create: `bom-mobile/src/api/groups.ts`

- [ ] **Step 1: Create useGroups hook**

Create `bom-mobile/src/api/groups.ts` (mirror web shape, use mobile axios path):

```ts
import { useQuery } from "@tanstack/react-query";
import { api } from "./client";

export interface SalesGroup {
  id: number;
  name: string;
  isActive: boolean;
}

export function useGroups() {
  return useQuery({
    queryKey: ["groups", "list"],
    queryFn: async () => (await api.get<SalesGroup[]>("/api/groups")).data,
    staleTime: 5 * 60_000,
  });
}
```

(V2.3-A confirmed mobile uses `./client` import + full `/api/...` path.)

- [ ] **Step 2: tsc**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/api/groups.ts
git commit -m "feat(mobile): add useGroups read-only hook (V23b)"
```

---

## Task 13: Mobile — `OwnedByBadge` component + apply to req + customer cards

**Files:**
- Create: `bom-mobile/src/components/OwnedByBadge.tsx`
- Modify: `bom-mobile/app/(sales)/index.tsx` (or wherever req cards render)
- Modify: `bom-mobile/app/(sales)/customers.tsx` (or wherever customer cards render — find via grep)

- [ ] **Step 1: Build OwnedByBadge**

Create `bom-mobile/src/components/OwnedByBadge.tsx`:

```tsx
import { Text, View } from "react-native";

interface Props {
  ownerName: string;       // e.g., "Sara Accounts"
  prefix?: string;          // e.g., "by" or "owned by"
}

export function OwnedByBadge({ ownerName, prefix = "by" }: Props) {
  return (
    <View>
      <Text style={{ fontSize: 11, color: "#64748b", marginTop: 2 }}>
        {prefix} {ownerName}
      </Text>
    </View>
  );
}
```

- [ ] **Step 2: Add to req cards**

Find the Sales req list card render. After the customer name / status row, add:

```tsx
{req.salesPersonId !== currentUserId && (
  <OwnedByBadge ownerName={req.salesPersonName} prefix="by" />
)}
```

This requires `req.salesPersonName` to be in the response shape. Check the existing `RequisitionListItem` DTO — if it lacks `salesPersonName`, **STOP** and add it via a small backend change (extend the DTO + projection) — do this as part of this same task (commit message can stay the same).

- [ ] **Step 3: Add to customer cards**

Find the customer list. After the customer name row, add:

```tsx
{customer.salesPersonId !== currentUserId && (
  <OwnedByBadge ownerName={customer.salesPersonName} prefix="owned by" />
)}
```

Same rule: check `Customer` DTO — if `salesPersonName` is missing, add it via a small backend change.

- [ ] **Step 4: tsc + build**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add bom-mobile/src/components/OwnedByBadge.tsx \
        bom-mobile/app/\(sales\)/index.tsx \
        bom-mobile/app/\(sales\)/customers.tsx \
        BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs \
        BomPriceApproval.API/Features/Customers/CustomersController.cs
git commit -m "feat(mobile): OwnedByBadge on req + customer cards for group peers (V23b)"
```

(Backend files included only if Step 2/3 added projection fields — omit otherwise.)

---

## Task 14: Web — same `OwnedByBadge` parallel on RequisitionsListPage and CustomersListPage

**Files:**
- Create: `bom-web/src/components/OwnedByBadge.tsx`
- Modify: `bom-web/src/features/requisitions/RequisitionsListPage.tsx` (or the file that renders the SP req list)
- Modify: `bom-web/src/features/customers/CustomersListPage.tsx`

- [ ] **Step 1: Build web OwnedByBadge**

Create `bom-web/src/components/OwnedByBadge.tsx`:

```tsx
interface Props {
  ownerName: string;
  prefix?: string;
}

export function OwnedByBadge({ ownerName, prefix = "by" }: Props) {
  return (
    <span className="text-xs text-slate-500 ml-2">
      {prefix} {ownerName}
    </span>
  );
}
```

- [ ] **Step 2: Apply to req + customer list rows**

For each row in the list, after the primary identifier (RefNo for reqs, Name for customers), conditionally render:

```tsx
{row.salesPersonId !== currentUser.id && (
  <OwnedByBadge ownerName={row.salesPersonName} prefix={isCustomer ? "owned by" : "by"} />
)}
```

- [ ] **Step 3: Tests + tsc**

Run:
```
cd bom-web && npm run test
cd bom-web && npx tsc --noEmit
```
Expected: green.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/components/OwnedByBadge.tsx \
        bom-web/src/features/requisitions/RequisitionsListPage.tsx \
        bom-web/src/features/customers/CustomersListPage.tsx
git commit -m "feat(web): OwnedByBadge on req + customer lists for group peers (V23b)"
```

---

## Task 15: Smoke prep — verify backend + web + mobile all green together

**Files:**
- (no file changes — verification step)

- [ ] **Step 1: Run full backend suite**

Run: `dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --nologo --configuration Release`
Expected: ALL pass (162 from V2.3-A + ~22 new from V2.3-B = ~184 total). Known flaky timing test may need 1 retry.

- [ ] **Step 2: Run web tests + tsc**

Run:
```
cd bom-web && npm run test
cd bom-web && npx tsc --noEmit
```
Expected: green; only pre-existing `baseUrl` deprecation warning.

- [ ] **Step 3: Run mobile tsc**

Run: `cd bom-mobile && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 4: No commit (verification only)**

If any check fails, fix before proceeding to Task 16.

---

## Task 16: Full smoke pass + CLAUDE.md update + final commit

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Walk the spec §12 11-item smoke checklist on a real device**

(Run backend + Expo + login on device; walk all 11 items; note any defects → fix → re-test.)

This step is for the human; subagent execution can skip and document smoke as deferred to user if device tunnel isn't ready.

- [ ] **Step 2: Update CLAUDE.md**

In `CLAUDE.md`, find the "Branch Isolation" section (V2.3-A added a subsection here). Insert AFTER the V2.3-A subsection, BEFORE the next major heading:

```markdown
### V2.3-B Sales Groups (post-2026-04-XX)

SalesPersons can be grouped into flat peer "sales groups". All members of a group share full visibility + edit/create rights on each other's customers and requisitions. Groups are branch-agnostic.

- **`User.GroupId`** (nullable FK to `SalesGroups`): only meaningful for SalesPerson role. Other roles are unaffected.
- **Visibility computation:** `SalesAuthorization.VisibleSalesPersonIds(user, db)` returns either `[user.Id]` (solo SP) or all SP members of the user's group. Used in `RequisitionsController.GetAll`, `CustomersController.GetAll`, and respective `CanAccess` helpers.
- **Group management:** Admin + Accountant roles via `POST/PUT/DELETE /api/groups` (soft-delete with in-use guard) and `PUT /api/users/{id}/group` (SP-only target).
- **Notifications stay routed by original `SalesPersonId`** — group peers do not receive notifs about each other's reqs.
- **Q11 clean cut on remove:** clearing `User.GroupId` immediately revokes group visibility in both directions.
```

Update the "last audited" footer date to today's date.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: document V23b sales groups in CLAUDE.md"
```

---

## Self-Review Checklist (run after writing this plan)

- [x] **Spec coverage:** Every spec section maps to at least one task.
  - §3 locked decisions → Tasks 1-8 implement them
  - §5 data model → Task 1
  - §6 API endpoints → Tasks 3-4
  - §7 permission/visibility → Tasks 5-7
  - §8 UI surfaces → Tasks 10-14
  - §9 edge cases → covered by Tasks 5-7 tests
  - §10 test coverage outline → Tasks 2-8 implement the tests
  - §11 migration notes → Task 1 (no data step)
  - §12 smoke checklist → Task 16
- [x] **Placeholder scan:** A few "(adjust to existing patterns)" hints in Tasks 7 + 13 + 14 — these reference codebase patterns the implementing engineer will see in context. Acceptable per writing-plans skill (legitimate adaptive references, not vague TODOs).
- [x] **Type consistency:** `SalesGroup`, `GroupAdminResponse`, `UserGroupResponse`, `SetUserGroupRequest`, `UseQuery` shapes are used identically across backend / web / mobile tasks. `useGroups` is in `bom-web/src/api/groups.ts` and `bom-mobile/src/api/groups.ts` — no naming clash with V2.3-A's `useBranches` (which lives in `branches.ts` parallel files).

---

## Execution Handoff

Plan complete and saved to [docs/superpowers/plans/2026-04-26-v23b-sales-groups.md](2026-04-26-v23b-sales-groups.md). Two execution options:

**1. Subagent-Driven (recommended)** — Fresh subagent per task with two-stage review between tasks. Same model used successfully for V2.3-A. Faster iteration in same session.

**2. Inline Execution** — Execute tasks in this session using `superpowers:executing-plans` skill, batch execution with checkpoints for review.

V2.3-A used Subagent-Driven and shipped 26 tasks in one session cleanly. Recommend the same for V2.3-B.
