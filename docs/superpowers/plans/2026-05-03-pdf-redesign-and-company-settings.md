# PDF Redesign (Letterhead Classic) + Admin Company Settings — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current PDF style with a Letterhead Classic layout, and move company-level fields (name, address, tel, TRN, email, website, validity, T&C) into a new admin-editable `CompanySettings` singleton.

**Architecture:** New `CompanySettings` table (singleton, id=1) seeded from current PdfService hardcodes. New `AdminCompanySettingsController` (GET + PUT, audited via existing `AdminAuditLogger`). `PdfService.GenerateQuotationAsync` rewritten to read settings + render new layout. New `/admin/company-settings` web page (Admin only).

**Tech Stack:** ASP.NET Core 8 + EF Core 8 + Npgsql + QuestPDF (backend); React 19 + Vite + TanStack Query + Tailwind + vitest (web).

**Spec:** [docs/superpowers/specs/2026-05-03-pdf-redesign-and-company-settings-design.md](../specs/2026-05-03-pdf-redesign-and-company-settings-design.md)

**Branch:** `feat/pdf-redesign-and-company-settings` off `master`. Create at start: `git checkout -b feat/pdf-redesign-and-company-settings master`.

**Operational notes:**
- Build/test with `--configuration Release` if local API process holds Debug DLL locks.
- Tests use Guid-isolated throwaway entities (no mutation of seeded users like `ali@`/`sara@`).
- Use `using var scope = factory.Services.CreateScope();` for per-test DbContext access.

---

## File Structure

### Backend — `BomPriceApproval.API/`

**Created:**
- `Domain/Entities/CompanySettings.cs` — singleton settings entity
- `Infrastructure/Data/Migrations/<TIMESTAMP>_AddCompanySettings.cs` — EF migration with seed
- `Features/Admin/AdminCompanySettingsController.cs` — GET + PUT
- `Features/Admin/CompanySettingsDtos.cs` — request/response records

**Modified:**
- `Domain/Enums/AdminActionType.cs` — append `UpdateCompanySettings`
- `Infrastructure/Data/AppDbContext.cs` — add `DbSet<CompanySettings>`, OnModelCreating config + seed
- `Infrastructure/Services/PdfService.cs` — wholesale rewrite of `GenerateQuotationAsync`

### Backend tests — `BomPriceApproval.Tests/`

**Created:**
- `Admin/CompanySettingsTests.cs` — GET/PUT/auth/validation (6 tests)

### Web — `bom-web/`

**Created:**
- `src/api/companySettings.ts` — typed hooks (`useCompanySettings`, `useUpdateCompanySettings`)
- `src/features/admin/company-settings/CompanySettingsPage.tsx`
- `src/features/admin/company-settings/CompanySettingsPage.test.tsx`

**Modified:**
- `src/App.tsx` — register `/admin/company-settings` route
- `src/components/layout/Sidebar.tsx` — add nav link "Company Settings" (Admin only)
- `src/api/admin.ts` — add `"UpdateCompanySettings"` to `AdminActionType` union
- `src/features/admin/audit-log/AuditLogPage.tsx` — add `UpdateCompanySettings: "Update Company Settings"` to `ACTION_TYPE_LABELS` + push to `ACTION_TYPES` array

### Docs

**Modified:**
- `CLAUDE.md` — short paragraph under V3 Workflow section documenting CompanySettings + admin endpoints.

---

## Task 1: Add `CompanySettings` entity + enum value + DbContext config

**Files:**
- Create: `BomPriceApproval.API/Domain/Entities/CompanySettings.cs`
- Modify: `BomPriceApproval.API/Domain/Enums/AdminActionType.cs`
- Modify: `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs`

- [ ] **Step 1: Create CompanySettings entity**

Create `Domain/Entities/CompanySettings.cs`:

```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class CompanySettings
{
    public int Id { get; set; }                          // always 1 (singleton)
    public string CompanyName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string Trn { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public int QuotationValidityDays { get; set; } = 30;
    public string TermsAndConditions { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
}
```

- [ ] **Step 2: Append UpdateCompanySettings to AdminActionType**

Edit `Domain/Enums/AdminActionType.cs` — append `UpdateCompanySettings` after the existing values (do NOT reorder existing values; enum is stored as string but order should stay stable for readers):

```csharp
namespace BomPriceApproval.API.Domain.Enums;

public enum AdminActionType
{
    DeleteRequisition,
    RollbackStatus,
    ReassignSp,
    UnlockBom,
    UnlockCosting,
    ResetPassword,
    OverridePrices,
    HardDeleteCustomer,
    RollbackToCosting,
    V3CutoverMigration,
    UpdateCompanySettings
}
```

- [ ] **Step 3: Register CompanySettings in AppDbContext**

Edit `Infrastructure/Data/AppDbContext.cs`:

1. Add the DbSet near the other DbSet declarations (alphabetically near `BomCost`):

```csharp
    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();
```

2. In `OnModelCreating`, after the existing `mb.Entity<Branch>().HasData(...)` seed and before any other config blocks, add:

```csharp
        mb.Entity<CompanySettings>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.CompanyName).IsRequired();
            e.Property(s => s.Address).IsRequired();
            e.Property(s => s.Telephone).IsRequired();
            e.Property(s => s.Trn).IsRequired();
            e.Property(s => s.Email).IsRequired();
            e.Property(s => s.Website).IsRequired();
            e.Property(s => s.TermsAndConditions).IsRequired();
            e.Property(s => s.QuotationValidityDays).IsRequired();
            e.HasOne(s => s.UpdatedByUser)
             .WithMany()
             .HasForeignKey(s => s.UpdatedByUserId)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasData(new CompanySettings
            {
                Id = 1,
                CompanyName = "FUJAIRAH PLASTIC FACTORY",
                Address = "Fujairah, United Arab Emirates",
                Telephone = "",
                Trn = "",
                Email = "info@fujairahplastic.com",
                Website = "",
                QuotationValidityDays = 30,
                TermsAndConditions = string.Join("\n", new[]
                {
                    "This quotation is valid for 30 days from the date of issue.",
                    "Prices are subject to change without prior notice after the validity period.",
                    "Payment terms as per mutually agreed contract.",
                    "Delivery: Ex-Works Fujairah unless otherwise agreed in writing.",
                    "All disputes are subject to the jurisdiction of UAE courts."
                }),
                UpdatedAt = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                UpdatedByUserId = null
            });
        });
```

- [ ] **Step 4: Build to verify entity compiles**

Run: `dotnet build --nologo -v q`
Expected: Build succeeds, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Domain/Entities/CompanySettings.cs \
        BomPriceApproval.API/Domain/Enums/AdminActionType.cs \
        BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs
git commit -m "feat(api): add CompanySettings singleton entity + UpdateCompanySettings audit type"
```

---

## Task 2: Generate + apply EF migration

**Files:**
- Create (auto-generated): `BomPriceApproval.API/Infrastructure/Data/Migrations/<TIMESTAMP>_AddCompanySettings.cs` + `.Designer.cs`
- Modify (auto-generated): `BomPriceApproval.API/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs`

- [ ] **Step 1: Verify no migration name collision**

Run: `ls "BomPriceApproval.API/Infrastructure/Data/Migrations/" | grep -i CompanySettings`
Expected: no match (empty output).

- [ ] **Step 2: Generate the migration**

Run: `dotnet ef migrations add AddCompanySettings --project BomPriceApproval.API --output-dir Infrastructure/Data/Migrations`
Expected: outputs three lines like `Build started... Build succeeded... Done.`; creates `<TIMESTAMP>_AddCompanySettings.cs` + `.Designer.cs` and updates `AppDbContextModelSnapshot.cs`.

- [ ] **Step 3: Inspect the generated migration**

Open the new `<TIMESTAMP>_AddCompanySettings.cs` and verify:
- `Up()` creates `CompanySettings` table with all 11 columns + FK to `Users` (`UpdatedByUserId` nullable, OnDelete=SetNull).
- `Up()` has an `InsertData(table: "CompanySettings", ...)` block with the seed values from Task 1 Step 3.
- `Down()` drops the table.

If anything looks off (e.g. seed missing, wrong PK strategy), revert with `dotnet ef migrations remove --project BomPriceApproval.API` and revisit Task 1 Step 3.

- [ ] **Step 4: Apply the migration to local DB**

Run: `dotnet ef database update --project BomPriceApproval.API`
Expected: `Applying migration '<TIMESTAMP>_AddCompanySettings'. Done.`

- [ ] **Step 5: Verify the seeded row exists**

Run: `psql -h localhost -p 5433 -U postgres -d bom_price_approval -c 'SELECT "Id", "CompanyName", "QuotationValidityDays" FROM "CompanySettings";'` (provide password from user-secrets when prompted).
Expected: one row returned: `1 | FUJAIRAH PLASTIC FACTORY | 30`.

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Data/Migrations/
git commit -m "feat(api): EF migration AddCompanySettings (singleton + seed)"
```

---

## Task 3: Add CompanySettings DTOs + AdminCompanySettingsController (GET endpoint)

**Files:**
- Create: `BomPriceApproval.API/Features/Admin/CompanySettingsDtos.cs`
- Create: `BomPriceApproval.API/Features/Admin/AdminCompanySettingsController.cs`

- [ ] **Step 1: Create DTOs file**

Create `Features/Admin/CompanySettingsDtos.cs`:

```csharp
namespace BomPriceApproval.API.Features.Admin;

public record CompanySettingsDto(
    string CompanyName,
    string Address,
    string Telephone,
    string Trn,
    string Email,
    string Website,
    int QuotationValidityDays,
    string TermsAndConditions,
    DateTime UpdatedAt,
    string? UpdatedByName);

public record UpdateCompanySettingsRequest(
    string CompanyName,
    string Address,
    string Telephone,
    string Trn,
    string Email,
    string Website,
    int QuotationValidityDays,
    string TermsAndConditions,
    string Reason);
```

- [ ] **Step 2: Create AdminCompanySettingsController with GET only**

Create `Features/Admin/AdminCompanySettingsController.cs`:

```csharp
using System.Security.Claims;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Admin;

[ApiController]
[Route("api/admin/company-settings")]
[Authorize(Roles = "Admin")]
public class AdminCompanySettingsController(AppDbContext db, AdminAuditLogger audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CompanySettingsDto>> Get()
    {
        var s = await db.CompanySettings
            .AsNoTracking()
            .Include(x => x.UpdatedByUser)
            .FirstOrDefaultAsync(x => x.Id == 1);
        if (s is null) return NotFound();

        return Ok(new CompanySettingsDto(
            s.CompanyName, s.Address, s.Telephone, s.Trn,
            s.Email, s.Website, s.QuotationValidityDays,
            s.TermsAndConditions, s.UpdatedAt,
            s.UpdatedByUser?.Name));
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
}
```

- [ ] **Step 3: Build to verify controller compiles**

Run: `dotnet build --nologo -v q`
Expected: Build succeeds.

- [ ] **Step 4: Manual smoke — start API and hit the endpoint**

Start API in a terminal: `dotnet run --project BomPriceApproval.API`
In another terminal, get an admin token (use the existing seeded admin from `Program.cs` seed — typically `admin@bompriceapproval.com` / `Admin@123!`):

```bash
TOKEN=$(curl -s -X POST http://localhost:7300/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"Email":"admin@bompriceapproval.com","Password":"Admin@123!"}' | jq -r .accessToken)

curl -s http://localhost:7300/api/admin/company-settings \
  -H "Authorization: Bearer $TOKEN" | jq .
```

Expected JSON:
```json
{
  "companyName": "FUJAIRAH PLASTIC FACTORY",
  "address": "Fujairah, United Arab Emirates",
  "telephone": "",
  "trn": "",
  "email": "info@fujairahplastic.com",
  "website": "",
  "quotationValidityDays": 30,
  "termsAndConditions": "This quotation is valid for 30 days...",
  "updatedAt": "2026-05-03T00:00:00Z",
  "updatedByName": null
}
```

If admin password differs locally, look it up from the user list page or check the seed in `Program.cs`. Stop the API after smoke (`Ctrl+C`).

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Admin/CompanySettingsDtos.cs \
        BomPriceApproval.API/Features/Admin/AdminCompanySettingsController.cs
git commit -m "feat(api): add GET /api/admin/company-settings endpoint"
```

---

## Task 4: Add PUT endpoint with validation + audit

**Files:**
- Modify: `BomPriceApproval.API/Features/Admin/AdminCompanySettingsController.cs`

- [ ] **Step 1: Add the PUT method to the controller**

Add the following method **inside** `AdminCompanySettingsController`, between `Get()` and the `CurrentUserId` property:

```csharp
    [HttpPut]
    public async Task<ActionResult<CompanySettingsDto>> Put([FromBody] UpdateCompanySettingsRequest? body)
    {
        if (body is null)
            return Validation.Detail("Request body is required").Return();

        // Validation
        var v = Validation.Detail("Company settings update is invalid");
        var hasError = false;

        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Trim().Length < 5)
        {
            v.Field("Reason", "Reason is required (min 5 chars).");
            hasError = true;
        }
        if (string.IsNullOrWhiteSpace(body.CompanyName))
        {
            v.Field("CompanyName", "Company name is required.");
            hasError = true;
        }
        if (body.QuotationValidityDays < 1 || body.QuotationValidityDays > 365)
        {
            v.Field("QuotationValidityDays", "Validity must be between 1 and 365 days.");
            hasError = true;
        }
        if (!string.IsNullOrWhiteSpace(body.Email) && !body.Email.Contains('@'))
        {
            v.Field("Email", "Email must contain '@'.");
            hasError = true;
        }
        if (hasError) return v.Return();

        var s = await db.CompanySettings.FirstOrDefaultAsync(x => x.Id == 1);
        if (s is null) return NotFound();

        var before = new
        {
            s.CompanyName, s.Address, s.Telephone, s.Trn, s.Email, s.Website,
            s.QuotationValidityDays, s.TermsAndConditions
        };

        // Trim string fields; normalize T&C line endings to \n
        s.CompanyName = body.CompanyName.Trim();
        s.Address = (body.Address ?? "").Trim();
        s.Telephone = (body.Telephone ?? "").Trim();
        s.Trn = (body.Trn ?? "").Trim();
        s.Email = (body.Email ?? "").Trim();
        s.Website = (body.Website ?? "").Trim();
        s.QuotationValidityDays = body.QuotationValidityDays;
        s.TermsAndConditions = (body.TermsAndConditions ?? "")
            .Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        s.UpdatedAt = DateTime.UtcNow;
        s.UpdatedByUserId = CurrentUserId;

        var after = new
        {
            s.CompanyName, s.Address, s.Telephone, s.Trn, s.Email, s.Website,
            s.QuotationValidityDays, s.TermsAndConditions
        };

        audit.Log(CurrentUserId, AdminActionType.UpdateCompanySettings,
            "CompanySettings", 1, body.Reason.Trim(), before, after);
        await db.SaveChangesAsync();

        // Re-load with the updated user nav so the response carries the name.
        var updatedBy = await db.Users.AsNoTracking()
            .Where(u => u.Id == CurrentUserId)
            .Select(u => u.Name)
            .FirstOrDefaultAsync();

        return Ok(new CompanySettingsDto(
            s.CompanyName, s.Address, s.Telephone, s.Trn,
            s.Email, s.Website, s.QuotationValidityDays,
            s.TermsAndConditions, s.UpdatedAt, updatedBy));
    }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build --nologo -v q`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.API/Features/Admin/AdminCompanySettingsController.cs
git commit -m "feat(api): add PUT /api/admin/company-settings with audit + validation"
```

---

## Task 5: Backend integration tests for company-settings endpoints

**Files:**
- Create: `BomPriceApproval.Tests/Admin/CompanySettingsTests.cs`

- [ ] **Step 1: Create the test file with 6 tests**

Create `BomPriceApproval.Tests/Admin/CompanySettingsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Features.Admin;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.Tests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Admin;

public class CompanySettingsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> TokenAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = password });
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    private void AuthAs(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task Get_AsAdmin_ReturnsSeededSettings()
    {
        var token = await TokenAsync("admin@bompriceapproval.com", "Admin@123!");
        AuthAs(token);

        var resp = await _client.GetAsync("/api/admin/company-settings");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<CompanySettingsDto>();
        dto!.CompanyName.Should().Be("FUJAIRAH PLASTIC FACTORY");
        dto.QuotationValidityDays.Should().Be(30);
        dto.Email.Should().Be("info@fujairahplastic.com");
        dto.TermsAndConditions.Should().Contain("30 days");
    }

    [Fact]
    public async Task Get_AsNonAdmin_Returns403()
    {
        var token = await TokenAsync("sara@bompriceapproval.com", "Acc@1234");
        AuthAs(token);

        var resp = await _client.GetAsync("/api/admin/company-settings");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Put_AsAdmin_UpdatesAndAudits()
    {
        var token = await TokenAsync("admin@bompriceapproval.com", "Admin@123!");
        AuthAs(token);

        var uniqueReason = $"Test update {Guid.NewGuid():N}";
        var newName = $"FPF Test {Guid.NewGuid():N}";
        var body = new UpdateCompanySettingsRequest(
            CompanyName: newName,
            Address: "PO Box 1, Alain, UAE",
            Telephone: "+971 3 111 2222",
            Trn: "100000001",
            Email: "test@example.com",
            Website: "www.example.com",
            QuotationValidityDays: 14,
            TermsAndConditions: "Line one\nLine two",
            Reason: uniqueReason);

        var resp = await _client.PutAsJsonAsync("/api/admin/company-settings", body);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<CompanySettingsDto>();
        dto!.CompanyName.Should().Be(newName);
        dto.QuotationValidityDays.Should().Be(14);
        dto.UpdatedByName.Should().NotBeNullOrEmpty();

        // Verify audit row + restore original via cleanup
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var audit = await db.AdminAuditLogs
            .Where(a => a.EntityType == "CompanySettings" && a.Reason == uniqueReason)
            .FirstOrDefaultAsync();
        audit.Should().NotBeNull();
        audit!.ActionType.Should().Be(AdminActionType.UpdateCompanySettings);
        audit.EntityId.Should().Be(1);
        audit.BeforeJson.Should().Contain("FUJAIRAH PLASTIC FACTORY");
        audit.AfterJson.Should().Contain(newName);

        // Cleanup: restore original settings + remove audit row
        var s = await db.CompanySettings.FirstAsync(x => x.Id == 1);
        s.CompanyName = "FUJAIRAH PLASTIC FACTORY";
        s.Address = "Fujairah, United Arab Emirates";
        s.Telephone = "";
        s.Trn = "";
        s.Email = "info@fujairahplastic.com";
        s.Website = "";
        s.QuotationValidityDays = 30;
        s.TermsAndConditions = string.Join("\n", new[]
        {
            "This quotation is valid for 30 days from the date of issue.",
            "Prices are subject to change without prior notice after the validity period.",
            "Payment terms as per mutually agreed contract.",
            "Delivery: Ex-Works Fujairah unless otherwise agreed in writing.",
            "All disputes are subject to the jurisdiction of UAE courts."
        });
        s.UpdatedByUserId = null;
        db.AdminAuditLogs.Remove(audit);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Put_AsNonAdmin_Returns403()
    {
        var token = await TokenAsync("sara@bompriceapproval.com", "Acc@1234");
        AuthAs(token);

        var body = new UpdateCompanySettingsRequest(
            "X", "X", "X", "X", "x@x.com", "X", 30, "X", "valid reason");
        var resp = await _client.PutAsJsonAsync("/api/admin/company-settings", body);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public async Task Put_InvalidValidityDays_Returns400(int days)
    {
        var token = await TokenAsync("admin@bompriceapproval.com", "Admin@123!");
        AuthAs(token);

        var body = new UpdateCompanySettingsRequest(
            "FPF", "Addr", "Tel", "Trn", "e@e.com", "Web",
            days, "Terms", "valid reason");
        var resp = await _client.PutAsJsonAsync("/api/admin/company-settings", body);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        problem!.Errors.Should().ContainKey("QuotationValidityDays");
    }

    [Fact]
    public async Task Put_EmptyReason_Returns400()
    {
        var token = await TokenAsync("admin@bompriceapproval.com", "Admin@123!");
        AuthAs(token);

        var body = new UpdateCompanySettingsRequest(
            "FPF", "Addr", "Tel", "Trn", "e@e.com", "Web", 30, "Terms", "abc");
        var resp = await _client.PutAsJsonAsync("/api/admin/company-settings", body);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        problem!.Errors.Should().ContainKey("Reason");
    }

    [Fact]
    public async Task Put_EmptyCompanyName_Returns400()
    {
        var token = await TokenAsync("admin@bompriceapproval.com", "Admin@123!");
        AuthAs(token);

        var body = new UpdateCompanySettingsRequest(
            "", "Addr", "Tel", "Trn", "e@e.com", "Web", 30, "Terms", "valid reason");
        var resp = await _client.PutAsJsonAsync("/api/admin/company-settings", body);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        problem!.Errors.Should().ContainKey("CompanyName");
    }
}
```

- [ ] **Step 2: Verify backend is running**

Run: `curl -s http://localhost:7300/swagger/index.html >/dev/null && echo OK || echo "Backend not running - start it first"`
Expected: `OK`. If not, start it: `dotnet run --project BomPriceApproval.API` (in a separate terminal) and wait for `Now listening on...` message.

- [ ] **Step 3: Run the new tests**

Run: `dotnet test --filter "FullyQualifiedName~CompanySettingsTests"`
Expected: 8 tests passed (6 facts + 2 inline `[Theory]` rows + 1 = actually 8 total: `Get_AsAdmin`, `Get_AsNonAdmin`, `Put_AsAdmin_UpdatesAndAudits`, `Put_AsNonAdmin`, `Put_InvalidValidityDays(0)`, `Put_InvalidValidityDays(-1)`, `Put_InvalidValidityDays(366)`, `Put_EmptyReason`, `Put_EmptyCompanyName` = 9 actually; verify all green).

If any test fails, read the failure carefully — most common cause is a different admin password locally than `Admin@123!`. Adjust the credential constant in tests if needed.

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.Tests/Admin/CompanySettingsTests.cs
git commit -m "test(admin): integration tests for company-settings GET/PUT/auth/validation"
```

---

## Task 6: Rewrite PdfService.GenerateQuotationAsync (Letterhead Classic + read settings)

**Files:**
- Modify: `BomPriceApproval.API/Infrastructure/Services/PdfService.cs`

- [ ] **Step 1: Replace the entire PdfService.cs file**

This is a wholesale rewrite. Open `BomPriceApproval.API/Infrastructure/Services/PdfService.cs` and replace its entire contents with:

```csharp
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BomPriceApproval.API.Infrastructure.Services;

/// <summary>
/// Letterhead Classic quotation PDF generator. Reads admin-editable
/// CompanySettings (singleton row id=1) for letterhead text + validity + T&amp;C.
/// Salesperson email shown next to Bill-To. Single MD signature only — no
/// salesperson signature, no footer, no Notes section.
/// </summary>
public class PdfService(AppDbContext db)
{
    private const string BrandDark = "#1e3a8a";
    private const string Text      = "#0f172a";
    private const string Muted     = "#475569";
    private const string Faint     = "#94a3b8";

    public async Task<byte[]> GenerateQuotationAsync(
        QuotationRequest req,
        QuotationApproval approval,
        User? signer = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var fullReq = await db.QuotationRequests
            .AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.Branch)
            .Include(r => r.SalesPerson)
            .Include(r => r.Items.OrderBy(ri => ri.SortOrder))
                .ThenInclude(ri => ri.Item)
            .Include(r => r.Items)
                .ThenInclude(ri => ri.BomHeader!)
                    .ThenInclude(b => b.Cost!)
            .Include(r => r.Items)
                .ThenInclude(ri => ri.BomHeader!)
                    .ThenInclude(b => b.Lines)
            .FirstOrDefaultAsync(r => r.Id == req.Id) ?? req;

        var bomHeaderIds = fullReq.Items
            .Where(ri => ri.BomHeader != null)
            .Select(ri => ri.BomHeader!.Id)
            .ToList();
        var costLinesByHeader = bomHeaderIds.Count == 0
            ? new Dictionary<int, List<BomCostLine>>()
            : await db.Set<BomCostLine>()
                .AsNoTracking()
                .Where(cl => bomHeaderIds.Contains(cl.BomHeaderId))
                .GroupBy(cl => cl.BomHeaderId)
                .ToDictionaryAsync(g => g.Key, g => g.ToList());

        var fxByCurrency = await db.ExchangeRates
            .AsNoTracking()
            .Where(r => r.IsActive)
            .GroupBy(r => r.CurrencyCode)
            .Select(g => g.OrderByDescending(r => r.EffectiveDate).First())
            .ToDictionaryAsync(r => r.CurrencyCode, r => r.RateToAed);

        var settings = await db.CompanySettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == 1) ?? DefaultSettings();

        var approvalItemMap = approval.Items.ToDictionary(ai => ai.RequisitionItemId);
        var isV3 = approval.Items.Any(ai => ai.MarginPerKg.HasValue);

        var validUntil = approval.ApprovedAt.AddDays(settings.QuotationValidityDays);
        var termsList = (settings.TermsAndConditions ?? "")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(36);
                page.MarginVertical(32);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Times New Roman").FontColor(Text));

                page.Content().Column(col =>
                {
                    // ── LETTERHEAD ────────────────────────────────────────
                    col.Item().AlignCenter().Text(settings.CompanyName)
                        .Bold().FontSize(22).FontColor(BrandDark).LetterSpacing(0.04f);

                    if (!string.IsNullOrWhiteSpace(settings.Address))
                        col.Item().PaddingTop(4).AlignCenter().Text(settings.Address)
                            .FontSize(10).FontColor(Muted);

                    var contactParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(settings.Telephone)) contactParts.Add($"Tel: {settings.Telephone}");
                    if (!string.IsNullOrWhiteSpace(settings.Trn))       contactParts.Add($"TRN: {settings.Trn}");
                    if (!string.IsNullOrWhiteSpace(settings.Email))     contactParts.Add(settings.Email);
                    if (!string.IsNullOrWhiteSpace(settings.Website))   contactParts.Add(settings.Website);
                    if (contactParts.Count > 0)
                        col.Item().PaddingTop(2).AlignCenter()
                            .Text(string.Join("  ·  ", contactParts))
                            .FontSize(9.5f).FontColor(Muted);

                    col.Item().PaddingTop(10).LineHorizontal(2f).LineColor(BrandDark);

                    // ── TITLE ─────────────────────────────────────────────
                    col.Item().PaddingTop(14).AlignCenter().Text("SALES QUOTATION")
                        .Bold().FontSize(14).FontColor(Text).LetterSpacing(0.18f);
                    col.Item().PaddingTop(2).AlignCenter().Container().Width(160).LineHorizontal(0.5f).LineColor(Text);

                    // ── META STRIP ────────────────────────────────────────
                    col.Item().PaddingTop(10).PaddingBottom(6).BorderBottom(0.5f).BorderColor(Faint).Row(row =>
                    {
                        MetaPair(row.RelativeItem(), "Ref:", fullReq.RefNo);
                        MetaPair(row.RelativeItem(), "Date:", approval.ApprovedAt.ToString("dd MMM yyyy"));
                        MetaPair(row.RelativeItem(), "Valid Until:", validUntil.ToString("dd MMM yyyy"));
                        MetaPair(row.RelativeItem(), "Currency:", fullReq.CurrencyCode);
                    });

                    // ── BILL TO + SALES REP ───────────────────────────────
                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            PartyHeader(c, "Bill To");
                            c.Item().PaddingTop(2).Text(fullReq.Customer.Name)
                                .Bold().FontSize(12).FontColor(Text);
                            c.Item().PaddingTop(1).Text(fullReq.Customer.Code).FontSize(9.5f).FontColor(Muted);
                            if (!string.IsNullOrWhiteSpace(fullReq.Customer.Address))
                                c.Item().PaddingTop(1).Text(fullReq.Customer.Address).FontSize(9.5f).FontColor(Muted);
                            if (!string.IsNullOrWhiteSpace(fullReq.Customer.PhoneNumber))
                                c.Item().PaddingTop(1).Text($"Tel: {fullReq.Customer.PhoneNumber}").FontSize(9.5f).FontColor(Muted);
                            if (!string.IsNullOrWhiteSpace(fullReq.Customer.Email))
                                c.Item().PaddingTop(1).Text(fullReq.Customer.Email).FontSize(9.5f).FontColor(Muted);
                        });
                        row.ConstantItem(22);
                        row.RelativeItem().Column(c =>
                        {
                            PartyHeader(c, "Sales Representative");
                            c.Item().PaddingTop(2).Text(fullReq.SalesPerson?.Name ?? "—")
                                .Bold().FontSize(12).FontColor(Text);
                            c.Item().PaddingTop(1).Text("Sales Department").FontSize(9.5f).FontColor(Muted);
                            if (!string.IsNullOrWhiteSpace(fullReq.SalesPerson?.Email))
                                c.Item().PaddingTop(1).Text(fullReq.SalesPerson!.Email).FontSize(9.5f).FontColor(Muted);
                        });
                    });

                    // ── SALUTATION ────────────────────────────────────────
                    col.Item().PaddingTop(12).Text(
                        "Dear Sir/Madam, with reference to your enquiry, we are pleased to submit our quotation as below:")
                        .FontSize(10).FontColor(Text);

                    // ── ITEMS TABLE ───────────────────────────────────────
                    decimal grandTotal = 0;
                    col.Item().PaddingTop(10).Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(34);
                            cd.RelativeColumn(4);
                            cd.RelativeColumn(1.5f);
                            cd.RelativeColumn(1.7f);
                            cd.RelativeColumn(2);
                        });

                        ItemsTableHeader(t, "S.No");
                        ItemsTableHeader(t, "Description");
                        ItemsTableHeader(t, "Qty (kg)", alignRight: true);
                        ItemsTableHeader(t, $"Rate ({fullReq.CurrencyCode})", alignRight: true);
                        ItemsTableHeader(t, $"Amount ({fullReq.CurrencyCode})", alignRight: true);

                        var rowNum = 0;
                        foreach (var ri in fullReq.Items.OrderBy(i => i.SortOrder))
                        {
                            if (!approvalItemMap.TryGetValue(ri.Id, out var ai)) continue;
                            rowNum++;

                            var unitPrice = isV3
                                ? ComputeV3PricePerKg(ri, ai, approval, fullReq.CurrencyCode,
                                    fullReq.ExchangeRateSnapshot, costLinesByHeader, fxByCurrency)
                                : (fullReq.CurrencyCode == "AED"
                                    ? ai.SalesPricePerKgAed
                                    : ai.SalesPricePerKgForeign ?? ai.SalesPricePerKgAed);

                            var lineTotal = unitPrice * ri.ExpectedQty;
                            grandTotal += lineTotal;

                            ItemsTableCell(t, rowNum.ToString());
                            ItemsTableCell(t, ri.Item.Description);
                            ItemsTableCell(t, ri.ExpectedQty.ToString("N0"), alignRight: true);
                            ItemsTableCell(t, unitPrice.ToString("N4"), alignRight: true);
                            ItemsTableCell(t, lineTotal.ToString("N2"), alignRight: true);
                        }
                    });

                    // ── TOTAL ─────────────────────────────────────────────
                    col.Item().PaddingTop(2)
                        .BorderTop(1.5f).BorderBottom(1.5f).BorderColor(BrandDark)
                        .PaddingVertical(8).PaddingHorizontal(6).Row(row =>
                    {
                        row.RelativeItem().Text("TOTAL AMOUNT")
                            .Bold().FontSize(12).FontColor(BrandDark);
                        row.ConstantItem(160).AlignRight()
                            .Text($"{fullReq.CurrencyCode}  {grandTotal:N2}")
                            .Bold().FontSize(12).FontColor(BrandDark);
                    });

                    // Exchange rate disclosure (non-AED)
                    if (fullReq.CurrencyCode != "AED")
                    {
                        var rate = approval.RateSnapshot ?? fullReq.ExchangeRateSnapshot;
                        if (rate is decimal r)
                        {
                            col.Item().PaddingTop(4).AlignRight()
                                .Text($"Exchange Rate: 1 {fullReq.CurrencyCode} = {r:N4} AED  (as of {approval.ApprovedAt:dd MMM yyyy})")
                                .FontSize(9).FontColor(Muted).Italic();
                        }
                    }

                    // ── TERMS & CONDITIONS ────────────────────────────────
                    if (termsList.Count > 0)
                    {
                        col.Item().PaddingTop(16).Text("Terms & Conditions:")
                            .Bold().FontSize(10).FontColor(Text).Underline();
                        col.Item().PaddingTop(4).Column(tc =>
                        {
                            for (int i = 0; i < termsList.Count; i++)
                            {
                                tc.Item().PaddingBottom(2).Text($"{i + 1}. {termsList[i]}")
                                    .FontSize(9.5f).FontColor("#334155");
                            }
                        });
                    }

                    // ── SIGNATURE (MD only, right-aligned) ────────────────
                    col.Item().PaddingTop(28).AlignRight().Width(220).Column(sig =>
                    {
                        sig.Item().Height(48).AlignCenter().Element(box =>
                        {
                            if (signer?.SignatureImage is { Length: > 0 })
                                box.Image(signer.SignatureImage).FitArea();
                            else
                                box.AlignBottom().AlignCenter().Text("(signature pending)")
                                    .FontSize(9).FontColor(Faint).Italic();
                        });
                        sig.Item().PaddingTop(2).LineHorizontal(0.5f).LineColor(Text);
                        sig.Item().PaddingTop(4).AlignCenter()
                            .Text($"For {settings.CompanyName}").Bold().FontSize(11).FontColor(Text);
                        sig.Item().PaddingTop(1).AlignCenter()
                            .Text("Authorized Signatory · Managing Director").FontSize(9).FontColor(Muted).Italic();
                    });
                });
            });
        }).GeneratePdf();
    }

    private static CompanySettings DefaultSettings() => new()
    {
        Id = 1,
        CompanyName = "FUJAIRAH PLASTIC FACTORY",
        Address = "Fujairah, United Arab Emirates",
        Telephone = "",
        Trn = "",
        Email = "info@fujairahplastic.com",
        Website = "",
        QuotationValidityDays = 30,
        TermsAndConditions = "",
    };

    private static decimal ComputeV3PricePerKg(
        RequisitionItem ri,
        ApprovalItem ai,
        QuotationApproval approval,
        string quoteCurrency,
        decimal? requisitionRateSnapshot,
        IReadOnlyDictionary<int, List<BomCostLine>> costLinesByHeader,
        IReadOnlyDictionary<string, decimal> fxByCurrency)
    {
        var bom = ri.BomHeader;
        var cost = bom?.Cost;
        if (bom is null || cost is null)
            return ai.MarginPerKg ?? ai.SalesPricePerKgAed;

        decimal rmCostAed = 0m;
        if (costLinesByHeader.TryGetValue(bom.Id, out var lines))
            rmCostAed = lines.Sum(l => l.CostPerKgInAed);

        decimal printingCostAed = 0m;
        if (cost.PrintingCostPerKg.HasValue)
        {
            var printingCcy = cost.PrintingCostCurrency ?? "AED";
            if (printingCcy == "AED")
                printingCostAed = cost.PrintingCostPerKg.Value;
            else if (fxByCurrency.TryGetValue(printingCcy, out var rate) && rate > 0)
                printingCostAed = cost.PrintingCostPerKg.Value * rate;
            else
                printingCostAed = cost.PrintingCostPerKg.Value;
        }

        var totalCostAed = rmCostAed + printingCostAed
            + cost.FohPerKg + cost.TransportPerKg + cost.CommissionPerKg;

        decimal totalCostInQuoteCcy;
        if (quoteCurrency == "AED")
            totalCostInQuoteCcy = totalCostAed;
        else
        {
            var saleRate = approval.RateSnapshot ?? requisitionRateSnapshot ?? 1m;
            totalCostInQuoteCcy = saleRate > 0 ? totalCostAed / saleRate : totalCostAed;
        }

        return totalCostInQuoteCcy + (ai.MarginPerKg ?? 0m);
    }

    private static void MetaPair(QuestPDF.Infrastructure.IContainer box, string label, string value)
    {
        box.Text(text =>
        {
            text.Span($"{label} ").FontSize(10).FontColor(Muted);
            text.Span(value).FontSize(10).Bold().FontColor(Text);
        });
    }

    private static void PartyHeader(QuestPDF.Fluent.ColumnDescriptor col, string title)
    {
        col.Item().BorderBottom(0.5f).BorderColor("#cbd5e1").PaddingBottom(3)
            .Text(title.ToUpperInvariant())
            .Bold().FontSize(9).FontColor(Muted).LetterSpacing(0.18f);
    }

    private static void ItemsTableHeader(QuestPDF.Fluent.TableDescriptor t, string text, bool alignRight = false)
    {
        var cell = t.Cell()
            .BorderTop(1).BorderBottom(1).BorderColor(BrandDark)
            .PaddingHorizontal(6).PaddingVertical(6);
        var container = alignRight ? cell.AlignRight() : cell;
        container.Text(text).Bold().FontSize(9.5f).FontColor(Text);
    }

    private static void ItemsTableCell(QuestPDF.Fluent.TableDescriptor t, string text, bool alignRight = false)
    {
        var cell = t.Cell()
            .BorderBottom(0.5f).BorderColor(Faint)
            .PaddingHorizontal(6).PaddingVertical(6);
        var container = alignRight ? cell.AlignRight() : cell;
        container.Text(text).FontSize(10).FontColor(Text);
    }
}
```

- [ ] **Step 2: Build to verify compile**

Run: `dotnet build --nologo -v q`
Expected: Build succeeds, 0 errors.

If `IContainer` reference fails, ensure the `using QuestPDF.Infrastructure;` line is present at top.

- [ ] **Step 3: Run existing tests to ensure no regressions in PDF callers**

Run: `dotnet test --filter "FullyQualifiedName~Approval|FullyQualifiedName~PdfService"`
Expected: All existing approval-flow tests pass (they verify byte-array non-empty, not visual content).

- [ ] **Step 4: Manual PDF smoke**

Find an existing approved requisition on local DB:

```bash
psql -h localhost -p 5433 -U postgres -d bom_price_approval \
  -c 'SELECT "Id", "RefNo" FROM "QuotationRequests" WHERE "Status" = 12 LIMIT 1;'
```

If no Signed quote exists, run an existing flow to create one (or skip this manual check and rely on later web-smoke). Otherwise, in the swagger UI (`http://localhost:7300/swagger`), authenticate as MD and call the PDF download endpoint for that req. Open the downloaded PDF and visually verify:
- Letterhead centered: company name + address + Tel/TRN/Email/Web line
- "SALES QUOTATION" centered title with brand-blue rule above
- Bill To (left) + Sales Representative (right) two-column block
- Items table with thin top/bottom rules + dotted row separators
- "TOTAL AMOUNT" row with double-rule top/bottom in brand-blue
- Numbered Terms & Conditions
- MD signature box bottom-right (showing "(signature pending)" or actual image)
- NO footer
- NO Notes section

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Services/PdfService.cs
git commit -m "feat(api): rewrite PdfService to Letterhead Classic + read CompanySettings"
```

---

## Task 7: Web — typed API hooks for company settings

**Files:**
- Create: `bom-web/src/api/companySettings.ts`

- [ ] **Step 1: Create the typed hook file**

Create `bom-web/src/api/companySettings.ts`:

```typescript
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";

export interface CompanySettings {
  companyName: string;
  address: string;
  telephone: string;
  trn: string;
  email: string;
  website: string;
  quotationValidityDays: number;
  termsAndConditions: string;
  updatedAt: string;
  updatedByName: string | null;
}

export interface UpdateCompanySettingsRequest {
  companyName: string;
  address: string;
  telephone: string;
  trn: string;
  email: string;
  website: string;
  quotationValidityDays: number;
  termsAndConditions: string;
  reason: string;
}

const QUERY_KEY = ["admin", "company-settings"] as const;

export function useCompanySettings() {
  return useQuery({
    queryKey: QUERY_KEY,
    queryFn: async () => {
      const { data } = await api.get<CompanySettings>("/api/admin/company-settings");
      return data;
    },
  });
}

export function useUpdateCompanySettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: UpdateCompanySettingsRequest) => {
      const { data } = await api.put<CompanySettings>("/api/admin/company-settings", body);
      return data;
    },
    onSuccess: (data) => {
      qc.setQueryData(QUERY_KEY, data);
    },
  });
}
```

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd bom-web && npx tsc -b --noEmit`
Expected: No errors. (Run from project root using `pushd`/`popd` if you prefer; just ensure cwd is `bom-web` for the tsc call.)

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/api/companySettings.ts
git commit -m "feat(web): add useCompanySettings + useUpdateCompanySettings hooks"
```

---

## Task 8: Web — CompanySettingsPage component

**Files:**
- Create: `bom-web/src/features/admin/company-settings/CompanySettingsPage.tsx`

- [ ] **Step 1: Create the page component**

Create `bom-web/src/features/admin/company-settings/CompanySettingsPage.tsx`:

```tsx
import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Card, CardContent } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import {
  useCompanySettings,
  useUpdateCompanySettings,
  type UpdateCompanySettingsRequest,
} from "@/api/companySettings";

interface FormState {
  companyName: string;
  address: string;
  telephone: string;
  trn: string;
  email: string;
  website: string;
  quotationValidityDays: number;
  termsAndConditions: string;
  reason: string;
}

const EMPTY_REASON: Pick<FormState, "reason"> = { reason: "" };

function toForm(s: ReturnType<typeof useCompanySettings>["data"] | undefined): FormState {
  return {
    companyName: s?.companyName ?? "",
    address: s?.address ?? "",
    telephone: s?.telephone ?? "",
    trn: s?.trn ?? "",
    email: s?.email ?? "",
    website: s?.website ?? "",
    quotationValidityDays: s?.quotationValidityDays ?? 30,
    termsAndConditions: s?.termsAndConditions ?? "",
    ...EMPTY_REASON,
  };
}

export function CompanySettingsPage() {
  const { data, isLoading, error } = useCompanySettings();
  const update = useUpdateCompanySettings();

  const [form, setForm] = useState<FormState>(() => toForm(undefined));
  const [errors, setErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    if (data) setForm(toForm(data));
  }, [data]);

  const handleSave = async () => {
    setErrors({});
    const payload: UpdateCompanySettingsRequest = {
      companyName: form.companyName,
      address: form.address,
      telephone: form.telephone,
      trn: form.trn,
      email: form.email,
      website: form.website,
      quotationValidityDays: form.quotationValidityDays,
      termsAndConditions: form.termsAndConditions,
      reason: form.reason,
    };
    try {
      await update.mutateAsync(payload);
      toast.success("Company settings updated");
      setForm((f) => ({ ...f, reason: "" }));
    } catch (e: unknown) {
      const ax = e as { response?: { data?: { errors?: Record<string, string[]> } } };
      const fieldErrors = ax?.response?.data?.errors;
      if (fieldErrors) {
        const flat: Record<string, string> = {};
        for (const [k, v] of Object.entries(fieldErrors)) flat[k] = v[0] ?? "Invalid value";
        setErrors(flat);
        toast.error("Please fix the highlighted fields");
      } else {
        toast.error("Failed to save company settings");
      }
    }
  };

  const handleDiscard = () => {
    setForm(toForm(data));
    setErrors({});
  };

  if (isLoading) return <div className="p-6 text-muted-foreground">Loading…</div>;
  if (error || !data) return <div className="p-6 text-red-600">Failed to load company settings.</div>;

  return (
    <div className="p-6 max-w-4xl mx-auto">
      <h1 className="text-2xl font-bold text-foreground mb-1">Company Settings</h1>
      <p className="text-sm text-muted-foreground mb-6">
        These values appear on every quotation PDF. Changes take effect immediately.
      </p>

      <Card>
        <CardContent className="p-6 space-y-6">
          {/* Letterhead */}
          <section>
            <h2 className="text-xs font-bold text-blue-700 dark:text-blue-300 uppercase tracking-widest mb-3">
              Letterhead
            </h2>
            <div className="space-y-3">
              <Field label="Company Name (headline)" error={errors.CompanyName}>
                <input
                  type="text"
                  value={form.companyName}
                  onChange={(e) => setForm({ ...form, companyName: e.target.value })}
                  className="input"
                />
              </Field>
              <Field label="Address (single line)" error={errors.Address}>
                <input
                  type="text"
                  value={form.address}
                  onChange={(e) => setForm({ ...form, address: e.target.value })}
                  className="input"
                />
              </Field>
              <div className="grid grid-cols-2 gap-3">
                <Field label="Telephone" error={errors.Telephone}>
                  <input
                    type="text"
                    value={form.telephone}
                    onChange={(e) => setForm({ ...form, telephone: e.target.value })}
                    className="input"
                  />
                </Field>
                <Field label="TRN" error={errors.Trn}>
                  <input
                    type="text"
                    value={form.trn}
                    onChange={(e) => setForm({ ...form, trn: e.target.value })}
                    className="input"
                  />
                </Field>
                <Field label="Email (company)" error={errors.Email}>
                  <input
                    type="email"
                    value={form.email}
                    onChange={(e) => setForm({ ...form, email: e.target.value })}
                    className="input"
                  />
                </Field>
                <Field label="Website" error={errors.Website}>
                  <input
                    type="text"
                    value={form.website}
                    onChange={(e) => setForm({ ...form, website: e.target.value })}
                    className="input"
                  />
                </Field>
              </div>
            </div>
          </section>

          {/* Quotation defaults */}
          <section>
            <h2 className="text-xs font-bold text-blue-700 dark:text-blue-300 uppercase tracking-widest mb-3">
              Quotation Defaults
            </h2>
            <Field
              label="Quotation Validity (days)"
              hint='"Valid Until" = approval date + this many days. Range 1–365.'
              error={errors.QuotationValidityDays}
            >
              <input
                type="number"
                min={1}
                max={365}
                value={form.quotationValidityDays}
                onChange={(e) =>
                  setForm({ ...form, quotationValidityDays: Number(e.target.value) || 0 })
                }
                className="input w-32 text-right"
              />
            </Field>
          </section>

          {/* Terms & conditions */}
          <section>
            <h2 className="text-xs font-bold text-blue-700 dark:text-blue-300 uppercase tracking-widest mb-3">
              Terms &amp; Conditions
            </h2>
            <Field
              label="One per line — numbering added automatically"
              hint="Each non-empty line becomes a numbered point. Blank lines ignored."
              error={errors.TermsAndConditions}
            >
              <textarea
                rows={8}
                value={form.termsAndConditions}
                onChange={(e) => setForm({ ...form, termsAndConditions: e.target.value })}
                className="input font-mono text-sm leading-relaxed"
              />
            </Field>
          </section>

          {/* Reason + actions */}
          <section className="border-t border-border pt-4">
            <Field
              label="Reason for change (audit log)"
              hint="Min 5 chars. Recorded in admin audit log."
              error={errors.Reason}
            >
              <input
                type="text"
                value={form.reason}
                onChange={(e) => setForm({ ...form, reason: e.target.value })}
                className="input"
                placeholder="e.g. Updated TRN per new license"
              />
            </Field>

            <div className="flex justify-end gap-2 mt-4">
              <Button variant="secondary" onClick={handleDiscard} disabled={update.isPending}>
                Discard Changes
              </Button>
              <Button onClick={handleSave} disabled={update.isPending}>
                {update.isPending ? "Saving…" : "Save Changes"}
              </Button>
            </div>

            {data.updatedAt && (
              <p className="text-xs text-muted-foreground mt-3">
                Last updated {new Date(data.updatedAt).toLocaleString()}
                {data.updatedByName ? ` by ${data.updatedByName}` : ""}.
              </p>
            )}
          </section>
        </CardContent>
      </Card>
    </div>
  );
}

function Field({
  label,
  hint,
  error,
  children,
}: {
  label: string;
  hint?: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label className="block text-xs font-semibold text-foreground mb-1">{label}</label>
      {children}
      {hint && !error && <p className="text-xs text-muted-foreground italic mt-1">{hint}</p>}
      {error && <p className="text-xs text-red-600 dark:text-red-400 mt-1">{error}</p>}
    </div>
  );
}
```

- [ ] **Step 2: Verify the page expects standard `.input` class**

Search for existing `.input` class definition:

Run: `grep -rn "^\.input\b\|@apply.*input\|class .*input" bom-web/src/index.css bom-web/tailwind.config.* 2>/dev/null | head -10`

If no `.input` class exists in `index.css`, add this to `bom-web/src/index.css` near other component classes (under the `@layer components` section if present, otherwise add it):

```css
@layer components {
  .input {
    @apply w-full px-3 py-2 border border-border rounded bg-background text-foreground text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/40 focus:border-blue-600;
  }
}
```

If a `.input` class already exists, skip this — re-use it.

- [ ] **Step 3: Build to verify TypeScript compiles**

Run: `cd bom-web && npx tsc -b --noEmit`
Expected: No errors.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/features/admin/company-settings/CompanySettingsPage.tsx \
        bom-web/src/index.css
git commit -m "feat(web): CompanySettingsPage with form + reason + audit-aware save"
```

---

## Task 9: Web — register route + sidebar link + audit-log label

**Files:**
- Modify: `bom-web/src/App.tsx`
- Modify: `bom-web/src/components/layout/Sidebar.tsx`
- Modify: `bom-web/src/api/admin.ts`
- Modify: `bom-web/src/features/admin/audit-log/AuditLogPage.tsx`

- [ ] **Step 1: Add route to App.tsx**

Edit `bom-web/src/App.tsx`:

1. Add the lazy import near the top, alongside `AuditLogPage` import:

```tsx
import { CompanySettingsPage } from "@/features/admin/company-settings/CompanySettingsPage";
```

2. Add the route entry inside the children array, immediately after the existing `admin/audit-log` route block:

```tsx
      {
        path: "admin/company-settings",
        element: (
          <ProtectedRoute allow={["Admin"]}>
            <CompanySettingsPage />
          </ProtectedRoute>
        ),
      },
```

- [ ] **Step 2: Add sidebar link**

Edit `bom-web/src/components/layout/Sidebar.tsx`:

1. Find the existing imports for icon components (lucide-react). Add `Settings` to the list:

```tsx
import { Settings } from "lucide-react"; // append to existing lucide-react import statement
```

(Actually if there's a single multi-name import, just add `, Settings` to it. Verify by reading the existing import statements at top of file.)

2. Find the `audit-log` nav entry (around line 77-82) and immediately after it, add:

```tsx
  {
    to: "/admin/company-settings",
    label: "Company Settings",
    icon: Settings,
    roles: ["Admin"],
  },
```

- [ ] **Step 3: Add UpdateCompanySettings to AdminActionType union + AuditLogPage labels/array**

Edit `bom-web/src/api/admin.ts`. Find the `AdminActionType` union and append:

```typescript
export type AdminActionType =
  | "DeleteRequisition"
  | "RollbackStatus"
  | "ReassignSp"
  | "UnlockBom"
  | "UnlockCosting"
  | "ResetPassword"
  | "OverridePrices"
  | "HardDeleteCustomer"
  // V3 additions:
  | "RollbackToCosting"
  | "V3CutoverMigration"
  | "UpdateCompanySettings";
```

Edit `bom-web/src/features/admin/audit-log/AuditLogPage.tsx`:

1. Add `UpdateCompanySettings: "Update Company Settings"` to the `ACTION_TYPE_LABELS` constant (after `V3CutoverMigration`).
2. Add `"UpdateCompanySettings"` to the `ACTION_TYPES` array (after `"V3CutoverMigration"`).

- [ ] **Step 4: Build to verify TypeScript compiles**

Run: `cd bom-web && npx tsc -b --noEmit`
Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/App.tsx \
        bom-web/src/components/layout/Sidebar.tsx \
        bom-web/src/api/admin.ts \
        bom-web/src/features/admin/audit-log/AuditLogPage.tsx
git commit -m "feat(web): wire /admin/company-settings route + sidebar link + audit label"
```

---

## Task 10: Web component test for CompanySettingsPage

**Files:**
- Create: `bom-web/src/features/admin/company-settings/CompanySettingsPage.test.tsx`

- [ ] **Step 1: Create the test file**

Create `bom-web/src/features/admin/company-settings/CompanySettingsPage.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CompanySettingsPage } from "./CompanySettingsPage";

vi.mock("@/api/axios", () => {
  const get = vi.fn();
  const put = vi.fn();
  return { api: { get, put } };
});

vi.mock("sonner", () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

import { api } from "@/api/axios";

const seedSettings = {
  companyName: "FUJAIRAH PLASTIC FACTORY",
  address: "Fujairah, UAE",
  telephone: "",
  trn: "",
  email: "info@fpf.com",
  website: "",
  quotationValidityDays: 30,
  termsAndConditions: "Line one\nLine two",
  updatedAt: new Date("2026-05-03T00:00:00Z").toISOString(),
  updatedByName: null,
};

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <CompanySettingsPage />
    </QueryClientProvider>
  );
}

describe("CompanySettingsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("loads and displays seeded settings", async () => {
    (api.get as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ data: seedSettings });

    renderPage();

    await waitFor(() => {
      expect(screen.getByDisplayValue("FUJAIRAH PLASTIC FACTORY")).toBeInTheDocument();
    });
    expect(screen.getByDisplayValue(30)).toBeInTheDocument();
    expect(screen.getByText("Last updated", { exact: false })).toBeInTheDocument();
  });

  it("submits PUT with form values on Save", async () => {
    (api.get as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ data: seedSettings });
    (api.put as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      data: { ...seedSettings, companyName: "FPF UPDATED", updatedByName: "Admin" },
    });

    renderPage();
    await waitFor(() => screen.getByDisplayValue("FUJAIRAH PLASTIC FACTORY"));

    const nameInput = screen.getByDisplayValue("FUJAIRAH PLASTIC FACTORY");
    fireEvent.change(nameInput, { target: { value: "FPF UPDATED" } });

    const reasonInput = screen.getByPlaceholderText(/Updated TRN/);
    fireEvent.change(reasonInput, { target: { value: "Test save reason" } });

    fireEvent.click(screen.getByRole("button", { name: /Save Changes/ }));

    await waitFor(() => {
      expect(api.put).toHaveBeenCalledWith(
        "/api/admin/company-settings",
        expect.objectContaining({
          companyName: "FPF UPDATED",
          reason: "Test save reason",
        })
      );
    });
  });

  it("Discard Changes reverts unsaved edits", async () => {
    (api.get as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ data: seedSettings });

    renderPage();
    await waitFor(() => screen.getByDisplayValue("FUJAIRAH PLASTIC FACTORY"));

    const nameInput = screen.getByDisplayValue("FUJAIRAH PLASTIC FACTORY");
    fireEvent.change(nameInput, { target: { value: "STALE EDIT" } });
    expect(screen.getByDisplayValue("STALE EDIT")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Discard Changes/ }));

    expect(screen.getByDisplayValue("FUJAIRAH PLASTIC FACTORY")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the new test**

Run: `cd bom-web && npx vitest run src/features/admin/company-settings/`
Expected: 3 tests pass.

If `screen.getByDisplayValue(30)` fails (vitest sometimes treats numbers differently), change to `screen.getByDisplayValue("30")`.

- [ ] **Step 3: Run the full vitest suite to ensure no regressions**

Run: `cd bom-web && npx vitest run`
Expected: All tests pass (291+ existing tests + 3 new = 294+).

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/features/admin/company-settings/CompanySettingsPage.test.tsx
git commit -m "test(web): CompanySettingsPage load/save/discard component tests"
```

---

## Task 11: Web smoke — manual verification on dev server

**Files:** none (smoke verification only)

- [ ] **Step 1: Verify backend is running**

Run: `curl -s http://localhost:7300/swagger/index.html >/dev/null && echo OK || echo "Start backend first"`

If not running: `dotnet run --project BomPriceApproval.API` in a terminal.

- [ ] **Step 2: Start the web dev server using preview tools**

Use the `preview_start` tool with the `bom-web` directory. Do NOT use `npm run dev` directly.

- [ ] **Step 3: Log in as admin via preview_fill + preview_click**

Navigate to `/login`, fill in admin credentials (`admin@bompriceapproval.com` / `Admin@123!`), submit.

- [ ] **Step 4: Navigate to /admin/company-settings**

Use `preview_click` on the new sidebar link "Company Settings" (or `preview_eval` to navigate: `window.location.href = "/admin/company-settings"`).

- [ ] **Step 5: Verify form loads with seeded values**

Use `preview_snapshot` to check that:
- Headline reads "Company Settings"
- Company Name input shows "FUJAIRAH PLASTIC FACTORY"
- Quotation Validity input shows "30"
- T&C textarea contains 5 lines

- [ ] **Step 6: Edit a field and save**

Change Telephone to `+971 3 999 0000`, fill Reason `Smoke test from dev` (≥ 5 chars), click Save Changes. Verify toast says "Company settings updated". Use `preview_snapshot` to confirm "Last updated …" line at bottom now shows current time + admin name.

- [ ] **Step 7: Verify audit log entry**

Navigate to `/admin/audit-log`. Verify a new row exists with:
- Action Type: "Update Company Settings"
- Entity Type: "CompanySettings", Entity ID: 1
- Reason: "Smoke test from dev"

Click the row to expand the diff panel; verify the Telephone field shows the change.

- [ ] **Step 8: Take a screenshot for the user**

Use `preview_screenshot` to capture the Company Settings page (saved to disk so it can be shared).

- [ ] **Step 9: Cleanup — revert telephone to empty**

Back on Company Settings page, clear the Telephone field, fill Reason `Revert smoke test`, click Save.

- [ ] **Step 10: Stop the preview**

Use `preview_stop` to terminate the dev server.

- [ ] **Step 11: No commit (smoke-only) — proceed to next task**

---

## Task 12: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add a short section under "V3 Workflow"**

Find the heading `### V3 Workflow (CURRENT — post-2026-04-30 cutover)` in `CLAUDE.md`. After the entire V3 section ends (before the V2.3 LEGACY section), add a new subsection:

````markdown
### Company Settings (post-2026-05-03)

Singleton `CompanySettings` table (id=1) drives the quotation PDF letterhead + validity + T&C. Admin-only UI at `/admin/company-settings` (web) — no mobile UI.

- **Fields:** `CompanyName`, `Address`, `Telephone`, `Trn`, `Email`, `Website`, `QuotationValidityDays` (1-365), `TermsAndConditions` (multi-line; each non-empty line becomes one numbered point in PDF), `UpdatedAt`, `UpdatedByUserId`.
- **Endpoints:** `GET /api/admin/company-settings` + `PUT /api/admin/company-settings` (audited via `AdminAuditLogger`, new `AdminActionType.UpdateCompanySettings`).
- **PDF:** `PdfService.GenerateQuotationAsync` reads `CompanySettings` at the top of every render. Falls back to a static default if the seeded row is missing (defensive only — should never happen post-migration).
- **PDF style:** Letterhead Classic — Times-serif, centered company name + address + Tel/TRN/Email/Web contact strip, brand-blue rule, 2-column Bill To / Sales Representative block (sales person email next to customer), thin-rule items table with dotted row separators, double-rule total in brand blue, numbered T&C, single MD signature block bottom-right. **No footer. No Notes section.**
- **Validity:** "Valid Until" = `approval.ApprovedAt + CompanySettings.QuotationValidityDays`. Admin can change anytime; takes effect on next quote.
````

- [ ] **Step 2: Verify edit applied cleanly**

Run: `grep -n "Company Settings (post-2026-05-03)" CLAUDE.md`
Expected: one match.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: document CompanySettings + Letterhead Classic PDF in CLAUDE.md"
```

---

## Task 13: Push branch + open PR

**Files:** none (git/gh operations only)

- [ ] **Step 1: Verify all tests pass one more time**

Run: `dotnet test`
Expected: All backend tests pass.

Run: `cd bom-web && npx vitest run`
Expected: All web tests pass.

Run: `cd bom-web && npx tsc -b --noEmit`
Expected: No type errors.

- [ ] **Step 2: Verify the branch state is clean**

Run: `git status`
Expected: Either "nothing to commit, working tree clean" or only untracked-but-irrelevant files (`.superpowers/`, `bin/`, `obj/`).

- [ ] **Step 3: Push the feature branch**

Run: `git push -u origin feat/pdf-redesign-and-company-settings`
Expected: branch pushed, tracking set up.

- [ ] **Step 4: Open the PR**

Run:

```bash
gh pr create --title "feat: PDF redesign (Letterhead Classic) + admin company settings" --body "$(cat <<'EOF'
## Summary
- Replaces the Modern Corporate quotation PDF with **Letterhead Classic** (Times-serif headline, centered letterhead, 2-col Bill To / Sales Rep, dotted-rule table, single MD signature, no footer, no Notes)
- New `CompanySettings` singleton table — admin edits company name / address / tel / TRN / email / website / quotation validity (days) / T&C from `/admin/company-settings`
- Salesperson email shown next to Bill To (sourced from `User.Email`); company email moved to letterhead contact strip
- Audited via existing `AdminAuditLogger`, new `AdminActionType.UpdateCompanySettings`

## Test plan
- [ ] Backend: `dotnet test --filter "FullyQualifiedName~CompanySettingsTests"` (9 tests pass)
- [ ] Backend: full suite `dotnet test` green
- [ ] Web: `npx vitest run src/features/admin/company-settings/` (3 tests pass)
- [ ] Web: full vitest suite green
- [ ] Web: `npx tsc -b --noEmit` clean
- [ ] Manual: log in as admin → /admin/company-settings → load + edit + save → audit row appears
- [ ] Manual: download PDF for a Signed quote → visually matches Letterhead Classic mockup

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 5: Report PR URL to user**

Surface the returned PR URL in the chat. Wait for CI to go green, then auto-merge per project Auto Mode rule (CI green + base is master + no `hold` label/comment + squash merge default).

- [ ] **Step 6: Apply the migration to Neon production after merge**

After PR merges to master, BEFORE Fly auto-deploy or in parallel with it, apply the migration to Neon:

```bash
dotnet ef database update --project BomPriceApproval.API --connection "$NEON_PROD_URI"
```

Where `$NEON_PROD_URI` is the production Neon connection string in Npgsql key=value form (see CLAUDE.md "Rotating Neon password" section for format).

Then deploy the API: `flyctl deploy --remote-only --config fly.toml`.

Verify production:
- `curl https://bom-fpf-api.fly.dev/health` returns `{"status":"ok"}`
- Log in as admin on https://bom-fpf.pages.dev → /admin/company-settings → form loads with seeded values
- Download a PDF from any Signed quote → visually matches Letterhead Classic

- [ ] **Step 7: Final report**

Summarize for the user: PR URL, merge status, prod migration applied (yes/no), production smoke result.

---

## Self-review checklist (executed before publishing this plan)

✅ **Spec coverage:**
- Backend entity + migration + seed → Tasks 1, 2
- API endpoints (GET + PUT) + DTOs + audit → Tasks 3, 4
- Backend tests (9 cases) → Task 5
- PdfService rewrite (Letterhead Classic + reads settings + 2-email split + no footer + no Notes) → Task 6
- Web API hooks → Task 7
- Web page UI (3 sections, reason, discard, save, last-updated) → Task 8
- Route + sidebar + audit-log label → Task 9
- Web component tests → Task 10
- Manual web smoke + screenshot → Task 11
- CLAUDE.md update → Task 12
- PR + Neon migration + Fly deploy → Task 13

✅ **Placeholder scan:** No "TBD", "TODO", "implement later", or "similar to Task N" placeholders. Every code step has full code.

✅ **Type consistency:** `CompanySettings` entity property names (`CompanyName`, `QuotationValidityDays`, `Trn`, etc.) match across entity → DbContext → migration seed → controller → DTO → frontend type → frontend form → tests. Same casing convention (PascalCase backend, camelCase frontend) preserved.
