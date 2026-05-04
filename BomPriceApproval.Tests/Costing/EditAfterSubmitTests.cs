using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.Tests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Costing;

/// <summary>
/// Verifies the V3 extended edit window: accountant can edit BOM lines and
/// cost data while the requisition is in MdPricing status; each save audits
/// to AdminAuditLog and the FIRST save per MdPricing window notifies all
/// active MDs (subsequent saves in the same window stay silent).
/// </summary>
public class EditAfterSubmitTests(WebApplicationFactory<Program> factory)
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

    /// <summary>
    /// Creates a throwaway requisition seeded into MdPricing status with one
    /// FG, one BOM line, and one BomCost row. Returns the req id.
    /// Uses Branch 1 (Fujairah) to match the seeded ali/sara branch assignments.
    /// Sara is assigned to all branches via V2.3-A seed, so AccountantAuthorizedForBranch
    /// passes for branch 1.
    /// </summary>
    private async Task<int> SeedReqInMdPricingAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sales = await db.Users.FirstAsync(u => u.Email == "ali@test.com");
        var branchId = sales.BranchId ?? 1;

        var customer = new Customer
        {
            Code = $"CUST-T-{Guid.NewGuid():N}".Substring(0, 12),
            Name = $"Test Customer {Guid.NewGuid():N}".Substring(0, 30),
            SalesPersonId = sales.Id,
            CreatedByUserId = sales.Id
        };
        db.Customers.Add(customer);

        // Use branch-specific items so BOM update branch validation passes.
        var fgItem = await db.Items.FirstAsync(i => i.Type == ItemType.FinishedGood && i.IsActive && i.BranchId == branchId);
        var rmItem = await db.Items.FirstAsync(i => i.Type == ItemType.RawMaterial && i.IsActive && i.BranchId == branchId);
        await db.SaveChangesAsync();

        var req = new QuotationRequest
        {
            BranchId = branchId,
            SalesPersonId = sales.Id,
            CustomerId = customer.Id,
            CurrencyCode = "AED",
            Status = RequisitionStatus.MdPricing,
            MdPricingNotifiedAfterEdit = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.QuotationRequests.Add(req);
        await db.SaveChangesAsync();

        var ri = new RequisitionItem
        {
            QuotationRequestId = req.Id,
            ItemId = fgItem.Id,
            ExpectedQty = 1000,
            SortOrder = 1,
            HasPrinting = false
        };
        db.RequisitionItems.Add(ri);
        await db.SaveChangesAsync();

        var bomHeader = new BomHeader { RequisitionItemId = ri.Id, CreatedByUserId = sales.Id };
        db.BomHeaders.Add(bomHeader);
        await db.SaveChangesAsync();

        var process = await db.Processes.FirstAsync();
        var bomLine = new BomLine
        {
            BomHeaderId = bomHeader.Id,
            ProcessId = process.Id,
            RawMaterialItemId = rmItem.Id,
            QtyPerKg = 1.0m,
            Micron = "50",
            WastagePct = 5
        };
        db.BomLines.Add(bomLine);
        await db.SaveChangesAsync();

        var bomCost = new BomCost
        {
            BomHeaderId = bomHeader.Id,
            PrintingCostPerKg = null,
            PrintingCostCurrency = null,
            FohPerKg = 0.5m,
            TransportPerKg = 0.2m,
            CommissionPerKg = 0.1m,
            RawMaterialCostTotal = 5.25m,
            LandedCostType = LandedCostType.FixedValue,
            LandedCostValue = 0m,
            FohAmount = 0.5m,
            TotalCostPerKg = 6.05m,
            SubmittedByUserId = sales.Id
        };
        db.BomCosts.Add(bomCost);

        var bomCostLine = new BomCostLine
        {
            BomHeaderId = bomHeader.Id,
            BomLineId = bomLine.Id,
            CostPerKg = 5.0m,
            CurrencyCode = "AED",
            CostPerKgInQuoteCurrency = 5.0m,
            CostPerKgInAed = 5.0m
        };
        db.BomCostLines.Add(bomCostLine);
        await db.SaveChangesAsync();

        return req.Id;
    }

    private async Task CleanupReqAsync(int reqId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var audit = await db.AdminAuditLogs
            .Where(a => a.EntityType == "QuotationRequest" && a.EntityId == reqId)
            .ToListAsync();
        if (audit.Count > 0) db.AdminAuditLogs.RemoveRange(audit);

        var notifs = await db.Notifications
            .Where(n => n.ReferenceType == "QuotationRequest" && n.ReferenceId == reqId)
            .ToListAsync();
        if (notifs.Count > 0) db.Notifications.RemoveRange(notifs);

        var req = await db.QuotationRequests
            .Include(r => r.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(b => b!.Lines)
            .FirstOrDefaultAsync(r => r.Id == reqId);
        if (req is null) { await db.SaveChangesAsync(); return; }

        var customerId = req.CustomerId;
        foreach (var ri in req.Items)
        {
            if (ri.BomHeader is not null)
            {
                var costLines = await db.BomCostLines.Where(cl => cl.BomHeaderId == ri.BomHeader.Id).ToListAsync();
                if (costLines.Count > 0) db.BomCostLines.RemoveRange(costLines);
                var cost = await db.BomCosts.FirstOrDefaultAsync(c => c.BomHeaderId == ri.BomHeader.Id);
                if (cost is not null) db.BomCosts.Remove(cost);
                db.BomLines.RemoveRange(ri.BomHeader.Lines);
                db.BomHeaders.Remove(ri.BomHeader);
            }
        }
        db.RequisitionItems.RemoveRange(req.Items);
        db.QuotationRequests.Remove(req);
        var customer = await db.Customers.FindAsync(customerId);
        if (customer is not null) db.Customers.Remove(customer);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateBom_AtMdPricing_AccountantSucceeds_AndAudits_AndNotifiesMdsOnce()
    {
        var reqId = await SeedReqInMdPricingAsync();
        try
        {
            var token = await TokenAsync("sara@test.com", "Test@1234");
            AuthAs(token);

            int fgId, lineId, currentItemId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ri = await db.RequisitionItems.FirstAsync(x => x.QuotationRequestId == reqId);
                fgId = ri.Id;
                var bom = await db.BomHeaders.FirstAsync(b => b.RequisitionItemId == fgId);
                var line = await db.BomLines.FirstAsync(l => l.BomHeaderId == bom.Id);
                lineId = line.Id;
                currentItemId = line.RawMaterialItemId;
            }

            // First edit — should succeed and trigger MD notification
            var resp1 = await _client.PutAsJsonAsync($"/api/costing/{reqId}/bom", new
            {
                FinishedGoodId = fgId,
                Lines = new[]
                {
                    new { Id = (int?)lineId, ItemId = currentItemId, QtyPerKg = 1.5m, Micron = (string?)"60", Delete = false }
                }
            });
            resp1.StatusCode.Should().Be(HttpStatusCode.OK);

            // Second edit — should succeed but NOT re-notify (notify-once guard)
            var resp2 = await _client.PutAsJsonAsync($"/api/costing/{reqId}/bom", new
            {
                FinishedGoodId = fgId,
                Lines = new[]
                {
                    new { Id = (int?)lineId, ItemId = currentItemId, QtyPerKg = 2.0m, Micron = (string?)"70", Delete = false }
                }
            });
            resp2.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope2 = factory.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

            // Exactly 2 audit rows (one per edit)
            var audit = await db2.AdminAuditLogs
                .Where(a => a.EntityType == "QuotationRequest"
                         && a.EntityId == reqId
                         && a.ActionType == AdminActionType.AccountantEditAfterSubmit)
                .ToListAsync();
            audit.Should().HaveCount(2);

            // Notifications sent ONCE per MD (not twice)
            var mdCount = await db2.Users
                .CountAsync(u => u.Role == UserRole.ManagingDirector && u.IsActive);
            var notifs = await db2.Notifications
                .Where(n => n.ReferenceType == "QuotationRequest" && n.ReferenceId == reqId)
                .ToListAsync();
            notifs.Should().HaveCount(mdCount, "exactly one notif per MD across the whole session");
            notifs.Should().AllSatisfy(n => n.Message.Should().Contain("costing edited"));

            // Flag is flipped
            var req = await db2.QuotationRequests.AsNoTracking().FirstAsync(r => r.Id == reqId);
            req.MdPricingNotifiedAfterEdit.Should().BeTrue();
        }
        finally
        {
            await CleanupReqAsync(reqId);
        }
    }

    [Fact]
    public async Task SaveCostData_AtMdPricing_AccountantSucceeds_AndAudits_AndDoesNotDoubleNotify()
    {
        var reqId = await SeedReqInMdPricingAsync();
        try
        {
            var token = await TokenAsync("sara@test.com", "Test@1234");
            AuthAs(token);

            int fgId, lineId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ri = await db.RequisitionItems.FirstAsync(x => x.QuotationRequestId == reqId);
                fgId = ri.Id;
                var bom = await db.BomHeaders.FirstAsync(b => b.RequisitionItemId == fgId);
                var line = await db.BomLines.FirstAsync(l => l.BomHeaderId == bom.Id);
                lineId = line.Id;
            }

            // First cost-data save — should succeed and trigger MD notification
            var resp1 = await _client.PutAsJsonAsync($"/api/costing/{reqId}/cost-data", new
            {
                FinishedGoods = new[]
                {
                    new
                    {
                        RequisitionItemId = fgId,
                        FohPerKg = 0.6m,
                        TransportPerKg = 0.2m,
                        CommissionPerKg = 0.1m,
                        PrintingCostPerKg = (decimal?)null,
                        PrintingCostCurrency = (string?)null,
                        RawMaterialCosts = new[]
                        {
                            new { BomLineId = lineId, CostPerKg = 5.0m, CurrencyCode = "AED", WastagePercent = 5m }
                        }
                    }
                }
            });
            resp1.StatusCode.Should().Be(HttpStatusCode.OK);

            // Second cost-data save — should succeed but NOT re-notify
            var resp2 = await _client.PutAsJsonAsync($"/api/costing/{reqId}/cost-data", new
            {
                FinishedGoods = new[]
                {
                    new
                    {
                        RequisitionItemId = fgId,
                        FohPerKg = 0.7m,
                        TransportPerKg = 0.2m,
                        CommissionPerKg = 0.1m,
                        PrintingCostPerKg = (decimal?)null,
                        PrintingCostCurrency = (string?)null,
                        RawMaterialCosts = new[]
                        {
                            new { BomLineId = lineId, CostPerKg = 5.5m, CurrencyCode = "AED", WastagePercent = 5m }
                        }
                    }
                }
            });
            resp2.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope2 = factory.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

            // Exactly 2 audit rows (one per save)
            var audit = await db2.AdminAuditLogs
                .Where(a => a.EntityType == "QuotationRequest"
                         && a.EntityId == reqId
                         && a.ActionType == AdminActionType.AccountantEditAfterSubmit)
                .ToListAsync();
            audit.Should().HaveCount(2);

            // Notifications sent exactly once per MD (not twice)
            var mdCount = await db2.Users
                .CountAsync(u => u.Role == UserRole.ManagingDirector && u.IsActive);
            var notifs = await db2.Notifications
                .Where(n => n.ReferenceType == "QuotationRequest" && n.ReferenceId == reqId)
                .ToListAsync();
            notifs.Should().HaveCount(mdCount);
        }
        finally
        {
            await CleanupReqAsync(reqId);
        }
    }

    [Fact]
    public async Task UpdateBom_AtCustomerConfirm_Returns400()
    {
        var reqId = await SeedReqInMdPricingAsync();
        try
        {
            // Flip the seeded req to CustomerConfirm status
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var req = await db.QuotationRequests.FirstAsync(r => r.Id == reqId);
                req.Status = RequisitionStatus.CustomerConfirm;
                req.MdPricingNotifiedAfterEdit = false;
                await db.SaveChangesAsync();
            }

            var token = await TokenAsync("sara@test.com", "Test@1234");
            AuthAs(token);

            int fgId, lineId, currentItemId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ri = await db.RequisitionItems.FirstAsync(x => x.QuotationRequestId == reqId);
                fgId = ri.Id;
                var bom = await db.BomHeaders.FirstAsync(b => b.RequisitionItemId == fgId);
                var line = await db.BomLines.FirstAsync(l => l.BomHeaderId == bom.Id);
                lineId = line.Id;
                currentItemId = line.RawMaterialItemId;
            }

            // BOM edit at CustomerConfirm must be rejected
            var resp = await _client.PutAsJsonAsync($"/api/costing/{reqId}/bom", new
            {
                FinishedGoodId = fgId,
                Lines = new[]
                {
                    new { Id = (int?)lineId, ItemId = currentItemId, QtyPerKg = 1.5m, Micron = (string?)"60", Delete = false }
                }
            });
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            await CleanupReqAsync(reqId);
        }
    }

    [Fact]
    public async Task UpdateBom_NotifResetsAfterAdminUnlockCosting()
    {
        var reqId = await SeedReqInMdPricingAsync();
        try
        {
            var token = await TokenAsync("sara@test.com", "Test@1234");
            AuthAs(token);

            int fgId, lineId, currentItemId;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ri = await db.RequisitionItems.FirstAsync(x => x.QuotationRequestId == reqId);
                fgId = ri.Id;
                var bom = await db.BomHeaders.FirstAsync(b => b.RequisitionItemId == fgId);
                var line = await db.BomLines.FirstAsync(l => l.BomHeaderId == bom.Id);
                lineId = line.Id;
                currentItemId = line.RawMaterialItemId;
            }

            // First edit at MdPricing — triggers notification
            (await _client.PutAsJsonAsync($"/api/costing/{reqId}/bom", new
            {
                FinishedGoodId = fgId,
                Lines = new[]
                {
                    new { Id = (int?)lineId, ItemId = currentItemId, QtyPerKg = 1.5m, Micron = (string?)"60", Delete = false }
                }
            })).StatusCode.Should().Be(HttpStatusCode.OK);

            int notifCountAfterFirstEdit;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                notifCountAfterFirstEdit = await db.Notifications
                    .CountAsync(n => n.ReferenceType == "QuotationRequest" && n.ReferenceId == reqId);
            }

            // Simulate re-entering MdPricing (as happens after admin rollback + re-submit):
            // Roll back to Costing + reset flag, then submit again → MdPricing.
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var req = await db.QuotationRequests.FirstAsync(r => r.Id == reqId);
                req.Status = RequisitionStatus.Costing;
                req.MdPricingNotifiedAfterEdit = false;
                await db.SaveChangesAsync();
            }

            // Re-submit costing (Costing → MdPricing), which resets the flag to false
            // and fires the MD "awaiting your margin" notification batch.
            var subResp = await _client.PostAsync($"/api/costing/{reqId}/submit", null);
            subResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // Capture active MD count to compute the expected notif increment.
            int mdCount;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                mdCount = await db.Users.CountAsync(u => u.Role == UserRole.ManagingDirector && u.IsActive);
            }

            // Second edit in the new MdPricing window — must fire a fresh notification batch
            (await _client.PutAsJsonAsync($"/api/costing/{reqId}/bom", new
            {
                FinishedGoodId = fgId,
                Lines = new[]
                {
                    new { Id = (int?)lineId, ItemId = currentItemId, QtyPerKg = 2.5m, Micron = (string?)"80", Delete = false }
                }
            })).StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope2 = factory.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var totalNotifs = await db2.Notifications
                .CountAsync(n => n.ReferenceType == "QuotationRequest" && n.ReferenceId == reqId);

            // After rollback: re-submit fires mdCount notifs + second BOM edit fires mdCount notifs
            totalNotifs.Should().BeGreaterThanOrEqualTo(notifCountAfterFirstEdit + 2 * mdCount,
                "re-submit fires one MD notif batch and second BOM edit fires another batch");
        }
        finally
        {
            await CleanupReqAsync(reqId);
        }
    }
}
