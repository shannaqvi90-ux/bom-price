# Multi-Item Validation Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close validation gaps across the requisition flow (Create → Add Item → BOM Submit → Costing Submit → MD Approve) using three defense-in-depth layers: Postgres CHECK/UNIQUE constraints, controller guards, and frontend refinements.

**Architecture:** DB migration adds constraints as the bedrock. Controllers inline-validate in the existing style (no FluentValidation). Frontend already implements ~90% of the validations the spec calls for; remaining work is a duplicate-refinement on `NewRequisitionPage`, a small shared `extractApiError` helper, and a negative-margin badge on `MdReviewPage`. Fixes one latent bug in `ApprovalsController.Approve` where mismatched input items are silently skipped.

**Tech Stack:** ASP.NET Core 8, EF Core 8 (Npgsql), xUnit + WebApplicationFactory + Testcontainers, React 19 + TanStack Query, react-hook-form + zod, Vitest + RTL.

**Spec:** `docs/superpowers/specs/2026-04-16-multi-item-validation-hardening-design.md`

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs` | Add UNIQUE index + 3 CHECK constraints in `OnModelCreating` |
| Create | `BomPriceApproval.API/Infrastructure/Data/Migrations/<ts>_AddRequisitionValidationConstraints.cs` | Generated migration |
| Modify | `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` | Guards in `Create` + `AddItem` |
| Modify | `BomPriceApproval.API/Features/Bom/BomController.cs` | Guards in `SaveLines` |
| Modify | `BomPriceApproval.API/Features/Costing/CostingController.cs` | Guards in `Submit` + remove silent `continue` |
| Modify | `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs` | Guards in `Approve` + remove silent `continue` |
| Create | `BomPriceApproval.Tests/Requisitions/ValidationTests.cs` | 6 new integration tests |
| Create | `BomPriceApproval.Tests/Approvals/ApprovalValidationTests.cs` | 4 new integration tests |
| Modify | `BomPriceApproval.Tests/Bom/BomSaveLinesTests.cs` | 2 new tests |
| Modify | `BomPriceApproval.Tests/Costing/CostingTests.cs` | 3 new tests |
| Create | `bom-web/src/lib/apiError.ts` | `extractApiError(err, fallback?): string` |
| Create | `bom-web/src/lib/apiError.test.ts` | Unit tests for the helper |
| Modify | `bom-web/src/features/requisitions/NewRequisitionPage.tsx` | Zod dedupe refinement + picker filter |
| Modify | `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx` | Extend tests |
| Modify | `bom-web/src/features/bom/BomEntryPage.tsx` | Use `extractApiError` on submit error |
| Modify | `bom-web/src/features/costing/CostingEntryPage.tsx` | Use `extractApiError` on submit error |
| Modify | `bom-web/src/features/approvals/MdReviewPage.tsx` | Use `extractApiError` + negative-margin badge |
| Modify | `bom-web/src/features/approvals/MdReviewPage.test.tsx` | Badge visibility tests |

---

## Task 1: DB constraints + migration + first test

**Files:**
- Modify: `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs`
- Create: `BomPriceApproval.API/Infrastructure/Data/Migrations/<timestamp>_AddRequisitionValidationConstraints.cs` (generated)
- Create: `BomPriceApproval.Tests/Requisitions/ValidationTests.cs`

- [ ] **Step 1: Add constraints in `AppDbContext.OnModelCreating`**

In `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs`, add the following block immediately **after** the existing `mb.Entity<RequisitionItem>().Property(ri => ri.ExpectedQty).HasPrecision(18, 4);` line (around line 114). Do not remove any existing lines.

```csharp
        // ─── Validation constraints ──────────────────────────────────────────
        mb.Entity<RequisitionItem>()
            .HasIndex(ri => new { ri.QuotationRequestId, ri.ItemId })
            .IsUnique();

        mb.Entity<RequisitionItem>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_requisition_items_expected_qty_positive",
                "\"ExpectedQty\" > 0"));

        mb.Entity<BomLine>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_bom_lines_qty_per_kg_positive",
                "\"QtyPerKg\" > 0"));

        mb.Entity<ApprovalItem>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_approval_items_sales_price_positive",
                "\"SalesPricePerKgAed\" > 0"));
```

- [ ] **Step 2: Generate the EF migration**

```bash
dotnet ef migrations add AddRequisitionValidationConstraints --project BomPriceApproval.API
```

Expected: a new file under `BomPriceApproval.API/Infrastructure/Data/Migrations/` with a timestamp prefix. Open it and verify it contains `migrationBuilder.CreateIndex(..., unique: true)` and three `AddCheckConstraint(...)` calls.

- [ ] **Step 3: Verify build**

```bash
dotnet build
```

Expected: build succeeds.

- [ ] **Step 4: Write the DB-level unique-violation test**

Create `BomPriceApproval.Tests/Requisitions/ValidationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

public class ValidationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private async Task<int> CreateActiveFinishedGoodAsync(string adminToken)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var code = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var resp = await _client.PostAsJsonAsync("/api/items",
            new { Code = code, Description = "Test FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var item = await resp.Content.ReadFromJsonAsync<ItemDto>();
        return item!.Id;
    }

    private async Task<int> GetCustomerIdAsync()
    {
        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        return customers!.First().Id;
    }

    [Fact]
    public async Task Create_DuplicateItemIds_Returns400()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        var itemId = await CreateActiveFinishedGoodAsync(admin);

        var sp = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[]
            {
                new { ItemId = itemId, ExpectedQty = 1m },
                new { ItemId = itemId, ExpectedQty = 2m },
            },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.Should().Contain("Duplicate");
    }
}

public record LoginResponse(string AccessToken, string RefreshToken);
public record ItemDto(int Id, string Code, string Description, string Type);
public record CustomerDto(int Id, string Name);
public record ErrorResponse(string Message);
public record CreatedRequisition(int Id, string RefNo);
public record RequisitionItemDetailDto(int Id, int ItemId);
public record RequisitionDetailDto(int Id, string RefNo, string Status, List<RequisitionItemDetailDto> Items);
public record ProcessDto(int Id, string Name);
```

- [ ] **Step 5: Run — expect FAIL**

```bash
dotnet test --filter "FullyQualifiedName~ValidationTests.Create_DuplicateItemIds_Returns400"
```

Expected: FAIL (either 201 Created if no duplicate check exists, or 500 from the DB UNIQUE after migration — either way, assertion on 400 fails). This is correct; Task 2 adds the controller check that produces the proper 400.

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs \
        BomPriceApproval.API/Infrastructure/Data/Migrations/ \
        BomPriceApproval.Tests/Requisitions/ValidationTests.cs
git commit -m "feat(db): add requisition validation constraints (unique items, qty>0, price>0)"
```

---

## Task 2: `RequisitionsController` guards — Create + AddItem

**Files:**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs`
- Modify: `BomPriceApproval.Tests/Requisitions/ValidationTests.cs`

- [ ] **Step 1: Append failing tests**

Append to the `ValidationTests` class body in `BomPriceApproval.Tests/Requisitions/ValidationTests.cs`:

```csharp
    [Fact]
    public async Task Create_ZeroQty_Returns400()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        var itemId = await CreateActiveFinishedGoodAsync(admin);

        var sp = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemId, ExpectedQty = 0m } },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.Should().Contain("ExpectedQty");
    }

    [Fact]
    public async Task Create_NegativeQty_Returns400()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        var itemId = await CreateActiveFinishedGoodAsync(admin);

        var sp = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemId, ExpectedQty = -1m } },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_NonExistentItem_Returns400()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = 999999, ExpectedQty = 1m } },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.ToLower().Should().Contain("unknown");
    }

    [Fact]
    public async Task Create_InactiveItem_Returns400()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        var itemId = await CreateActiveFinishedGoodAsync(admin);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        var put = await _client.PutAsJsonAsync($"/api/items/{itemId}",
            new { Code = $"DEAC-{Guid.NewGuid():N}".Substring(0, 10), Description = "Deactivated",
                  Type = "FinishedGood", IsActive = false, LastPurchasePrice = (decimal?)null });
        put.IsSuccessStatusCode.Should().BeTrue();

        var sp = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemId, ExpectedQty = 1m } },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddItem_DuplicateItem_Returns400()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        var itemA = await CreateActiveFinishedGoodAsync(admin);

        var sp = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var createResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemA, ExpectedQty = 1m } },
            CurrencyCode = "AED"
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<CreatedRequisition>();

        var addResp = await _client.PostAsJsonAsync(
            $"/api/requisitions/{created!.Id}/items",
            new { ItemId = itemA, ExpectedQty = 2m });

        addResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddItem_ZeroQty_Returns400()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        var itemA = await CreateActiveFinishedGoodAsync(admin);
        var itemB = await CreateActiveFinishedGoodAsync(admin);

        var sp = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var createResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemA, ExpectedQty = 1m } },
            CurrencyCode = "AED"
        });
        var created = await createResp.Content.ReadFromJsonAsync<CreatedRequisition>();

        var addResp = await _client.PostAsJsonAsync(
            $"/api/requisitions/{created!.Id}/items",
            new { ItemId = itemB, ExpectedQty = 0m });

        addResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
```

- [ ] **Step 2: Run — expect FAILs**

```bash
dotnet test --filter "FullyQualifiedName~ValidationTests"
```

Expected: all 6 `ValidationTests` fail.

- [ ] **Step 3: Implement controller guards**

In `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs`, modify the `Create` method. Find the existing block:

```csharp
        if (req.Items.Count == 0)
            return BadRequest(new { message = "At least one item is required." });
```

And replace it with:

```csharp
        if (req.Items.Count == 0)
            return BadRequest(new { message = "At least one item is required." });

        if (req.Items.Any(i => i.ExpectedQty <= 0))
            return BadRequest(new { message = "ExpectedQty must be greater than 0." });

        var distinctItemIds = req.Items.Select(i => i.ItemId).Distinct().ToList();
        if (distinctItemIds.Count != req.Items.Count)
            return BadRequest(new { message = "Duplicate items in requisition are not allowed." });

        var activeItemIds = await db.Items
            .Where(i => distinctItemIds.Contains(i.Id) && i.IsActive)
            .Select(i => i.Id)
            .ToListAsync();
        var missingItems = distinctItemIds.Except(activeItemIds).ToList();
        if (missingItems.Count > 0)
            return BadRequest(new { message = $"Unknown or inactive items: {string.Join(", ", missingItems)}" });
```

In the same file, modify the `AddItem` method. Find:

```csharp
        if (q.Status != RequisitionStatus.BomPending)
            return BadRequest(new { message = "Items can only be added when status is BomPending" });
```

And replace with:

```csharp
        if (q.Status != RequisitionStatus.BomPending)
            return BadRequest(new { message = "Items can only be added when status is BomPending" });

        if (req.ExpectedQty <= 0)
            return BadRequest(new { message = "ExpectedQty must be greater than 0." });

        if (q.Items.Any(i => i.ItemId == req.ItemId))
            return BadRequest(new { message = "Item already added to this requisition." });

        var itemIsValid = await db.Items.AnyAsync(i => i.Id == req.ItemId && i.IsActive);
        if (!itemIsValid)
            return BadRequest(new { message = $"Unknown or inactive item: {req.ItemId}" });
```

- [ ] **Step 4: Run — expect PASS**

```bash
dotnet test --filter "FullyQualifiedName~ValidationTests"
```

Expected: all 6 tests pass.

- [ ] **Step 5: Run the full backend suite**

```bash
dotnet test
```

Expected: all tests pass. If a pre-existing test fails, it was likely relying on an item being re-addable or zero-qty being accepted. Fix the test data, not the guard.

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs \
        BomPriceApproval.Tests/Requisitions/ValidationTests.cs
git commit -m "feat(api): validate qty, duplicates, and item existence in Create/AddItem"
```

---

## Task 3: `BomController.SaveLines` guards

**Files:**
- Modify: `BomPriceApproval.API/Features/Bom/BomController.cs`
- Modify: `BomPriceApproval.Tests/Bom/BomSaveLinesTests.cs`

- [ ] **Step 1: Append failing tests**

Append to the `BomSaveLinesTests` class in `BomPriceApproval.Tests/Bom/BomSaveLinesTests.cs`. Both tests use the exact same bootstrap as the existing `SaveLines_ReplacesLinesWithoutChangingStatus` test in that file (copy steps 1-3 from that test body up to and including the `await _client.PostAsync($"/api/bom/{requisitionId}/items/{requisitionItemId}/start", null);` line). The only difference is the payload in step 4. Example for the zero-qty test:

```csharp
    [Fact]
    public async Task SaveLines_ZeroQty_Returns400()
    {
        // Copy the bootstrap from SaveLines_ReplacesLinesWithoutChangingStatus verbatim,
        // ending with: await _client.PostAsync($"/api/bom/{requisitionId}/items/{requisitionItemId}/start", null);

        var saveResp = await _client.PutAsJsonAsync(
            $"/api/bom/{requisitionId}/items/{requisitionItemId}/lines",
            new
            {
                Lines = new[]
                {
                    new { ProcessId = process!.Id, RawMaterialItemId = rawMaterial!.Id, QtyPerKg = 0m, WastagePct = 2.0m }
                }
            });

        saveResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SaveLines_NegativeWastage_Returns400()
    {
        // Same bootstrap as above.

        var saveResp = await _client.PutAsJsonAsync(
            $"/api/bom/{requisitionId}/items/{requisitionItemId}/lines",
            new
            {
                Lines = new[]
                {
                    new { ProcessId = process!.Id, RawMaterialItemId = rawMaterial!.Id, QtyPerKg = 1m, WastagePct = -5m }
                }
            });

        saveResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
```

(Optionally extract the bootstrap into a private helper `SetupBomItemAsync()` that returns `(requisitionId, requisitionItemId, processId, rawMaterialId)` — ~40 lines. Do this only if you're comfortable; otherwise inline.)

- [ ] **Step 2: Run — expect FAIL**

```bash
dotnet test --filter "FullyQualifiedName~BomSaveLinesTests.SaveLines_ZeroQty_Returns400|FullyQualifiedName~BomSaveLinesTests.SaveLines_NegativeWastage_Returns400"
```

Expected: both fail. `SaveLines_ZeroQty_Returns400` likely returns 500 from the DB CHECK; still fails the `.Should().Be(400)` assertion.

- [ ] **Step 3: Implement guards**

In `BomPriceApproval.API/Features/Bom/BomController.cs`, modify the `SaveLines` method. After the existing `if (bom.CreatedByUserId != CurrentUserId) return Forbid();` line (around line 108), and **before** `db.BomLines.RemoveRange(bom.Lines);`, insert:

```csharp
        if (request.Lines.Any(l => l.QtyPerKg <= 0))
            return BadRequest(new { message = "QtyPerKg must be greater than 0." });

        if (request.Lines.Any(l => l.WastagePct < 0))
            return BadRequest(new { message = "WastagePct cannot be negative." });

        var processIds = request.Lines.Select(l => l.ProcessId).Distinct().ToList();
        var validProcessCount = await db.Processes.CountAsync(p => processIds.Contains(p.Id));
        if (validProcessCount != processIds.Count)
            return BadRequest(new { message = "One or more ProcessIds are invalid." });

        var rawMatIds = request.Lines.Select(l => l.RawMaterialItemId).Distinct().ToList();
        var validRawMatCount = await db.Items.CountAsync(i => rawMatIds.Contains(i.Id) && i.IsActive);
        if (validRawMatCount != rawMatIds.Count)
            return BadRequest(new { message = "One or more RawMaterialItemIds are invalid or inactive." });
```

- [ ] **Step 4: Run — expect PASS**

```bash
dotnet test --filter "FullyQualifiedName~BomSaveLinesTests"
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Bom/BomController.cs \
        BomPriceApproval.Tests/Bom/BomSaveLinesTests.cs
git commit -m "feat(api): validate qty, wastage, and lookup ids in BomController.SaveLines"
```

---

## Task 4: `CostingController.Submit` guards + fix silent skip

**Files:**
- Modify: `BomPriceApproval.API/Features/Costing/CostingController.cs`
- Modify: `BomPriceApproval.Tests/Costing/CostingTests.cs`

- [ ] **Step 1: Read the existing `CostingTests.cs`**

Read `BomPriceApproval.Tests/Costing/CostingTests.cs` to understand the helpers already available. The file has at least one test that bootstraps through to `CostingInProgress`; copy that bootstrap into a private helper `BootstrapToCostingAsync(int bomLineCount)` that returns `(int RequisitionId, int RequisitionItemId, int[] BomLineIds)`. Keep the helper ≤ 80 lines. Follow exactly the login/setup patterns in `BomSaveLinesTests.SaveLines_ReplacesLinesWithoutChangingStatus` for the bootstrap through to BOM submit, then continue with `POST /api/costing/{requisitionId}/items/{requisitionItemId}/start` to enter `CostingInProgress`.

- [ ] **Step 2: Append failing tests**

Append to the costing test class:

```csharp
    [Fact]
    public async Task Submit_NegativeCost_Returns400()
    {
        var (reqId, itemId, bomLineIds) = await BootstrapToCostingAsync(bomLineCount: 1);

        var accToken = await LoginAsync("acc@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accToken);

        var resp = await _client.PostAsJsonAsync(
            $"/api/costing/{reqId}/items/{itemId}/submit",
            new
            {
                RawMaterialCosts = new[] { new { BomLineId = bomLineIds[0], CostPerKg = -5m, CurrencyCode = "AED" } },
                LandedCostType = "Percentage",
                LandedCostValue = 5m,
                FohAmount = 1m
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Submit_UnknownBomLineId_Returns400()
    {
        var (reqId, itemId, _) = await BootstrapToCostingAsync(bomLineCount: 1);

        var accToken = await LoginAsync("acc@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accToken);

        var resp = await _client.PostAsJsonAsync(
            $"/api/costing/{reqId}/items/{itemId}/submit",
            new
            {
                RawMaterialCosts = new[] { new { BomLineId = 999999, CostPerKg = 5m, CurrencyCode = "AED" } },
                LandedCostType = "Percentage",
                LandedCostValue = 5m,
                FohAmount = 1m
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Submit_MissingLineCost_Returns400()
    {
        var (reqId, itemId, bomLineIds) = await BootstrapToCostingAsync(bomLineCount: 2);

        var accToken = await LoginAsync("acc@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accToken);

        var resp = await _client.PostAsJsonAsync(
            $"/api/costing/{reqId}/items/{itemId}/submit",
            new
            {
                RawMaterialCosts = new[] { new { BomLineId = bomLineIds[0], CostPerKg = 5m, CurrencyCode = "AED" } },
                LandedCostType = "Percentage",
                LandedCostValue = 5m,
                FohAmount = 1m
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<BomPriceApproval.Tests.Requisitions.ErrorResponse>();
        body!.Message.Should().Contain("Missing cost");
    }
```

- [ ] **Step 3: Run — expect FAIL**

```bash
dotnet test --filter "FullyQualifiedName~CostingTests.Submit_"
```

Expected: all 3 new tests fail.

- [ ] **Step 4: Implement guards**

In `BomPriceApproval.API/Features/Costing/CostingController.cs`, modify the `Submit` method. After the existing `if (bom is null) return BadRequest(new { message = "No BOM found for this item" });` line (around line 151), and **before** `var quoteCurrency = ...`, insert:

```csharp
        if (request.RawMaterialCosts.Any(rc => rc.CostPerKg < 0))
            return BadRequest(new { message = "CostPerKg cannot be negative." });

        var submittedBomLineIds = request.RawMaterialCosts.Select(rc => rc.BomLineId).Distinct().ToList();
        var bomLineIds = bom.Lines.Select(l => l.Id).ToList();
        var unknownBomLines = submittedBomLineIds.Except(bomLineIds).ToList();
        if (unknownBomLines.Count > 0)
            return BadRequest(new { message = $"Unknown BOM line(s): {string.Join(", ", unknownBomLines)}" });

        var missingBomLines = bomLineIds.Except(submittedBomLineIds).ToList();
        if (missingBomLines.Count > 0)
            return BadRequest(new { message = $"Missing cost for BOM line(s): {string.Join(", ", missingBomLines)}" });
```

In the same `Submit` method, find the existing inside-foreach line:

```csharp
            if (line is null) continue;
```

Delete this line. (After the guards above, every `rc.BomLineId` matches a real line, so the silent skip is no longer reachable — but leaving it in would hide future regressions. Removing is safer and clearer.)

- [ ] **Step 5: Run — expect PASS**

```bash
dotnet test --filter "FullyQualifiedName~CostingTests"
```

Expected: all costing tests pass.

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Features/Costing/CostingController.cs \
        BomPriceApproval.Tests/Costing/CostingTests.cs
git commit -m "feat(api): validate costs and bom-line coverage in Costing.Submit; remove silent skip"
```

---

## Task 5: `ApprovalsController.Approve` guards + fix latent bug

**Files:**
- Modify: `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs`
- Create: `BomPriceApproval.Tests/Approvals/ApprovalValidationTests.cs`

- [ ] **Step 1: Read the existing workflow test**

Read `BomPriceApproval.Tests/Requisitions/RequisitionWorkflowTests.cs` first. It already bootstraps the entire happy path from Create → BOM Submit → Costing Submit → MdReview. Use its sequence as the template for a new helper `BootstrapToMdReviewAsync(int itemCount)` that returns `(int ReqId, int[] RequisitionItemIds)`. Keep the helper ≤ 100 lines.

- [ ] **Step 2: Create the test file**

Create `BomPriceApproval.Tests/Approvals/ApprovalValidationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using BomPriceApproval.Tests.Requisitions;

namespace BomPriceApproval.Tests.Approvals;

public class ApprovalValidationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    // Implement this following RequisitionWorkflowTests.cs end-to-end happy path pattern.
    // For itemCount=2, create the requisition with two finished goods; for each item,
    // start+save+submit BOM then start+submit costing; finish in MdReview status.
    private async Task<(int ReqId, int[] ItemIds)> BootstrapToMdReviewAsync(int itemCount)
    {
        throw new NotImplementedException(
            "Mirror RequisitionWorkflowTests end-to-end happy path; return (reqId, requisitionItemIds[]).");
    }

    [Fact]
    public async Task Approve_ZeroPrice_Returns400()
    {
        var (reqId, itemIds) = await BootstrapToMdReviewAsync(itemCount: 1);
        var md = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", md);

        var resp = await _client.PostAsJsonAsync(
            $"/api/approvals/{reqId}/approve",
            new { Items = new[] { new { RequisitionItemId = itemIds[0], SalesPricePerKgAed = 0m } }, Notes = "" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Approve_MissingItemInInput_Returns400()
    {
        var (reqId, itemIds) = await BootstrapToMdReviewAsync(itemCount: 2);
        var md = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", md);

        // Only submit 1 of 2 items
        var resp = await _client.PostAsJsonAsync(
            $"/api/approvals/{reqId}/approve",
            new { Items = new[] { new { RequisitionItemId = itemIds[0], SalesPricePerKgAed = 10m } }, Notes = "" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.ToLower().Should().Contain("missing");
    }

    [Fact]
    public async Task Approve_DuplicateItemInInput_Returns400()
    {
        var (reqId, itemIds) = await BootstrapToMdReviewAsync(itemCount: 1);
        var md = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", md);

        var resp = await _client.PostAsJsonAsync(
            $"/api/approvals/{reqId}/approve",
            new
            {
                Items = new[]
                {
                    new { RequisitionItemId = itemIds[0], SalesPricePerKgAed = 10m },
                    new { RequisitionItemId = itemIds[0], SalesPricePerKgAed = 20m },
                },
                Notes = ""
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Approve_NegativeMargin_Succeeds()
    {
        // The default bootstrap yields a BOM whose total cost is positive; set price to a
        // very small positive value to force negative margin while still > 0.
        var (reqId, itemIds) = await BootstrapToMdReviewAsync(itemCount: 1);
        var md = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", md);

        var resp = await _client.PostAsJsonAsync(
            $"/api/approvals/{reqId}/approve",
            new { Items = new[] { new { RequisitionItemId = itemIds[0], SalesPricePerKgAed = 0.01m } }, Notes = "" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK); // documents soft-warning policy
    }
}
```

- [ ] **Step 3: Run — expect the 4 tests to fail to compile (helper not implemented)**

```bash
dotnet test --filter "FullyQualifiedName~ApprovalValidationTests"
```

Expected: tests fail with `NotImplementedException` from the bootstrap helper.

- [ ] **Step 4: Implement the bootstrap helper**

Implement `BootstrapToMdReviewAsync` by following `RequisitionWorkflowTests.cs` end-to-end pattern. Create N finished goods and 1 raw material, 1 process, 1 exchange rate for AED only; then for each requisition item: `start` BOM → `save-lines` BOM → `submit` BOM (batch submit is a single call at the requisition level); then for each item: `start` costing → `submit` costing. The requisition should end in `MdReview`.

- [ ] **Step 5: Run — expect 3 fail, 1 pass**

```bash
dotnet test --filter "FullyQualifiedName~ApprovalValidationTests"
```

Expected:
- `Approve_ZeroPrice_Returns400` → FAIL (no controller check)
- `Approve_MissingItemInInput_Returns400` → FAIL (latent bug — returns 200)
- `Approve_DuplicateItemInInput_Returns400` → FAIL
- `Approve_NegativeMargin_Succeeds` → PASS (documents current soft-warning behavior)

- [ ] **Step 6: Implement controller guards**

In `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs`, modify the `Approve` method. After the existing `if (req.Status != RequisitionStatus.MdReview) return BadRequest(...);` block (around line 63), and **before** `var approval = new QuotationApproval { ... };`, insert:

```csharp
        if (request.Items is null || request.Items.Count == 0)
            return BadRequest(new { message = "No items provided for approval." });

        if (request.Items.Any(i => i.SalesPricePerKgAed <= 0))
            return BadRequest(new { message = "SalesPrice must be greater than 0." });

        var inputIds = request.Items.Select(i => i.RequisitionItemId).ToList();
        if (inputIds.Count != inputIds.Distinct().Count())
            return BadRequest(new { message = "Duplicate items in approval request." });

        var requisitionItemIds = req.Items.Select(i => i.Id).ToList();
        var missingInputs = requisitionItemIds.Except(inputIds).ToList();
        if (missingInputs.Count > 0)
            return BadRequest(new { message = $"Missing price for item(s): {string.Join(", ", missingInputs)}" });

        var orphanInputs = inputIds.Except(requisitionItemIds).ToList();
        if (orphanInputs.Count > 0)
            return BadRequest(new { message = $"Unknown item(s) in request: {string.Join(", ", orphanInputs)}" });

        if (req.Items.Any(i => i.BomHeader?.Cost is null))
            return BadRequest(new { message = "All items must have a costed BOM before approval." });
```

In the same method, find the existing inside-foreach line:

```csharp
            if (ri?.BomHeader?.Cost is null) continue;
```

Delete it. (We've verified above that every input matches a costed requisition item.)

- [ ] **Step 7: Run — expect all 4 PASS**

```bash
dotnet test --filter "FullyQualifiedName~ApprovalValidationTests"
```

Expected: all 4 tests pass.

- [ ] **Step 8: Run the full backend suite**

```bash
dotnet test
```

Expected: all tests pass. If `RequisitionWorkflowTests` breaks, the happy-path test was likely submitting only one item when the requisition had multiple — update the test data to submit all items with positive prices.

- [ ] **Step 9: Commit**

```bash
git add BomPriceApproval.API/Features/Approvals/ApprovalsController.cs \
        BomPriceApproval.Tests/Approvals/ApprovalValidationTests.cs
git commit -m "feat(api): validate prices and item coverage in Approve; fix silent skip bug"
```

---

## Task 6: Frontend `apiError` helper

**Files:**
- Create: `bom-web/src/lib/apiError.ts`
- Create: `bom-web/src/lib/apiError.test.ts`

- [ ] **Step 1: Write the failing test**

Create `bom-web/src/lib/apiError.test.ts`:

```ts
import { describe, it, expect } from "vitest";
import { extractApiError } from "./apiError";

describe("extractApiError", () => {
  it("returns response.data.message when present", () => {
    const err = { response: { data: { message: "Bad qty" } } };
    expect(extractApiError(err)).toBe("Bad qty");
  });

  it("returns fallback when no message", () => {
    const err = { response: { data: {} } };
    expect(extractApiError(err, "fallback")).toBe("fallback");
  });

  it("returns default fallback when no response", () => {
    expect(extractApiError(new Error("boom"))).toBe("Something went wrong");
  });

  it("handles unknown shapes safely", () => {
    expect(extractApiError(null)).toBe("Something went wrong");
    expect(extractApiError(undefined)).toBe("Something went wrong");
    expect(extractApiError("string")).toBe("Something went wrong");
  });
});
```

- [ ] **Step 2: Run — expect FAIL (module not found)**

```bash
cd bom-web && npx vitest run src/lib/apiError.test.ts
```

- [ ] **Step 3: Implement**

Create `bom-web/src/lib/apiError.ts`:

```ts
export function extractApiError(err: unknown, fallback = "Something went wrong"): string {
  if (err && typeof err === "object" && "response" in err) {
    const resp = (err as { response?: { data?: { message?: unknown } } }).response;
    const msg = resp?.data?.message;
    if (typeof msg === "string" && msg.length > 0) return msg;
  }
  return fallback;
}
```

- [ ] **Step 4: Run — expect PASS**

```bash
cd bom-web && npx vitest run src/lib/apiError.test.ts
```

Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/lib/apiError.ts bom-web/src/lib/apiError.test.ts
git commit -m "feat(web): add extractApiError helper for surfacing backend 400 messages"
```

---

## Task 7: `NewRequisitionPage` — Zod dedupe + picker filter

**Files:**
- Modify: `bom-web/src/features/requisitions/NewRequisitionPage.tsx`
- Modify: `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx`

- [ ] **Step 1: Append failing tests**

Append to the existing `describe("NewRequisitionPage", ...)` block in `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx`:

```ts
  it("excludes already-selected items from the second row's picker", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url.includes("/customers")) return Promise.resolve({ data: [{ id: 1, name: "ACME" }] });
      if (url.includes("/items"))
        return Promise.resolve({
          data: [
            { id: 10, code: "A", description: "Item A", type: "FinishedGood", isActive: true },
            { id: 20, code: "B", description: "Item B", type: "FinishedGood", isActive: true },
          ],
        });
      if (url.includes("/exchange-rates/active")) return Promise.resolve({ data: [] });
      return Promise.resolve({ data: [] });
    });

    const user = userEvent.setup();
    wrap(<NewRequisitionPage />);
    await waitFor(() => expect(screen.getByText(/New Requisition/i)).toBeInTheDocument());

    // Add a second row
    await user.click(screen.getByRole("button", { name: /Add Item/i }));

    // Select Item A in row 0
    const pickers = screen.getAllByPlaceholderText(/Search items/i);
    await user.click(pickers[0]);
    await user.click(screen.getByText("Item A"));

    // Open row 1's picker — Item A should no longer appear
    await user.click(pickers[1]);
    const visibleItemA = screen.queryAllByText("Item A");
    // One "Item A" may still be shown as the selected value in row 0, but it should
    // NOT appear inside the open row-1 dropdown list.
    expect(visibleItemA.length).toBeLessThanOrEqual(1);
  });
```

- [ ] **Step 2: Run — expect FAIL**

```bash
cd bom-web && npx vitest run src/features/requisitions/NewRequisitionPage.test.tsx
```

Expected: the new test fails (filter not implemented).

- [ ] **Step 3: Implement — extend schema + filter picker options**

In `bom-web/src/features/requisitions/NewRequisitionPage.tsx`:

**3a.** Update the imports at the top of the file (line 2). Replace:

```ts
import { useForm, Controller, useFieldArray } from "react-hook-form";
```

With:

```ts
import { useForm, Controller, useFieldArray, useWatch } from "react-hook-form";
```

**3b.** Replace the existing `schema` definition (around line 25-32):

```ts
const schema = z.object({
  customer: z
    .object({ id: z.number() })
    .nullable()
    .refine((v) => v !== null, { message: "Customer is required" }),
  items: z.array(itemRowSchema).min(1, "At least one item is required"),
  currencyCode: z.string().min(1, "Currency is required"),
});
```

With:

```ts
const schema = z.object({
  customer: z
    .object({ id: z.number() })
    .nullable()
    .refine((v) => v !== null, { message: "Customer is required" }),
  items: z
    .array(itemRowSchema)
    .min(1, "At least one item is required")
    .refine(
      (arr) => {
        const ids = arr
          .map((r) => r.item?.id)
          .filter((v): v is number => typeof v === "number");
        return new Set(ids).size === ids.length;
      },
      { message: "Duplicate items not allowed" },
    ),
  currencyCode: z.string().min(1, "Currency is required"),
});
```

**3c.** Immediately after the `const { fields, append, remove } = useFieldArray({ control, name: "items" });` line (around line 58), insert:

```ts
  const watchedItems = useWatch({ control, name: "items" });

  const availableItemsFor = (rowIndex: number): Item[] => {
    const base = itemsQ.data ?? [];
    const takenIds = new Set(
      (watchedItems ?? [])
        .map((row, i) => (i !== rowIndex ? row?.item?.id : undefined))
        .filter((v): v is number => typeof v === "number"),
    );
    return base.filter((it) => !takenIds.has(it.id));
  };
```

**3d.** In the per-row `SearchableSelect<Item>` (around line 132-141), replace:

```tsx
                            options={itemsQ.data ?? []}
```

With:

```tsx
                            options={availableItemsFor(index)}
```

- [ ] **Step 4: Run — expect PASS**

```bash
cd bom-web && npx vitest run src/features/requisitions/NewRequisitionPage.test.tsx
```

Expected: all tests (old + new) pass.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/features/requisitions/NewRequisitionPage.tsx \
        bom-web/src/features/requisitions/NewRequisitionPage.test.tsx
git commit -m "feat(web): filter already-selected items and zod-block duplicates in NewRequisitionPage"
```

---

## Task 8: Wire `extractApiError` in 3 pages + MD negative-margin badge

**Files:**
- Modify: `bom-web/src/features/bom/BomEntryPage.tsx`
- Modify: `bom-web/src/features/costing/CostingEntryPage.tsx`
- Modify: `bom-web/src/features/approvals/MdReviewPage.tsx`
- Modify: `bom-web/src/features/approvals/MdReviewPage.test.tsx`

This task has 3 independent edits and 1 test addition. Do them in 3 sub-commits.

### 8.A — `BomEntryPage`: surface submit errors

- [ ] **Step 1: Read `BomEntryPage.tsx`**

Note: the file already auto-saves silently on line changes; the only user-facing submit is `handleSubmit` (line ~262) calling `submitBom.mutate(requisitionId, { onSuccess: navigate })`. There is no visible error state on submit failure today.

- [ ] **Step 2: Add import + error state + surface message**

At the top of `bom-web/src/features/bom/BomEntryPage.tsx`, add the import:

```ts
import { extractApiError } from "@/lib/apiError";
```

Inside the component body, near the other `useState` calls (around line 85), add:

```ts
  const [submitError, setSubmitError] = useState<string | null>(null);
```

Replace the existing `handleSubmit` function body (around line 262-266):

```ts
  function handleSubmit() {
    submitBom.mutate(requisitionId, {
      onSuccess: () => navigate(`/requisitions/${requisitionId}`),
    });
  }
```

With:

```ts
  function handleSubmit() {
    setSubmitError(null);
    submitBom.mutate(requisitionId, {
      onSuccess: () => navigate(`/requisitions/${requisitionId}`),
      onError: (err) => setSubmitError(extractApiError(err)),
    });
  }
```

In the JSX, find the Submit-All block (around line 570-579):

```tsx
          {!isReadOnly && (
            <div className="flex justify-end">
              <Button
                onClick={handleSubmit}
                disabled={!allItemsReady || submitBom.isPending}
              >
                {submitBom.isPending ? "Submitting…" : "Submit All"}
              </Button>
            </div>
          )}
```

Replace with:

```tsx
          {!isReadOnly && (
            <div className="flex flex-col items-end gap-1">
              <Button
                onClick={handleSubmit}
                disabled={!allItemsReady || submitBom.isPending}
              >
                {submitBom.isPending ? "Submitting…" : "Submit All"}
              </Button>
              {submitError && <span className="text-xs text-destructive">{submitError}</span>}
            </div>
          )}
```

- [ ] **Step 3: Run BomEntryPage tests**

```bash
cd bom-web && npx vitest run src/features/bom/BomEntryPage.test.tsx
```

Expected: all existing tests still pass.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/features/bom/BomEntryPage.tsx
git commit -m "feat(web): surface backend errors on BOM submit via extractApiError"
```

### 8.B — `CostingEntryPage`: replace inline extraction with helper

- [ ] **Step 1: Add import + replace inline extraction**

At the top of `bom-web/src/features/costing/CostingEntryPage.tsx`, add:

```ts
import { extractApiError } from "@/lib/apiError";
```

Find the existing `onError` block (around line 281-288):

```ts
        onError: (err: unknown) => {
          const e = err as { response?: { status?: number; data?: { message?: string } } };
          if (e.response?.status === 400 && e.response.data?.message) {
            setSubmitError(e.response.data.message);
          } else {
            setSubmitError("Failed to submit costing.");
          }
        },
```

Replace with:

```ts
        onError: (err: unknown) => {
          setSubmitError(extractApiError(err, "Failed to submit costing."));
        },
```

- [ ] **Step 2: Run tests**

```bash
cd bom-web && npx vitest run src/features/costing/CostingEntryPage.test.tsx
```

Expected: all existing tests still pass.

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/features/costing/CostingEntryPage.tsx
git commit -m "refactor(web): use extractApiError helper in CostingEntryPage"
```

### 8.C — `MdReviewPage`: helper + negative-margin badge

- [ ] **Step 1: Append failing tests**

Append to the existing describe block in `bom-web/src/features/approvals/MdReviewPage.test.tsx`:

```ts
  it("shows '⚠ Negative margin' badge when price < totalCost but keeps Approve enabled", async () => {
    // Mock GET /approvals/:id with one item, totalCostPerKg=5
    // Enter price=1 → badge visible; Approve button is enabled (soft warning policy)
    // (Use the existing test harness pattern from this file for mocks.)
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url.includes(`/approvals/`))
        return Promise.resolve({
          data: {
            refNo: "REQ-0001",
            customerName: "ACME",
            currencyCode: "AED",
            exchangeRate: null,
            items: [
              {
                requisitionItemId: 1,
                itemDescription: "Widget",
                expectedQty: 100,
                rawMaterialCostPerKg: 4,
                landedCostPerKg: 0.5,
                fohPerKg: 0.5,
                totalCostPerKg: 5,
                materialCostPct: 80,
                landedCostPct: 10,
                fohPct: 10,
              },
            ],
          },
        });
      if (url.includes(`/bom/`))
        return Promise.resolve({
          data: { refNo: "REQ-0001", requisitionStatus: "MdReview", items: [] },
        });
      return Promise.resolve({ data: [] });
    });

    const user = userEvent.setup();
    wrap(<MdReviewPage />, { route: "/requisitions/1/approval" });
    await waitFor(() => expect(screen.getByText(/Widget/i)).toBeInTheDocument());

    const priceInput = screen.getByLabelText(/Sales Price/i);
    await user.type(priceInput, "1");

    expect(screen.getByText(/Negative margin/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Approve All/i })).toBeEnabled();
  });

  it("does not show the negative-margin badge when price >= totalCost", async () => {
    // Same mock as above; price=10 → no badge.
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url.includes(`/approvals/`))
        return Promise.resolve({
          data: {
            refNo: "REQ-0001",
            customerName: "ACME",
            currencyCode: "AED",
            exchangeRate: null,
            items: [
              {
                requisitionItemId: 1,
                itemDescription: "Widget",
                expectedQty: 100,
                rawMaterialCostPerKg: 4,
                landedCostPerKg: 0.5,
                fohPerKg: 0.5,
                totalCostPerKg: 5,
                materialCostPct: 80,
                landedCostPct: 10,
                fohPct: 10,
              },
            ],
          },
        });
      if (url.includes(`/bom/`))
        return Promise.resolve({
          data: { refNo: "REQ-0001", requisitionStatus: "MdReview", items: [] },
        });
      return Promise.resolve({ data: [] });
    });

    const user = userEvent.setup();
    wrap(<MdReviewPage />, { route: "/requisitions/1/approval" });
    await waitFor(() => expect(screen.getByText(/Widget/i)).toBeInTheDocument());

    const priceInput = screen.getByLabelText(/Sales Price/i);
    await user.type(priceInput, "10");

    expect(screen.queryByText(/Negative margin/i)).not.toBeInTheDocument();
  });
```

(If the existing `wrap` helper in this test file does not accept a `route` option, use whatever router-setup helper is already in the file. Read the existing test for the exact signature.)

- [ ] **Step 2: Run — expect FAIL**

```bash
cd bom-web && npx vitest run src/features/approvals/MdReviewPage.test.tsx
```

Expected: the two new tests fail (no badge rendered).

- [ ] **Step 3: Add import + replace inline extractions + add badge**

At the top of `bom-web/src/features/approvals/MdReviewPage.tsx`, add:

```ts
import { extractApiError } from "@/lib/apiError";
```

In `handleApprove` (around line 92), find:

```ts
    } catch (e) {
      const msg =
        (e as { response?: { data?: { message?: string } } })?.response?.data
          ?.message ?? "Failed to approve.";
      setValidationError(msg);
    }
```

Replace with:

```ts
    } catch (e) {
      setValidationError(extractApiError(e, "Failed to approve."));
    }
```

In `handleReject` (around line 128), find:

```ts
    } catch (e) {
      const msg =
        (e as { response?: { data?: { message?: string } } })?.response?.data
          ?.message ?? "Failed to reject.";
      setValidationError(msg);
    }
```

Replace with:

```ts
    } catch (e) {
      setValidationError(extractApiError(e, "Failed to reject."));
    }
```

Now add the negative-margin badge. In the per-item JSX (around line 226-236), find:

```tsx
                      {hasValidPrice && (
                        <div
                          className={`rounded-md p-2 text-center text-sm font-semibold ${
                            marginPct > 0
                              ? "bg-green-50 text-green-800"
                              : "bg-red-50 text-red-800"
                          }`}
                        >
                          Margin: {marginPct.toFixed(2)}%
                        </div>
                      )}
```

Replace with:

```tsx
                      {hasValidPrice && (
                        <div
                          className={`rounded-md p-2 text-center text-sm font-semibold ${
                            marginPct > 0
                              ? "bg-green-50 text-green-800"
                              : "bg-red-50 text-red-800"
                          }`}
                        >
                          <span>Margin: {marginPct.toFixed(2)}%</span>
                          {marginPct < 0 && (
                            <span className="ml-2 inline-flex items-center rounded-full bg-red-100 px-2 py-0.5 text-xs">
                              ⚠ Negative margin
                            </span>
                          )}
                        </div>
                      )}
```

- [ ] **Step 4: Run — expect PASS**

```bash
cd bom-web && npx vitest run src/features/approvals/MdReviewPage.test.tsx
```

Expected: all tests pass.

- [ ] **Step 5: Run the full frontend suite**

```bash
cd bom-web && npx vitest run
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add bom-web/src/features/approvals/MdReviewPage.tsx \
        bom-web/src/features/approvals/MdReviewPage.test.tsx
git commit -m "feat(web): extractApiError in MdReviewPage + negative-margin soft-warning badge"
```

---

## Self-Review

**Spec coverage:**

| Spec requirement | Task |
|---|---|
| UNIQUE `(QuotationRequestId, ItemId)` | 1 |
| CHECK `RequisitionItem.ExpectedQty > 0` | 1 |
| CHECK `BomLine.QtyPerKg > 0` | 1 |
| CHECK `ApprovalItem.SalesPricePerKgAed > 0` | 1 |
| `Create`: qty > 0, no duplicates, item active | 2 |
| `AddItem`: qty > 0, not-already-added, item active | 2 |
| `SaveLines`: qty > 0, wastage ≥ 0, lookups | 3 |
| `Costing.Submit`: cost ≥ 0, valid+complete BOM lines, fix silent skip | 4 |
| `Approve`: price > 0, all items covered, no dups, BOM costed, fix silent skip | 5 |
| `Approve`: no negative-margin block (soft only) | 5 (`Approve_NegativeMargin_Succeeds`) |
| Frontend `extractApiError` helper | 6 |
| `NewRequisitionPage`: dedupe refinement + picker filter | 7 |
| `BomEntryPage`: surface backend submit errors | 8.A |
| `CostingEntryPage`: use `extractApiError` | 8.B |
| `MdReviewPage`: use `extractApiError` + negative-margin badge, Approve stays enabled | 8.C |

**Placeholders:** none. Two bootstrap helpers (`BootstrapToCostingAsync` in Task 4, `BootstrapToMdReviewAsync` in Task 5) are described by pointing at the exact existing test files to mirror (`BomSaveLinesTests.SaveLines_ReplacesLinesWithoutChangingStatus` and `RequisitionWorkflowTests.cs` end-to-end). This is intentional — writing them inline would duplicate ~200 lines of test bootstrap the engineer can read directly.

**Type consistency:** `extractApiError(err, fallback?): string` — defined in Task 6, imported by exact name in 8.A/B/C. `availableItemsFor` scoped to Task 7 only. Error messages match between controller code and test `.Contain(...)` assertions (`"ExpectedQty"`, `"Duplicate"`, `"unknown"`, `"Missing cost"`, `"missing"`).

**Scope:** validation hardening only. No workflow changes, no `RequisitionDetailPage` UI expansion (flagged as out-of-scope in spec), no new libraries.

**Dependency order:** 1 → 2 → 3/4/5 (parallelizable) → 6 → 7 → 8.A/B/C (parallelizable). Frontend tasks (6-8) do not depend on backend tasks but subagent-driven execution will review each task before the next.
