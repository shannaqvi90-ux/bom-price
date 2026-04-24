# Customer Creation by Sales / Accountant — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** [docs/superpowers/specs/2026-04-23-customer-creation-by-sales-accountant-design.md](../specs/2026-04-23-customer-creation-by-sales-accountant-design.md)

**Goal:** Allow Accountants to create/update customers and requisitions, add inline customer creation on web + mobile requisition forms, and give Accountants the ability to change the customer on a requisition during costing with a full audit trail.

**Architecture:** Three layers: (1) Backend permission flips + new `CustomerChangeHistory` entity/table + PATCH `/api/requisitions/{id}/customer` + GET `/api/requisitions/{id}/customer-history`; (2) Web sidebar + `AddCustomerModal` wired into `NewRequisitionPage` + new `ChangeCustomerModal` on `CostingEntryPage` + `CustomerHistoryModal` read-only view; (3) Mobile bottom-sheet `CustomerQuickCreateSheet` on `(sales)/new.tsx`.

**Tech Stack:** ASP.NET Core 8, EF Core 8 (Npgsql), xUnit + FluentAssertions + WebApplicationFactory, React 19 + Vite + TanStack Query + React Hook Form + Zod, React Native (Expo SDK 51) + Moti + NativeWind + expo-haptics.

**Branch:** `feature/customer-creation-inline` off `master` (created AFTER mobile V1 merge).

---

## Prerequisites (before starting)

- [ ] `feature/mobile-md-features` is merged to `master`
- [ ] Clean working tree on `master` (`git status` empty)
- [ ] Backend builds green: `dotnet build --nologo -v q`
- [ ] Web builds green: `cd bom-web && npm run build`
- [ ] Create branch: `git checkout -b feature/customer-creation-inline`

---

## Phase 1 — Backend: Permission opens

**Files modified:**
- Modify: `BomPriceApproval.API/Features/Customers/CustomersController.cs:48` and `:79`
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs:117`, `:225`, `:270`, `:297`
- Test: `BomPriceApproval.Tests/Customers/CustomersCrudTests.cs`
- Test: `BomPriceApproval.Tests/Requisitions/RequisitionWorkflowTests.cs`

### Task 1.1: Add failing test — accountant can create customers

- [ ] **Step 1: Add test method to `CustomersCrudTests.cs`** (inside the class body, after the existing `GetAll_AsSalesPerson_OnlyReturnsOwnCustomers` test)

```csharp
[Fact]
public async Task Create_AsAccountant_Succeeds()
{
    var login = await LoginAsync("sara@test.com", "Test@1234");
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

    var code = $"ACCCUST-{Guid.NewGuid():N}".Substring(0, 20);
    var resp = await _client.PostAsJsonAsync("/api/customers", new
    {
        Code = code, Name = "Accountant Co", Address = "", Email = "", PhoneNumber = ""
    });

    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    var body = await resp.Content.ReadFromJsonAsync<CustomerResponse>();
    body!.SalesPersonId.Should().BeNull();
    body.CreatedByUserId.Should().Be(login.UserId);
}

[Fact]
public async Task Update_AsAccountant_Succeeds()
{
    var adminLogin = await LoginAsync("admin@test.com", "Admin@1234");
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminLogin.AccessToken);

    var code = $"ACCUPD-{Guid.NewGuid():N}".Substring(0, 20);
    var created = await _client.PostAsJsonAsync("/api/customers", new
    {
        Code = code, Name = "Orig", Address = "", Email = "", PhoneNumber = ""
    });
    created.StatusCode.Should().Be(HttpStatusCode.Created);
    var createdBody = await created.Content.ReadFromJsonAsync<CustomerResponse>();

    var acctLogin = await LoginAsync("sara@test.com", "Test@1234");
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctLogin.AccessToken);

    var upd = await _client.PutAsJsonAsync($"/api/customers/{createdBody!.Id}", new
    {
        Name = "Updated by Accountant", Address = "", Email = "", PhoneNumber = ""
    });
    upd.StatusCode.Should().Be(HttpStatusCode.NoContent);
}
```

- [ ] **Step 2: Run the new tests — expect 403 failure**

Run: `dotnet test --filter "FullyQualifiedName~CustomersCrudTests.Create_AsAccountant_Succeeds|FullyQualifiedName~CustomersCrudTests.Update_AsAccountant_Succeeds"`

Expected: FAIL with HTTP 403 (Forbidden) — because current `[Authorize(Roles = "SalesPerson,Admin")]` excludes Accountant.

### Task 1.2: Open customer endpoint permissions

- [ ] **Step 1: Edit `CustomersController.cs:48`** — change `[Authorize(Roles = "SalesPerson,Admin")]` on `Create` to:

```csharp
[Authorize(Roles = "SalesPerson,Admin,Accountant")]
```

- [ ] **Step 2: Edit `CustomersController.cs:79`** — change same attribute on `Update` to:

```csharp
[Authorize(Roles = "SalesPerson,Admin,Accountant")]
```

- [ ] **Step 3: Run the new tests**

Run: `dotnet test --filter "FullyQualifiedName~CustomersCrudTests.Create_AsAccountant_Succeeds|FullyQualifiedName~CustomersCrudTests.Update_AsAccountant_Succeeds"`

Expected: PASS (2 tests).

- [ ] **Step 4: Run full CustomersCrudTests to ensure no regression**

Run: `dotnet test --filter "FullyQualifiedName~CustomersCrudTests"`

Expected: all pre-existing tests still PASS.

### Task 1.3: Add failing test — accountant can create requisition

- [ ] **Step 1: Inspect `RequisitionWorkflowTests.cs` helpers** to mirror style

Open `BomPriceApproval.Tests/Requisitions/RequisitionWorkflowTests.cs` and note the login helper + customer/item seed pattern used by `Create_AsSalesPerson_Succeeds` or similar. Reuse the same helper methods.

- [ ] **Step 2: Add test method at the end of `RequisitionWorkflowTests.cs`**

```csharp
[Fact]
public async Task Create_AsAccountant_UsesJwtBranch_AndSucceeds()
{
    // Arrange — accountant logs in
    var acctLogin = await LoginAsync("sara@test.com", "Test@1234");
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctLogin.AccessToken);

    // Fetch a customer and an active item to use
    var customersResp = await _client.GetAsync("/api/customers");
    customersResp.StatusCode.Should().Be(HttpStatusCode.OK);
    var customers = await customersResp.Content.ReadFromJsonAsync<List<CustomerShort>>();
    customers!.Should().NotBeEmpty();

    var itemsResp = await _client.GetAsync("/api/items");
    itemsResp.StatusCode.Should().Be(HttpStatusCode.OK);
    var items = await itemsResp.Content.ReadFromJsonAsync<List<ItemShort>>();
    items!.Should().NotBeEmpty();

    // Act — POST requisition
    var create = await _client.PostAsJsonAsync("/api/requisitions", new
    {
        CustomerId = customers.First().Id,
        CurrencyCode = "AED",
        Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 100.0m } }
    });

    // Assert
    create.StatusCode.Should().Be(HttpStatusCode.Created);
    var body = await create.Content.ReadFromJsonAsync<CreateResponse>();
    body!.RefNo.Should().StartWith("REQ-");

    // Verify branch isolation — created req's BranchId equals accountant's JWT BranchId
    var detail = await _client.GetFromJsonAsync<RequisitionDetailShort>($"/api/requisitions/{body.Id}");
    detail!.BranchId.Should().Be(acctLogin.BranchId!.Value);
}

// If not already defined in the test file, add these records near the other private records:
private record CustomerShort(int Id, string Name);
private record ItemShort(int Id, string Code, string Description);
private record CreateResponse(int Id, string RefNo);
private record RequisitionDetailShort(int Id, int BranchId);
```

> Note: `RequisitionDetailShort` only needs the fields this test asserts. If the record name or shape conflicts with an existing one in the file, rename to `AcctReqDetail` and reuse similarly.

- [ ] **Step 3: Run the test — expect 403**

Run: `dotnet test --filter "FullyQualifiedName~RequisitionWorkflowTests.Create_AsAccountant_UsesJwtBranch_AndSucceeds"`

Expected: FAIL with HTTP 403.

### Task 1.4: Open requisition endpoint permissions

- [ ] **Step 1: Edit `RequisitionsController.cs:117`** — change `[Authorize(Roles = "SalesPerson")]` on `Create` to:

```csharp
[Authorize(Roles = "SalesPerson,Accountant")]
```

- [ ] **Step 2: Edit `RequisitionsController.cs:225`** — change on `AddItem` to:

```csharp
[Authorize(Roles = "SalesPerson,Accountant")]
```

- [ ] **Step 3: Edit `RequisitionsController.cs:270`** — change on `RemoveItem` to:

```csharp
[Authorize(Roles = "SalesPerson,Accountant")]
```

- [ ] **Step 4: Edit `RequisitionsController.cs:297`** — change on `Resubmit` to:

```csharp
[Authorize(Roles = "SalesPerson,Accountant")]
```

### Task 1.5: Adjust ownership checks so Accountant bypasses them

**Why:** Controller methods like `AddItem` enforce `q.SalesPersonId != CurrentUserId` — an Accountant can't be that salesperson, so without adjustment they'd get 403 even on requisitions they created. We widen the check to "caller owns the req OR caller is Accountant/Admin".

- [ ] **Step 1: Edit `RequisitionsController.cs` — `AddItem`** — replace the existing `if (q.SalesPersonId != CurrentUserId) return Forbid();` with:

```csharp
if (q.SalesPersonId != CurrentUserId && CurrentRole != "Accountant" && CurrentRole != "Admin")
    return Forbid();
```

- [ ] **Step 2: Edit `RequisitionsController.cs` — `RemoveItem`** — same replacement as Step 1.

- [ ] **Step 3: Edit `RequisitionsController.cs` — `Resubmit`** — same replacement as Step 1.

- [ ] **Step 4: Build**

Run: `dotnet build --nologo -v q`

Expected: 0 errors.

- [ ] **Step 5: Run all tests in the file**

Run: `dotnet test --filter "FullyQualifiedName~RequisitionWorkflowTests"`

Expected: ALL PASS (including the new Accountant test).

### Task 1.6: Commit Phase 1

- [ ] **Step 1: Show diff**

Run: `git diff --stat`

- [ ] **Step 2: Show proposed commit message, get user approval**

Proposed message: `feat(api): allow accountant to create/update customers and create requisitions`

Wait for user "haan" before running `git commit`.

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.API/Features/Customers/CustomersController.cs \
        BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs \
        BomPriceApproval.Tests/Customers/CustomersCrudTests.cs \
        BomPriceApproval.Tests/Requisitions/RequisitionWorkflowTests.cs
git commit -m "feat(api): allow accountant to create/update customers and create requisitions"
```

---

## Phase 2 — Backend: CustomerChangeHistory entity + PATCH endpoint

**Files created/modified:**
- Create: `BomPriceApproval.API/Domain/Entities/CustomerChangeHistory.cs`
- Modify: `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs` (add DbSet + OnModelCreating config)
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` (add PATCH endpoint)
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs` (add `ChangeCustomerRequest`)
- Create: `BomPriceApproval.API/Infrastructure/Data/Migrations/<autogen>_AddCustomerChangeHistory.cs` (via EF migrations add)
- Create: `BomPriceApproval.Tests/Requisitions/ChangeCustomerTests.cs`

### Task 2.1: Create the entity class

- [ ] **Step 1: Create file `BomPriceApproval.API/Domain/Entities/CustomerChangeHistory.cs`** with:

```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class CustomerChangeHistory
{
    public int Id { get; set; }
    public int RequisitionId { get; set; }
    public int OldCustomerId { get; set; }
    public int NewCustomerId { get; set; }
    public int ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }

    public QuotationRequest Requisition { get; set; } = null!;
    public Customer OldCustomer { get; set; } = null!;
    public Customer NewCustomer { get; set; } = null!;
    public User ChangedBy { get; set; } = null!;
}
```

### Task 2.2: Register entity in AppDbContext

- [ ] **Step 1: Open `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs`** and locate the `DbSet<>` properties block. Add:

```csharp
public DbSet<CustomerChangeHistory> CustomerChangeHistories => Set<CustomerChangeHistory>();
```

- [ ] **Step 2: In the same file, `OnModelCreating` method**, add the entity configuration. Place it near the other entity configs (search for `modelBuilder.Entity<QuotationRequest>`):

```csharp
modelBuilder.Entity<CustomerChangeHistory>(e =>
{
    e.HasKey(x => x.Id);
    e.Property(x => x.Reason).HasMaxLength(500);
    e.Property(x => x.ChangedAt).HasDefaultValueSql("now() at time zone 'utc'");

    e.HasOne(x => x.Requisition)
        .WithMany()
        .HasForeignKey(x => x.RequisitionId)
        .OnDelete(DeleteBehavior.Cascade);

    e.HasOne(x => x.OldCustomer)
        .WithMany()
        .HasForeignKey(x => x.OldCustomerId)
        .OnDelete(DeleteBehavior.Restrict);

    e.HasOne(x => x.NewCustomer)
        .WithMany()
        .HasForeignKey(x => x.NewCustomerId)
        .OnDelete(DeleteBehavior.Restrict);

    e.HasOne(x => x.ChangedBy)
        .WithMany()
        .HasForeignKey(x => x.ChangedByUserId)
        .OnDelete(DeleteBehavior.Restrict);

    e.HasIndex(x => x.RequisitionId);
    e.HasIndex(x => x.ChangedAt).IsDescending();
});
```

### Task 2.3: Generate EF migration

- [ ] **Step 1: Run migration add**

Run: `dotnet ef migrations add AddCustomerChangeHistory --project BomPriceApproval.API`

Expected output: `Done. To undo this action, use 'ef migrations remove'.` New files appear under `BomPriceApproval.API/Infrastructure/Data/Migrations/`.

- [ ] **Step 2: Inspect the generated `.cs` migration file** — confirm `CustomerChangeHistories` table is created with FKs and indexes as per Task 2.2 config. If the column types look wrong (e.g., `ChangedAt` not `timestamptz`), stop and fix the config.

- [ ] **Step 3: Apply the migration to the dev DB**

Run: `dotnet ef database update --project BomPriceApproval.API`

Expected: `Applying migration '<ts>_AddCustomerChangeHistory'. Done.`

### Task 2.4: Add `ChangeCustomerRequest` DTO

- [ ] **Step 1: Open `BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs`** and append:

```csharp
public record ChangeCustomerRequest(
    [Required] int CustomerId,
    [MaxLength(500)] string? Reason);
```

> Ensure `using System.ComponentModel.DataAnnotations;` is already at the top of the file (it is in current codebase — verify).

### Task 2.5: Add failing tests for PATCH endpoint

- [ ] **Step 1: Create `BomPriceApproval.Tests/Requisitions/ChangeCustomerTests.cs`** with:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

public class ChangeCustomerTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    // Seeds: creates a fresh requisition in BomPending, walks it through BOM, returns req id, customer ids, and branch info.
    // Returns the req id PLUS the first customer id used AND a second customer id to swap to.
    private async Task<(int ReqId, int OriginalCustomerId, int SwapCustomerId)> SeedRequisitionAtCostingPending()
    {
        // 1. Sales creates req with first customer
        var sales = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sales.AccessToken);

        var customers = await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers");
        var items = await _client.GetFromJsonAsync<List<ItemShort>>("/api/items");
        customers!.Count.Should().BeGreaterOrEqualTo(2, "need two customers for swap");

        var original = customers[0];
        var swap = customers[1];

        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = original.Id,
            CurrencyCode = "AED",
            Items = new[] { new { ItemId = items!.First().Id, ExpectedQty = 10m } }
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<CreateResponse>();
        var reqId = created!.Id;

        // 2. BomCreator starts + submits BOM for the single item → req moves to CostingPending
        var bom = await LoginAsync("bom@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bom.AccessToken);

        var reqItemsResp = await _client.GetFromJsonAsync<RequisitionDetailFull>($"/api/requisitions/{reqId}");
        var reqItemId = reqItemsResp!.Items[0].Id;

        var startBom = await _client.PostAsync($"/api/bom/start/{reqItemId}", null);
        startBom.EnsureSuccessStatusCode();

        var saveBom = await _client.PostAsJsonAsync($"/api/bom/{reqItemId}/lines", new
        {
            Lines = new[] { new { RawMaterialId = items.First().Id, KgPerUnit = 1.5m, WastagePct = 2.0m } }
        });
        saveBom.EnsureSuccessStatusCode();

        var submitBom = await _client.PostAsync($"/api/bom/{reqItemId}/submit", null);
        submitBom.EnsureSuccessStatusCode();

        return (reqId, original.Id, swap.Id);
    }

    [Fact]
    public async Task ChangeCustomer_AsAccountant_InCostingPending_Succeeds_AndLogsHistory()
    {
        var (reqId, origId, swapId) = await SeedRequisitionAtCostingPending();

        var acct = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acct.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/customer", new
        {
            CustomerId = swapId,
            Reason = "Accountant correcting customer assignment"
        });

        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify the req now points to the swap customer
        var detail = await _client.GetFromJsonAsync<RequisitionDetailFull>($"/api/requisitions/{reqId}");
        detail!.CustomerId.Should().Be(swapId);

        // Verify history contains one entry
        var history = await _client.GetFromJsonAsync<List<HistoryEntry>>($"/api/requisitions/{reqId}/customer-history");
        history!.Should().HaveCount(1);
        history[0].OldCustomerId.Should().Be(origId);
        history[0].NewCustomerId.Should().Be(swapId);
        history[0].ChangedByUserId.Should().Be(acct.UserId);
        history[0].Reason.Should().Be("Accountant correcting customer assignment");
    }

    [Fact]
    public async Task ChangeCustomer_SameCustomer_Returns400()
    {
        var (reqId, origId, _) = await SeedRequisitionAtCostingPending();

        var acct = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acct.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/customer", new
        {
            CustomerId = origId, Reason = (string?)null
        });

        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangeCustomer_NonExistentCustomer_Returns404()
    {
        var (reqId, _, _) = await SeedRequisitionAtCostingPending();

        var acct = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acct.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/customer", new
        {
            CustomerId = 999_999, Reason = (string?)null
        });

        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ChangeCustomer_NonExistentRequisition_Returns404()
    {
        var acct = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acct.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/999999/customer", new
        {
            CustomerId = 1, Reason = (string?)null
        });

        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ChangeCustomer_AsSales_Returns403()
    {
        var (reqId, _, swapId) = await SeedRequisitionAtCostingPending();

        var sales = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sales.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/customer", new
        {
            CustomerId = swapId, Reason = (string?)null
        });

        patch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChangeCustomer_OutsideCostingStates_Returns400()
    {
        // Sales creates req — status BomPending, which is OUTSIDE allowed states
        var sales = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sales.AccessToken);

        var customers = await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers");
        var items = await _client.GetFromJsonAsync<List<ItemShort>>("/api/items");
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            CurrencyCode = "AED",
            Items = new[] { new { ItemId = items!.First().Id, ExpectedQty = 10m } }
        });
        create.EnsureSuccessStatusCode();
        var reqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        var acct = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acct.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/customer", new
        {
            CustomerId = customers[1].Id, Reason = (string?)null
        });

        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetCustomerHistory_EmptyWhenNoChanges_Returns200()
    {
        var (reqId, _, _) = await SeedRequisitionAtCostingPending();

        var acct = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acct.AccessToken);

        var resp = await _client.GetAsync($"/api/requisitions/{reqId}/customer-history");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await resp.Content.ReadFromJsonAsync<List<HistoryEntry>>();
        list!.Should().BeEmpty();
    }

    // --- Private records -----------------------------------------------------
    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record CustomerShort(int Id, string Name);
    private record ItemShort(int Id, string Code, string Description);
    private record CreateResponse(int Id, string RefNo);
    private record RequisitionItemShort(int Id, int ItemId, string Description, decimal ExpectedQty, int SortOrder);
    private record RequisitionDetailFull(int Id, int CustomerId, List<RequisitionItemShort> Items);
    private record HistoryEntry(int Id, int OldCustomerId, string OldCustomerName, int NewCustomerId, string NewCustomerName, int ChangedByUserId, string ChangedByUserName, DateTime ChangedAt, string? Reason);
}
```

> Note: `PatchAsJsonAsync` is provided by `System.Net.Http.Json` in .NET 8. Confirm no additional using is needed beyond what's already at the top.

- [ ] **Step 2: Run the new test file**

Run: `dotnet test --filter "FullyQualifiedName~ChangeCustomerTests"`

Expected: ALL FAIL (endpoint not implemented yet). Most likely 404s because the route doesn't exist.

### Task 2.6: Implement PATCH `/api/requisitions/{id}/customer`

- [ ] **Step 1: Add `using` for the new entity at the top of `RequisitionsController.cs`** if not already (it's in the same namespace under `Domain.Entities` — the existing `using BomPriceApproval.API.Domain.Entities;` covers it).

- [ ] **Step 2: Append the new action to `RequisitionsController.cs`** (above the final `CanAccess` helper):

```csharp
[HttpPatch("{id}/customer")]
[Authorize(Roles = "Accountant,Admin")]
public async Task<IActionResult> ChangeCustomer(int id, ChangeCustomerRequest req)
{
    var q = await db.QuotationRequests.FirstOrDefaultAsync(r => r.Id == id);
    if (q is null) return NotFound();

    if (q.Status != RequisitionStatus.CostingPending &&
        q.Status != RequisitionStatus.CostingInProgress)
    {
        return Validation
            .Detail("Customer can only be changed during the costing stage.")
            .Field("Status", "Not in a costing state.")
            .Return();
    }

    if (req.CustomerId == q.CustomerId)
    {
        return Validation
            .Detail("New customer is the same as the current customer.")
            .Field("CustomerId", "No change.")
            .Return();
    }

    var newCustomerExists = await db.Customers.AnyAsync(c => c.Id == req.CustomerId);
    if (!newCustomerExists) return NotFound();

    var oldCustomerId = q.CustomerId;

    await using var tx = await db.Database.BeginTransactionAsync();

    q.CustomerId = req.CustomerId;
    q.UpdatedAt = DateTime.UtcNow;

    db.CustomerChangeHistories.Add(new CustomerChangeHistory
    {
        RequisitionId = q.Id,
        OldCustomerId = oldCustomerId,
        NewCustomerId = req.CustomerId,
        ChangedByUserId = CurrentUserId,
        Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim()
    });

    await db.SaveChangesAsync();
    await tx.CommitAsync();

    // Fire-and-forget notification (same try/catch pattern as Create)
    try
    {
        var oldCust = await db.Customers.FindAsync(oldCustomerId);
        var newCust = await db.Customers.FindAsync(req.CustomerId);
        var actor = await db.Users.FindAsync(CurrentUserId);
        var message = $"Customer on {q.RefNo} changed from {oldCust?.Name} to {newCust?.Name} by {actor?.Name}";
        await notificationService.SendAsync(q.SalesPersonId, message, q.Id, "QuotationRequest");
    }
    catch (Exception ex)
    {
        logger.LogError(ex,
            "Notification dispatch failed after successful customer change for {Entity} {Id}",
            "QuotationRequest", q.Id);
    }

    return NoContent();
}
```

- [ ] **Step 3: Build**

Run: `dotnet build --nologo -v q`

Expected: 0 errors.

- [ ] **Step 4: Run the new tests**

Run: `dotnet test --filter "FullyQualifiedName~ChangeCustomerTests"`

Expected: 7 tests; all should PASS except `GetCustomerHistory_EmptyWhenNoChanges_Returns200` (the GET endpoint is added in Phase 3). Note the `ChangeCustomer_...` tests that call `/customer-history` as a side-assertion will still fail — that's OK, Phase 3 closes them.

> **Acceptance for Phase 2:** The 5 tests not touching the history endpoint (`...Succeeds_AndLogsHistory` DB-side only, `...SameCustomer_Returns400`, `...NonExistentCustomer_Returns404`, `...NonExistentRequisition_Returns404`, `...AsSales_Returns403`, `...OutsideCostingStates_Returns400`) pass. The two that call `/customer-history` (`...Succeeds_AndLogsHistory`, `GetCustomerHistory_...`) will pass once Phase 3 lands. To unblock commit, temporarily comment those GET calls inside the test or run only the non-GET asserts:

Run: `dotnet test --filter "FullyQualifiedName~ChangeCustomerTests.ChangeCustomer_SameCustomer_Returns400|...NonExistentCustomer|...NonExistentRequisition|...AsSales|...OutsideCostingStates"`

Expected: 5 PASS.

### Task 2.7: Commit Phase 2

- [ ] **Step 1: Show diff**

Run: `git diff --stat`

- [ ] **Step 2: Show proposed commit message, get user approval**

Proposed message: `feat(api): PATCH /api/requisitions/{id}/customer + CustomerChangeHistory entity + migration`

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.API/Domain/Entities/CustomerChangeHistory.cs \
        BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs \
        BomPriceApproval.API/Infrastructure/Data/Migrations/ \
        BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs \
        BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs \
        BomPriceApproval.Tests/Requisitions/ChangeCustomerTests.cs
git commit -m "feat(api): PATCH /api/requisitions/{id}/customer + CustomerChangeHistory entity + migration"
```

---

## Phase 3 — Backend: GET customer-history endpoint

**Files modified:**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` (add GET history endpoint)
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs` (add `CustomerChangeHistoryResponse`)

### Task 3.1: Add response DTO

- [ ] **Step 1: Append to `RequisitionDtos.cs`**:

```csharp
public record CustomerChangeHistoryResponse(
    int Id,
    int OldCustomerId,
    string OldCustomerName,
    int NewCustomerId,
    string NewCustomerName,
    int ChangedByUserId,
    string ChangedByUserName,
    DateTime ChangedAt,
    string? Reason);
```

### Task 3.2: Implement GET endpoint

- [ ] **Step 1: Append to `RequisitionsController.cs`** (below the PATCH action, above `CanAccess`):

```csharp
[HttpGet("{id}/customer-history")]
public async Task<IActionResult> GetCustomerHistory(int id)
{
    var q = await db.QuotationRequests.FirstOrDefaultAsync(r => r.Id == id);
    if (q is null) return NotFound();
    if (!CanAccess(q)) return Forbid();

    var entries = await db.CustomerChangeHistories
        .Where(h => h.RequisitionId == id)
        .OrderByDescending(h => h.ChangedAt)
        .Select(h => new CustomerChangeHistoryResponse(
            h.Id,
            h.OldCustomerId, h.OldCustomer.Name,
            h.NewCustomerId, h.NewCustomer.Name,
            h.ChangedByUserId, h.ChangedBy.Name,
            h.ChangedAt, h.Reason))
        .ToListAsync();

    return Ok(entries);
}
```

### Task 3.3: Restore the GET-using test calls and run the full suite

- [ ] **Step 1: If you commented out GET calls in Phase 2 tests, uncomment them now.**

- [ ] **Step 2: Run the full change-customer suite**

Run: `dotnet test --filter "FullyQualifiedName~ChangeCustomerTests"`

Expected: all 7 tests PASS.

- [ ] **Step 3: Run the full test suite** to catch regressions in other features

Run: `dotnet test`

Expected: all pre-existing tests still PASS (the only flaky one to tolerate is the Auth timing test per `project_flaky_timing_test.md` — retry once if it fails).

### Task 3.4: Commit Phase 3

- [ ] **Step 1: Show diff** — `git diff --stat`

- [ ] **Step 2: Propose message + get approval**

Proposed message: `feat(api): GET /api/requisitions/{id}/customer-history`

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs \
        BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs \
        BomPriceApproval.Tests/Requisitions/ChangeCustomerTests.cs
git commit -m "feat(api): GET /api/requisitions/{id}/customer-history"
```

---

## Phase 4 — Web: Sidebar + types + API hooks

**Files modified/created:**
- Modify: `bom-web/src/components/layout/Sidebar.tsx` (add Accountant to roles arrays)
- Modify: `bom-web/src/types/api.ts` (add `ChangeCustomerRequest`, `CustomerChangeHistoryEntry`)
- Modify: `bom-web/src/features/requisitions/requisitionsApi.ts` (add `useChangeRequisitionCustomer`, `useCustomerChangeHistory`)

### Task 4.1: Update Sidebar roles

- [ ] **Step 1: Open `bom-web/src/components/layout/Sidebar.tsx`** and locate the nav entries array. Add `"Accountant"` to:
  - `Requisitions` link `roles` array
  - `New Requisition` link `roles` array (if it's a separate entry)
  - `Customers` link `roles` array

Example diff for the Customers entry:

```diff
   {
     to: "/customers",
     label: "Customers",
     icon: Contact,
-    roles: ["Admin", "SalesPerson"],
+    roles: ["Admin", "SalesPerson", "Accountant"],
   },
```

Apply the same pattern to the other two entries.

### Task 4.2: Add types

- [ ] **Step 1: Open `bom-web/src/types/api.ts`** and append:

```typescript
export type ChangeCustomerRequest = {
  customerId: number;
  reason?: string | null;
};

export type CustomerChangeHistoryEntry = {
  id: number;
  oldCustomerId: number;
  oldCustomerName: string;
  newCustomerId: number;
  newCustomerName: string;
  changedByUserId: number;
  changedByUserName: string;
  changedAt: string;
  reason: string | null;
};
```

### Task 4.3: Add API hooks

- [ ] **Step 1: Open `bom-web/src/features/requisitions/requisitionsApi.ts`** and append:

```typescript
import type { ChangeCustomerRequest, CustomerChangeHistoryEntry } from "@/types/api";

export function useChangeRequisitionCustomer(requisitionId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: ChangeCustomerRequest) =>
      api.patch<void>(`/requisitions/${requisitionId}/customer`, body).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["requisitions"] });
      qc.invalidateQueries({ queryKey: ["customer-history", requisitionId] });
    },
  });
}

export function useCustomerChangeHistory(requisitionId: number, enabled = true) {
  return useQuery({
    queryKey: ["customer-history", requisitionId],
    queryFn: () =>
      api
        .get<CustomerChangeHistoryEntry[]>(`/requisitions/${requisitionId}/customer-history`)
        .then((r) => r.data),
    enabled,
  });
}
```

> Verify the existing file's imports already include `useMutation`, `useQuery`, `useQueryClient`, and `api`. If `api` isn't imported, add `import { api } from "@/api/axios";`.

### Task 4.4: Commit Phase 4

- [ ] **Step 1: Build web** — `cd bom-web && npm run build` — expect 0 errors
- [ ] **Step 2: Show diff** — `git diff --stat`
- [ ] **Step 3: Propose commit message + get approval**

Proposed message: `feat(web): add accountant sidebar access + change-customer API hooks + types`

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/components/layout/Sidebar.tsx \
        bom-web/src/types/api.ts \
        bom-web/src/features/requisitions/requisitionsApi.ts
git commit -m "feat(web): add accountant sidebar access + change-customer API hooks + types"
```

---

## Phase 5 — Web: Inline add-customer on NewRequisitionPage

**Files modified:**
- Modify: `bom-web/src/features/requisitions/NewRequisitionPage.tsx`
- Modify: `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx`

### Task 5.1: Add failing test for inline add-customer

- [ ] **Step 1: Open `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx`** and inspect how MSW handlers / QueryClient are set up (use the existing test-file scaffolding).

- [ ] **Step 2: Append test**

```typescript
it("opens AddCustomerModal and auto-selects the newly created customer", async () => {
  const user = userEvent.setup();
  renderWithProviders(<NewRequisitionPage />);

  await user.click(await screen.findByRole("button", { name: /add new customer/i }));
  // modal appears
  expect(await screen.findByRole("dialog", { name: /add customer/i })).toBeInTheDocument();

  // fill code + name
  await user.type(screen.getByLabelText(/code/i), "NEWCUST1");
  await user.type(screen.getByLabelText(/name/i), "New Inline Customer");
  await user.click(screen.getByRole("button", { name: /^save$/i }));

  // modal closes, picker now shows the new customer selected
  await waitFor(() => {
    expect(screen.queryByRole("dialog", { name: /add customer/i })).not.toBeInTheDocument();
  });
  expect(screen.getByText("New Inline Customer")).toBeInTheDocument();
});
```

> Match the existing import style + helper name (`renderWithProviders` may be `render` depending on setup). MSW must have a POST /customers handler that responds with the new customer including an auto id. If missing, add:

```typescript
http.post("*/api/customers", async ({ request }) => {
  const body = await request.json();
  return HttpResponse.json({
    id: 999,
    code: body.code,
    name: body.name,
    address: body.address ?? "",
    email: body.email ?? "",
    phoneNumber: body.phoneNumber ?? "",
    salesPersonId: 1,
    salesPersonName: "Ali",
    createdByUserId: 1,
  }, { status: 201 });
}),
```

- [ ] **Step 3: Run — expect failure** (no "add new customer" button exists yet)

Run: `cd bom-web && npm run test -- NewRequisitionPage`

Expected: FAIL — `Unable to find an accessible element with the role "button" and name /add new customer/i`.

### Task 5.2: Implement inline add-customer on NewRequisitionPage

- [ ] **Step 1: Open `bom-web/src/features/requisitions/NewRequisitionPage.tsx`**. At the top, add imports:

```typescript
import { useState } from "react";
import { AddCustomerModal } from "@/features/customers/AddCustomerModal";
```

- [ ] **Step 2: Add state hook inside the component**

```typescript
const [addCustomerOpen, setAddCustomerOpen] = useState(false);
```

- [ ] **Step 3: Modify the Customer label row to include a trigger button**. Replace:

```tsx
<label htmlFor="customer" className="text-sm font-medium">
  Customer
</label>
```

with:

```tsx
<div className="flex items-center justify-between">
  <label htmlFor="customer" className="text-sm font-medium">
    Customer
  </label>
  <button
    type="button"
    onClick={() => setAddCustomerOpen(true)}
    className="text-sm text-primary hover:underline"
  >
    + Add new customer
  </button>
</div>
```

- [ ] **Step 4: Update `AddCustomerModal`** — it currently doesn't expose the created customer to the parent. Modify signature:

Open `bom-web/src/features/customers/AddCustomerModal.tsx` and change:

```typescript
interface Props {
  open: boolean;
  onClose: () => void;
}
```

to:

```typescript
interface Props {
  open: boolean;
  onClose: () => void;
  onCreated?: (customer: Customer) => void;
}

// add at top:
import type { Customer } from "@/types/api";
```

And update `onSubmit` inside that file:

```typescript
const onSubmit = handleSubmit(async (values) => {
  try {
    const created = await create.mutateAsync(values);
    onCreated?.(created);
    reset();
    onClose();
  } catch {
    // error displayed via create.isError
  }
});
```

- [ ] **Step 5: Mount the modal inside `NewRequisitionPage`** — at the bottom of the component's return (before the closing outer `<div>`):

```tsx
<AddCustomerModal
  open={addCustomerOpen}
  onClose={() => setAddCustomerOpen(false)}
  onCreated={(customer) => {
    setValue("customer", { id: customer.id });
  }}
/>
```

- [ ] **Step 6: Ensure `setValue` is destructured from `useForm`**. Add to the destructure list:

```typescript
const {
  control,
  handleSubmit,
  register,
  setError,
  setValue,              // <-- new
  formState: { errors, isSubmitting },
} = useForm<FormValues>({ ... });
```

> The SearchableSelect reads `field.value` — so setting `{ id: customer.id }` is enough for it to show the new selection after refetch completes. The `useCustomers` invalidation inside `useCreateCustomer` ensures the list includes the new customer by the time the picker renders.

- [ ] **Step 7: Run the test**

Run: `cd bom-web && npm run test -- NewRequisitionPage`

Expected: PASS. If the picker doesn't show the new name immediately because of a query cache race, add a short `await waitFor(...)` to the assertion.

### Task 5.3: Commit Phase 5

- [ ] **Step 1: Show diff** — `git diff --stat`
- [ ] **Step 2: Propose message + approval**

Proposed message: `feat(web): inline "add customer" on NewRequisitionPage`

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/features/requisitions/NewRequisitionPage.tsx \
        bom-web/src/features/requisitions/NewRequisitionPage.test.tsx \
        bom-web/src/features/customers/AddCustomerModal.tsx
git commit -m "feat(web): inline \"add customer\" on NewRequisitionPage"
```

---

## Phase 6 — Web: Change-customer modal on Costing + history badge

**Files created/modified:**
- Create: `bom-web/src/features/requisitions/ChangeCustomerModal.tsx`
- Create: `bom-web/src/features/requisitions/CustomerHistoryModal.tsx`
- Modify: `bom-web/src/features/costing/CostingEntryPage.tsx`
- Modify: `bom-web/src/features/requisitions/RequisitionDetailPage.tsx`
- Modify: `bom-web/src/features/approvals/MdReviewPage.tsx`
- Modify: corresponding `*.test.tsx` files

### Task 6.1: Build `CustomerHistoryModal` (read-only timeline)

- [ ] **Step 1: Create file** `bom-web/src/features/requisitions/CustomerHistoryModal.tsx`:

```tsx
import { Dialog } from "@/components/ui/Dialog";
import { useCustomerChangeHistory } from "./requisitionsApi";

interface Props {
  open: boolean;
  onClose: () => void;
  requisitionId: number;
}

export function CustomerHistoryModal({ open, onClose, requisitionId }: Props) {
  const q = useCustomerChangeHistory(requisitionId, open);

  return (
    <Dialog open={open} onClose={onClose} title="Customer change history">
      {q.isLoading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : q.isError ? (
        <p className="text-sm text-destructive">Failed to load history.</p>
      ) : (q.data ?? []).length === 0 ? (
        <p className="text-sm text-muted-foreground">No changes yet.</p>
      ) : (
        <ol className="space-y-3">
          {q.data!.map((h) => (
            <li key={h.id} className="border-l-2 border-amber-400 pl-3">
              <p className="text-sm">
                <span className="font-medium">{h.oldCustomerName}</span>
                <span className="text-muted-foreground mx-1">→</span>
                <span className="font-medium">{h.newCustomerName}</span>
              </p>
              <p className="text-xs text-muted-foreground">
                by {h.changedByUserName} on {new Date(h.changedAt).toLocaleString()}
              </p>
              {h.reason && <p className="text-xs mt-1 italic">"{h.reason}"</p>}
            </li>
          ))}
        </ol>
      )}
    </Dialog>
  );
}
```

### Task 6.2: Build `ChangeCustomerModal`

- [ ] **Step 1: Create file** `bom-web/src/features/requisitions/ChangeCustomerModal.tsx`:

```tsx
import { useState } from "react";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { SearchableSelect } from "@/components/ui/SearchableSelect";
import { useCustomers } from "@/api/lookups";
import { useChangeRequisitionCustomer } from "./requisitionsApi";
import { AddCustomerModal } from "@/features/customers/AddCustomerModal";
import { notify } from "@/lib/notify";
import type { Customer } from "@/types/api";

interface Props {
  open: boolean;
  onClose: () => void;
  requisitionId: number;
  currentCustomerId: number;
  currentCustomerName: string;
}

export function ChangeCustomerModal({
  open, onClose, requisitionId, currentCustomerId, currentCustomerName,
}: Props) {
  const customersQ = useCustomers();
  const mutation = useChangeRequisitionCustomer(requisitionId);
  const [newCustomer, setNewCustomer] = useState<Customer | null>(null);
  const [reason, setReason] = useState("");
  const [addOpen, setAddOpen] = useState(false);

  const options = (customersQ.data ?? []).filter((c) => c.id !== currentCustomerId);

  async function onConfirm() {
    if (!newCustomer) return;
    try {
      await mutation.mutateAsync({
        customerId: newCustomer.id,
        reason: reason.trim() || null,
      });
      notify.success("Customer changed. Logged in audit history.");
      setNewCustomer(null);
      setReason("");
      onClose();
    } catch (e) {
      notify.fromApiError(e, "Failed to change customer");
    }
  }

  return (
    <Dialog open={open} onClose={onClose} title="Change customer">
      <div className="space-y-4">
        <div>
          <p className="text-xs text-muted-foreground">Current customer</p>
          <p className="text-sm font-medium">{currentCustomerName}</p>
        </div>

        <div className="space-y-2">
          <div className="flex items-center justify-between">
            <label htmlFor="new-customer" className="text-sm font-medium">
              New customer
            </label>
            <button
              type="button"
              onClick={() => setAddOpen(true)}
              className="text-sm text-primary hover:underline"
            >
              + Add new customer
            </button>
          </div>
          <SearchableSelect<Customer>
            id="new-customer"
            options={options}
            value={newCustomer}
            onChange={setNewCustomer}
            getLabel={(c) => c.name}
            getValue={(c) => c.id}
            placeholder="Search customers…"
          />
        </div>

        <div className="space-y-1">
          <label htmlFor="reason" className="text-sm font-medium">
            Reason (optional)
          </label>
          <textarea
            id="reason"
            rows={3}
            maxLength={500}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            className="w-full rounded-md border px-3 py-2 text-sm"
            placeholder="Why is this changing? (visible in audit history)"
          />
        </div>

        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button
            onClick={onConfirm}
            disabled={!newCustomer || mutation.isPending}
          >
            {mutation.isPending ? "Saving…" : "Confirm"}
          </Button>
        </div>
      </div>

      <AddCustomerModal
        open={addOpen}
        onClose={() => setAddOpen(false)}
        onCreated={(c) => setNewCustomer(c)}
      />
    </Dialog>
  );
}
```

### Task 6.3: Wire the button + badges into CostingEntryPage / RequisitionDetailPage / MdReviewPage

- [ ] **Step 1: CostingEntryPage** — add to the header card, above or below existing summary fields:

```tsx
import { useState } from "react";
import { ChangeCustomerModal } from "@/features/requisitions/ChangeCustomerModal";
import { CustomerHistoryModal } from "@/features/requisitions/CustomerHistoryModal";
import { useCustomerChangeHistory } from "@/features/requisitions/requisitionsApi";
import { useAuth } from "@/lib/auth";

// inside component:
const { role } = useAuth();
const [changeOpen, setChangeOpen] = useState(false);
const [historyOpen, setHistoryOpen] = useState(false);
const historyQ = useCustomerChangeHistory(requisition.id);
const canChange = (role === "Accountant" || role === "Admin") &&
                  (requisition.status === "CostingPending" || requisition.status === "CostingInProgress");
const historyCount = historyQ.data?.length ?? 0;

// in the render, next to the customer label:
{canChange && (
  <button
    type="button"
    onClick={() => setChangeOpen(true)}
    className="text-sm text-primary hover:underline ml-2"
  >
    Change customer
  </button>
)}
{historyCount > 0 && (
  <button
    type="button"
    onClick={() => setHistoryOpen(true)}
    className="text-xs text-amber-700 hover:underline ml-2"
  >
    View history ({historyCount})
  </button>
)}

<ChangeCustomerModal
  open={changeOpen}
  onClose={() => setChangeOpen(false)}
  requisitionId={requisition.id}
  currentCustomerId={requisition.customerId}
  currentCustomerName={requisition.customerName}
/>
<CustomerHistoryModal
  open={historyOpen}
  onClose={() => setHistoryOpen(false)}
  requisitionId={requisition.id}
/>
```

> Adjust property names to match actual `requisition` shape in this page (it may be `req` or similar).

- [ ] **Step 2: RequisitionDetailPage** — add the amber history badge near the top-of-page header:

```tsx
import { useState } from "react";
import { CustomerHistoryModal } from "./CustomerHistoryModal";
import { useCustomerChangeHistory } from "./requisitionsApi";

// inside component:
const [historyOpen, setHistoryOpen] = useState(false);
const historyQ = useCustomerChangeHistory(requisition.id);
const historyCount = historyQ.data?.length ?? 0;

// render near the customer field:
{historyCount > 0 && (
  <button
    type="button"
    onClick={() => setHistoryOpen(true)}
    className="ml-2 inline-flex items-center rounded-full bg-amber-100 px-2 py-0.5 text-xs text-amber-800 hover:bg-amber-200"
  >
    Customer changed ({historyCount})
  </button>
)}
<CustomerHistoryModal
  open={historyOpen}
  onClose={() => setHistoryOpen(false)}
  requisitionId={requisition.id}
/>
```

- [ ] **Step 3: MdReviewPage** — same pattern as Step 2 for the MD review page header.

### Task 6.4: Add test coverage

- [ ] **Step 1: Extend `CostingEntryPage.test.tsx`** with:

```typescript
it("shows Change customer button for accountant in CostingPending", async () => {
  // set role=Accountant in auth mock, status=CostingPending in msw handler
  renderWithProviders(<CostingEntryPage />);
  expect(await screen.findByRole("button", { name: /change customer/i })).toBeInTheDocument();
});

it("hides Change customer button for BomCreator", async () => {
  // set role=BomCreator
  renderWithProviders(<CostingEntryPage />);
  await waitForPageLoad();
  expect(screen.queryByRole("button", { name: /change customer/i })).not.toBeInTheDocument();
});
```

> Adjust mock paths per existing file's structure.

- [ ] **Step 2: Extend `RequisitionDetailPage.test.tsx`** + `MdReviewPage.test.tsx`:

```typescript
it("shows customer-changed badge when history has entries", async () => {
  // configure msw handler for /customer-history to return one entry
  renderWithProviders(<RequisitionDetailPage />);
  expect(await screen.findByRole("button", { name: /customer changed/i })).toBeInTheDocument();
});
```

- [ ] **Step 3: Run web tests**

Run: `cd bom-web && npm run test`

Expected: all tests PASS.

### Task 6.5: Commit Phase 6

- [ ] **Step 1: Show diff** — `git diff --stat`
- [ ] **Step 2: Propose message + approval**

Proposed message: `feat(web): change-customer modal on costing + history badge/modal on detail + MD review`

- [ ] **Step 3: Commit** all changed files:

```bash
git add bom-web/src/features/requisitions/ChangeCustomerModal.tsx \
        bom-web/src/features/requisitions/CustomerHistoryModal.tsx \
        bom-web/src/features/costing/CostingEntryPage.tsx \
        bom-web/src/features/requisitions/RequisitionDetailPage.tsx \
        bom-web/src/features/approvals/MdReviewPage.tsx \
        bom-web/src/features/costing/CostingEntryPage.test.tsx \
        bom-web/src/features/requisitions/RequisitionDetailPage.test.tsx \
        bom-web/src/features/approvals/MdReviewPage.test.tsx
git commit -m "feat(web): change-customer modal on costing + history badge/modal on detail + MD review"
```

---

## Phase 7 — Mobile: Inline customer quick-create on (sales)/new

**Files created/modified:**
- Create: `bom-mobile/src/components/CustomerQuickCreateSheet.tsx`
- Create (if missing): `bom-mobile/src/api/customers.ts` (hook for POST /api/customers)
- Modify: `bom-mobile/app/(sales)/new.tsx`
- Modify: `bom-mobile/src/utils/validation.ts` (if not already, add a schema for customer quick-create)

### Task 7.1: Add the mutation hook

- [ ] **Step 1: Check for existing customers api file**

Run: `ls bom-mobile/src/api/customers.ts 2>/dev/null || echo "MISSING"`

- [ ] **Step 2a: If missing, create `bom-mobile/src/api/customers.ts`**:

```typescript
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";

export type CreateCustomerBody = {
  code: string;
  name: string;
  address: string;
  email: string;
  phoneNumber: string;
};

export type CustomerResponse = {
  id: number;
  code: string;
  name: string;
  address: string;
  email: string;
  phoneNumber: string;
  salesPersonId: number | null;
  salesPersonName: string | null;
  createdByUserId: number;
};

export function useCreateCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateCustomerBody) =>
      api.post<CustomerResponse>("/api/customers", body).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["customers"] });
    },
  });
}
```

> Note: verify the correct axios client module — look at `bom-mobile/src/api/lookups.ts` for the import pattern of `api`.

- [ ] **Step 2b: If the file exists**, add `useCreateCustomer` to it if not already present (same body as 2a).

### Task 7.2: Add Zod schema

- [ ] **Step 1: Open `bom-mobile/src/utils/validation.ts`** and append:

```typescript
export const createCustomerSchema = z.object({
  code: z.string().trim().min(1, "Code is required").max(20, "Max 20 chars"),
  name: z.string().trim().min(1, "Name is required").max(200, "Max 200 chars"),
  address: z.string().max(500, "Max 500 chars").optional().default(""),
  email: z.string().email("Invalid email").or(z.literal("")).optional().default(""),
  phoneNumber: z.string().max(50, "Max 50 chars").optional().default(""),
});

export type CreateCustomerInput = z.infer<typeof createCustomerSchema>;
```

### Task 7.3: Create the bottom-sheet component

- [ ] **Step 1: Create `bom-mobile/src/components/CustomerQuickCreateSheet.tsx`**:

```tsx
import { useEffect } from "react";
import { Modal, View, Text, ScrollView, Platform, KeyboardAvoidingView } from "react-native";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Input } from "@/components/Input";
import { Button } from "@/components/Button";
import { ErrorBanner } from "@/components/ErrorBanner";
import { useCreateCustomer, type CustomerResponse } from "@/api/customers";
import { createCustomerSchema, type CreateCustomerInput } from "@/utils/validation";

interface Props {
  open: boolean;
  onClose: () => void;
  onCreated: (customer: CustomerResponse) => void;
}

export function CustomerQuickCreateSheet({ open, onClose, onCreated }: Props) {
  const createMut = useCreateCustomer();

  const {
    control,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<CreateCustomerInput>({
    resolver: zodResolver(createCustomerSchema),
    defaultValues: { code: "", name: "", address: "", email: "", phoneNumber: "" },
  });

  useEffect(() => {
    if (!open) reset();
  }, [open, reset]);

  const onSubmit = handleSubmit(async (values) => {
    try {
      const created = await createMut.mutateAsync(values);
      await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      onCreated(created);
      onClose();
    } catch (e: unknown) {
      const status = (e as { response?: { status?: number } }).response?.status;
      if (status === 409) {
        setError("code", { type: "server", message: "Code already exists" });
      } else {
        const msg =
          (e as { response?: { data?: { message?: string } } }).response?.data?.message ??
          (e instanceof Error ? e.message : "Failed to add customer");
        setError("root" as never, { type: "server", message: msg });
      }
    }
  });

  return (
    <Modal visible={open} transparent animationType="none" onRequestClose={onClose}>
      <View className="flex-1 justify-end bg-black/40">
        <MotiView
          from={{ translateY: 400 }}
          animate={{ translateY: 0 }}
          exit={{ translateY: 400 }}
          transition={{ type: "timing", duration: 220 }}
          className="rounded-t-2xl bg-white"
        >
          <KeyboardAvoidingView behavior={Platform.OS === "ios" ? "padding" : undefined}>
            <ScrollView contentContainerClassName="p-5">
              <Text className="text-lg font-bold text-slate-900 mb-4">Add customer</Text>

              {(errors as { root?: { message?: string } }).root?.message && (
                <ErrorBanner
                  message={(errors as { root?: { message?: string } }).root!.message!}
                />
              )}

              <Controller
                control={control}
                name="code"
                render={({ field }) => (
                  <Input
                    label="Code"
                    value={field.value}
                    onChangeText={field.onChange}
                    error={errors.code?.message}
                  />
                )}
              />
              <Controller
                control={control}
                name="name"
                render={({ field }) => (
                  <Input
                    label="Name"
                    value={field.value}
                    onChangeText={field.onChange}
                    error={errors.name?.message}
                  />
                )}
              />
              <Controller
                control={control}
                name="address"
                render={({ field }) => (
                  <Input
                    label="Address"
                    value={field.value}
                    onChangeText={field.onChange}
                    error={errors.address?.message}
                  />
                )}
              />
              <Controller
                control={control}
                name="email"
                render={({ field }) => (
                  <Input
                    label="Email"
                    keyboardType="email-address"
                    autoCapitalize="none"
                    value={field.value}
                    onChangeText={field.onChange}
                    error={errors.email?.message}
                  />
                )}
              />
              <Controller
                control={control}
                name="phoneNumber"
                render={({ field }) => (
                  <Input
                    label="Phone"
                    keyboardType="phone-pad"
                    value={field.value}
                    onChangeText={field.onChange}
                    error={errors.phoneNumber?.message}
                  />
                )}
              />

              <View className="flex-row gap-3 mt-4">
                <View className="flex-1">
                  <Button title="Cancel" variant="ghost" onPress={onClose} />
                </View>
                <View className="flex-1">
                  <Button
                    title="Save"
                    onPress={onSubmit}
                    loading={isSubmitting || createMut.isPending}
                  />
                </View>
              </View>
            </ScrollView>
          </KeyboardAvoidingView>
        </MotiView>
      </View>
    </Modal>
  );
}
```

> Notes:
> - If `@/components/Button` doesn't accept `variant="ghost"`, pass a different prop or use a plain `Pressable`. Verify the existing Button API.
> - If `@/components/ErrorBanner` doesn't accept no `onRetry` prop, drop the prop.

### Task 7.4: Wire it into (sales)/new.tsx

- [ ] **Step 1: Open `bom-mobile/app/(sales)/new.tsx`** and add imports:

```typescript
import { useState } from "react";
import { CustomerQuickCreateSheet } from "@/components/CustomerQuickCreateSheet";
import { useQueryClient } from "@tanstack/react-query";
```

- [ ] **Step 2: Add state + handler inside component**

```typescript
const [addSheetOpen, setAddSheetOpen] = useState(false);
const qc = useQueryClient();
```

- [ ] **Step 3: Modify the customer Controller block** — after the `SearchablePicker`, add the trigger + sheet mount:

Replace the existing Customer Controller block:

```tsx
<Controller
  control={control}
  name="customerId"
  render={({ field }) => (
    <SearchablePicker
      label="Customer"
      placeholder="Select customer..."
      value={field.value || null}
      onChange={field.onChange}
      loading={customersQ.isPending}
      options={
        (customersQ.data ?? []).map((c) => ({
          id: c.id,
          label: c.name,
          sublabel: c.code,
        }))
      }
      error={errors.customerId?.message}
    />
  )}
/>
```

with:

```tsx
<Controller
  control={control}
  name="customerId"
  render={({ field }) => (
    <View>
      <SearchablePicker
        label="Customer"
        placeholder="Select customer..."
        value={field.value || null}
        onChange={field.onChange}
        loading={customersQ.isPending}
        options={
          (customersQ.data ?? []).map((c) => ({
            id: c.id,
            label: c.name,
            sublabel: c.code,
          }))
        }
        error={errors.customerId?.message}
      />
      <Text
        onPress={() => setAddSheetOpen(true)}
        className="text-brand-600 font-semibold self-start mb-3"
      >
        + New customer
      </Text>
      <CustomerQuickCreateSheet
        open={addSheetOpen}
        onClose={() => setAddSheetOpen(false)}
        onCreated={(c) => {
          qc.invalidateQueries({ queryKey: ["customers"] });
          field.onChange(c.id);
        }}
      />
    </View>
  )}
/>
```

> `brand-600` retained to match existing salescreens per memory `project_mobile_plan1_status.md`. When sales screens later get redesigned, switch to the new design-token class.

### Task 7.5: Run TypeScript + manual smoke

- [ ] **Step 1: TypeScript check**

Run: `cd bom-mobile && npx tsc --noEmit`

Expected: 0 errors. Fix any path/import issues.

- [ ] **Step 2: Jest test — validation schema unit**

Add a quick test in `bom-mobile/__tests__/validation.test.ts` (if existing test dir differs, mirror the existing pattern):

```typescript
import { createCustomerSchema } from "@/utils/validation";

describe("createCustomerSchema", () => {
  it("requires code and name", () => {
    const r = createCustomerSchema.safeParse({});
    expect(r.success).toBe(false);
  });

  it("accepts minimal valid payload", () => {
    const r = createCustomerSchema.safeParse({ code: "C1", name: "N1" });
    expect(r.success).toBe(true);
  });

  it("rejects code > 20 chars", () => {
    const r = createCustomerSchema.safeParse({ code: "X".repeat(21), name: "N" });
    expect(r.success).toBe(false);
  });
});
```

Run: `cd bom-mobile && npm run test`

Expected: all tests PASS.

- [ ] **Step 3: Manual smoke on device** — start Metro + backend, open `(sales)/new` in Expo Go:
  - Tap `+ New customer`
  - Bottom sheet slides up
  - Fill code + name, tap Save
  - Sheet closes, success haptic felt
  - Customer picker now shows the new customer selected

### Task 7.6: Commit Phase 7

- [ ] **Step 1: Show diff** — `git diff --stat`
- [ ] **Step 2: Propose message + approval**

Proposed message: `feat(mobile): inline "+ New customer" bottom-sheet on sales/new`

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/components/CustomerQuickCreateSheet.tsx \
        bom-mobile/src/api/customers.ts \
        bom-mobile/src/utils/validation.ts \
        bom-mobile/app/\(sales\)/new.tsx \
        bom-mobile/__tests__/validation.test.ts
git commit -m "feat(mobile): inline \"+ New customer\" bottom-sheet on sales/new"
```

---

## Post-phase — Final verification

### Task F.1: Full backend test suite

- [ ] Run: `dotnet test`

Expected: all PASS (tolerate the flaky Auth timing test per `project_flaky_timing_test.md` — retry once).

### Task F.2: Full web test suite

- [ ] Run: `cd bom-web && npm run test`

Expected: all PASS.

### Task F.3: Mobile TS + Jest

- [ ] Run: `cd bom-mobile && npx tsc --noEmit && npm run test`

Expected: 0 TS errors, all Jest PASS.

### Task F.4: Manual end-to-end on web

- [ ] Backend running on `http://localhost:7300`, web on `http://localhost:5300`
- [ ] Log in as `ali@test.com` (Sales) → go to New Requisition → click `+ Add new customer` → add one → verify it's auto-selected → submit requisition successfully
- [ ] Log in as `sara@test.com` (Accountant) → verify Customers + Requisitions + New Requisition links appear in sidebar
- [ ] Create a new requisition as accountant → walk it to CostingPending (start + submit BOM as `bom@test.com`) → on Costing page, click `Change customer` → pick another customer + enter a reason → verify toast + customer updated
- [ ] View Requisition Detail page → verify `Customer changed (1)` badge appears → click → history modal shows the old → new entry
- [ ] Log in as `md@test.com` and load the same requisition's MD review → verify the same badge appears

### Task F.5: Manual smoke on Android Expo Go

- [ ] Sales login → New Requisition → `+ New customer` bottom sheet → add → auto-select → submit → success

### Task F.6: Push (user command only — DO NOT run autonomously)

- [ ] Per `CLAUDE.md`, never push autonomously. Wait for user's explicit "push karo" before `git push -u origin feature/customer-creation-inline`.

---

## Self-review checklist (plan author)

Completed before handing this plan over:

- ✅ **Spec coverage:**
  - Spec §5.1 permissions → Phase 1
  - Spec §5.2 branch handling (simplified — Accountant uses JWT branch) → Phase 1
  - Spec §5.3 PATCH endpoint → Phase 2
  - Spec §5.4 GET history endpoint → Phase 3
  - Spec §5.5 entity + migration → Phase 2
  - Spec §6.1 sidebar → Phase 4
  - Spec §6.2 NewRequisitionPage inline create → Phase 5
  - Spec §6.3 Costing change-customer + history → Phase 6
  - Spec §6.4 detail/MD history badge → Phase 6
  - Spec §7.1 mobile bottom-sheet → Phase 7
  - Spec §9 testing → covered across every phase
- ✅ **No placeholders:** all code blocks complete; no "TBD"/"implement later"
- ✅ **Type consistency:** `ChangeCustomerRequest`, `CustomerChangeHistoryEntry`, `CustomerQuickCreateSheet`, `useChangeRequisitionCustomer`, `useCustomerChangeHistory` all referenced the same way across tasks
- ✅ **Execution order:** backend before frontend; tests before implementation within each phase; commits after each phase is independently green
- ✅ **Reversibility:** each phase commits as a standalone logical unit — revertible individually if needed
