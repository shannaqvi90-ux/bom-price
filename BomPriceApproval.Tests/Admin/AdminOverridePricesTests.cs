using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.Tests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Admin;

public class AdminOverridePricesTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> TokenAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    /// <summary>
    /// Seeds a fully-approved requisition (1 item, Status=Approved, with a current
    /// non-superseded QuotationApproval+ApprovalItem). Returns (reqId, riId, approvalId).
    /// Bypasses the workflow API and creates entities directly for test isolation.
    /// </summary>
    private async Task<(int ReqId, int ReqItemId, int ApprovalId, int OriginalMdUserId)> SeedApprovedReqAsync(
        string currencyCode = "AED",
        decimal? exchangeRate = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sp = await db.Users.FirstAsync(u => u.Email == "ali@test.com");
        var md = await db.Users.FirstAsync(u => u.Role == UserRole.ManagingDirector && u.IsActive);
        var customer = await db.Customers.FirstAsync(c => !c.IsDeleted);

        // Item — needs a fresh code to avoid unique-index collision
        var token = $"{Guid.NewGuid():N}".Substring(0, 8);
        var item = new Item
        {
            Code = $"OVR-{token}",
            Description = $"Override-test-item-{token}",
            Type = ItemType.FinishedGood,
            BranchId = sp.BranchId!.Value,
            IsActive = true
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var req = new QuotationRequest
        {
            BranchId = sp.BranchId!.Value,
            SalesPersonId = sp.Id,
            CustomerId = customer.Id,
            CurrencyCode = currencyCode,
            ExchangeRateSnapshot = exchangeRate,
            Status = RequisitionStatus.Approved,
        };
        db.QuotationRequests.Add(req);
        await db.SaveChangesAsync();

        var ri = new RequisitionItem
        {
            QuotationRequestId = req.Id,
            ItemId = item.Id,
            ExpectedQty = 100m,
            SortOrder = 1,
        };
        db.RequisitionItems.Add(ri);
        await db.SaveChangesAsync();

        var bom = new BomHeader
        {
            RequisitionItemId = ri.Id,
            CreatedByUserId = sp.Id,
            TotalCostPerKg = 8m,
        };
        db.BomHeaders.Add(bom);
        await db.SaveChangesAsync();

        var accountant = await db.Users.FirstAsync(u => u.Role == UserRole.Accountant && u.IsActive);
        var cost = new BomCost
        {
            BomHeaderId = bom.Id,
            RawMaterialCostTotal = 6m,
            FohAmount = 0.4m,
            TotalCostPerKg = 8m,
            SubmittedByUserId = accountant.Id,
        };
        db.BomCosts.Add(cost);
        await db.SaveChangesAsync();

        var approval = new QuotationApproval
        {
            QuotationRequestId = req.Id,
            ApprovedByUserId = md.Id,
            ApprovedAt = DateTime.UtcNow,
            IsApproved = true,
            IsSuperseded = false,
            RateSnapshot = exchangeRate,
        };
        db.QuotationApprovals.Add(approval);
        await db.SaveChangesAsync();

        var apItem = new ApprovalItem
        {
            QuotationApprovalId = approval.Id,
            RequisitionItemId = ri.Id,
            SalesPricePerKgAed = 10m,
            SalesPricePerKgForeign = exchangeRate.HasValue ? 10m / exchangeRate.Value : null,
            ProfitMarginPct = 20m,
            MaterialCostPct = 75m,
            OtherCostPct = 5m,
        };
        db.ApprovalItems.Add(apItem);
        await db.SaveChangesAsync();

        return (req.Id, ri.Id, approval.Id, md.Id);
    }

    private async Task CleanupAsync(int reqId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var auditRows = await db.AdminAuditLogs
            .Where(a => a.EntityType == "Requisition" && a.EntityId == reqId)
            .ToListAsync();
        if (auditRows.Count > 0) db.AdminAuditLogs.RemoveRange(auditRows);

        var notifs = await db.Notifications
            .Where(n => n.ReferenceType == nameof(NotificationType.PricesOverridden) && n.ReferenceId == reqId)
            .ToListAsync();
        if (notifs.Count > 0) db.Notifications.RemoveRange(notifs);

        var approvals = await db.QuotationApprovals
            .Include(a => a.Items)
            .Where(a => a.QuotationRequestId == reqId).ToListAsync();
        foreach (var a in approvals)
        {
            db.ApprovalItems.RemoveRange(a.Items);
            db.QuotationApprovals.Remove(a);
        }

        var ris = await db.RequisitionItems
            .Where(r => r.QuotationRequestId == reqId)
            .Include(r => r.BomHeader).ThenInclude(b => b!.Cost)
            .ToListAsync();
        foreach (var ri in ris)
        {
            if (ri.BomHeader is not null)
            {
                if (ri.BomHeader.Cost is not null) db.BomCosts.Remove(ri.BomHeader.Cost);
                db.BomHeaders.Remove(ri.BomHeader);
            }
            var itemId = ri.ItemId;
            db.RequisitionItems.Remove(ri);
            await db.SaveChangesAsync();
            // Item cleanup: only the test-created throwaway items (code starts with OVR-)
            var item = await db.Items.FindAsync(itemId);
            if (item is not null && item.Code.StartsWith("OVR-")) db.Items.Remove(item);
        }

        var req = await db.QuotationRequests.FindAsync(reqId);
        if (req is not null) db.QuotationRequests.Remove(req);
        await db.SaveChangesAsync();
    }

    private static object MakeBody(int reqItemId, decimal aed = 12m, decimal? foreign = null,
        decimal margin = 20m, decimal mat = 75m, decimal other = 5m, string reason = "smoke override test")
        => new
        {
            Reason = reason,
            Items = new[]
            {
                new
                {
                    RequisitionItemId = reqItemId,
                    SalesPricePerKgAed = aed,
                    SalesPricePerKgForeign = foreign,
                    ProfitMarginPct = margin,
                    MaterialCostPct = mat,
                    OtherCostPct = other
                }
            }
        };

    [Fact]
    public async Task Override_NotApproved_Returns400()
    {
        var (reqId, riId, _, _) = await SeedApprovedReqAsync();
        // Move it back to BomPending to simulate not-approved state
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.QuotationRequests.FindAsync(reqId);
            req!.Status = RequisitionStatus.BomPending;
            await db.SaveChangesAsync();
        }
        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/override-prices",
                MakeBody(riId));
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally { await CleanupAsync(reqId); }
    }

    [Fact]
    public async Task Override_NonAdmin_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("ali@test.com", "Test@1234"));
        var resp = await _client.PostAsJsonAsync(
            "/api/admin/requisitions/1/override-prices",
            MakeBody(1));
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Override_ReasonTooShort_Returns400()
    {
        var (reqId, riId, _, _) = await SeedApprovedReqAsync();
        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/override-prices",
                MakeBody(riId, reason: "x"));
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally { await CleanupAsync(reqId); }
    }

    [Fact]
    public async Task Override_MissingItem_Returns400()
    {
        var (reqId, riId, _, _) = await SeedApprovedReqAsync();
        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
            // Body has bogus RequisitionItemId not on the req
            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/override-prices",
                MakeBody(riId + 99999));
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally { await CleanupAsync(reqId); }
    }

    [Fact]
    public async Task Override_NegativePrice_Returns400()
    {
        var (reqId, riId, _, _) = await SeedApprovedReqAsync();
        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/override-prices",
                MakeBody(riId, aed: -5m));
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally { await CleanupAsync(reqId); }
    }

    [Fact]
    public async Task Override_PercentSumOff_Returns400()
    {
        var (reqId, riId, _, _) = await SeedApprovedReqAsync();
        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
            // 30 + 60 + 5 = 95 (not 100)
            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/override-prices",
                MakeBody(riId, margin: 30m, mat: 60m, other: 5m));
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally { await CleanupAsync(reqId); }
    }

    [Fact]
    public async Task Override_AedCurrency_NoForeignRequired_Succeeds()
    {
        var (reqId, riId, _, _) = await SeedApprovedReqAsync();
        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/override-prices",
                MakeBody(riId, foreign: null)); // foreign null OK for AED
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally { await CleanupAsync(reqId); }
    }

    [Fact]
    public async Task Override_NonAedCurrency_NullForeign_Returns400()
    {
        var (reqId, riId, _, _) = await SeedApprovedReqAsync(currencyCode: "USD", exchangeRate: 3.67m);
        try
        {
            // Setup an active USD rate so the endpoint can re-snap
            using (var s = factory.Services.CreateScope())
            {
                var setupDb = s.ServiceProvider.GetRequiredService<AppDbContext>();
                if (!await setupDb.ExchangeRates.AnyAsync(e => e.CurrencyCode == "USD" && e.IsActive))
                {
                    var admin = await setupDb.Users.FirstAsync(u => u.Email == "admin@test.com");
                    setupDb.ExchangeRates.Add(new ExchangeRate
                    {
                        CurrencyCode = "USD",
                        CurrencyName = "US Dollar",
                        RateToAed = 3.67m,
                        IsActive = true,
                        EffectiveDate = DateTime.UtcNow,
                        SetByUserId = admin.Id,
                    });
                    await setupDb.SaveChangesAsync();
                }
            }

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/override-prices",
                MakeBody(riId, foreign: null)); // null foreign on non-AED is rejected
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally { await CleanupAsync(reqId); }
    }

    [Fact]
    public async Task Override_HappyPath_SupersedesOldApproval_CreatesNew_AuditWritten_StatusStaysApproved()
    {
        var (reqId, riId, oldApprovalId, _) = await SeedApprovedReqAsync();
        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/override-prices",
                MakeBody(riId, aed: 12.5m, margin: 22m, mat: 70m, other: 8m,
                    reason: "market price moved"));

            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var newApprovalId = body.GetProperty("newApprovalId").GetInt32();
            var supersededId = body.GetProperty("supersededApprovalId").GetInt32();
            supersededId.Should().Be(oldApprovalId);
            newApprovalId.Should().BePositive().And.NotBe(oldApprovalId);

            using var verifyScope = factory.Services.CreateScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Old approval superseded
            var oldApproval = await db.QuotationApprovals.FindAsync(oldApprovalId);
            oldApproval!.IsSuperseded.Should().BeTrue();
            oldApproval.SupersededAt.Should().NotBeNull();

            // New approval created with override values
            var newApproval = await db.QuotationApprovals
                .Include(a => a.Items)
                .FirstAsync(a => a.Id == newApprovalId);
            newApproval.IsSuperseded.Should().BeFalse();
            newApproval.IsApproved.Should().BeTrue();
            newApproval.Notes.Should().StartWith("[Override]");
            newApproval.Items.Should().ContainSingle();
            newApproval.Items.First().SalesPricePerKgAed.Should().Be(12.5m);
            newApproval.Items.First().ProfitMarginPct.Should().Be(22m);

            // Status stays Approved (D10)
            var req = await db.QuotationRequests.FindAsync(reqId);
            req!.Status.Should().Be(RequisitionStatus.Approved);

            // Audit row written
            var auditRow = await db.AdminAuditLogs
                .FirstOrDefaultAsync(a => a.EntityType == "Requisition"
                                          && a.EntityId == reqId
                                          && a.ActionType == AdminActionType.OverridePrices);
            auditRow.Should().NotBeNull();
            auditRow!.Reason.Should().Be("market price moved");
        }
        finally { await CleanupAsync(reqId); }
    }

    [Fact]
    public async Task Override_AuditBeforeAfter_ContentAsserted()
    {
        var (reqId, riId, oldApprovalId, _) = await SeedApprovedReqAsync();
        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/override-prices",
                MakeBody(riId, aed: 15m, reason: "audit content check"));
            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            using var verifyScope = factory.Services.CreateScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var auditRow = await db.AdminAuditLogs
                .FirstAsync(a => a.EntityType == "Requisition"
                                 && a.EntityId == reqId
                                 && a.ActionType == AdminActionType.OverridePrices);

            // Forever-decision: enums serialized as strings (audit serializer
            // uses default .NET PascalCase keys)
            auditRow.AfterJson.Should().NotContain("\"IsApproved\":1");
            // Old prices in BeforeJson — reference the old approval row
            auditRow.BeforeJson.Should().Contain("\"Id\": " + oldApprovalId);
            auditRow.BeforeJson.Should().Contain("10");  // original SalesPricePerKgAed
            // New prices in AfterJson
            auditRow.AfterJson.Should().Contain("15");
            auditRow.AfterJson.Should().Contain("\"SupersededApprovalId\": " + oldApprovalId);
        }
        finally { await CleanupAsync(reqId); }
    }

    [Fact]
    public async Task Override_RateSnapshot_ReSnappedOnNonAed()
    {
        // Seed with USD currency, original rate = 3.50
        var (reqId, riId, _, _) = await SeedApprovedReqAsync(currencyCode: "USD", exchangeRate: 3.50m);
        try
        {
            // Insert a "newer" active rate that the override will pick up
            using (var s = factory.Services.CreateScope())
            {
                var setupDb = s.ServiceProvider.GetRequiredService<AppDbContext>();
                // Mark any existing USD rates as inactive first
                var existingRates = await setupDb.ExchangeRates
                    .Where(e => e.CurrencyCode == "USD")
                    .ToListAsync();
                foreach (var r in existingRates) r.IsActive = false;
                var admin = await setupDb.Users.FirstAsync(u => u.Email == "admin@test.com");
                setupDb.ExchangeRates.Add(new ExchangeRate
                {
                    CurrencyCode = "USD",
                    CurrencyName = "US Dollar",
                    RateToAed = 3.70m,
                    IsActive = true,
                    EffectiveDate = DateTime.UtcNow,
                    SetByUserId = admin.Id,
                });
                await setupDb.SaveChangesAsync();
            }

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/override-prices",
                MakeBody(riId, aed: 14m, foreign: 14m / 3.70m, reason: "rate snap test"));
            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            using var verifyScope = factory.Services.CreateScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var newApproval = await db.QuotationApprovals
                .Where(a => a.QuotationRequestId == reqId && !a.IsSuperseded)
                .FirstAsync();
            newApproval.RateSnapshot.Should().Be(3.70m, "the override should re-snap to the current active rate (D7)");
        }
        finally
        {
            await CleanupAsync(reqId);
            // Cleanup our seeded test rates
            using var s = factory.Services.CreateScope();
            var rateDb = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var stale = await rateDb.ExchangeRates
                .Where(e => e.CurrencyCode == "USD" && (e.RateToAed == 3.50m || e.RateToAed == 3.70m))
                .ToListAsync();
            if (stale.Count > 0) { rateDb.ExchangeRates.RemoveRange(stale); await rateDb.SaveChangesAsync(); }
        }
    }
}
