# Costing Entry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Accountant-facing Costing Entry page that captures per-line raw material costs in multiple currencies, auto-saves a draft, converts to the requisition's quote currency on submit, and stores permanent per-line cost history.

**Architecture:** Extend the existing `CostingController` with a new `PUT /draft` endpoint and rewrite `GET` / `POST /submit` to return BOM lines with pre-fills and to write per-line history with currency conversion. Three new tables back this: `CostingDraft` (transient JSON blob), `BomCostLine` (immutable permanent history), `ItemLastCost` (per-item last-submitted cost lookup). The React page mirrors the BOM Entry page — process-grouped layout, debounced auto-save, final submit — with an added currency dropdown per line and stale-price warning.

**Tech Stack:** ASP.NET Core 8, EF Core 8 + Npgsql, React 19 + Vite, TanStack Query v5, TypeScript, Tailwind CSS v4, Vitest + React Testing Library, xUnit + FluentAssertions + Testcontainers.

**Spec:** [docs/superpowers/specs/2026-04-14-costing-entry-design.md](../specs/2026-04-14-costing-entry-design.md)

---

## File Map

### Backend — new files

| File | Responsibility |
|---|---|
| `BomPriceApproval.API/Domain/Entities/CostingDraft.cs` | Transient draft; one row per `BomHeader` |
| `BomPriceApproval.API/Domain/Entities/BomCostLine.cs` | Permanent immutable per-line cost history |
| `BomPriceApproval.API/Domain/Entities/ItemLastCost.cs` | Per-item last-submitted cost lookup |
| `BomPriceApproval.API/Infrastructure/Data/Migrations/<timestamp>_CostingEntry.cs` | EF migration for the three new tables |
| `BomPriceApproval.Tests/Costing/CostingTests.cs` | Integration tests for costing workflow |

### Backend — modified files

| File | Change |
|---|---|
| `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs` | Register 3 new `DbSet`s, add `HasPrecision` and relationships |
| `BomPriceApproval.API/Features/Costing/CostingDtos.cs` | Add `CurrencyCode` to `RawMaterialCostInput`, add draft request/response DTOs, extend `CostingDetailResponse` |
| `BomPriceApproval.API/Features/Costing/CostingController.cs` | Add branch isolation to all endpoints, rewrite `GET` to return BOM lines + draft + pre-fills, add `PUT /draft`, rewrite `POST /submit` for currency conversion + `BomCostLine` + `ItemLastCost` |

### Frontend — new files

| File | Responsibility |
|---|---|
| `bom-web/src/features/costing/costingApi.ts` | TanStack Query hooks: `useCosting`, `useStartCosting`, `useSaveCostingDraft`, `useSubmitCosting` |
| `bom-web/src/features/costing/CostingEntryPage.tsx` | Costing entry UI — process-grouped lines, currency per line, landed cost, FOH, auto-save, submit |
| `bom-web/src/features/costing/CostingEntryPage.test.tsx` | Vitest tests for the page |

### Frontend — modified files

| File | Change |
|---|---|
| `bom-web/src/types/api.ts` | Add `LastCostInfo`, `CostingBomLine`, `CostingDraft`, `CostingDetail` interfaces |
| `bom-web/src/App.tsx` | Register `/requisitions/:id/costing` route (Accountant only) |
| `bom-web/src/features/requisitions/RequisitionDetailPage.tsx` | Enable "Start Costing" / "Continue Costing" navigation for Accountant |

---

## Task 1: Add three costing entities

**Files:**
- Create: `BomPriceApproval.API/Domain/Entities/CostingDraft.cs`
- Create: `BomPriceApproval.API/Domain/Entities/BomCostLine.cs`
- Create: `BomPriceApproval.API/Domain/Entities/ItemLastCost.cs`

- [ ] **Step 1: Create `CostingDraft.cs`**

```csharp
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Domain.Entities;

public class CostingDraft
{
    public int Id { get; set; }
    public int BomHeaderId { get; set; }
    public string LinesJson { get; set; } = "[]";
    public LandedCostType LandedCostType { get; set; }
    public decimal LandedCostValue { get; set; }
    public decimal FohAmount { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public BomHeader BomHeader { get; set; } = null!;
}
```

- [ ] **Step 2: Create `BomCostLine.cs`**

```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class BomCostLine
{
    public int Id { get; set; }
    public int BomHeaderId { get; set; }
    public int BomLineId { get; set; }
    public decimal CostPerKg { get; set; }
    public string CurrencyCode { get; set; } = "AED";
    public decimal CostPerKgInQuoteCurrency { get; set; }
    public BomHeader BomHeader { get; set; } = null!;
    public BomLine BomLine { get; set; } = null!;
}
```

- [ ] **Step 3: Create `ItemLastCost.cs`**

```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class ItemLastCost
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public decimal CostPerKg { get; set; }
    public string CurrencyCode { get; set; } = "AED";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int UpdatedByUserId { get; set; }
    public Item Item { get; set; } = null!;
    public User UpdatedBy { get; set; } = null!;
}
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Domain/Entities/CostingDraft.cs BomPriceApproval.API/Domain/Entities/BomCostLine.cs BomPriceApproval.API/Domain/Entities/ItemLastCost.cs
git commit -m "feat(api): add CostingDraft, BomCostLine, ItemLastCost entities"
```

---

## Task 2: Register DbSets + relationships + precision

**Files:**
- Modify: `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs`

- [ ] **Step 1: Register the three `DbSet`s**

In `AppDbContext`, add after the existing `DbSet<Notification>` declaration (around line 20):

```csharp
public DbSet<CostingDraft> CostingDrafts => Set<CostingDraft>();
public DbSet<BomCostLine> BomCostLines => Set<BomCostLine>();
public DbSet<ItemLastCost> ItemLastCosts => Set<ItemLastCost>();
```

- [ ] **Step 2: Add relationships + unique indexes in `OnModelCreating`**

At the bottom of `OnModelCreating`, before the closing brace, append:

```csharp
// CostingDraft — one per BomHeader
mb.Entity<CostingDraft>()
    .HasOne(d => d.BomHeader)
    .WithOne()
    .HasForeignKey<CostingDraft>(d => d.BomHeaderId)
    .OnDelete(DeleteBehavior.Cascade);
mb.Entity<CostingDraft>().HasIndex(d => d.BomHeaderId).IsUnique();

// BomCostLine — many per BomHeader and per BomLine (permanent history)
mb.Entity<BomCostLine>()
    .HasOne(l => l.BomHeader)
    .WithMany()
    .HasForeignKey(l => l.BomHeaderId)
    .OnDelete(DeleteBehavior.Cascade);
mb.Entity<BomCostLine>()
    .HasOne(l => l.BomLine)
    .WithMany()
    .HasForeignKey(l => l.BomLineId)
    .OnDelete(DeleteBehavior.Restrict);

// ItemLastCost — one per Item
mb.Entity<ItemLastCost>()
    .HasOne(l => l.Item)
    .WithOne()
    .HasForeignKey<ItemLastCost>(l => l.ItemId)
    .OnDelete(DeleteBehavior.Cascade);
mb.Entity<ItemLastCost>()
    .HasOne(l => l.UpdatedBy)
    .WithMany()
    .HasForeignKey(l => l.UpdatedByUserId)
    .OnDelete(DeleteBehavior.Restrict);
mb.Entity<ItemLastCost>().HasIndex(l => l.ItemId).IsUnique();
```

- [ ] **Step 3: Add decimal precision for all new money columns**

Still inside `OnModelCreating`, still at the bottom, append:

```csharp
mb.Entity<CostingDraft>().Property(d => d.LandedCostValue).HasPrecision(18, 4);
mb.Entity<CostingDraft>().Property(d => d.FohAmount).HasPrecision(18, 4);
mb.Entity<BomCostLine>().Property(l => l.CostPerKg).HasPrecision(18, 4);
mb.Entity<BomCostLine>().Property(l => l.CostPerKgInQuoteCurrency).HasPrecision(18, 4);
mb.Entity<ItemLastCost>().Property(l => l.CostPerKg).HasPrecision(18, 4);
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs
git commit -m "feat(api): register costing DbSets with precision and relationships"
```

---

## Task 3: Create and apply EF migration

**Files:**
- Create: `BomPriceApproval.API/Infrastructure/Data/Migrations/<timestamp>_CostingEntry.cs` (generated)

- [ ] **Step 1: Generate migration**

Run: `dotnet ef migrations add CostingEntry --project BomPriceApproval.API`
Expected: Migration file created under `BomPriceApproval.API/Infrastructure/Data/Migrations/`, output ends with `Done.`.

- [ ] **Step 2: Inspect the generated migration**

Open the new `<timestamp>_CostingEntry.cs`. Confirm:
- `CreateTable("CostingDrafts", ...)` with unique index on `BomHeaderId`
- `CreateTable("BomCostLines", ...)` with FKs to `BomHeaders` and `BomLines`
- `CreateTable("ItemLastCosts", ...)` with unique index on `ItemId` and FK to `Items` + `Users`

If anything is missing, delete the migration and revisit Task 2.

- [ ] **Step 3: Apply migration**

Run: `dotnet ef database update --project BomPriceApproval.API`
Expected: output ends with `Done.` and no errors.

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Data/Migrations/
git commit -m "feat(api): migration for costing draft, line history and item last cost"
```

---

## Task 4: Extend costing DTOs

**Files:**
- Modify: `BomPriceApproval.API/Features/Costing/CostingDtos.cs`

- [ ] **Step 1: Replace the file contents**

Replace the entire contents of `CostingDtos.cs` with:

```csharp
using System.ComponentModel.DataAnnotations;
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Costing;

public record RawMaterialCostInput(int BomLineId, decimal CostPerKg, string CurrencyCode);

public record SubmitCostingRequest(
    List<RawMaterialCostInput> RawMaterialCosts,
    LandedCostType LandedCostType,
    decimal LandedCostValue,
    decimal FohAmount);

public record CostingDraftLineInput(int BomLineId, decimal CostPerKg, string CurrencyCode);

public record SaveCostingDraftRequest(
    [Required] List<CostingDraftLineInput> Lines,
    LandedCostType LandedCostType,
    decimal LandedCostValue,
    decimal FohAmount);

public record LastCostInfo(decimal CostPerKg, string CurrencyCode, DateTime UpdatedAt);

public record CostingBomLineResponse(
    int BomLineId,
    int ProcessId,
    string ProcessName,
    int RawMaterialItemId,
    string RawMaterialDescription,
    decimal QtyPerKg,
    decimal WastagePct,
    LastCostInfo? LastCost);

public record CostingDraftResponse(
    List<CostingDraftLineInput> Lines,
    LandedCostType LandedCostType,
    decimal LandedCostValue,
    decimal FohAmount);

public record CostingDetailResponse(
    int Id,
    decimal RawMaterialCostTotal,
    string LandedCostType,
    decimal LandedCostValue,
    decimal FohAmount,
    decimal TotalCostPerKg,
    DateTime? SubmittedAt,
    List<CostingBomLineResponse> BomLines,
    CostingDraftResponse? Draft);
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build fails with errors in `CostingController.cs` about missing `SubmittedAt`/`BomLines`/`Draft` arguments — this is expected and will be fixed in Task 5.

- [ ] **Step 3: Do NOT commit yet**

The project won't compile until Task 5 rewrites the controller. Proceed directly to Task 5.

---

## Task 5: Rewrite `CostingController`

**Files:**
- Modify: `BomPriceApproval.API/Features/Costing/CostingController.cs`

- [ ] **Step 1: Replace the file contents**

Replace the entire contents of `CostingController.cs` with:

```csharp
using System.Security.Claims;
using System.Text.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Costing;

[ApiController]
[Route("api/costing")]
[Authorize(Roles = "Accountant")]
public class CostingController(AppDbContext db, NotificationService notificationService) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private int? CurrentBranchId => int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0 ? b : null;

    [HttpGet("{requisitionId}")]
    public async Task<IActionResult> Get(int requisitionId)
    {
        var bom = await db.BomHeaders
            .Include(b => b.QuotationRequest)
            .Include(b => b.Cost)
            .Include(b => b.Lines).ThenInclude(l => l.Process)
            .Include(b => b.Lines).ThenInclude(l => l.RawMaterial)
            .FirstOrDefaultAsync(b => b.QuotationRequestId == requisitionId);

        if (bom is null) return NotFound();
        if (CurrentBranchId.HasValue && bom.QuotationRequest.BranchId != CurrentBranchId)
            return Forbid();

        var rawMaterialItemIds = bom.Lines.Select(l => l.RawMaterialItemId).ToList();
        var lastCosts = await db.ItemLastCosts
            .Where(c => rawMaterialItemIds.Contains(c.ItemId))
            .ToDictionaryAsync(c => c.ItemId);

        var bomLineResponses = bom.Lines.Select(l =>
        {
            LastCostInfo? lastCost = lastCosts.TryGetValue(l.RawMaterialItemId, out var lc)
                ? new LastCostInfo(lc.CostPerKg, lc.CurrencyCode, lc.UpdatedAt)
                : null;
            return new CostingBomLineResponse(
                l.Id, l.ProcessId, l.Process.Name,
                l.RawMaterialItemId, l.RawMaterial.Description,
                l.QtyPerKg, l.WastagePct, lastCost);
        }).ToList();

        var draftRow = await db.CostingDrafts.FirstOrDefaultAsync(d => d.BomHeaderId == bom.Id);
        CostingDraftResponse? draft = null;
        if (draftRow is not null)
        {
            var lines = JsonSerializer.Deserialize<List<CostingDraftLineInput>>(draftRow.LinesJson)
                ?? new List<CostingDraftLineInput>();
            draft = new CostingDraftResponse(lines, draftRow.LandedCostType, draftRow.LandedCostValue, draftRow.FohAmount);
        }

        var c = bom.Cost;
        return Ok(new CostingDetailResponse(
            c?.Id ?? 0,
            c?.RawMaterialCostTotal ?? 0m,
            (c?.LandedCostType ?? LandedCostType.Percentage).ToString(),
            c?.LandedCostValue ?? 0m,
            c?.FohAmount ?? 0m,
            c?.TotalCostPerKg ?? 0m,
            c?.SubmittedAt,
            bomLineResponses,
            draft));
    }

    [HttpPost("{requisitionId}/start")]
    public async Task<IActionResult> Start(int requisitionId)
    {
        var req = await db.QuotationRequests.FindAsync(requisitionId);
        if (req is null) return NotFound();
        if (CurrentBranchId.HasValue && req.BranchId != CurrentBranchId)
            return Forbid();
        if (req.Status != RequisitionStatus.CostingPending)
            return BadRequest(new { message = "Requisition is not in CostingPending status" });

        req.Status = RequisitionStatus.CostingInProgress;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{requisitionId}/draft")]
    public async Task<IActionResult> SaveDraft(int requisitionId, SaveCostingDraftRequest request)
    {
        var req = await db.QuotationRequests.FindAsync(requisitionId);
        if (req is null) return NotFound();
        if (CurrentBranchId.HasValue && req.BranchId != CurrentBranchId)
            return Forbid();
        if (req.Status != RequisitionStatus.CostingInProgress)
            return BadRequest(new { message = "Draft can only be saved when status is CostingInProgress" });

        var bom = await db.BomHeaders.FirstOrDefaultAsync(b => b.QuotationRequestId == requisitionId);
        if (bom is null) return NotFound();

        var linesJson = JsonSerializer.Serialize(request.Lines);
        var draft = await db.CostingDrafts.FirstOrDefaultAsync(d => d.BomHeaderId == bom.Id);
        if (draft is null)
        {
            draft = new CostingDraft { BomHeaderId = bom.Id };
            db.CostingDrafts.Add(draft);
        }
        draft.LinesJson = linesJson;
        draft.LandedCostType = request.LandedCostType;
        draft.LandedCostValue = request.LandedCostValue;
        draft.FohAmount = request.FohAmount;
        draft.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{requisitionId}/submit")]
    public async Task<IActionResult> Submit(int requisitionId, SubmitCostingRequest request)
    {
        var req = await db.QuotationRequests.Include(q => q.Branch).FirstOrDefaultAsync(q => q.Id == requisitionId);
        if (req is null) return NotFound();
        if (CurrentBranchId.HasValue && req.BranchId != CurrentBranchId)
            return Forbid();
        if (req.Status != RequisitionStatus.CostingInProgress)
            return BadRequest(new { message = "Costing can only be submitted when status is CostingInProgress" });

        var bom = await db.BomHeaders
            .Include(b => b.Lines)
            .Include(b => b.Cost)
            .FirstOrDefaultAsync(b => b.QuotationRequestId == requisitionId);
        if (bom is null) return BadRequest(new { message = "No BOM found for this requisition" });

        var quoteCurrency = (req.CurrencyCode ?? "AED").ToUpperInvariant();

        // Build currency → rate-to-AED map (1.0 for AED)
        var usedCurrencies = request.RawMaterialCosts
            .Select(r => (r.CurrencyCode ?? "AED").ToUpperInvariant())
            .Append(quoteCurrency)
            .Distinct()
            .ToList();

        var rates = await db.ExchangeRates
            .Where(e => e.IsActive && usedCurrencies.Contains(e.CurrencyCode))
            .ToDictionaryAsync(e => e.CurrencyCode.ToUpperInvariant(), e => e.RateToAed);

        decimal RateToAed(string code)
        {
            if (code == "AED") return 1m;
            if (!rates.TryGetValue(code, out var r))
                throw new InvalidOperationException($"No exchange rate found for {code}. Contact admin.");
            return r;
        }

        decimal quoteRateToAed;
        try { quoteRateToAed = RateToAed(quoteCurrency); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        // Calculate raw material cost total in quote currency + prepare BomCostLine rows
        decimal rawMaterialTotal = 0;
        var newCostLines = new List<BomCostLine>();
        foreach (var rc in request.RawMaterialCosts)
        {
            var line = bom.Lines.FirstOrDefault(l => l.Id == rc.BomLineId);
            if (line is null) continue;

            var currency = (rc.CurrencyCode ?? "AED").ToUpperInvariant();
            decimal entryRate;
            try { entryRate = RateToAed(currency); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

            var costInQuote = rc.CostPerKg * entryRate / quoteRateToAed;
            rawMaterialTotal += costInQuote * line.QtyPerKg * (1 + line.WastagePct / 100);

            newCostLines.Add(new BomCostLine
            {
                BomHeaderId = bom.Id,
                BomLineId = line.Id,
                CostPerKg = rc.CostPerKg,
                CurrencyCode = currency,
                CostPerKgInQuoteCurrency = costInQuote
            });
        }

        // Calculate landed cost (already in quote currency)
        decimal landedCost = request.LandedCostType == LandedCostType.Percentage
            ? rawMaterialTotal * request.LandedCostValue / 100
            : request.LandedCostValue;

        decimal totalCost = rawMaterialTotal + landedCost + request.FohAmount;

        // Replace prior BomCost aggregate and BomCostLine rows for this BomHeader
        if (bom.Cost is not null) db.BomCosts.Remove(bom.Cost);
        var existingLines = await db.BomCostLines.Where(l => l.BomHeaderId == bom.Id).ToListAsync();
        if (existingLines.Count > 0) db.BomCostLines.RemoveRange(existingLines);
        db.BomCostLines.AddRange(newCostLines);

        var cost = new BomCost
        {
            BomHeaderId = bom.Id,
            RawMaterialCostTotal = rawMaterialTotal,
            LandedCostType = request.LandedCostType,
            LandedCostValue = request.LandedCostValue,
            FohAmount = request.FohAmount,
            TotalCostPerKg = totalCost,
            SubmittedByUserId = CurrentUserId
        };
        db.BomCosts.Add(cost);

        bom.TotalCostPerKg = totalCost;

        // Upsert ItemLastCost for each raw material (stores original entry cost + currency)
        var rawItemIds = newCostLines
            .Select(l => bom.Lines.First(bl => bl.Id == l.BomLineId).RawMaterialItemId)
            .Distinct()
            .ToList();
        var existingLastCosts = await db.ItemLastCosts
            .Where(l => rawItemIds.Contains(l.ItemId))
            .ToDictionaryAsync(l => l.ItemId);

        foreach (var costLine in newCostLines)
        {
            var bomLine = bom.Lines.First(bl => bl.Id == costLine.BomLineId);
            var itemId = bomLine.RawMaterialItemId;
            if (existingLastCosts.TryGetValue(itemId, out var lc))
            {
                lc.CostPerKg = costLine.CostPerKg;
                lc.CurrencyCode = costLine.CurrencyCode;
                lc.UpdatedAt = DateTime.UtcNow;
                lc.UpdatedByUserId = CurrentUserId;
            }
            else
            {
                db.ItemLastCosts.Add(new ItemLastCost
                {
                    ItemId = itemId,
                    CostPerKg = costLine.CostPerKg,
                    CurrencyCode = costLine.CurrencyCode,
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedByUserId = CurrentUserId
                });
                existingLastCosts[itemId] = null!; // avoid duplicate add inside same loop
            }
        }

        // Delete draft
        var draft = await db.CostingDrafts.FirstOrDefaultAsync(d => d.BomHeaderId == bom.Id);
        if (draft is not null) db.CostingDrafts.Remove(draft);

        req.Status = RequisitionStatus.MdReview;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Notify active MDs
        var mds = await db.Users.Where(u => u.Role == UserRole.ManagingDirector && u.IsActive).ToListAsync();
        foreach (var md in mds)
            await notificationService.SendAsync(md.Id,
                $"Costing complete, ready for approval: {req.RefNo}", req.Id, "QuotationRequest");

        return NoContent();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.API/Features/Costing/CostingDtos.cs BomPriceApproval.API/Features/Costing/CostingController.cs
git commit -m "feat(api): costing endpoints with multi-currency and permanent line history"
```

---

## Task 6: Backend integration tests

**Files:**
- Create: `BomPriceApproval.Tests/Costing/CostingTests.cs`

- [ ] **Step 1: Write the integration test file**

Create `BomPriceApproval.Tests/Costing/CostingTests.cs` with:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Costing;

public class CostingTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private async Task<int> CreateRequisitionWithBomInCostingPendingAsync(string quoteCurrency = "AED")
    {
        // 1. SalesPerson creates items + requisition
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "Test FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemDto>();
        var rmResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "Test RM", Type = "RawMaterial", LastPurchasePrice = (decimal?)null });
        var rm = await rmResp.Content.ReadFromJsonAsync<ItemDto>();

        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            ItemId = fg!.Id,
            ExpectedQty = 100m,
            CurrencyCode = quoteCurrency
        });
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();
        var requisitionId = created!.Id;

        // 2. Admin creates a process
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var procCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await _client.PostAsJsonAsync("/api/processes", new { Name = procCode, DisplayOrder = 99 });
        var process = await procResp.Content.ReadFromJsonAsync<ProcessDto>();

        // 3. BomCreator starts + submits BOM → CostingPending
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);
        await _client.PostAsync($"/api/bom/{requisitionId}/start", null);
        await _client.PostAsJsonAsync($"/api/bom/{requisitionId}/submit", new
        {
            Lines = new[]
            {
                new { ProcessId = process!.Id, RawMaterialItemId = rm!.Id, QtyPerKg = 0.85m, WastagePct = 2.0m }
            }
        });

        return requisitionId;
    }

    private async Task<CostingDetailDto> GetCostingAsync(int requisitionId)
    {
        var resp = await _client.GetAsync($"/api/costing/{requisitionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<CostingDetailDto>())!;
    }

    [Fact]
    public async Task Start_TransitionsCostingPendingToCostingInProgress()
    {
        var requisitionId = await CreateRequisitionWithBomInCostingPendingAsync();

        var acctToken = await LoginAsync("carol@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);

        var startResp = await _client.PostAsync($"/api/costing/{requisitionId}/start", null);
        startResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);
        var req = await _client.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{requisitionId}");
        req!.Status.Should().Be("CostingInProgress");
    }

    [Fact]
    public async Task SaveDraft_PersistsAndGetReturnsDraft()
    {
        var requisitionId = await CreateRequisitionWithBomInCostingPendingAsync();
        var acctToken = await LoginAsync("carol@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{requisitionId}/start", null);

        var detail = await GetCostingAsync(requisitionId);
        detail.BomLines.Should().HaveCount(1);
        var bomLineId = detail.BomLines[0].BomLineId;

        var draftResp = await _client.PutAsJsonAsync($"/api/costing/{requisitionId}/draft", new
        {
            Lines = new[] { new { BomLineId = bomLineId, CostPerKg = 1.25m, CurrencyCode = "USD" } },
            LandedCostType = "Percentage",
            LandedCostValue = 5m,
            FohAmount = 0.12m
        });
        draftResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Status still CostingInProgress
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);
        var req = await _client.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{requisitionId}");
        req!.Status.Should().Be("CostingInProgress");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        var reloaded = await GetCostingAsync(requisitionId);
        reloaded.Draft.Should().NotBeNull();
        reloaded.Draft!.Lines.Should().HaveCount(1);
        reloaded.Draft.Lines[0].CostPerKg.Should().Be(1.25m);
        reloaded.Draft.Lines[0].CurrencyCode.Should().Be("USD");
        reloaded.Draft.LandedCostValue.Should().Be(5m);
    }

    [Fact]
    public async Task Submit_ConvertsCurrencyWritesLinesUpsertsLastCostAndMovesToMdReview()
    {
        var requisitionId = await CreateRequisitionWithBomInCostingPendingAsync(quoteCurrency: "AED");
        var acctToken = await LoginAsync("carol@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{requisitionId}/start", null);

        var detail = await GetCostingAsync(requisitionId);
        var bomLineId = detail.BomLines[0].BomLineId;

        // Submit with USD cost — seeded USD rate is 3.6725, quote AED = 1.0
        var submitResp = await _client.PostAsJsonAsync($"/api/costing/{requisitionId}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 1.00m, CurrencyCode = "USD" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });
        submitResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Requisition → MdReview
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);
        var req = await _client.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{requisitionId}");
        req!.Status.Should().Be("MdReview");
    }

    [Fact]
    public async Task Submit_ReturnsBadRequest_WhenExchangeRateMissing()
    {
        var requisitionId = await CreateRequisitionWithBomInCostingPendingAsync(quoteCurrency: "AED");
        var acctToken = await LoginAsync("carol@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{requisitionId}/start", null);

        var detail = await GetCostingAsync(requisitionId);
        var bomLineId = detail.BomLines[0].BomLineId;

        // SAR rate not seeded → should fail
        var submitResp = await _client.PostAsJsonAsync($"/api/costing/{requisitionId}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 5.0m, CurrencyCode = "SAR" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });
        submitResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Recosting_NewRequisitionDoesNotModifyPreviousBomCostLines()
    {
        // Submit costing on requisition A
        var reqA = await CreateRequisitionWithBomInCostingPendingAsync();
        var acctToken = await LoginAsync("carol@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{reqA}/start", null);
        var detailA = await GetCostingAsync(reqA);
        var bomLineIdA = detailA.BomLines[0].BomLineId;
        await _client.PostAsJsonAsync($"/api/costing/{reqA}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineIdA, CostPerKg = 2.00m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        // Snapshot BomCost aggregate for A
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        var afterA = await GetCostingAsync(reqA);
        var totalA = afterA.TotalCostPerKg;

        // Submit costing on requisition B with a different cost for same raw material
        var reqB = await CreateRequisitionWithBomInCostingPendingAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{reqB}/start", null);
        var detailB = await GetCostingAsync(reqB);
        var bomLineIdB = detailB.BomLines[0].BomLineId;
        await _client.PostAsJsonAsync($"/api/costing/{reqB}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineIdB, CostPerKg = 9.99m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        // Requisition A total must be unchanged
        var reloadedA = await GetCostingAsync(reqA);
        reloadedA.TotalCostPerKg.Should().Be(totalA);
    }

    // ── DTOs ──
    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record ItemDto(int Id, string Code, string Description, string Type, int BranchId, bool IsActive, decimal? LastPurchasePrice);
    private record CustomerDto(int Id, string Code, string Name);
    private record ProcessDto(int Id, string Name, int DisplayOrder, bool IsActive);
    private record CreatedRequisition(int Id, string RefNo);
    private record RequisitionDto(int Id, string RefNo, string Status);
    private record LastCostDto(decimal CostPerKg, string CurrencyCode, DateTime UpdatedAt);
    private record CostingBomLineDto(int BomLineId, int ProcessId, string ProcessName,
        int RawMaterialItemId, string RawMaterialDescription, decimal QtyPerKg, decimal WastagePct,
        LastCostDto? LastCost);
    private record CostingDraftLineDto(int BomLineId, decimal CostPerKg, string CurrencyCode);
    private record CostingDraftDto(List<CostingDraftLineDto> Lines, string LandedCostType,
        decimal LandedCostValue, decimal FohAmount);
    private record CostingDetailDto(int Id, decimal RawMaterialCostTotal, string LandedCostType,
        decimal LandedCostValue, decimal FohAmount, decimal TotalCostPerKg, DateTime? SubmittedAt,
        List<CostingBomLineDto> BomLines, CostingDraftDto? Draft);
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~CostingTests"`
Expected: 5 tests pass.

If a test fails: read the actual error, debug the specific failing path, do not change the assertions blindly.

- [ ] **Step 3: Run full test suite to catch regressions**

Run: `dotnet test`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.Tests/Costing/CostingTests.cs
git commit -m "test(api): integration tests for costing draft, submit and immutability"
```

---

## Task 7: Add frontend TypeScript types

**Files:**
- Modify: `bom-web/src/types/api.ts`

- [ ] **Step 1: Append costing interfaces**

At the bottom of `bom-web/src/types/api.ts`, append:

```ts
// ─── Costing Entry ────────────────────────────────────────────────────────────

export interface LastCostInfo {
  costPerKg: number;
  currencyCode: string;
  updatedAt: string;
}

export interface CostingBomLine {
  bomLineId: number;
  processId: number;
  processName: string;
  rawMaterialItemId: number;
  rawMaterialDescription: string;
  qtyPerKg: number;
  wastagePct: number;
  lastCost: LastCostInfo | null;
}

export type LandedCostType = "Percentage" | "FixedValue";

export interface CostingDraftLine {
  bomLineId: number;
  costPerKg: number;
  currencyCode: string;
}

export interface CostingDraft {
  lines: CostingDraftLine[];
  landedCostType: LandedCostType;
  landedCostValue: number;
  fohAmount: number;
}

export interface CostingDetail {
  id: number;
  rawMaterialCostTotal: number;
  landedCostType: string;
  landedCostValue: number;
  fohAmount: number;
  totalCostPerKg: number;
  submittedAt: string | null;
  bomLines: CostingBomLine[];
  draft: CostingDraft | null;
}
```

- [ ] **Step 2: Type-check**

Run: `cd bom-web && npm run build`
Expected: Build succeeds with no type errors.

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/types/api.ts
git commit -m "feat(web): add costing types"
```

---

## Task 8: Frontend costing API hooks

**Files:**
- Create: `bom-web/src/features/costing/costingApi.ts`

- [ ] **Step 1: Create the hooks file**

```ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type { CostingDetail, LandedCostType } from "@/types/api";

export const costingKeys = {
  detail: (requisitionId: number) => ["costing", requisitionId] as const,
};

export interface CostingLinePayload {
  bomLineId: number;
  costPerKg: number;
  currencyCode: string;
}

export interface SaveCostingDraftPayload {
  lines: CostingLinePayload[];
  landedCostType: LandedCostType;
  landedCostValue: number;
  fohAmount: number;
}

export interface SubmitCostingPayload {
  rawMaterialCosts: CostingLinePayload[];
  landedCostType: LandedCostType;
  landedCostValue: number;
  fohAmount: number;
}

export function useCosting(requisitionId: number) {
  return useQuery({
    queryKey: costingKeys.detail(requisitionId),
    queryFn: () =>
      api.get<CostingDetail>(`/costing/${requisitionId}`).then((r) => r.data),
    enabled: Number.isFinite(requisitionId) && requisitionId > 0,
    retry: false,
  });
}

export function useStartCosting() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (requisitionId: number) =>
      api.post(`/costing/${requisitionId}/start`),
    onSuccess: (_d, requisitionId) => {
      qc.invalidateQueries({ queryKey: costingKeys.detail(requisitionId) });
    },
  });
}

export function useSaveCostingDraft() {
  return useMutation({
    mutationFn: ({
      requisitionId,
      payload,
    }: {
      requisitionId: number;
      payload: SaveCostingDraftPayload;
    }) => api.put(`/costing/${requisitionId}/draft`, payload),
  });
}

export function useSubmitCosting() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      requisitionId,
      payload,
    }: {
      requisitionId: number;
      payload: SubmitCostingPayload;
    }) => api.post(`/costing/${requisitionId}/submit`, payload),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: ["requisitions"] });
      qc.invalidateQueries({ queryKey: costingKeys.detail(requisitionId) });
    },
  });
}
```

- [ ] **Step 2: Type-check**

Run: `cd bom-web && npm run build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/features/costing/costingApi.ts
git commit -m "feat(web): costing API hooks"
```

---

## Task 9: Frontend Costing Entry page

**Files:**
- Create: `bom-web/src/features/costing/CostingEntryPage.tsx`

- [ ] **Step 1: Create the page**

```tsx
import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { useActiveExchangeRates } from "@/api/lookups";
import { useRequisition } from "@/features/requisitions/requisitionsApi";
import {
  useCosting,
  useStartCosting,
  useSaveCostingDraft,
  useSubmitCosting,
} from "./costingApi";
import type { CostingBomLine, LandedCostType } from "@/types/api";

interface LocalCostLine {
  bomLineId: number;
  processId: number;
  processName: string;
  rawMaterialItemId: number;
  rawMaterialDescription: string;
  qtyPerKg: number;
  wastagePct: number;
  costPerKg: number;
  currencyCode: string;
  lastCost: { costPerKg: number; currencyCode: string; updatedAt: string } | null;
}

const STALE_DAYS = 10;
const DEBOUNCE_MS = 800;

function daysSince(iso: string): number {
  const diff = Date.now() - new Date(iso).getTime();
  return Math.floor(diff / 86_400_000);
}

function rateToAed(currency: string, rates: { currencyCode: string; rateToAed: number }[]): number | null {
  if (currency === "AED") return 1;
  const row = rates.find((r) => r.currencyCode === currency);
  return row ? row.rateToAed : null;
}

function convert(amount: number, from: string, to: string, rates: { currencyCode: string; rateToAed: number }[]): number | null {
  const fromRate = rateToAed(from, rates);
  const toRate = rateToAed(to, rates);
  if (fromRate === null || toRate === null) return null;
  return (amount * fromRate) / toRate;
}

export default function CostingEntryPage() {
  const { id } = useParams<{ id: string }>();
  const requisitionId = Number(id);
  const navigate = useNavigate();

  const { data: requisition } = useRequisition(requisitionId);
  const { data: costing, isLoading: costingLoading, refetch } = useCosting(requisitionId);
  const { data: exchangeRates = [] } = useActiveExchangeRates();

  const startCosting = useStartCosting();
  const saveDraft = useSaveCostingDraft();
  const submitCosting = useSubmitCosting();

  const [lines, setLines] = useState<LocalCostLine[]>([]);
  const [landedCostType, setLandedCostType] = useState<LandedCostType>("Percentage");
  const [landedCostValue, setLandedCostValue] = useState<number>(0);
  const [fohAmount, setFohAmount] = useState<number>(0);
  const [saveStatus, setSaveStatus] = useState<"idle" | "saving" | "saved" | "error">("idle");
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [hydrated, setHydrated] = useState(false);
  const hasStartedRef = useRef(false);
  const debounceRef = useRef<number | undefined>(undefined);

  // Auto-start when CostingPending
  useEffect(() => {
    if (
      requisition?.status === "CostingPending" &&
      !hasStartedRef.current &&
      !startCosting.isPending
    ) {
      hasStartedRef.current = true;
      startCosting.mutate(requisitionId, { onSuccess: () => refetch() });
    }
  }, [requisition?.status, requisitionId]);

  // Hydrate local state from server once
  useEffect(() => {
    if (!costing || hydrated) return;
    const quoteCurrency = requisition?.currencyCode ?? "AED";

    const draftByLineId = new Map(
      (costing.draft?.lines ?? []).map((l) => [l.bomLineId, l]),
    );

    const local: LocalCostLine[] = costing.bomLines.map((bl: CostingBomLine) => {
      const draftLine = draftByLineId.get(bl.bomLineId);
      return {
        bomLineId: bl.bomLineId,
        processId: bl.processId,
        processName: bl.processName,
        rawMaterialItemId: bl.rawMaterialItemId,
        rawMaterialDescription: bl.rawMaterialDescription,
        qtyPerKg: bl.qtyPerKg,
        wastagePct: bl.wastagePct,
        costPerKg: draftLine?.costPerKg ?? bl.lastCost?.costPerKg ?? 0,
        currencyCode:
          draftLine?.currencyCode ?? bl.lastCost?.currencyCode ?? quoteCurrency,
        lastCost: bl.lastCost,
      };
    });
    setLines(local);

    if (costing.draft) {
      setLandedCostType(costing.draft.landedCostType);
      setLandedCostValue(costing.draft.landedCostValue);
      setFohAmount(costing.draft.fohAmount);
    }
    setHydrated(true);
  }, [costing, hydrated, requisition]);

  const quoteCurrency = requisition?.currencyCode ?? "AED";
  const currencyOptions = useMemo(() => {
    const codes = new Set(exchangeRates.map((r) => r.currencyCode));
    codes.add("AED");
    return Array.from(codes).sort();
  }, [exchangeRates]);

  const isReadOnly =
    requisition !== undefined &&
    requisition.status !== "CostingPending" &&
    requisition.status !== "CostingInProgress";

  // ── Auto-save ──
  function triggerAutoSave(nextLines: LocalCostLine[], nextType: LandedCostType, nextValue: number, nextFoh: number) {
    if (isReadOnly) return;
    if (debounceRef.current) window.clearTimeout(debounceRef.current);
    debounceRef.current = window.setTimeout(() => {
      setSaveStatus("saving");
      saveDraft.mutate(
        {
          requisitionId,
          payload: {
            lines: nextLines.map((l) => ({
              bomLineId: l.bomLineId,
              costPerKg: l.costPerKg,
              currencyCode: l.currencyCode,
            })),
            landedCostType: nextType,
            landedCostValue: nextValue,
            fohAmount: nextFoh,
          },
        },
        {
          onSuccess: () => setSaveStatus("saved"),
          onError: () => setSaveStatus("error"),
        },
      );
    }, DEBOUNCE_MS);
  }

  function updateLine(bomLineId: number, patch: Partial<LocalCostLine>) {
    const next = lines.map((l) => (l.bomLineId === bomLineId ? { ...l, ...patch } : l));
    setLines(next);
    triggerAutoSave(next, landedCostType, landedCostValue, fohAmount);
  }

  function updateLandedType(v: LandedCostType) {
    setLandedCostType(v);
    triggerAutoSave(lines, v, landedCostValue, fohAmount);
  }
  function updateLandedValue(v: number) {
    setLandedCostValue(v);
    triggerAutoSave(lines, landedCostType, v, fohAmount);
  }
  function updateFoh(v: number) {
    setFohAmount(v);
    triggerAutoSave(lines, landedCostType, landedCostValue, v);
  }

  // ── Totals (live preview in quote currency) ──
  const totals = useMemo(() => {
    let rawTotal = 0;
    for (const l of lines) {
      const inQuote = convert(l.costPerKg, l.currencyCode, quoteCurrency, exchangeRates);
      if (inQuote === null) continue;
      rawTotal += inQuote * l.qtyPerKg * (1 + l.wastagePct / 100);
    }
    const landed =
      landedCostType === "Percentage" ? (rawTotal * landedCostValue) / 100 : landedCostValue;
    const total = rawTotal + landed + fohAmount;
    return { rawTotal, landed, foh: fohAmount, total };
  }, [lines, landedCostType, landedCostValue, fohAmount, quoteCurrency, exchangeRates]);

  // Group by process for display
  const processGroups = useMemo(() => {
    const order: { processId: number; processName: string }[] = [];
    const seen = new Set<number>();
    for (const l of lines) {
      if (!seen.has(l.processId)) {
        seen.add(l.processId);
        order.push({ processId: l.processId, processName: l.processName });
      }
    }
    return order;
  }, [lines]);

  const canSubmit = lines.length > 0 && lines.every((l) => l.costPerKg > 0);

  function handleSubmit() {
    setSubmitError(null);
    submitCosting.mutate(
      {
        requisitionId,
        payload: {
          rawMaterialCosts: lines.map((l) => ({
            bomLineId: l.bomLineId,
            costPerKg: l.costPerKg,
            currencyCode: l.currencyCode,
          })),
          landedCostType,
          landedCostValue,
          fohAmount,
        },
      },
      {
        onSuccess: () => navigate(`/requisitions/${requisitionId}`),
        onError: (err: unknown) => {
          const e = err as { response?: { status?: number; data?: { message?: string } } };
          if (e.response?.status === 400 && e.response.data?.message) {
            setSubmitError(e.response.data.message);
          } else {
            setSubmitError("Failed to submit costing.");
          }
        },
      },
    );
  }

  // ── Render ──
  if (startCosting.isError) {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center text-destructive">
          Failed to start costing. Please go back and try again.
        </CardContent>
      </Card>
    );
  }

  if (costingLoading || startCosting.isPending || !requisition || !costing) {
    return <p className="text-sm text-muted-foreground">Loading costing…</p>;
  }

  return (
    <div className="space-y-4">
      <Link
        to={`/requisitions/${requisitionId}`}
        className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-4 w-4" /> Back to {requisition.refNo}
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            {isReadOnly ? "Costing (read-only)" : "Costing Entry"}
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            {requisition.itemDescription} — {requisition.customerName} · Quote currency:{" "}
            <span className="font-mono">{quoteCurrency}</span>
          </p>
        </div>
        <span className="text-xs text-muted-foreground">
          {saveStatus === "saving" && "Saving…"}
          {saveStatus === "saved" && "Saved ✓"}
          {saveStatus === "error" && <span className="text-destructive">Failed to save draft.</span>}
        </span>
      </div>

      {processGroups.map((group) => {
        const sectionLines = lines.filter((l) => l.processId === group.processId);
        return (
          <div key={group.processId} className="rounded-lg border border-border overflow-hidden">
            <div className="bg-muted/50 px-4 py-3">
              <span className="font-semibold text-sm">⚙ {group.processName}</span>
            </div>
            <div className="grid grid-cols-[2fr_80px_80px_120px_90px_2fr] gap-2 px-4 py-2 text-xs text-muted-foreground border-b border-border">
              <span>Raw Material</span>
              <span>Qty / kg</span>
              <span>Waste %</span>
              <span>Cost / kg</span>
              <span>Currency</span>
              <span>Last Price</span>
            </div>
            {sectionLines.map((line) => {
              const ageDays = line.lastCost ? daysSince(line.lastCost.updatedAt) : null;
              const stale = ageDays !== null && ageDays > STALE_DAYS;
              return (
                <div
                  key={line.bomLineId}
                  className="grid grid-cols-[2fr_80px_80px_120px_90px_2fr] gap-2 px-4 py-2 text-sm border-b border-border items-center"
                >
                  <span>{line.rawMaterialDescription}</span>
                  <span className="font-mono text-muted-foreground">{line.qtyPerKg.toFixed(4)}</span>
                  <span className="font-mono text-muted-foreground">{line.wastagePct.toFixed(2)}%</span>
                  <input
                    type="number"
                    step="0.0001"
                    min="0"
                    disabled={isReadOnly}
                    value={line.costPerKg || ""}
                    onChange={(e) =>
                      updateLine(line.bomLineId, { costPerKg: parseFloat(e.target.value) || 0 })
                    }
                    className="h-9 rounded-md border border-input bg-background px-2 text-sm font-mono"
                    aria-label={`Cost per kg for ${line.rawMaterialDescription}`}
                  />
                  <select
                    disabled={isReadOnly}
                    value={line.currencyCode}
                    onChange={(e) => updateLine(line.bomLineId, { currencyCode: e.target.value })}
                    className="h-9 rounded-md border border-input bg-background px-2 text-sm"
                    aria-label={`Currency for ${line.rawMaterialDescription}`}
                  >
                    {currencyOptions.map((c) => (
                      <option key={c} value={c}>
                        {c}
                      </option>
                    ))}
                  </select>
                  {line.lastCost ? (
                    <span className={`text-xs ${stale ? "text-yellow-400" : "text-muted-foreground"}`}>
                      {stale && "⚠ "}
                      {line.lastCost.currencyCode} {line.lastCost.costPerKg.toFixed(4)} · {ageDays} days ago
                      {stale && " — verify from ERP"}
                    </span>
                  ) : (
                    <span className="text-xs text-muted-foreground/60">No previous price</span>
                  )}
                </div>
              );
            })}
          </div>
        );
      })}

      {/* Landed cost & FOH */}
      <div className="rounded-lg border border-border px-4 py-3 space-y-3">
        <div className="text-xs font-semibold text-muted-foreground">Landed Cost &amp; Overheads</div>
        <div className="flex flex-wrap items-center gap-4 text-sm">
          <label className="flex items-center gap-2">
            <span className="text-muted-foreground">Type</span>
            <select
              disabled={isReadOnly}
              value={landedCostType}
              onChange={(e) => updateLandedType(e.target.value as LandedCostType)}
              className="h-9 rounded-md border border-input bg-background px-2 text-sm"
            >
              <option value="Percentage">Percentage</option>
              <option value="FixedValue">Fixed Value</option>
            </select>
          </label>
          <label className="flex items-center gap-2">
            <span className="text-muted-foreground">Value</span>
            <input
              type="number"
              step="0.0001"
              min="0"
              disabled={isReadOnly}
              value={landedCostValue || ""}
              onChange={(e) => updateLandedValue(parseFloat(e.target.value) || 0)}
              className="h-9 w-28 rounded-md border border-input bg-background px-2 text-sm font-mono"
            />
            <span className="text-xs text-muted-foreground">
              {landedCostType === "Percentage" ? "% of raw material total" : `${quoteCurrency} per kg`}
            </span>
          </label>
          <label className="flex items-center gap-2">
            <span className="text-muted-foreground">FOH (per kg)</span>
            <input
              type="number"
              step="0.0001"
              min="0"
              disabled={isReadOnly}
              value={fohAmount || ""}
              onChange={(e) => updateFoh(parseFloat(e.target.value) || 0)}
              className="h-9 w-28 rounded-md border border-input bg-background px-2 text-sm font-mono"
            />
            <span className="text-xs text-muted-foreground">{quoteCurrency}</span>
          </label>
        </div>
      </div>

      {/* Summary + Submit */}
      <div className="rounded-lg border border-border px-4 py-3 flex flex-wrap items-center justify-between gap-4">
        <div className="flex gap-6 text-sm">
          <div className="text-center">
            <div className="text-xs text-muted-foreground">Raw Material Total</div>
            <div className="font-mono font-semibold">
              {quoteCurrency} {totals.rawTotal.toFixed(4)}
            </div>
          </div>
          <div className="text-center">
            <div className="text-xs text-muted-foreground">Landed Cost</div>
            <div className="font-mono font-semibold">
              {quoteCurrency} {totals.landed.toFixed(4)}
            </div>
          </div>
          <div className="text-center">
            <div className="text-xs text-muted-foreground">FOH</div>
            <div className="font-mono font-semibold">
              {quoteCurrency} {totals.foh.toFixed(4)}
            </div>
          </div>
          <div className="text-center">
            <div className="text-xs text-muted-foreground">Total Cost / kg</div>
            <div className="font-mono font-semibold text-green-500">
              {quoteCurrency} {totals.total.toFixed(4)}
            </div>
          </div>
        </div>
        {!isReadOnly && (
          <div className="flex flex-col items-end gap-1">
            <Button
              onClick={handleSubmit}
              disabled={!canSubmit || submitCosting.isPending}
              title={!canSubmit ? "Enter cost for all lines before submitting" : undefined}
            >
              {submitCosting.isPending ? "Submitting…" : "Submit Costing ↗"}
            </Button>
            {submitError && <span className="text-xs text-destructive">{submitError}</span>}
          </div>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Type-check**

Run: `cd bom-web && npm run build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/features/costing/CostingEntryPage.tsx
git commit -m "feat(web): costing entry page with multi-currency and auto-save"
```

---

## Task 10: Route wiring + detail page button

**Files:**
- Modify: `bom-web/src/App.tsx`
- Modify: `bom-web/src/features/requisitions/RequisitionDetailPage.tsx`

- [ ] **Step 1: Register the costing route**

In `bom-web/src/App.tsx`, add this import alongside the other feature imports:

```tsx
import CostingEntryPage from "@/features/costing/CostingEntryPage";
```

Then inside `children`, after the `requisitions/:id/bom` route, add:

```tsx
{
  path: "requisitions/:id/costing",
  element: (
    <ProtectedRoute allow={["Accountant"]}>
      <CostingEntryPage />
    </ProtectedRoute>
  ),
},
```

- [ ] **Step 2: Enable "Continue Costing" label in detail page**

In `bom-web/src/features/requisitions/RequisitionDetailPage.tsx`, change `actionButtonFor` so the Accountant branch returns distinct labels:

```tsx
  if (role === "Accountant" && status === "CostingPending") {
    return { label: "Start Costing", path: "costing" };
  }
  if (role === "Accountant" && status === "CostingInProgress") {
    return { label: "Continue Costing", path: "costing" };
  }
```

(Replace the single `if (role === "Accountant" && (status === "CostingPending" || status === "CostingInProgress"))` block.)

- [ ] **Step 3: Remove the "Coming soon" disable in the action button**

Still in `RequisitionDetailPage.tsx`, change the `<Button>` to enable the `costing` path:

```tsx
<Button
  onClick={() => navigate(`/requisitions/${id}/${action.path}`)}
  disabled={action.path !== "bom" && action.path !== "costing"}
  title={action.path !== "bom" && action.path !== "costing" ? "Coming soon" : undefined}
>
  {action.label}
</Button>
```

- [ ] **Step 4: Type-check**

Run: `cd bom-web && npm run build`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/App.tsx bom-web/src/features/requisitions/RequisitionDetailPage.tsx
git commit -m "feat(web): route + detail page button for costing entry"
```

---

## Task 11: Frontend tests for the Costing Entry page

**Files:**
- Create: `bom-web/src/features/costing/CostingEntryPage.test.tsx`

- [ ] **Step 1: Write the component tests**

Create `bom-web/src/features/costing/CostingEntryPage.test.tsx` with:

```tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import CostingEntryPage from "./CostingEntryPage";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({
  api: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
  },
}));

const mockedApi = api as unknown as {
  get: ReturnType<typeof vi.fn>;
  post: ReturnType<typeof vi.fn>;
  put: ReturnType<typeof vi.fn>;
};

function renderPage(requisitionId = 5) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[`/requisitions/${requisitionId}/costing`]}>
        <Routes>
          <Route path="/requisitions/:id/costing" element={<CostingEntryPage />} />
          <Route path="/requisitions/:id" element={<div>Requisition Detail</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function mockGet(path: string, data: unknown) {
  mockedApi.get.mockImplementation((url: string) => {
    if (url.startsWith(path)) return Promise.resolve({ data });
    return Promise.reject(new Error(`Unexpected GET ${url}`));
  });
}

beforeEach(() => {
  vi.clearAllMocks();
});

const baseRequisition = {
  id: 5,
  refNo: "REQ-0005",
  status: "CostingInProgress",
  itemDescription: "PP Pipe 110mm",
  customerName: "Fujairah Pipes LLC",
  currencyCode: "AED",
  itemId: 1,
  customerId: 1,
  customerEmail: "",
  customerPhone: "",
  customerAddress: "",
  expectedQty: 100,
  exchangeRateSnapshot: null,
  branchId: 1,
  branchName: "Fujairah",
  salesPersonId: 1,
  salesPersonName: "Ali",
  createdAt: "2026-04-14T00:00:00Z",
  updatedAt: "2026-04-14T00:00:00Z",
  bom: null,
  approval: null,
};

const baseBomLine = {
  bomLineId: 100,
  processId: 1,
  processName: "Extrusion",
  rawMaterialItemId: 10,
  rawMaterialDescription: "HDPE Granules",
  qtyPerKg: 0.85,
  wastagePct: 2.0,
};

function defaultGetHandler(costing: unknown, requisition = baseRequisition) {
  mockedApi.get.mockImplementation((url: string) => {
    if (url.startsWith("/costing/")) return Promise.resolve({ data: costing });
    if (url.startsWith("/requisitions/")) return Promise.resolve({ data: requisition });
    if (url.startsWith("/exchange-rates")) return Promise.resolve({ data: [
      { id: 1, currencyCode: "USD", currencyName: "US Dollar", rateToAed: 3.6725, effectiveDate: "", isActive: true, setByName: "" },
    ] });
    return Promise.reject(new Error(`Unexpected GET ${url}`));
  });
}

describe("CostingEntryPage", () => {
  it("pre-fills cost from last cost when no draft", async () => {
    const lastCost = { costPerKg: 1.25, currencyCode: "USD", updatedAt: new Date().toISOString() };
    defaultGetHandler({
      id: 0,
      rawMaterialCostTotal: 0,
      landedCostType: "Percentage",
      landedCostValue: 0,
      fohAmount: 0,
      totalCostPerKg: 0,
      submittedAt: null,
      bomLines: [{ ...baseBomLine, lastCost }],
      draft: null,
    });
    renderPage();
    const costInput = await screen.findByLabelText(/Cost per kg for HDPE Granules/i);
    expect((costInput as HTMLInputElement).value).toBe("1.25");
  });

  it("prefers draft values over last cost", async () => {
    const lastCost = { costPerKg: 1.25, currencyCode: "USD", updatedAt: new Date().toISOString() };
    defaultGetHandler({
      id: 0,
      rawMaterialCostTotal: 0,
      landedCostType: "Percentage",
      landedCostValue: 5,
      fohAmount: 0.12,
      totalCostPerKg: 0,
      submittedAt: null,
      bomLines: [{ ...baseBomLine, lastCost }],
      draft: {
        lines: [{ bomLineId: 100, costPerKg: 9.99, currencyCode: "AED" }],
        landedCostType: "Percentage",
        landedCostValue: 5,
        fohAmount: 0.12,
      },
    });
    renderPage();
    const costInput = await screen.findByLabelText(/Cost per kg for HDPE Granules/i);
    expect((costInput as HTMLInputElement).value).toBe("9.99");
  });

  it("shows stale warning when lastCost is older than 10 days", async () => {
    const old = new Date();
    old.setDate(old.getDate() - 14);
    defaultGetHandler({
      id: 0,
      rawMaterialCostTotal: 0,
      landedCostType: "Percentage",
      landedCostValue: 0,
      fohAmount: 0,
      totalCostPerKg: 0,
      submittedAt: null,
      bomLines: [{ ...baseBomLine, lastCost: { costPerKg: 0.8, currencyCode: "USD", updatedAt: old.toISOString() } }],
      draft: null,
    });
    renderPage();
    expect(await screen.findByText(/verify from ERP/i)).toBeInTheDocument();
  });

  it("does not show stale warning when lastCost is 3 days old", async () => {
    const recent = new Date();
    recent.setDate(recent.getDate() - 3);
    defaultGetHandler({
      id: 0,
      rawMaterialCostTotal: 0,
      landedCostType: "Percentage",
      landedCostValue: 0,
      fohAmount: 0,
      totalCostPerKg: 0,
      submittedAt: null,
      bomLines: [{ ...baseBomLine, lastCost: { costPerKg: 0.8, currencyCode: "USD", updatedAt: recent.toISOString() } }],
      draft: null,
    });
    renderPage();
    await screen.findByLabelText(/Cost per kg for HDPE Granules/i);
    expect(screen.queryByText(/verify from ERP/i)).toBeNull();
  });

  it("disables submit when any cost is 0", async () => {
    defaultGetHandler({
      id: 0,
      rawMaterialCostTotal: 0,
      landedCostType: "Percentage",
      landedCostValue: 0,
      fohAmount: 0,
      totalCostPerKg: 0,
      submittedAt: null,
      bomLines: [{ ...baseBomLine, lastCost: null }],
      draft: null,
    });
    renderPage();
    const btn = await screen.findByRole("button", { name: /Submit Costing/i });
    expect(btn).toBeDisabled();
  });

  it("auto-saves draft after typing a cost", async () => {
    vi.useFakeTimers();
    defaultGetHandler({
      id: 0,
      rawMaterialCostTotal: 0,
      landedCostType: "Percentage",
      landedCostValue: 0,
      fohAmount: 0,
      totalCostPerKg: 0,
      submittedAt: null,
      bomLines: [{ ...baseBomLine, lastCost: null }],
      draft: null,
    });
    mockedApi.put.mockResolvedValue({ status: 204 });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

    renderPage();
    const costInput = await screen.findByLabelText(/Cost per kg for HDPE Granules/i);
    await user.clear(costInput);
    await user.type(costInput, "1.5");
    vi.advanceTimersByTime(900);

    await waitFor(() => {
      expect(mockedApi.put).toHaveBeenCalledWith(
        "/costing/5/draft",
        expect.objectContaining({
          lines: [expect.objectContaining({ costPerKg: 1.5 })],
        }),
      );
    });
    vi.useRealTimers();
  });

  it("navigates to detail page after successful submit", async () => {
    defaultGetHandler({
      id: 0,
      rawMaterialCostTotal: 0,
      landedCostType: "Percentage",
      landedCostValue: 0,
      fohAmount: 0,
      totalCostPerKg: 0,
      submittedAt: null,
      bomLines: [{
        ...baseBomLine,
        lastCost: { costPerKg: 1.25, currencyCode: "AED", updatedAt: new Date().toISOString() },
      }],
      draft: null,
    });
    mockedApi.post.mockResolvedValue({ status: 204 });
    const user = userEvent.setup();
    renderPage();
    const btn = await screen.findByRole("button", { name: /Submit Costing/i });
    await user.click(btn);
    expect(await screen.findByText("Requisition Detail")).toBeInTheDocument();
  });

  it("shows inline message when submit fails with missing exchange rate", async () => {
    defaultGetHandler({
      id: 0,
      rawMaterialCostTotal: 0,
      landedCostType: "Percentage",
      landedCostValue: 0,
      fohAmount: 0,
      totalCostPerKg: 0,
      submittedAt: null,
      bomLines: [{
        ...baseBomLine,
        lastCost: { costPerKg: 5, currencyCode: "SAR", updatedAt: new Date().toISOString() },
      }],
      draft: null,
    });
    mockedApi.post.mockRejectedValue({
      response: { status: 400, data: { message: "No exchange rate found for SAR. Contact admin." } },
    });
    const user = userEvent.setup();
    renderPage();
    const btn = await screen.findByRole("button", { name: /Submit Costing/i });
    await user.click(btn);
    expect(await screen.findByText(/No exchange rate found for SAR/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the tests**

Run: `cd bom-web && npm test -- CostingEntryPage`
Expected: All 8 tests pass.

- [ ] **Step 3: Run full frontend test suite**

Run: `cd bom-web && npm test`
Expected: No regressions.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/features/costing/CostingEntryPage.test.tsx
git commit -m "test(web): costing entry page"
```

---

## Final verification

- [ ] **Step 1: Run all backend tests**

Run: `dotnet test`
Expected: all green.

- [ ] **Step 2: Run all frontend tests**

Run: `cd bom-web && npm test`
Expected: all green.

- [ ] **Step 3: Manual smoke (optional)**

Start API (`dotnet run --project BomPriceApproval.API`) and web (`cd bom-web && npm run dev`). Log in as Accountant (`carol@test.com / Test@1234`), navigate to a requisition in `CostingPending`, click "Start Costing", enter prices in mixed currencies, confirm "Saved ✓" appears, reload page → values persist, hit Submit → redirects to detail page with status `MdReview`.

---

## Notes for the implementer

- Follow the existing feature-slice pattern: controllers call EF directly, no service layer.
- Branch isolation lives in the controller via `CurrentBranchId`; don't introduce a filter abstraction.
- Don't mock the database in tests — `WebApplicationFactory<Program>` + Testcontainers is the established pattern.
- Conversion math: `costInAed = costInX * rateToAed(X)`; `costInQuote = costInAed / rateToAed(Q)`; `AED` has an implicit rate of `1`.
- `BomCostLine` is immutable after submit in the business sense — but the submit endpoint deletes and re-inserts rows only for the **current** `BomHeader`. Old `BomHeader`'s rows are never touched. Task 6's "Recosting" test enforces this.
- `ItemLastCost` upsert stores the **entry currency and original cost**, not the converted value. The next costing entry pre-fills with the same currency, so the Accountant sees what they last entered.
- Keep file size under control: the page component is large but self-contained and mirrors the existing `BomEntryPage.tsx` shape — don't split prematurely.
