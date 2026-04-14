# Customers & Items Management + Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add web UI + Excel import for Customers and Items, plus ERP purchase-ledger import with column mapping that updates `Item.LastPurchasePrice`.

**Architecture:** Feature-by-feature. Phase 1 builds Customer end-to-end (entity → API → import service → web UI). Phase 2 does the same for Item and adds the purchase-ledger column-mapping flow. Follows the existing feature-slice controller pattern. Reuses `ClosedXML` / `CsvHelper` (already in project) for imports.

**Tech Stack:** ASP.NET Core 8, EF Core 8 (PostgreSQL), ClosedXML, CsvHelper, xUnit + WebApplicationFactory + Testcontainers (backend). React 19 + Vite + TanStack Query + React Router + shadcn-style UI components (frontend).

**Spec:** `docs/superpowers/specs/2026-04-14-customers-items-import-design.md`

---

## Reference: Existing Patterns

**Backend — controller pattern** (see `Features/Requisitions/RequisitionsController.cs`):
- Uses primary constructor: `public class XController(AppDbContext db) : ControllerBase`
- Pulls claims via: `int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)`, `User.FindFirstValue(ClaimTypes.Role)!`, and `int.TryParse(User.FindFirstValue("branchId"), out var b)`
- Branch isolation: non-admin users have `BranchId`; admins/accountants/MD have null

**Backend — test pattern** (see `BomPriceApproval.Tests/Auth/AuthTests.cs`):
- Uses `WebApplicationFactory<Program>` as class fixture
- FluentAssertions + JSON helpers
- Testcontainers spin up real Postgres (already configured — do NOT mock)

**Frontend — API module pattern** (see `src/features/requisitions/requisitionsApi.ts`):
- Export `xKeys` object for query keys
- Export `useXQuery` / `useXMutation` hooks wrapping `useQuery` / `useMutation`
- Import `api` from `@/api/axios`; types from `@/types/api`

**Frontend — page pattern** (see `src/features/requisitions/RequisitionListPage.tsx`):
- Uses `DataTable` from `@/components/ui/DataTable` with TanStack columns
- Uses `useAuthStore` for role/branch
- Error state renders a `Card` with Retry button

---

## File Structure

### Backend (new / modified)

```
BomPriceApproval.API/
├── Domain/Entities/
│   ├── Customer.cs                                    [MODIFY] add Code, SalesPersonId; drop BranchId
│   └── Item.cs                                        [MODIFY] add LastPurchasePrice
├── Features/
│   ├── Customers/
│   │   ├── CustomerDtos.cs                            [MODIFY] add Code + SalesPersonId
│   │   ├── CustomersController.cs                     [MODIFY] auth + filters
│   │   └── CustomerImportController.cs                [NEW]
│   └── Items/
│       ├── ItemDtos.cs                                [MODIFY] add LastPurchasePrice + ledger DTOs
│       ├── ItemsController.cs                         [MODIFY] expose LastPurchasePrice
│       └── ItemImportController.cs                    [MODIFY] add template + ledger endpoints
├── Infrastructure/
│   ├── Data/
│   │   ├── AppDbContext.cs                            [MODIFY] add precision for LastPurchasePrice + unique Customer.Code
│   │   └── Migrations/                                [NEW] 2 migrations
│   └── Services/
│       ├── CustomerImportService.cs                   [NEW]
│       ├── ItemImportService.cs                       [MODIFY] handle LastPurchasePrice
│       └── PurchaseLedgerService.cs                   [NEW]
└── Program.cs                                         [MODIFY] seed data: add Code to customers, drop BranchId
```

### Backend tests

```
BomPriceApproval.Tests/
├── Customers/
│   ├── CustomersCrudTests.cs                          [NEW]
│   └── CustomerImportTests.cs                         [NEW]
└── Items/
    ├── ItemsCrudTests.cs                              [NEW]
    └── PurchaseLedgerImportTests.cs                   [NEW]
```

### Frontend (new / modified)

```
bom-web/src/
├── api/
│   └── lookups.ts                                     [MODIFY] add LastPurchasePrice to Item lookup
├── types/
│   └── api.ts                                         [MODIFY] add Customer/Item types
├── features/
│   ├── customers/                                     [NEW DIRECTORY]
│   │   ├── customersApi.ts
│   │   ├── CustomerListPage.tsx
│   │   ├── CustomerListPage.test.tsx
│   │   ├── components/
│   │   │   ├── AddCustomerModal.tsx
│   │   │   └── ImportCustomersModal.tsx
│   └── items/                                         [NEW DIRECTORY]
│       ├── itemsApi.ts
│       ├── ItemListPage.tsx
│       ├── ItemListPage.test.tsx
│       └── components/
│           ├── AddItemModal.tsx
│           ├── ImportItemsModal.tsx
│           └── ImportLedgerModal.tsx
├── components/layout/
│   └── AppShell.tsx                                   [MODIFY] add Customers/Items sidebar links
└── App.tsx                                            [MODIFY] add /customers + /items routes
```

---

## PHASE 1 — CUSTOMER FEATURE

### Task 1: Customer entity — add Code, SalesPersonId, drop BranchId

**Files:**
- Modify: `BomPriceApproval.API/Domain/Entities/Customer.cs`

- [ ] **Step 1:** Replace the Customer entity with the new shape.

```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class Customer
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public int? SalesPersonId { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public User? SalesPerson { get; set; }
    public User CreatedBy { get; set; } = null!;
}
```

- [ ] **Step 2:** Search for any references to `Customer.BranchId` or `customer.Branch` across the solution and fix them.

Run: `dotnet build BomPriceApproval.API` — expect errors listing every caller.

- [ ] **Step 3:** Update `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs`: wherever it filters customers by `BranchId`, replace with `SalesPersonId == CurrentUserId` for SalesPersons and no filter for other roles. (Details: search `Customers` queries and adjust the `.Where`.)

- [ ] **Step 4:** Build again. `dotnet build BomPriceApproval.API` must pass.

- [ ] **Step 5:** Commit.

```bash
git add BomPriceApproval.API/Domain/Entities/Customer.cs BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs
git commit -m "refactor(api): drop Customer.BranchId, add Code + SalesPersonId"
```

---

### Task 2: Add unique index on Customer.Code + configure AppDbContext

**Files:**
- Modify: `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs`

- [ ] **Step 1:** In `OnModelCreating`, add unique index on `Customer.Code`:

```csharp
mb.Entity<Customer>().HasIndex(c => c.Code).IsUnique();
mb.Entity<Customer>()
    .HasOne(c => c.SalesPerson)
    .WithMany()
    .HasForeignKey(c => c.SalesPersonId)
    .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 2:** Update `Program.cs` seed block — change every customer creation to include `Code` and `SalesPersonId = salesPerson.Id` instead of `BranchId`. Example:

```csharp
new Customer
{
    Code = "CUST-001",
    Name = "ACME Trading LLC", Address = "Fujairah Free Zone",
    Email = "orders@acme.test", PhoneNumber = "+97192000001",
    SalesPersonId = salesPerson.Id, CreatedByUserId = salesPerson.Id
},
new Customer
{
    Code = "CUST-002",
    Name = "Gulf Plastics Co", Address = "Industrial Area, Fujairah",
    Email = "procurement@gulfplastics.test", PhoneNumber = "+97192000002",
    SalesPersonId = salesPerson.Id, CreatedByUserId = salesPerson.Id
}
```

- [ ] **Step 3:** Build. `dotnet build` passes.

- [ ] **Step 4:** Commit.

```bash
git add BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs BomPriceApproval.API/Program.cs
git commit -m "feat(api): add Customer.Code unique index and seed ERP codes"
```

---

### Task 3: Create EF Core migration for Customer schema change

**Files:**
- Create: `BomPriceApproval.API/Infrastructure/Data/Migrations/<timestamp>_CustomerCodeSalesPerson.cs`

- [ ] **Step 1:** Generate the migration:

```bash
dotnet ef migrations add CustomerCodeSalesPerson --project BomPriceApproval.API
```

- [ ] **Step 2:** Open the generated `*_CustomerCodeSalesPerson.cs` file. Inside the `Up` method, BEFORE the `DropColumn("BranchId")` call, backfill `Code` for existing rows:

```csharp
migrationBuilder.AddColumn<string>(
    name: "Code",
    table: "Customers",
    type: "text",
    nullable: false,
    defaultValue: "");

migrationBuilder.Sql(@"UPDATE ""Customers"" SET ""Code"" = 'LEGACY-' || ""Id""::text WHERE ""Code"" = '';");
```

(If the generated code already has the `AddColumn`, keep it as-is and ONLY insert the `Sql` statement after it, BEFORE the `CreateIndex` line.)

- [ ] **Step 3:** Apply the migration against the local DB:

```bash
dotnet ef database update --project BomPriceApproval.API
```

- [ ] **Step 4:** Run the API briefly (Ctrl+C after seed logs) to verify it starts without errors. The seed block should skip (users already seeded).

- [ ] **Step 5:** Commit.

```bash
git add BomPriceApproval.API/Infrastructure/Data/Migrations/
git commit -m "feat(api): migrate Customers — add Code, drop BranchId"
```

---

### Task 4: Customer DTOs — add Code + SalesPersonId

**Files:**
- Modify: `BomPriceApproval.API/Features/Customers/CustomerDtos.cs`

- [ ] **Step 1:** Replace the file contents:

```csharp
namespace BomPriceApproval.API.Features.Customers;

public record CreateCustomerRequest(string Code, string Name, string Address, string Email, string PhoneNumber);
public record UpdateCustomerRequest(string Name, string Address, string Email, string PhoneNumber);
public record CustomerResponse(
    int Id,
    string Code,
    string Name,
    string Address,
    string Email,
    string PhoneNumber,
    int? SalesPersonId,
    string? SalesPersonName,
    int CreatedByUserId);
```

- [ ] **Step 2:** Build. Expect the `CustomersController` to break — that's fixed in Task 5.

- [ ] **Step 3:** (No commit yet — rolled into Task 5.)

---

### Task 5: Rewrite CustomersController — auth, filters, create flow

**Files:**
- Modify: `BomPriceApproval.API/Features/Customers/CustomersController.cs`

- [ ] **Step 1:** Replace the file:

```csharp
using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Customers;

[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController(AppDbContext db) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole => User.FindFirstValue(ClaimTypes.Role)!;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var query = db.Customers.Include(c => c.SalesPerson).AsQueryable();
        if (CurrentRole == "SalesPerson")
            query = query.Where(c => c.SalesPersonId == CurrentUserId);

        var list = await query
            .OrderBy(c => c.Name)
            .Select(c => new CustomerResponse(
                c.Id, c.Code, c.Name, c.Address, c.Email, c.PhoneNumber,
                c.SalesPersonId, c.SalesPerson != null ? c.SalesPerson.Name : null,
                c.CreatedByUserId))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var c = await db.Customers.Include(c => c.SalesPerson).FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();
        if (CurrentRole == "SalesPerson" && c.SalesPersonId != CurrentUserId) return Forbid();
        return Ok(new CustomerResponse(
            c.Id, c.Code, c.Name, c.Address, c.Email, c.PhoneNumber,
            c.SalesPersonId, c.SalesPerson?.Name, c.CreatedByUserId));
    }

    [HttpPost]
    [Authorize(Roles = "SalesPerson,Admin")]
    public async Task<IActionResult> Create(CreateCustomerRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { message = "Customer code is required." });

        if (await db.Customers.AnyAsync(c => c.Code == req.Code))
            return Conflict(new { message = $"Customer with code '{req.Code}' already exists." });

        var customer = new Customer
        {
            Code = req.Code.Trim(),
            Name = req.Name,
            Address = req.Address,
            Email = req.Email,
            PhoneNumber = req.PhoneNumber,
            SalesPersonId = CurrentRole == "SalesPerson" ? CurrentUserId : null,
            CreatedByUserId = CurrentUserId
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = customer.Id },
            new CustomerResponse(customer.Id, customer.Code, customer.Name, customer.Address,
                customer.Email, customer.PhoneNumber, customer.SalesPersonId, null, customer.CreatedByUserId));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "SalesPerson,Admin")]
    public async Task<IActionResult> Update(int id, UpdateCustomerRequest req)
    {
        var c = await db.Customers.FindAsync(id);
        if (c is null) return NotFound();
        if (CurrentRole == "SalesPerson" && c.SalesPersonId != CurrentUserId) return Forbid();

        c.Name = req.Name;
        c.Address = req.Address;
        c.Email = req.Email;
        c.PhoneNumber = req.PhoneNumber;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
```

- [ ] **Step 2:** Build. `dotnet build` passes.

- [ ] **Step 3:** Commit.

```bash
git add BomPriceApproval.API/Features/Customers/
git commit -m "feat(api): Customers CRUD — Code required, Admin can create, dedup globally"
```

---

### Task 6: Test CustomersController — auth, dedup, filtering

**Files:**
- Create: `BomPriceApproval.Tests/Customers/CustomersCrudTests.cs`

- [ ] **Step 1:** Create the test file:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Customers;

public class CustomersCrudTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    [Fact]
    public async Task Create_DuplicateCode_Returns409()
    {
        var token = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var first = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = "DUPTEST-1", Name = "A", Address = "", Email = "", PhoneNumber = ""
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = "DUPTEST-1", Name = "B", Address = "", Email = "", PhoneNumber = ""
        });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_AsAdmin_SetsSalesPersonIdToNull()
    {
        var token = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = "ADMIN-CUST-1", Name = "Admin Co", Address = "", Email = "", PhoneNumber = ""
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<CustomerResponse>();
        body!.SalesPersonId.Should().BeNull();
    }

    [Fact]
    public async Task GetAll_AsSalesPerson_OnlyReturnsOwnCustomers()
    {
        var token = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/customers");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await resp.Content.ReadFromJsonAsync<List<CustomerResponse>>();
        list!.Should().OnlyContain(c => c.SalesPersonId == 2 /* ali is user 2; adjust if seed order differs */);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record CustomerResponse(int Id, string Code, string Name, string Address, string Email, string PhoneNumber, int? SalesPersonId, string? SalesPersonName, int CreatedByUserId);
}
```

- [ ] **Step 2:** Run the tests. They should fail if the seed has Ali's user id as something other than 2. Adjust the assertion to use a dynamic lookup if it does — fetch the current user id from the login response and filter with that instead of hardcoding `2`.

```bash
dotnet test --filter "FullyQualifiedName~CustomersCrudTests"
```

Expected: all 3 PASS.

- [ ] **Step 3:** Commit.

```bash
git add BomPriceApproval.Tests/Customers/CustomersCrudTests.cs
git commit -m "test(api): Customers CRUD — dedup, admin creation, role filtering"
```

---

### Task 7: CustomerImportService — Excel/CSV import with Code dedup

**Files:**
- Create: `BomPriceApproval.API/Infrastructure/Services/CustomerImportService.cs`

- [ ] **Step 1:** Create the file:

```csharp
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using ClosedXML.Excel;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace BomPriceApproval.API.Infrastructure.Services;

public class CustomerImportService(AppDbContext db)
{
    public async Task<ImportResult> ImportExcelAsync(Stream stream, int createdByUserId)
    {
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheet(1);
        var rows = ws.RangeUsed().RowsUsed().Skip(1); // skip header

        var records = rows.Select(row => (
            Code: row.Cell(1).GetString().Trim(),
            Name: row.Cell(2).GetString().Trim(),
            Address: row.Cell(3).GetString().Trim(),
            Email: row.Cell(4).GetString().Trim(),
            Phone: row.Cell(5).GetString().Trim()
        )).ToList();

        return await ImportAsync(records, createdByUserId);
    }

    public async Task<ImportResult> ImportCsvAsync(Stream stream, int createdByUserId)
    {
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        var records = csv.GetRecords<CsvRow>()
            .Select(r => (r.Code, r.Name, r.Address, r.Email, r.PhoneNumber))
            .ToList();
        return await ImportAsync(records, createdByUserId);
    }

    private async Task<ImportResult> ImportAsync(
        IEnumerable<(string Code, string Name, string Address, string Email, string Phone)> records,
        int createdByUserId)
    {
        int imported = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var (code, name, address, email, phone) in records)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                errors.Add($"Skipped row with missing code (name='{name}')");
                skipped++;
                continue;
            }

            if (await db.Customers.AnyAsync(c => c.Code == code))
            {
                skipped++;
                continue;
            }

            db.Customers.Add(new Customer
            {
                Code = code,
                Name = name,
                Address = address,
                Email = email,
                PhoneNumber = phone,
                SalesPersonId = null,
                CreatedByUserId = createdByUserId
            });
            imported++;
        }

        await db.SaveChangesAsync();
        return new ImportResult(imported, skipped, errors);
    }

    public static byte[] GenerateTemplate()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Customers");
        ws.Cell(1, 1).Value = "Code";
        ws.Cell(1, 2).Value = "Name";
        ws.Cell(1, 3).Value = "Address";
        ws.Cell(1, 4).Value = "Email";
        ws.Cell(1, 5).Value = "PhoneNumber";
        ws.Row(1).Style.Font.Bold = true;
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private class CsvRow
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Address { get; set; } = "";
        public string Email { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
    }
}
```

- [ ] **Step 2:** Register the service in `Program.cs` alongside `ItemImportService`:

Find the line `builder.Services.AddScoped<ItemImportService>();` and add below it:

```csharp
builder.Services.AddScoped<CustomerImportService>();
```

- [ ] **Step 3:** Build. `dotnet build` passes.

- [ ] **Step 4:** Commit.

```bash
git add BomPriceApproval.API/Infrastructure/Services/CustomerImportService.cs BomPriceApproval.API/Program.cs
git commit -m "feat(api): CustomerImportService — xlsx/csv import with Code dedup + template"
```

---

### Task 8: CustomerImportController — template + import endpoints

**Files:**
- Create: `BomPriceApproval.API/Features/Customers/CustomerImportController.cs`

- [ ] **Step 1:** Create the file:

```csharp
using System.Security.Claims;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BomPriceApproval.API.Features.Customers;

[ApiController]
[Route("api/customers/import")]
[Authorize(Roles = "Admin")]
public class CustomerImportController(CustomerImportService importService) : ControllerBase
{
    [HttpGet("template")]
    public IActionResult Template()
    {
        var bytes = CustomerImportService.GenerateTemplate();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "customers-template.xlsx");
    }

    [HttpPost]
    public async Task<IActionResult> Import([FromForm] IFormFile file)
    {
        if (file.Length == 0) return BadRequest(new { message = "File is empty" });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext is not (".xlsx" or ".csv"))
            return BadRequest(new { message = "Only .xlsx and .csv files are supported" });

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        using var stream = file.OpenReadStream();
        var result = ext == ".xlsx"
            ? await importService.ImportExcelAsync(stream, userId)
            : await importService.ImportCsvAsync(stream, userId);

        return Ok(result);
    }
}
```

- [ ] **Step 2:** Build. `dotnet build` passes.

- [ ] **Step 3:** Commit.

```bash
git add BomPriceApproval.API/Features/Customers/CustomerImportController.cs
git commit -m "feat(api): CustomerImportController — template download + import endpoint"
```

---

### Task 9: Test CustomerImportController — template, dedup, role gate

**Files:**
- Create: `BomPriceApproval.Tests/Customers/CustomerImportTests.cs`

- [ ] **Step 1:** Create the file:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Customers;

public class CustomerImportTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    [Fact]
    public async Task Template_AsAdmin_ReturnsXlsx()
    {
        var token = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/customers/import/template");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Template_AsSalesPerson_Returns403()
    {
        var token = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/customers/import/template");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Import_SkipsDuplicateCodes()
    {
        var token = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Build an in-memory xlsx with: 1 new code + 1 duplicate of CUST-001 (seeded)
        byte[] bytes;
        using (var wb = new XLWorkbook())
        {
            var ws = wb.Worksheets.Add("Customers");
            ws.Cell(1, 1).Value = "Code"; ws.Cell(1, 2).Value = "Name";
            ws.Cell(1, 3).Value = "Address"; ws.Cell(1, 4).Value = "Email"; ws.Cell(1, 5).Value = "PhoneNumber";
            ws.Cell(2, 1).Value = "IMPORT-NEW-1"; ws.Cell(2, 2).Value = "New Co";
            ws.Cell(3, 1).Value = "CUST-001"; ws.Cell(3, 2).Value = "Dup Co";
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            bytes = ms.ToArray();
        }

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fileContent, "file", "test.xlsx");

        var resp = await _client.PostAsync("/api/customers/import", form);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await resp.Content.ReadFromJsonAsync<ImportResultDto>();
        result!.Imported.Should().Be(1);
        result.Skipped.Should().Be(1);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record ImportResultDto(int Imported, int Skipped, List<string> Errors);
}
```

- [ ] **Step 2:** Run tests.

```bash
dotnet test --filter "FullyQualifiedName~CustomerImportTests"
```

Expected: all 3 PASS.

- [ ] **Step 3:** Commit.

```bash
git add BomPriceApproval.Tests/Customers/CustomerImportTests.cs
git commit -m "test(api): CustomerImport — template, role gate, dedup"
```

---

### Task 10: Frontend — customer types + API module

**Files:**
- Modify: `bom-web/src/types/api.ts`
- Create: `bom-web/src/features/customers/customersApi.ts`

- [ ] **Step 1:** In `bom-web/src/types/api.ts`, add the Customer types (append to the existing file):

```typescript
export interface Customer {
  id: number;
  code: string;
  name: string;
  address: string;
  email: string;
  phoneNumber: string;
  salesPersonId: number | null;
  salesPersonName: string | null;
  createdByUserId: number;
}

export interface CreateCustomerRequest {
  code: string;
  name: string;
  address: string;
  email: string;
  phoneNumber: string;
}

export interface ImportResult {
  imported: number;
  skipped: number;
  errors: string[];
}
```

- [ ] **Step 2:** Create the API module:

```typescript
// bom-web/src/features/customers/customersApi.ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type { Customer, CreateCustomerRequest, ImportResult } from "@/types/api";

export const customerKeys = {
  all: ["customers"] as const,
  list: () => [...customerKeys.all, "list"] as const,
};

export function useCustomers() {
  return useQuery({
    queryKey: customerKeys.list(),
    queryFn: () => api.get<Customer[]>("/customers").then((r) => r.data),
  });
}

export function useCreateCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateCustomerRequest) =>
      api.post<Customer>("/customers", body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: customerKeys.all }),
  });
}

export function useImportCustomers() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (file: File) => {
      const fd = new FormData();
      fd.append("file", file);
      return api
        .post<ImportResult>("/customers/import", fd, {
          headers: { "Content-Type": "multipart/form-data" },
        })
        .then((r) => r.data);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: customerKeys.all }),
  });
}

export function downloadCustomerTemplate() {
  return api
    .get("/customers/import/template", { responseType: "blob" })
    .then((r) => {
      const url = window.URL.createObjectURL(r.data as Blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = "customers-template.xlsx";
      a.click();
      window.URL.revokeObjectURL(url);
    });
}
```

- [ ] **Step 3:** Commit.

```bash
git add bom-web/src/types/api.ts bom-web/src/features/customers/customersApi.ts
git commit -m "feat(web): customer types and API hooks"
```

---

### Task 11: Frontend — AddCustomerModal component

**Files:**
- Create: `bom-web/src/features/customers/components/AddCustomerModal.tsx`

- [ ] **Step 1:** Create the file:

```tsx
import { useState } from "react";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { useCreateCustomer } from "../customersApi";
import { useAuthStore } from "@/store/authStore";

interface Props { open: boolean; onOpenChange: (open: boolean) => void; }

export function AddCustomerModal({ open, onOpenChange }: Props) {
  const role = useAuthStore((s) => s.user?.role);
  const userName = useAuthStore((s) => s.user?.name);
  const createCustomer = useCreateCustomer();

  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [address, setAddress] = useState("");
  const [email, setEmail] = useState("");
  const [phoneNumber, setPhone] = useState("");
  const [error, setError] = useState<string | null>(null);

  const reset = () => {
    setCode(""); setName(""); setAddress(""); setEmail(""); setPhone(""); setError(null);
  };

  const submit = async () => {
    setError(null);
    if (!code.trim() || !name.trim()) {
      setError("Code and Name are required.");
      return;
    }
    try {
      await createCustomer.mutateAsync({ code, name, address, email, phoneNumber });
      reset();
      onOpenChange(false);
    } catch (e: unknown) {
      const err = e as { response?: { data?: { message?: string } } };
      setError(err.response?.data?.message ?? "Failed to create customer");
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader><DialogTitle>Add Customer</DialogTitle></DialogHeader>
        <div className="space-y-3">
          <div>
            <Label htmlFor="cust-code">Customer Code *</Label>
            <Input id="cust-code" value={code} onChange={(e) => setCode(e.target.value)} placeholder="ERP customer code" />
          </div>
          <div>
            <Label htmlFor="cust-name">Name *</Label>
            <Input id="cust-name" value={name} onChange={(e) => setName(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="cust-address">Address</Label>
            <Input id="cust-address" value={address} onChange={(e) => setAddress(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="cust-email">Email</Label>
            <Input id="cust-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="cust-phone">Phone</Label>
            <Input id="cust-phone" value={phoneNumber} onChange={(e) => setPhone(e.target.value)} />
          </div>
          {role === "SalesPerson" && (
            <div>
              <Label>Salesperson</Label>
              <Input value={userName ?? ""} disabled />
            </div>
          )}
          {error && <p className="text-destructive text-sm">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={() => { reset(); onOpenChange(false); }}>Cancel</Button>
          <Button onClick={submit} disabled={createCustomer.isPending}>
            {createCustomer.isPending ? "Saving..." : "Save"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
```

- [ ] **Step 2:** Verify existing Dialog/Input/Label/Button components exist under `bom-web/src/components/ui/`. If any are missing, use the shadcn CLI to add them or copy from an existing modal (e.g., grep for `Dialog` in the requisitions pages):

```bash
grep -r "from \"@/components/ui/Dialog\"" bom-web/src
```

If Dialog is missing, substitute with whatever modal primitive exists in `src/components/ui/` — the pattern in NewRequisitionPage.tsx is authoritative.

- [ ] **Step 3:** Commit.

```bash
git add bom-web/src/features/customers/components/AddCustomerModal.tsx
git commit -m "feat(web): AddCustomerModal form"
```

---

### Task 12: Frontend — ImportCustomersModal

**Files:**
- Create: `bom-web/src/features/customers/components/ImportCustomersModal.tsx`

- [ ] **Step 1:** Create the file:

```tsx
import { useState } from "react";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { useImportCustomers, downloadCustomerTemplate } from "../customersApi";
import type { ImportResult } from "@/types/api";

interface Props { open: boolean; onOpenChange: (open: boolean) => void; }

export function ImportCustomersModal({ open, onOpenChange }: Props) {
  const importMutation = useImportCustomers();
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const close = () => { setFile(null); setResult(null); setError(null); onOpenChange(false); };

  const submit = async () => {
    if (!file) return;
    setError(null);
    try {
      const r = await importMutation.mutateAsync(file);
      setResult(r);
    } catch (e: unknown) {
      const err = e as { response?: { data?: { message?: string } } };
      setError(err.response?.data?.message ?? "Import failed");
    }
  };

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) close(); }}>
      <DialogContent>
        <DialogHeader><DialogTitle>Import Customers</DialogTitle></DialogHeader>
        <div className="space-y-4">
          <Button variant="ghost" onClick={() => downloadCustomerTemplate()}>
            Download template
          </Button>
          <div>
            <Input
              type="file"
              accept=".xlsx,.csv"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
          </div>
          {result && (
            <div className="rounded border p-3 text-sm">
              <p>{result.imported} imported, {result.skipped} skipped</p>
              {result.errors.length > 0 && (
                <ul className="list-disc pl-4 mt-2 text-destructive">
                  {result.errors.map((er, i) => <li key={i}>{er}</li>)}
                </ul>
              )}
            </div>
          )}
          {error && <p className="text-destructive text-sm">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={close}>Close</Button>
          <Button onClick={submit} disabled={!file || importMutation.isPending}>
            {importMutation.isPending ? "Importing..." : "Import"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
```

- [ ] **Step 2:** Commit.

```bash
git add bom-web/src/features/customers/components/ImportCustomersModal.tsx
git commit -m "feat(web): ImportCustomersModal — template download + upload + result"
```

---

### Task 13: Frontend — CustomerListPage

**Files:**
- Create: `bom-web/src/features/customers/CustomerListPage.tsx`

- [ ] **Step 1:** Create the file:

```tsx
import { useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { useCustomers } from "./customersApi";
import { AddCustomerModal } from "./components/AddCustomerModal";
import { ImportCustomersModal } from "./components/ImportCustomersModal";
import { DataTable } from "@/components/ui/DataTable";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { useAuthStore } from "@/store/authStore";
import type { Customer } from "@/types/api";

const columns: ColumnDef<Customer>[] = [
  { accessorKey: "code", header: "Code", cell: (i) => <span className="font-mono text-xs">{i.getValue() as string}</span> },
  { accessorKey: "name", header: "Name" },
  { accessorKey: "email", header: "Email" },
  { accessorKey: "phoneNumber", header: "Phone" },
  { accessorKey: "salesPersonName", header: "Salesperson", cell: (i) => (i.getValue() as string) ?? "—" },
];

export default function CustomerListPage() {
  const role = useAuthStore((s) => s.user?.role);
  const { data, isLoading, isError, refetch } = useCustomers();
  const [addOpen, setAddOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);

  const canAdd = role === "SalesPerson" || role === "Admin";
  const canImport = role === "Admin";

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">Customers</h1>
        <div className="flex gap-2">
          {canImport && <Button variant="outline" onClick={() => setImportOpen(true)}>Import</Button>}
          {canAdd && <Button onClick={() => setAddOpen(true)}>Add Customer</Button>}
        </div>
      </div>

      {isError && (
        <Card>
          <CardContent className="flex items-center justify-between">
            <p className="text-destructive">Failed to load customers.</p>
            <Button variant="ghost" onClick={() => refetch()}>Retry</Button>
          </CardContent>
        </Card>
      )}

      <DataTable
        columns={columns}
        data={data ?? []}
        isLoading={isLoading}
        emptyState={<p>No customers yet.</p>}
      />

      <AddCustomerModal open={addOpen} onOpenChange={setAddOpen} />
      {canImport && <ImportCustomersModal open={importOpen} onOpenChange={setImportOpen} />}
    </div>
  );
}
```

- [ ] **Step 2:** Commit.

```bash
git add bom-web/src/features/customers/CustomerListPage.tsx
git commit -m "feat(web): CustomerListPage"
```

---

### Task 14: Wire customers route + sidebar link

**Files:**
- Modify: `bom-web/src/App.tsx`
- Modify: `bom-web/src/components/layout/AppShell.tsx`

- [ ] **Step 1:** In `App.tsx`, add the import and route:

```tsx
import CustomerListPage from "@/features/customers/CustomerListPage";
```

Add this inside the `children` array, after the requisitions routes:

```tsx
{
  path: "customers",
  element: (
    <ProtectedRoute
      allow={["Admin", "SalesPerson", "BomCreator", "Accountant", "ManagingDirector"]}
    >
      <CustomerListPage />
    </ProtectedRoute>
  ),
},
```

- [ ] **Step 2:** Open `bom-web/src/components/layout/AppShell.tsx` and find the sidebar nav section. Add a "Customers" link alongside the existing links (match the visual style of the Requisitions link). Example:

```tsx
<NavLink to="/customers" className={navClass}>Customers</NavLink>
```

- [ ] **Step 3:** Manually verify in dev: run `npm run dev` in `bom-web/`, navigate to `/customers`. Add button visible for SalesPerson & Admin. Import button visible only for Admin.

- [ ] **Step 4:** Commit.

```bash
git add bom-web/src/App.tsx bom-web/src/components/layout/AppShell.tsx
git commit -m "feat(web): wire /customers route and sidebar link"
```

---

### Task 15: Test CustomerListPage — renders, role-gated buttons

**Files:**
- Create: `bom-web/src/features/customers/CustomerListPage.test.tsx`

- [ ] **Step 1:** Create a minimal render test that mirrors the pattern from `RequisitionListPage.test.tsx` — open that file and copy its setup (MSW handlers, router provider, auth store mock). Then assert:

```tsx
// Key assertions only — full setup copied from RequisitionListPage.test.tsx

it("shows Add + Import buttons for Admin", async () => {
  // ... set auth store to Admin role
  render(<CustomerListPage />, { wrapper });
  expect(await screen.findByRole("button", { name: /add customer/i })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /import/i })).toBeInTheDocument();
});

it("hides Import button for SalesPerson", async () => {
  // ... set auth store to SalesPerson role
  render(<CustomerListPage />, { wrapper });
  expect(await screen.findByRole("button", { name: /add customer/i })).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /import/i })).not.toBeInTheDocument();
});
```

- [ ] **Step 2:** Run the tests:

```bash
cd bom-web && npm test -- CustomerListPage
```

Expected: PASS.

- [ ] **Step 3:** Commit.

```bash
git add bom-web/src/features/customers/CustomerListPage.test.tsx
git commit -m "test(web): CustomerListPage role-gated buttons"
```

---

**PHASE 1 CHECKPOINT:** Customer feature complete end-to-end. Run full test suite:

```bash
dotnet test
cd bom-web && npm test
```

Both must pass before starting Phase 2.

---

## PHASE 2 — ITEM FEATURE + PURCHASE LEDGER

### Task 16: Item entity — add LastPurchasePrice

**Files:**
- Modify: `BomPriceApproval.API/Domain/Entities/Item.cs`
- Modify: `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs`

- [ ] **Step 1:** Add the field to `Item.cs`:

```csharp
public decimal? LastPurchasePrice { get; set; }
```

- [ ] **Step 2:** In `AppDbContext.OnModelCreating`, add precision config alongside existing ones:

```csharp
mb.Entity<Item>().Property(i => i.LastPurchasePrice).HasPrecision(18, 4);
```

- [ ] **Step 3:** Generate the migration:

```bash
dotnet ef migrations add ItemLastPurchasePrice --project BomPriceApproval.API
```

- [ ] **Step 4:** Apply it:

```bash
dotnet ef database update --project BomPriceApproval.API
```

- [ ] **Step 5:** Commit.

```bash
git add BomPriceApproval.API/Domain/Entities/Item.cs BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs BomPriceApproval.API/Infrastructure/Data/Migrations/
git commit -m "feat(api): add Item.LastPurchasePrice column"
```

---

### Task 17: Item DTOs + ItemsController expose LastPurchasePrice

**Files:**
- Modify: `BomPriceApproval.API/Features/Items/ItemDtos.cs`
- Modify: `BomPriceApproval.API/Features/Items/ItemsController.cs`

- [ ] **Step 1:** Replace `ItemDtos.cs`:

```csharp
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Items;

public record CreateItemRequest(string Code, string Description, ItemType Type, decimal? LastPurchasePrice);
public record ItemResponse(int Id, string Code, string Description, string Type, int BranchId, bool IsActive, decimal? LastPurchasePrice);
public record SimilarItemResult(int Id, string Code, string Description);

public record LedgerHeadersResponse(List<string> Headers);
public record LedgerImportRequest(string ItemCodeColumn, string DateColumn, string UnitPriceColumn, int BranchId);
public record LedgerImportResult(int Updated, int Skipped, List<string> UnmatchedCodes);
```

- [ ] **Step 2:** In `ItemsController.cs`:
  - Update the `GetAll` projection to include `LastPurchasePrice` — change the `.Select` to:

```csharp
.Select(i => new ItemResponse(i.Id, i.Code, i.Description, i.Type.ToString(), i.BranchId, i.IsActive, i.LastPurchasePrice))
```

  - Update the `Create` method:

```csharp
var item = new Item
{
    Code = req.Code, Description = req.Description, Type = req.Type,
    BranchId = CurrentBranchId.Value,
    LastPurchasePrice = req.LastPurchasePrice
};
```

And update the `CreatedAtAction` return so the response includes the new field:

```csharp
return CreatedAtAction(nameof(GetAll),
    new ItemResponse(item.Id, item.Code, item.Description, item.Type.ToString(), item.BranchId, item.IsActive, item.LastPurchasePrice));
```

- [ ] **Step 3:** Build. `dotnet build` passes.

- [ ] **Step 4:** Commit.

```bash
git add BomPriceApproval.API/Features/Items/
git commit -m "feat(api): expose Item.LastPurchasePrice in DTOs and create endpoint"
```

---

### Task 18: ItemImportService — handle LastPurchasePrice + template generator

**Files:**
- Modify: `BomPriceApproval.API/Infrastructure/Services/ItemImportService.cs`

- [ ] **Step 1:** Replace the file:

```csharp
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using ClosedXML.Excel;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace BomPriceApproval.API.Infrastructure.Services;

public record ImportResult(int Imported, int Skipped, List<string> Errors);

public class ItemImportService(AppDbContext db)
{
    public async Task<ImportResult> ImportExcelAsync(Stream stream, int branchId)
    {
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheet(1);
        var rows = ws.RangeUsed().RowsUsed().Skip(1);

        var items = rows.Select(row => (
            Code: row.Cell(1).GetString().Trim(),
            Description: row.Cell(2).GetString().Trim(),
            TypeStr: row.Cell(3).GetString().Trim(),
            LastPurchasePrice: row.Cell(4).TryGetValue<decimal>(out var p) ? p : (decimal?)null
        )).ToList();

        return await ImportItemsAsync(items, branchId);
    }

    public async Task<ImportResult> ImportCsvAsync(Stream stream, int branchId)
    {
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        var records = csv.GetRecords<CsvRow>().ToList();
        var items = records.Select(r => (
            r.Code, r.Description, TypeStr: r.Type,
            LastPurchasePrice: (decimal?)r.LastPurchasePrice
        ));
        return await ImportItemsAsync(items, branchId);
    }

    private async Task<ImportResult> ImportItemsAsync(
        IEnumerable<(string Code, string Description, string TypeStr, decimal? LastPurchasePrice)> items,
        int branchId)
    {
        int imported = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var (code, description, typeStr, price) in items)
        {
            if (!Enum.TryParse<ItemType>(typeStr, ignoreCase: true, out var type))
            {
                errors.Add($"Invalid type '{typeStr}' for item code '{code}'");
                skipped++;
                continue;
            }
            if (await db.Items.AnyAsync(i => i.Code == code && i.BranchId == branchId))
            {
                skipped++;
                continue;
            }
            db.Items.Add(new Item
            {
                Code = code, Description = description, Type = type,
                BranchId = branchId, LastPurchasePrice = price
            });
            imported++;
        }

        await db.SaveChangesAsync();
        return new ImportResult(imported, skipped, errors);
    }

    public static byte[] GenerateTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Items");
        ws.Cell(1, 1).Value = "Code";
        ws.Cell(1, 2).Value = "Description";
        ws.Cell(1, 3).Value = "Type";
        ws.Cell(1, 4).Value = "LastPurchasePrice";
        ws.Row(1).Style.Font.Bold = true;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private class CsvRow
    {
        public string Code { get; set; } = "";
        public string Description { get; set; } = "";
        public string Type { get; set; } = "";
        public decimal LastPurchasePrice { get; set; }
    }
}
```

- [ ] **Step 2:** Build. `dotnet build` passes.

- [ ] **Step 3:** Commit.

```bash
git add BomPriceApproval.API/Infrastructure/Services/ItemImportService.cs
git commit -m "feat(api): ItemImportService reads LastPurchasePrice + template generator"
```

---

### Task 19: PurchaseLedgerService — headers extraction + mapped import

**Files:**
- Create: `BomPriceApproval.API/Infrastructure/Services/PurchaseLedgerService.cs`

- [ ] **Step 1:** Create the file:

```csharp
using BomPriceApproval.API.Infrastructure.Data;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Infrastructure.Services;

public record LedgerImportSummary(int Updated, int Skipped, List<string> UnmatchedCodes);

public class PurchaseLedgerService(AppDbContext db)
{
    public List<string> ExtractHeaders(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);
        var headerRow = ws.FirstRowUsed();
        return headerRow.CellsUsed().Select(c => c.GetString().Trim()).ToList();
    }

    public async Task<LedgerImportSummary> ImportAsync(
        Stream stream, string itemCodeColumn, string dateColumn, string unitPriceColumn, int branchId)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);

        var headerRow = ws.FirstRowUsed();
        var headerToIndex = headerRow.CellsUsed()
            .ToDictionary(c => c.GetString().Trim(), c => c.Address.ColumnNumber);

        if (!headerToIndex.TryGetValue(itemCodeColumn, out var codeCol))
            throw new InvalidOperationException($"Column '{itemCodeColumn}' not found");
        if (!headerToIndex.TryGetValue(dateColumn, out var dateCol))
            throw new InvalidOperationException($"Column '{dateColumn}' not found");
        if (!headerToIndex.TryGetValue(unitPriceColumn, out var priceCol))
            throw new InvalidOperationException($"Column '{unitPriceColumn}' not found");

        // Parse rows (skip header)
        var rows = ws.RangeUsed().RowsUsed().Skip(1).Select(row => new
        {
            Code = row.Cell(codeCol).GetString().Trim(),
            Date = row.Cell(dateCol).TryGetValue<DateTime>(out var d) ? d : DateTime.MinValue,
            Price = row.Cell(priceCol).TryGetValue<decimal>(out var p) ? p : 0m
        }).Where(r => !string.IsNullOrEmpty(r.Code) && r.Date != DateTime.MinValue && r.Price > 0);

        // Group by item code, pick most recent row per code
        var latestByCode = rows
            .GroupBy(r => r.Code)
            .Select(g => g.OrderByDescending(r => r.Date).First())
            .ToList();

        int updated = 0, skipped = 0;
        var unmatched = new List<string>();

        foreach (var entry in latestByCode)
        {
            var item = await db.Items.FirstOrDefaultAsync(i => i.Code == entry.Code && i.BranchId == branchId);
            if (item is null)
            {
                unmatched.Add(entry.Code);
                skipped++;
                continue;
            }
            item.LastPurchasePrice = entry.Price;
            updated++;
        }

        await db.SaveChangesAsync();
        return new LedgerImportSummary(updated, skipped, unmatched);
    }
}
```

- [ ] **Step 2:** Register in `Program.cs` alongside other services:

```csharp
builder.Services.AddScoped<PurchaseLedgerService>();
```

- [ ] **Step 3:** Build. Passes.

- [ ] **Step 4:** Commit.

```bash
git add BomPriceApproval.API/Infrastructure/Services/PurchaseLedgerService.cs BomPriceApproval.API/Program.cs
git commit -m "feat(api): PurchaseLedgerService — extract headers + mapped import"
```

---

### Task 20: Update ItemImportController — add template + ledger endpoints

**Files:**
- Modify: `BomPriceApproval.API/Features/Items/ItemImportController.cs`

- [ ] **Step 1:** Replace the file:

```csharp
using BomPriceApproval.API.Features.Items;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BomPriceApproval.API.Features.Items;

[ApiController]
[Route("api/items/import")]
[Authorize(Roles = "Admin")]
public class ItemImportController(
    ItemImportService importService,
    PurchaseLedgerService ledgerService) : ControllerBase
{
    [HttpGet("template")]
    public IActionResult Template()
    {
        var bytes = ItemImportService.GenerateTemplate();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "items-template.xlsx");
    }

    [HttpPost]
    public async Task<IActionResult> Import([FromForm] IFormFile file, [FromForm] int branchId)
    {
        if (file.Length == 0) return BadRequest(new { message = "File is empty" });
        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext is not (".xlsx" or ".csv"))
            return BadRequest(new { message = "Only .xlsx and .csv files are supported" });

        using var stream = file.OpenReadStream();
        var result = ext == ".xlsx"
            ? await importService.ImportExcelAsync(stream, branchId)
            : await importService.ImportCsvAsync(stream, branchId);
        return Ok(result);
    }

    [HttpPost("ledger/headers")]
    public IActionResult LedgerHeaders([FromForm] IFormFile file)
    {
        if (file.Length == 0) return BadRequest(new { message = "File is empty" });
        if (Path.GetExtension(file.FileName).ToLower() != ".xlsx")
            return BadRequest(new { message = "Only .xlsx files are supported for ledger import" });

        using var stream = file.OpenReadStream();
        var headers = ledgerService.ExtractHeaders(stream);
        return Ok(new LedgerHeadersResponse(headers));
    }

    [HttpPost("ledger")]
    public async Task<IActionResult> LedgerImport(
        [FromForm] IFormFile file,
        [FromForm] string itemCodeColumn,
        [FromForm] string dateColumn,
        [FromForm] string unitPriceColumn,
        [FromForm] int branchId)
    {
        if (file.Length == 0) return BadRequest(new { message = "File is empty" });
        if (Path.GetExtension(file.FileName).ToLower() != ".xlsx")
            return BadRequest(new { message = "Only .xlsx files are supported for ledger import" });

        using var stream = file.OpenReadStream();
        try
        {
            var result = await ledgerService.ImportAsync(stream, itemCodeColumn, dateColumn, unitPriceColumn, branchId);
            return Ok(new LedgerImportResult(result.Updated, result.Skipped, result.UnmatchedCodes));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
```

- [ ] **Step 2:** Build. Passes.

- [ ] **Step 3:** Commit.

```bash
git add BomPriceApproval.API/Features/Items/ItemImportController.cs
git commit -m "feat(api): item template + purchase ledger endpoints"
```

---

### Task 21: Test PurchaseLedgerService — headers + last-price aggregation

**Files:**
- Create: `BomPriceApproval.Tests/Items/PurchaseLedgerImportTests.cs`

- [ ] **Step 1:** Create the file:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Items;

public class PurchaseLedgerImportTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAdminAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = "admin@test.com", Password = "Admin@1234" });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private static byte[] BuildLedgerXlsx(params (string Code, DateTime Date, decimal Price)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Ledger");
        ws.Cell(1, 1).Value = "SKU";
        ws.Cell(1, 2).Value = "PurchaseDate";
        ws.Cell(1, 3).Value = "Rate";
        for (int i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].Code;
            ws.Cell(i + 2, 2).Value = rows[i].Date;
            ws.Cell(i + 2, 3).Value = rows[i].Price;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task LedgerHeaders_ReturnsColumnNames()
    {
        var token = await LoginAdminAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var bytes = BuildLedgerXlsx(("HDPE-20", new DateTime(2026, 1, 1), 12m));
        using var form = new MultipartFormDataContent();
        var fc = new ByteArrayContent(bytes);
        fc.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fc, "file", "ledger.xlsx");

        var resp = await _client.PostAsync("/api/items/import/ledger/headers", form);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<HeadersResponse>();
        body!.Headers.Should().BeEquivalentTo(new[] { "SKU", "PurchaseDate", "Rate" });
    }

    [Fact]
    public async Task LedgerImport_PicksMostRecentPricePerItem()
    {
        var token = await LoginAdminAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var bytes = BuildLedgerXlsx(
            ("HDPE-20", new DateTime(2026, 1, 1), 10m),
            ("HDPE-20", new DateTime(2026, 3, 1), 15m),   // <-- most recent
            ("HDPE-20", new DateTime(2026, 2, 1), 12m),
            ("UNKNOWN", new DateTime(2026, 3, 5), 99m)
        );

        using var form = new MultipartFormDataContent();
        var fc = new ByteArrayContent(bytes);
        fc.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fc, "file", "ledger.xlsx");
        form.Add(new StringContent("SKU"), "itemCodeColumn");
        form.Add(new StringContent("PurchaseDate"), "dateColumn");
        form.Add(new StringContent("Rate"), "unitPriceColumn");
        form.Add(new StringContent("1"), "branchId");

        var resp = await _client.PostAsync("/api/items/import/ledger", form);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await resp.Content.ReadFromJsonAsync<ImportSummary>();
        result!.Updated.Should().Be(1);
        result.Skipped.Should().Be(1);
        result.UnmatchedCodes.Should().Contain("UNKNOWN");

        // Verify the saved LastPurchasePrice
        var items = await _client.GetFromJsonAsync<List<ItemDto>>("/api/items");
        items!.First(i => i.Code == "HDPE-20").LastPurchasePrice.Should().Be(15m);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record HeadersResponse(List<string> Headers);
    private record ImportSummary(int Updated, int Skipped, List<string> UnmatchedCodes);
    private record ItemDto(int Id, string Code, string Description, string Type, int BranchId, bool IsActive, decimal? LastPurchasePrice);
}
```

- [ ] **Step 2:** Run tests:

```bash
dotnet test --filter "FullyQualifiedName~PurchaseLedgerImportTests"
```

Expected: both PASS.

- [ ] **Step 3:** Commit.

```bash
git add BomPriceApproval.Tests/Items/PurchaseLedgerImportTests.cs
git commit -m "test(api): purchase ledger — headers + most-recent-price per item"
```

---

### Task 22: Frontend — item types + API module

**Files:**
- Modify: `bom-web/src/types/api.ts`
- Create: `bom-web/src/features/items/itemsApi.ts`

- [ ] **Step 1:** In `types/api.ts`, extend/add the Item types (if `Item` already exists from the lookups module, make sure it has `lastPurchasePrice`):

```typescript
export interface Item {
  id: number;
  code: string;
  description: string;
  type: "FinishedGood" | "RawMaterial";
  branchId: number;
  isActive: boolean;
  lastPurchasePrice: number | null;
}

export interface CreateItemRequest {
  code: string;
  description: string;
  type: "FinishedGood" | "RawMaterial";
  lastPurchasePrice: number | null;
}

export interface LedgerHeadersResponse { headers: string[]; }
export interface LedgerImportResult {
  updated: number;
  skipped: number;
  unmatchedCodes: string[];
}
```

- [ ] **Step 2:** Create `itemsApi.ts`:

```typescript
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type {
  Item, CreateItemRequest, ImportResult, LedgerHeadersResponse, LedgerImportResult
} from "@/types/api";

export const itemKeys = {
  all: ["items"] as const,
  list: () => [...itemKeys.all, "list"] as const,
};

export function useItems() {
  return useQuery({
    queryKey: itemKeys.list(),
    queryFn: () => api.get<Item[]>("/items").then((r) => r.data),
  });
}

export function useCreateItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateItemRequest) =>
      api.post<Item>("/items", body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: itemKeys.all }),
  });
}

export function useImportItems() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ file, branchId }: { file: File; branchId: number }) => {
      const fd = new FormData();
      fd.append("file", file);
      fd.append("branchId", String(branchId));
      return api.post<ImportResult>("/items/import", fd, {
        headers: { "Content-Type": "multipart/form-data" },
      }).then((r) => r.data);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: itemKeys.all }),
  });
}

export function downloadItemTemplate() {
  return api.get("/items/import/template", { responseType: "blob" }).then((r) => {
    const url = window.URL.createObjectURL(r.data as Blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "items-template.xlsx";
    a.click();
    window.URL.revokeObjectURL(url);
  });
}

export function useLedgerHeaders() {
  return useMutation({
    mutationFn: (file: File) => {
      const fd = new FormData();
      fd.append("file", file);
      return api.post<LedgerHeadersResponse>("/items/import/ledger/headers", fd, {
        headers: { "Content-Type": "multipart/form-data" },
      }).then((r) => r.data);
    },
  });
}

export function useLedgerImport() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (args: {
      file: File;
      itemCodeColumn: string;
      dateColumn: string;
      unitPriceColumn: string;
      branchId: number;
    }) => {
      const fd = new FormData();
      fd.append("file", args.file);
      fd.append("itemCodeColumn", args.itemCodeColumn);
      fd.append("dateColumn", args.dateColumn);
      fd.append("unitPriceColumn", args.unitPriceColumn);
      fd.append("branchId", String(args.branchId));
      return api.post<LedgerImportResult>("/items/import/ledger", fd, {
        headers: { "Content-Type": "multipart/form-data" },
      }).then((r) => r.data);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: itemKeys.all }),
  });
}
```

- [ ] **Step 3:** Commit.

```bash
git add bom-web/src/types/api.ts bom-web/src/features/items/itemsApi.ts
git commit -m "feat(web): item types and API hooks (CRUD + import + ledger)"
```

---

### Task 23: AddItemModal component

**Files:**
- Create: `bom-web/src/features/items/components/AddItemModal.tsx`

- [ ] **Step 1:** Create the file:

```tsx
import { useState } from "react";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { useCreateItem } from "../itemsApi";

interface Props { open: boolean; onOpenChange: (open: boolean) => void; }

export function AddItemModal({ open, onOpenChange }: Props) {
  const createItem = useCreateItem();
  const [code, setCode] = useState("");
  const [description, setDescription] = useState("");
  const [type, setType] = useState<"FinishedGood" | "RawMaterial">("FinishedGood");
  const [price, setPrice] = useState("");
  const [error, setError] = useState<string | null>(null);

  const reset = () => { setCode(""); setDescription(""); setType("FinishedGood"); setPrice(""); setError(null); };

  const submit = async () => {
    setError(null);
    if (!code.trim() || !description.trim()) { setError("Code and Description are required."); return; }
    try {
      await createItem.mutateAsync({
        code, description, type,
        lastPurchasePrice: price ? Number(price) : null
      });
      reset();
      onOpenChange(false);
    } catch (e: unknown) {
      const err = e as { response?: { data?: { message?: string } } };
      setError(err.response?.data?.message ?? "Failed to create item");
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader><DialogTitle>Add Item</DialogTitle></DialogHeader>
        <div className="space-y-3">
          <div><Label>Code *</Label><Input value={code} onChange={(e) => setCode(e.target.value)} /></div>
          <div><Label>Description *</Label><Input value={description} onChange={(e) => setDescription(e.target.value)} /></div>
          <div>
            <Label>Type *</Label>
            <select className="w-full rounded border bg-background p-2"
              value={type} onChange={(e) => setType(e.target.value as "FinishedGood" | "RawMaterial")}>
              <option value="FinishedGood">Finished Good</option>
              <option value="RawMaterial">Raw Material</option>
            </select>
          </div>
          <div>
            <Label>Last Purchase Price</Label>
            <Input type="number" step="0.0001" value={price} onChange={(e) => setPrice(e.target.value)} />
          </div>
          {error && <p className="text-destructive text-sm">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={() => { reset(); onOpenChange(false); }}>Cancel</Button>
          <Button onClick={submit} disabled={createItem.isPending}>
            {createItem.isPending ? "Saving..." : "Save"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
```

- [ ] **Step 2:** Commit.

```bash
git add bom-web/src/features/items/components/AddItemModal.tsx
git commit -m "feat(web): AddItemModal"
```

---

### Task 24: ImportItemsModal component

**Files:**
- Create: `bom-web/src/features/items/components/ImportItemsModal.tsx`

- [ ] **Step 1:** Create the file (mirrors `ImportCustomersModal` but includes branch selection):

```tsx
import { useState } from "react";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { useImportItems, downloadItemTemplate } from "../itemsApi";
import type { ImportResult } from "@/types/api";

interface Props { open: boolean; onOpenChange: (open: boolean) => void; branches: { id: number; name: string }[]; }

export function ImportItemsModal({ open, onOpenChange, branches }: Props) {
  const importMutation = useImportItems();
  const [file, setFile] = useState<File | null>(null);
  const [branchId, setBranchId] = useState<number>(branches[0]?.id ?? 1);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const close = () => { setFile(null); setResult(null); setError(null); onOpenChange(false); };

  const submit = async () => {
    if (!file) return;
    setError(null);
    try {
      const r = await importMutation.mutateAsync({ file, branchId });
      setResult(r);
    } catch (e: unknown) {
      const err = e as { response?: { data?: { message?: string } } };
      setError(err.response?.data?.message ?? "Import failed");
    }
  };

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) close(); }}>
      <DialogContent>
        <DialogHeader><DialogTitle>Import Items</DialogTitle></DialogHeader>
        <div className="space-y-4">
          <div>
            <Label>Branch</Label>
            <select className="w-full rounded border bg-background p-2"
              value={branchId} onChange={(e) => setBranchId(Number(e.target.value))}>
              {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
            </select>
          </div>
          <Button variant="ghost" onClick={() => downloadItemTemplate()}>Download template</Button>
          <div>
            <Input type="file" accept=".xlsx,.csv"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
          </div>
          {result && (
            <div className="rounded border p-3 text-sm">
              <p>{result.imported} imported, {result.skipped} skipped</p>
              {result.errors.length > 0 && (
                <ul className="list-disc pl-4 mt-2 text-destructive">
                  {result.errors.map((er, i) => <li key={i}>{er}</li>)}
                </ul>
              )}
            </div>
          )}
          {error && <p className="text-destructive text-sm">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={close}>Close</Button>
          <Button onClick={submit} disabled={!file || importMutation.isPending}>
            {importMutation.isPending ? "Importing..." : "Import"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
```

- [ ] **Step 2:** Commit.

```bash
git add bom-web/src/features/items/components/ImportItemsModal.tsx
git commit -m "feat(web): ImportItemsModal"
```

---

### Task 25: ImportLedgerModal — 3-step column mapping

**Files:**
- Create: `bom-web/src/features/items/components/ImportLedgerModal.tsx`

- [ ] **Step 1:** Create the file:

```tsx
import { useState } from "react";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { useLedgerHeaders, useLedgerImport } from "../itemsApi";
import type { LedgerImportResult } from "@/types/api";

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  branches: { id: number; name: string }[];
}

type Step = 1 | 2 | 3;

export function ImportLedgerModal({ open, onOpenChange, branches }: Props) {
  const headersMutation = useLedgerHeaders();
  const importMutation = useLedgerImport();

  const [step, setStep] = useState<Step>(1);
  const [file, setFile] = useState<File | null>(null);
  const [branchId, setBranchId] = useState<number>(branches[0]?.id ?? 1);
  const [headers, setHeaders] = useState<string[]>([]);
  const [codeCol, setCodeCol] = useState("");
  const [dateCol, setDateCol] = useState("");
  const [priceCol, setPriceCol] = useState("");
  const [result, setResult] = useState<LedgerImportResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reset = () => {
    setStep(1); setFile(null); setHeaders([]); setCodeCol(""); setDateCol("");
    setPriceCol(""); setResult(null); setError(null);
  };
  const close = () => { reset(); onOpenChange(false); };

  const goToMapping = async () => {
    if (!file) return;
    setError(null);
    try {
      const r = await headersMutation.mutateAsync(file);
      setHeaders(r.headers);
      if (r.headers.length > 0) {
        setCodeCol(r.headers[0]);
        setDateCol(r.headers[Math.min(1, r.headers.length - 1)]);
        setPriceCol(r.headers[Math.min(2, r.headers.length - 1)]);
      }
      setStep(2);
    } catch (e: unknown) {
      const err = e as { response?: { data?: { message?: string } } };
      setError(err.response?.data?.message ?? "Failed to read headers");
    }
  };

  const runImport = async () => {
    if (!file || !codeCol || !dateCol || !priceCol) return;
    setError(null);
    try {
      const r = await importMutation.mutateAsync({
        file, itemCodeColumn: codeCol, dateColumn: dateCol,
        unitPriceColumn: priceCol, branchId
      });
      setResult(r);
      setStep(3);
    } catch (e: unknown) {
      const err = e as { response?: { data?: { message?: string } } };
      setError(err.response?.data?.message ?? "Import failed");
    }
  };

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) close(); }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Import from Purchase Ledger (Step {step} of 3)</DialogTitle>
        </DialogHeader>

        {step === 1 && (
          <div className="space-y-3">
            <div>
              <Label>Branch</Label>
              <select className="w-full rounded border bg-background p-2"
                value={branchId} onChange={(e) => setBranchId(Number(e.target.value))}>
                {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
              </select>
            </div>
            <div>
              <Label>ERP Excel file (.xlsx)</Label>
              <Input type="file" accept=".xlsx"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
            </div>
          </div>
        )}

        {step === 2 && (
          <div className="space-y-3">
            <p className="text-sm">Map the columns from your file:</p>
            {([
              ["Item Code column", codeCol, setCodeCol],
              ["Date column", dateCol, setDateCol],
              ["Unit Price column", priceCol, setPriceCol],
            ] as const).map(([label, value, setter]) => (
              <div key={label}>
                <Label>{label}</Label>
                <select className="w-full rounded border bg-background p-2"
                  value={value} onChange={(e) => setter(e.target.value)}>
                  {headers.map((h) => <option key={h} value={h}>{h}</option>)}
                </select>
              </div>
            ))}
          </div>
        )}

        {step === 3 && result && (
          <div className="space-y-3">
            <p>{result.updated} items updated, {result.skipped} skipped</p>
            {result.unmatchedCodes.length > 0 && (
              <div>
                <p className="text-sm font-semibold">Codes not found in system:</p>
                <ul className="list-disc pl-4 text-sm">
                  {result.unmatchedCodes.slice(0, 20).map((c) => <li key={c}>{c}</li>)}
                  {result.unmatchedCodes.length > 20 && <li>…and {result.unmatchedCodes.length - 20} more</li>}
                </ul>
              </div>
            )}
          </div>
        )}

        {error && <p className="text-destructive text-sm">{error}</p>}

        <DialogFooter>
          {step === 1 && (
            <>
              <Button variant="ghost" onClick={close}>Cancel</Button>
              <Button onClick={goToMapping} disabled={!file || headersMutation.isPending}>
                {headersMutation.isPending ? "Reading..." : "Next"}
              </Button>
            </>
          )}
          {step === 2 && (
            <>
              <Button variant="ghost" onClick={() => setStep(1)}>Back</Button>
              <Button onClick={runImport} disabled={importMutation.isPending}>
                {importMutation.isPending ? "Importing..." : "Import"}
              </Button>
            </>
          )}
          {step === 3 && <Button onClick={close}>Done</Button>}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
```

- [ ] **Step 2:** Commit.

```bash
git add bom-web/src/features/items/components/ImportLedgerModal.tsx
git commit -m "feat(web): ImportLedgerModal — 3-step column mapping"
```

---

### Task 26: ItemListPage

**Files:**
- Create: `bom-web/src/features/items/ItemListPage.tsx`

- [ ] **Step 1:** Create the file:

```tsx
import { useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { useItems } from "./itemsApi";
import { AddItemModal } from "./components/AddItemModal";
import { ImportItemsModal } from "./components/ImportItemsModal";
import { ImportLedgerModal } from "./components/ImportLedgerModal";
import { DataTable } from "@/components/ui/DataTable";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { useAuthStore } from "@/store/authStore";
import { useBranches } from "@/api/lookups";
import type { Item } from "@/types/api";

const columns: ColumnDef<Item>[] = [
  { accessorKey: "code", header: "Code", cell: (i) => <span className="font-mono text-xs">{i.getValue() as string}</span> },
  { accessorKey: "description", header: "Description" },
  { accessorKey: "type", header: "Type" },
  {
    accessorKey: "lastPurchasePrice",
    header: "Last Purchase Price",
    cell: (i) => {
      const v = i.getValue() as number | null;
      return v == null ? "—" : v.toFixed(4);
    },
  },
  { accessorKey: "isActive", header: "Active", cell: (i) => ((i.getValue() as boolean) ? "Yes" : "No") },
];

export default function ItemListPage() {
  const role = useAuthStore((s) => s.user?.role);
  const { data, isLoading, isError, refetch } = useItems();
  const { data: branches = [] } = useBranches();

  const [addOpen, setAddOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [ledgerOpen, setLedgerOpen] = useState(false);

  const canAdd = role === "SalesPerson" || role === "Admin";
  const canImport = role === "Admin";

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">Items</h1>
        <div className="flex gap-2">
          {canImport && <Button variant="outline" onClick={() => setLedgerOpen(true)}>Import from Ledger</Button>}
          {canImport && <Button variant="outline" onClick={() => setImportOpen(true)}>Import</Button>}
          {canAdd && <Button onClick={() => setAddOpen(true)}>Add Item</Button>}
        </div>
      </div>

      {isError && (
        <Card>
          <CardContent className="flex items-center justify-between">
            <p className="text-destructive">Failed to load items.</p>
            <Button variant="ghost" onClick={() => refetch()}>Retry</Button>
          </CardContent>
        </Card>
      )}

      <DataTable columns={columns} data={data ?? []} isLoading={isLoading} emptyState={<p>No items yet.</p>} />

      <AddItemModal open={addOpen} onOpenChange={setAddOpen} />
      {canImport && <ImportItemsModal open={importOpen} onOpenChange={setImportOpen} branches={branches} />}
      {canImport && <ImportLedgerModal open={ledgerOpen} onOpenChange={setLedgerOpen} branches={branches} />}
    </div>
  );
}
```

- [ ] **Step 2:** Confirm `useBranches()` exists in `src/api/lookups.ts`. If it doesn't, open `lookups.ts` and add a hook that calls `GET /api/branches` returning `{id, name}[]`. The endpoint already exists (see `BranchesController.cs`).

- [ ] **Step 3:** Commit.

```bash
git add bom-web/src/features/items/ItemListPage.tsx
git commit -m "feat(web): ItemListPage"
```

---

### Task 27: Wire items route + sidebar link

**Files:**
- Modify: `bom-web/src/App.tsx`
- Modify: `bom-web/src/components/layout/AppShell.tsx`

- [ ] **Step 1:** In `App.tsx`, import and add the route alongside the `/customers` route:

```tsx
import ItemListPage from "@/features/items/ItemListPage";
```

```tsx
{
  path: "items",
  element: (
    <ProtectedRoute allow={["Admin", "SalesPerson", "BomCreator", "Accountant", "ManagingDirector"]}>
      <ItemListPage />
    </ProtectedRoute>
  ),
},
```

- [ ] **Step 2:** In `AppShell.tsx`, add a sidebar link for Items matching the style of Customers/Requisitions.

```tsx
<NavLink to="/items" className={navClass}>Items</NavLink>
```

- [ ] **Step 3:** Manual smoke test: `npm run dev`, log in as Admin → see Items page with all three buttons. Log in as SalesPerson → only "Add Item" visible. Log in as BomCreator → no buttons, just the list.

- [ ] **Step 4:** Commit.

```bash
git add bom-web/src/App.tsx bom-web/src/components/layout/AppShell.tsx
git commit -m "feat(web): wire /items route and sidebar link"
```

---

### Task 28: Test ItemListPage — role-gated buttons + ledger button visibility

**Files:**
- Create: `bom-web/src/features/items/ItemListPage.test.tsx`

- [ ] **Step 1:** Create the file, copying setup from `CustomerListPage.test.tsx`:

```tsx
// Key assertions only — setup mirrors CustomerListPage.test.tsx

it("shows Add, Import, Import from Ledger for Admin", async () => {
  // set auth to Admin
  render(<ItemListPage />, { wrapper });
  expect(await screen.findByRole("button", { name: /add item/i })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /^import$/i })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /import from ledger/i })).toBeInTheDocument();
});

it("shows only Add Item for SalesPerson", async () => {
  // set auth to SalesPerson
  render(<ItemListPage />, { wrapper });
  expect(await screen.findByRole("button", { name: /add item/i })).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /^import$/i })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /import from ledger/i })).not.toBeInTheDocument();
});
```

- [ ] **Step 2:** Run tests.

```bash
cd bom-web && npm test -- ItemListPage
```

Expected: PASS.

- [ ] **Step 3:** Commit.

```bash
git add bom-web/src/features/items/ItemListPage.test.tsx
git commit -m "test(web): ItemListPage role-gated buttons"
```

---

### Task 29: Final verification — full test suite passes

- [ ] **Step 1:** Run backend tests.

```bash
dotnet test
```

Expected: all green.

- [ ] **Step 2:** Run frontend tests.

```bash
cd bom-web && npm test -- --run
```

Expected: all green.

- [ ] **Step 3:** Build frontend production bundle.

```bash
cd bom-web && npm run build
```

Expected: clean build.

- [ ] **Step 4:** Manual smoke test as Admin:
  1. Login at `/login` with admin@test.com / Admin@1234
  2. Navigate to `/customers` — add a customer, import a csv/xlsx
  3. Navigate to `/items` — add an item, import a csv/xlsx, import a purchase ledger (use mapping flow)
  4. Verify LastPurchasePrice shows in item table after ledger import

- [ ] **Step 5:** No commit needed (all work already committed).

---

## Notes / Out of Scope

- **Pagination / search for customer and item lists** — deferred; current lists return all records for the user's scope.
- **Editing customers/items inline from the list view** — only "Add" is in scope for this plan. Existing Update endpoints remain.
- **CSV support for purchase ledger** — only `.xlsx` is supported for ledger imports (ClosedXML-based). CSV can be added later.
- **Legacy customer `Code` backfill correction** — the migration writes `LEGACY-{Id}` placeholders; manual correction is out of scope.
