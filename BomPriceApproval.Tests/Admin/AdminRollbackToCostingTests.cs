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

/// <summary>
/// V3 admin "Rollback to Costing" — replaces the legacy V2.3 UnlockCosting action.
/// Allowed only from MdPricing → Costing. For deeper rollbacks the admin uses the
/// generic /rollback-status endpoint.
/// </summary>
public class AdminRollbackToCostingTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> TokenAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    /// <summary>
    /// Seeds a minimal direct-DB requisition with the given V3 status. Used for
    /// status-guard tests where we don't care about the workflow data underneath.
    /// </summary>
    private async Task<int> SeedRequisitionAsync(RequisitionStatus status)
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
            Status = status,
        };
        db.QuotationRequests.Add(req);
        await db.SaveChangesAsync();
        return req.Id;
    }

    private async Task CleanupAsync(int? reqId, int? auditId = null)
    {
        using var cleanupScope = factory.Services.CreateScope();
        var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (auditId.HasValue)
        {
            var staleAudit = await cleanupDb.AdminAuditLogs.FindAsync(auditId.Value);
            if (staleAudit is not null) cleanupDb.AdminAuditLogs.Remove(staleAudit);
        }

        if (reqId.HasValue)
        {
            var staleReq = await cleanupDb.QuotationRequests.FindAsync(reqId.Value);
            if (staleReq is not null) cleanupDb.QuotationRequests.Remove(staleReq);
        }

        await cleanupDb.SaveChangesAsync();
    }

    // ──────────────────────────────────────────────────────────────
    // Test 1: MdPricing flips to Costing
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RollbackToCosting_FromMdPricing_FlipsToCosting()
    {
        var reqId = await SeedRequisitionAsync(RequisitionStatus.MdPricing);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/rollback-to-costing",
                new { Reason = "rollback to costing test from MdPricing" });

            resp.StatusCode.Should().Be(HttpStatusCode.OK,
                "MdPricing is the only allowed status for rollback-to-costing");

            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await verifyDb.QuotationRequests.FindAsync(reqId);
            req!.Status.Should().Be(RequisitionStatus.Costing,
                "the status should have been flipped to Costing in the DB");
        }
        finally
        {
            using var cleanupScope = factory.Services.CreateScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var auditRows = await cleanupDb.AdminAuditLogs
                .Where(a => a.EntityType == "Requisition" && a.EntityId == reqId).ToListAsync();
            if (auditRows.Count > 0) cleanupDb.AdminAuditLogs.RemoveRange(auditRows);
            var staleReq = await cleanupDb.QuotationRequests.FindAsync(reqId);
            if (staleReq is not null) cleanupDb.QuotationRequests.Remove(staleReq);
            await cleanupDb.SaveChangesAsync();
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 2: Blocked V3 statuses return 400
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(RequisitionStatus.Costing)]
    [InlineData(RequisitionStatus.CustomerConfirm)]
    [InlineData(RequisitionStatus.MdFinalSign)]
    [InlineData(RequisitionStatus.Signed)]
    [InlineData(RequisitionStatus.Rejected)]
    [InlineData(RequisitionStatus.Cancelled)]
    [InlineData(RequisitionStatus.Draft)]
    public async Task RollbackToCosting_FromBlockedStatus_Returns400(RequisitionStatus blockedStatus)
    {
        var reqId = await SeedRequisitionAsync(blockedStatus);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/rollback-to-costing",
                new { Reason = "rollback to costing from blocked status test" });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                $"status {blockedStatus} should be blocked from rollback-to-costing");
        }
        finally
        {
            await CleanupAsync(reqId);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 3: Existing BOM/cost data is preserved after rollback
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RollbackToCosting_PreservesExistingCostingData()
    {
        int reqId = 0;
        int reqItemId = 0;
        int bomHeaderId = 0;
        int costId = 0;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var sp = await db.Users.FirstAsync(u => u.Email == "ali@test.com");
            var customer = await db.Customers.FirstAsync(c => c.SalesPersonId == sp.Id);
            var item = await db.Items.FirstAsync(i => i.BranchId == sp.BranchId);

            var req = new QuotationRequest
            {
                BranchId = sp.BranchId!.Value,
                SalesPersonId = sp.Id,
                CustomerId = customer.Id,
                CurrencyCode = "AED",
                Status = RequisitionStatus.MdPricing,
            };
            db.QuotationRequests.Add(req);
            await db.SaveChangesAsync();
            reqId = req.Id;

            var reqItem = new RequisitionItem
            {
                QuotationRequestId = reqId,
                ItemId = item.Id,
                ExpectedQty = 10,
                SortOrder = 1,
            };
            db.RequisitionItems.Add(reqItem);
            await db.SaveChangesAsync();
            reqItemId = reqItem.Id;

            var accountant = await db.Users.FirstAsync(u => u.Role == UserRole.Accountant && u.IsActive);
            var bom = new BomHeader
            {
                RequisitionItemId = reqItemId,
                CreatedByUserId = sp.Id,
                TotalCostPerKg = 15.50m,
            };
            db.BomHeaders.Add(bom);
            await db.SaveChangesAsync();
            bomHeaderId = bom.Id;

            var cost = new BomCost
            {
                BomHeaderId = bomHeaderId,
                RawMaterialCostTotal = 12m,
                FohAmount = 1m,
                TotalCostPerKg = 15.50m,
                SubmittedByUserId = accountant.Id,
            };
            db.BomCosts.Add(cost);
            await db.SaveChangesAsync();
            costId = cost.Id;
        }

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/rollback-to-costing",
                new { Reason = "rollback to costing preservation test" });

            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var costStillExists = await verifyDb.BomCosts.AnyAsync(c => c.Id == costId);
            costStillExists.Should().BeTrue("BomCost data must not be deleted by the rollback operation");
            var bomStillExists = await verifyDb.BomHeaders.AnyAsync(b => b.Id == bomHeaderId);
            bomStillExists.Should().BeTrue("BomHeader data must not be deleted by the rollback operation");
        }
        finally
        {
            using var cleanupScope = factory.Services.CreateScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var auditRows = await cleanupDb.AdminAuditLogs
                .Where(a => a.EntityType == "Requisition" && a.EntityId == reqId).ToListAsync();
            if (auditRows.Count > 0) cleanupDb.AdminAuditLogs.RemoveRange(auditRows);

            var costToDelete = await cleanupDb.BomCosts.FindAsync(costId);
            if (costToDelete is not null) cleanupDb.BomCosts.Remove(costToDelete);

            var bomToDelete = await cleanupDb.BomHeaders.FindAsync(bomHeaderId);
            if (bomToDelete is not null) cleanupDb.BomHeaders.Remove(bomToDelete);

            var reqItemToDelete = await cleanupDb.RequisitionItems.FindAsync(reqItemId);
            if (reqItemToDelete is not null) cleanupDb.RequisitionItems.Remove(reqItemToDelete);

            var staleReq = await cleanupDb.QuotationRequests.FindAsync(reqId);
            if (staleReq is not null) cleanupDb.QuotationRequests.Remove(staleReq);

            await cleanupDb.SaveChangesAsync();
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 4: Writes audit log with correct ActionType + Before/After JSON
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RollbackToCosting_WritesAuditLog()
    {
        var reqId = await SeedRequisitionAsync(RequisitionStatus.MdPricing);
        var uniqueReason = $"rollback-to-costing-audit-{Guid.NewGuid():N}";
        int? auditId = null;

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/rollback-to-costing",
                new { Reason = uniqueReason });

            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var auditRow = await verifyDb.AdminAuditLogs.FirstOrDefaultAsync(a =>
                a.EntityType == "Requisition" &&
                a.EntityId == reqId &&
                a.Reason == uniqueReason);

            auditRow.Should().NotBeNull("an audit row must be written for the rollback");
            auditRow!.ActionType.Should().Be(AdminActionType.RollbackToCosting);

            using var beforeDoc = JsonDocument.Parse(auditRow.BeforeJson);
            var beforeStatus = beforeDoc.RootElement.GetProperty("Status").GetString();
            beforeStatus.Should().Be("MdPricing", "BeforeJson must capture original MdPricing status as string");

            auditRow.AfterJson.Should().NotBeNull();
            using var afterDoc = JsonDocument.Parse(auditRow.AfterJson!);
            var afterStatus = afterDoc.RootElement.GetProperty("Status").GetString();
            afterStatus.Should().Be("Costing", "AfterJson must capture target Costing status as string");

            auditId = auditRow.Id;
        }
        finally
        {
            await CleanupAsync(reqId, auditId);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 5: Missing reason returns 400
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RollbackToCosting_MissingReason_Returns400()
    {
        var reqId = await SeedRequisitionAsync(RequisitionStatus.MdPricing);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/rollback-to-costing",
                new { Reason = "" });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, "empty reason should be rejected");
        }
        finally
        {
            await CleanupAsync(reqId);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 6: Null body returns 400
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RollbackToCosting_NullBody_Returns400()
    {
        var reqId = await SeedRequisitionAsync(RequisitionStatus.MdPricing);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"/api/admin/requisitions/{reqId}/rollback-to-costing");
            var resp = await _client.SendAsync(request);

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "null body should be rejected before any DB lookup");
        }
        finally
        {
            await CleanupAsync(reqId);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 7: Non-admin returns 403
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RollbackToCosting_AsNonAdmin_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("ali@test.com", "Test@1234"));

        var resp = await _client.PostAsJsonAsync(
            "/api/admin/requisitions/1/rollback-to-costing",
            new { Reason = "unauthorized rollback attempt" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ──────────────────────────────────────────────────────────────
    // Test 8: Unknown req ID returns 404
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RollbackToCosting_UnknownReqId_Returns404()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

        var resp = await _client.PostAsJsonAsync(
            "/api/admin/requisitions/9999999/rollback-to-costing",
            new { Reason = "looking for nonexistent requisition here" });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
