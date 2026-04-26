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

namespace BomPriceApproval.Tests.Admin;

public class AdminDeleteRequisitionTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> TokenAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    /// <summary>Seeds a minimal throwaway requisition and returns its Id.</summary>
    private async Task<int> SeedRequisitionAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sp = await db.Users.FirstAsync(u => u.Email == "ali@test.com");
        var customer = await db.Customers.FirstAsync(c => c.SalesPersonId == sp.Id);

        var req = new QuotationRequest
        {
            BranchId = sp.BranchId!.Value,
            SalesPersonId = sp.Id,
            CustomerId = customer.Id,
            CurrencyCode = "AED",
            Status = RequisitionStatus.BomPending,
        };
        db.QuotationRequests.Add(req);
        await db.SaveChangesAsync();
        return req.Id;
    }

    [Fact]
    public async Task DeleteRequisition_HappyPath_Returns204AndCascades()
    {
        var reqId = await SeedRequisitionAsync();

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,
                $"/api/admin/requisitions/{reqId}")
            {
                Content = JsonContent.Create(new { Reason = "test cleanup deletion" })
            });

            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // Behavioral: verify req is gone from DB
            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var gone = await verifyDb.QuotationRequests.FindAsync(reqId);
            gone.Should().BeNull("the requisition should have been hard-deleted");

            // Behavioral: verify audit row was written
            var auditRow = await verifyDb.AdminAuditLogs.FirstOrDefaultAsync(a =>
                a.EntityType == "Requisition" && a.EntityId == reqId);
            auditRow.Should().NotBeNull("an audit row must be written for the deletion");
        }
        finally
        {
            // Cleanup audit row (req itself is already deleted by the endpoint)
            using var cleanupScope = factory.Services.CreateScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var staleAudit = await cleanupDb.AdminAuditLogs.FirstOrDefaultAsync(a =>
                a.EntityType == "Requisition" && a.EntityId == reqId);
            if (staleAudit is not null)
            {
                cleanupDb.AdminAuditLogs.Remove(staleAudit);
                await cleanupDb.SaveChangesAsync();
            }
            // In case the endpoint failed and the req was not deleted, clean it up
            var staleReq = await cleanupDb.QuotationRequests.FindAsync(reqId);
            if (staleReq is not null)
            {
                cleanupDb.QuotationRequests.Remove(staleReq);
                await cleanupDb.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task DeleteRequisition_MissingReason_Returns400()
    {
        var reqId = await SeedRequisitionAsync();

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,
                $"/api/admin/requisitions/{reqId}")
            {
                Content = JsonContent.Create(new { Reason = "" })
            });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            using var cleanupScope = factory.Services.CreateScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var staleReq = await cleanupDb.QuotationRequests.FindAsync(reqId);
            if (staleReq is not null)
            {
                cleanupDb.QuotationRequests.Remove(staleReq);
                await cleanupDb.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task DeleteRequisition_AsNonAdmin_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("ali@test.com", "Test@1234"));

        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,
            "/api/admin/requisitions/1")
        {
            Content = JsonContent.Create(new { Reason = "unauthorized attempt" })
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteRequisition_UnknownId_Returns404()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,
            "/api/admin/requisitions/9999999")
        {
            Content = JsonContent.Create(new { Reason = "looking for nonexistent req" })
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteRequisition_NullBody_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

        // Send DELETE with no body — should hit the null-body guard and return 400
        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/admin/requisitions/1"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteRequisition_WithFullChildren_CascadesAll()
    {
        var token = await TokenAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        int reqId;
        int reqItemId;
        int bomHeaderId;
        int bomLineId;

        using (var seedScope = factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var sp           = await db.Users.FirstAsync(u => u.Email == "ali@test.com");
            var bomCreator   = await db.Users.FirstAsync(u => u.Email == "bob@test.com");
            var accountant   = await db.Users.FirstAsync(u => u.Email == "sara@test.com");
            var customer     = await db.Customers.FirstAsync(c => c.SalesPersonId == sp.Id);
            var item         = await db.Items.FirstAsync();
            var rawMat       = await db.Items.FirstAsync(i => i.Id != item.Id);
            var process      = await db.Processes.FirstAsync();

            var req = new QuotationRequest
            {
                CustomerId    = customer.Id,
                SalesPersonId = sp.Id,
                BranchId      = sp.BranchId!.Value,
                Status        = RequisitionStatus.CostingPending,
                CreatedAt     = DateTime.UtcNow
            };
            db.QuotationRequests.Add(req);
            await db.SaveChangesAsync();
            reqId = req.Id;

            var ri = new RequisitionItem { QuotationRequestId = req.Id, ItemId = item.Id, ExpectedQty = 100m };
            db.RequisitionItems.Add(ri);
            await db.SaveChangesAsync();
            reqItemId = ri.Id;

            var bom = new BomHeader
            {
                RequisitionItemId = ri.Id,
                CreatedByUserId   = bomCreator.Id
            };
            db.BomHeaders.Add(bom);
            await db.SaveChangesAsync();
            bomHeaderId = bom.Id;

            var bomLine = new BomLine
            {
                BomHeaderId       = bom.Id,
                ProcessId         = process.Id,
                RawMaterialItemId = rawMat.Id,
                QtyPerKg          = 0.8m,
                WastagePct        = 5m
            };
            db.BomLines.Add(bomLine);
            await db.SaveChangesAsync();
            bomLineId = bomLine.Id;

            // BomCost — exercises the BomCost → BomHeader cascade
            var bomCost = new BomCost
            {
                BomHeaderId          = bom.Id,
                RawMaterialCostTotal = 10m,
                LandedCostType       = LandedCostType.Percentage,
                LandedCostValue      = 5m,
                FohAmount            = 2m,
                TotalCostPerKg       = 17m,
                SubmittedByUserId    = accountant.Id,
                SubmittedAt          = DateTime.UtcNow
            };
            db.BomCosts.Add(bomCost);

            // BomCostLine — this FK is Restrict on BomLineId; exercises the cascade gap
            var bomCostLine = new BomCostLine
            {
                BomHeaderId            = bom.Id,
                BomLineId              = bomLine.Id,
                CostPerKg              = 10m,
                CurrencyCode           = "AED",
                CostPerKgInQuoteCurrency = 10m,
                CostPerKgInAed         = 10m
            };
            db.BomCostLines.Add(bomCostLine);

            await db.SaveChangesAsync();
        }

        try
        {
            var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/requisitions/{reqId}")
            {
                Content = JsonContent.Create(new { Reason = "cascade test deletion" })
            });
            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await verifyDb.QuotationRequests.AnyAsync(r => r.Id == reqId)).Should().BeFalse("req should be hard-deleted");
            (await verifyDb.RequisitionItems.AnyAsync(ri => ri.Id == reqItemId)).Should().BeFalse("RequisitionItem should cascade-delete");
            (await verifyDb.BomHeaders.AnyAsync(b => b.Id == bomHeaderId)).Should().BeFalse("BomHeader should cascade-delete");
            (await verifyDb.BomLines.AnyAsync(l => l.Id == bomLineId)).Should().BeFalse("BomLine should cascade-delete");
            (await verifyDb.BomCostLines.AnyAsync(l => l.BomHeaderId == bomHeaderId)).Should().BeFalse("BomCostLine should cascade-delete");
        }
        finally
        {
            // Best-effort cleanup of any leftover audit row and seed data
            using var cleanupScope = factory.Services.CreateScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var staleAudit = await cleanupDb.AdminAuditLogs
                .Where(a => a.EntityType == "Requisition" && a.EntityId == reqId).ToListAsync();
            if (staleAudit.Count > 0) { cleanupDb.AdminAuditLogs.RemoveRange(staleAudit); await cleanupDb.SaveChangesAsync(); }

            // If delete endpoint failed, clean up the seeded tree manually
            var staleBomCostLines = await cleanupDb.BomCostLines.Where(l => l.BomHeaderId == bomHeaderId).ToListAsync();
            if (staleBomCostLines.Count > 0) { cleanupDb.BomCostLines.RemoveRange(staleBomCostLines); await cleanupDb.SaveChangesAsync(); }

            var staleBomCosts = await cleanupDb.BomCosts.Where(c => c.BomHeaderId == bomHeaderId).ToListAsync();
            if (staleBomCosts.Count > 0) { cleanupDb.BomCosts.RemoveRange(staleBomCosts); await cleanupDb.SaveChangesAsync(); }

            var staleBomLines = await cleanupDb.BomLines.Where(l => l.BomHeaderId == bomHeaderId).ToListAsync();
            if (staleBomLines.Count > 0) { cleanupDb.BomLines.RemoveRange(staleBomLines); await cleanupDb.SaveChangesAsync(); }

            var staleHeaders = await cleanupDb.BomHeaders.Where(b => b.Id == bomHeaderId).ToListAsync();
            if (staleHeaders.Count > 0) { cleanupDb.BomHeaders.RemoveRange(staleHeaders); await cleanupDb.SaveChangesAsync(); }

            var staleItems = await cleanupDb.RequisitionItems.Where(ri => ri.Id == reqItemId).ToListAsync();
            if (staleItems.Count > 0) { cleanupDb.RequisitionItems.RemoveRange(staleItems); await cleanupDb.SaveChangesAsync(); }

            var staleReq = await cleanupDb.QuotationRequests.FindAsync(reqId);
            if (staleReq is not null) { cleanupDb.QuotationRequests.Remove(staleReq); await cleanupDb.SaveChangesAsync(); }
        }
    }
}
