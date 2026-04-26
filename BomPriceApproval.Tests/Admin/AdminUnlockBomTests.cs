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

public class AdminUnlockBomTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> TokenAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    /// <summary>Seeds a minimal throwaway requisition with the given status and returns its Id.</summary>
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
    // Test 1: Allowed statuses flip to BomInProgress
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(RequisitionStatus.CostingPending)]
    [InlineData(RequisitionStatus.CostingInProgress)]
    [InlineData(RequisitionStatus.MdReview)]
    public async Task UnlockBom_FromAllowedStatus_FlipsToBomInProgress(RequisitionStatus fromStatus)
    {
        var reqId = await SeedRequisitionAsync(fromStatus);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/unlock-bom",
                new { Reason = "unlock bom test from allowed status" });

            resp.StatusCode.Should().Be(HttpStatusCode.OK,
                $"status {fromStatus} should be unlockable to BomInProgress");

            // Behavioral: verify DB was updated
            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await verifyDb.QuotationRequests.FindAsync(reqId);
            req!.Status.Should().Be(RequisitionStatus.BomInProgress,
                "the status should have been flipped to BomInProgress in the DB");
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
    // Test 2: Blocked statuses return 400
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(RequisitionStatus.Approved)]
    [InlineData(RequisitionStatus.Rejected)]
    [InlineData(RequisitionStatus.BomPending)]
    [InlineData(RequisitionStatus.BomInProgress)]
    public async Task UnlockBom_FromBlockedStatus_Returns400(RequisitionStatus blockedStatus)
    {
        var reqId = await SeedRequisitionAsync(blockedStatus);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/unlock-bom",
                new { Reason = "unlock bom from blocked status test" });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                $"status {blockedStatus} should be blocked from BOM unlock");
        }
        finally
        {
            await CleanupAsync(reqId);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 3: Draft returns 400
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnlockBom_FromDraft_Returns400()
    {
        var reqId = await SeedRequisitionAsync(RequisitionStatus.Draft);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/unlock-bom",
                new { Reason = "unlock bom from draft status test" });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "Draft status should be blocked from BOM unlock");
        }
        finally
        {
            await CleanupAsync(reqId);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 4: Existing BOM data is preserved after unlock
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnlockBom_PreservesExistingBomData()
    {
        int reqId = 0;
        int reqItemId = 0;
        int bomHeaderId = 0;

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
                Status = RequisitionStatus.CostingPending,
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

            var bomCreator = await db.Users.FirstAsync(u => u.Role == UserRole.BomCreator);
            var bom = new BomHeader
            {
                RequisitionItemId = reqItemId,
                CreatedByUserId = bomCreator.Id,
                TotalCostPerKg = 15.50m,
            };
            db.BomHeaders.Add(bom);
            await db.SaveChangesAsync();
            bomHeaderId = bom.Id;
        }

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/unlock-bom",
                new { Reason = "unlock bom preservation test" });

            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            // Behavioral: BOM header should still be in DB
            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bomStillExists = await verifyDb.BomHeaders.AnyAsync(b => b.Id == bomHeaderId);
            bomStillExists.Should().BeTrue("BOM data must not be deleted by the unlock operation");
        }
        finally
        {
            using var cleanupScope = factory.Services.CreateScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var auditRows = await cleanupDb.AdminAuditLogs
                .Where(a => a.EntityType == "Requisition" && a.EntityId == reqId).ToListAsync();
            if (auditRows.Count > 0) cleanupDb.AdminAuditLogs.RemoveRange(auditRows);

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
    // Test 5: Writes audit log with correct Before/After JSON
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnlockBom_WritesAuditLog()
    {
        var reqId = await SeedRequisitionAsync(RequisitionStatus.MdReview);
        var uniqueReason = $"unlock-bom-audit-{Guid.NewGuid():N}";
        int? auditId = null;

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/unlock-bom",
                new { Reason = uniqueReason });

            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            // Behavioral: verify audit row written
            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var auditRow = await verifyDb.AdminAuditLogs.FirstOrDefaultAsync(a =>
                a.EntityType == "Requisition" &&
                a.EntityId == reqId &&
                a.Reason == uniqueReason);

            auditRow.Should().NotBeNull("an audit row must be written for the BOM unlock");
            auditRow!.ActionType.Should().Be(AdminActionType.UnlockBom);

            // BeforeJson should contain Status="MdReview"
            using var beforeDoc = JsonDocument.Parse(auditRow.BeforeJson);
            var beforeStatus = beforeDoc.RootElement.GetProperty("Status").GetString();
            beforeStatus.Should().Be("MdReview", "BeforeJson must capture original MdReview status as string");

            // AfterJson should contain Status="BomInProgress"
            auditRow.AfterJson.Should().NotBeNull();
            using var afterDoc = JsonDocument.Parse(auditRow.AfterJson!);
            var afterStatus = afterDoc.RootElement.GetProperty("Status").GetString();
            afterStatus.Should().Be("BomInProgress", "AfterJson must capture target BomInProgress status as string");

            auditId = auditRow.Id;
        }
        finally
        {
            await CleanupAsync(reqId, auditId);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 6: Missing reason returns 400
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnlockBom_MissingReason_Returns400()
    {
        var reqId = await SeedRequisitionAsync(RequisitionStatus.MdReview);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/unlock-bom",
                new { Reason = "" });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, "empty reason should be rejected");
        }
        finally
        {
            await CleanupAsync(reqId);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 7: Null body returns 400
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnlockBom_NullBody_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

        // POST with no body — should hit the null-body guard
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/requisitions/1/unlock-bom");
        var resp = await _client.SendAsync(request);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ──────────────────────────────────────────────────────────────
    // Test 8: Non-admin returns 403
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnlockBom_AsNonAdmin_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("ali@test.com", "Test@1234"));

        var resp = await _client.PostAsJsonAsync(
            "/api/admin/requisitions/1/unlock-bom",
            new { Reason = "unauthorized unlock attempt" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ──────────────────────────────────────────────────────────────
    // Test 9: Unknown req ID returns 404
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnlockBom_UnknownReqId_Returns404()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

        var resp = await _client.PostAsJsonAsync(
            "/api/admin/requisitions/9999999/unlock-bom",
            new { Reason = "looking for nonexistent requisition here" });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
