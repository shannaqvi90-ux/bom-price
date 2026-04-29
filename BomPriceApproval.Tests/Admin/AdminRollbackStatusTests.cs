using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Features.Admin;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.Tests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Admin;

/// <summary>
/// V3 admin status rollback. Whitelist comes from RequisitionStateMachine.AdminRollback:
///   Signed → MdFinalSign
///   MdFinalSign → CustomerConfirm
///   CustomerConfirm → MdPricing
///   MdPricing → Costing
///   Costing → Draft
/// Cancelled and Rejected are terminal and cannot be rolled back.
/// </summary>
public class AdminRollbackStatusTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> TokenAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    /// <summary>Seeds a minimal throwaway requisition with a given status and returns its Id.</summary>
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
    // V3 whitelisted transitions (one step back per source)
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(RequisitionStatus.Signed, RequisitionStatus.MdFinalSign)]
    [InlineData(RequisitionStatus.MdFinalSign, RequisitionStatus.CustomerConfirm)]
    [InlineData(RequisitionStatus.CustomerConfirm, RequisitionStatus.MdPricing)]
    [InlineData(RequisitionStatus.MdPricing, RequisitionStatus.Costing)]
    [InlineData(RequisitionStatus.Costing, RequisitionStatus.Draft)]
    public async Task Rollback_AllowedTransition_Returns200AndFlipsStatus(
        RequisitionStatus fromStatus, RequisitionStatus toStatus)
    {
        var reqId = await SeedRequisitionAsync(fromStatus);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/rollback-status",
                new { TargetStatus = toStatus, Reason = "allowed V3 rollback test" });

            resp.StatusCode.Should().Be(HttpStatusCode.OK, $"transition {fromStatus} → {toStatus} should be allowed");

            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await verifyDb.QuotationRequests.FindAsync(reqId);
            req!.Status.Should().Be(toStatus, "the status should have been flipped in the DB");
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
    // V3 terminal statuses cannot be rolled back
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(RequisitionStatus.Rejected, RequisitionStatus.MdPricing)]
    [InlineData(RequisitionStatus.Cancelled, RequisitionStatus.Draft)]
    public async Task Rollback_FromTerminalStatus_Returns400(
        RequisitionStatus fromStatus, RequisitionStatus toStatus)
    {
        var reqId = await SeedRequisitionAsync(fromStatus);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/rollback-status",
                new { TargetStatus = toStatus, Reason = "attempt rollback from terminal" });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                $"{fromStatus} is terminal and not on the rollback whitelist");
        }
        finally
        {
            await CleanupAsync(reqId);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Forward jumps and skip-jumps are blocked
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(RequisitionStatus.Draft, RequisitionStatus.Costing)]              // forward
    [InlineData(RequisitionStatus.MdPricing, RequisitionStatus.Draft)]            // skip-jump (target not in whitelist for this source)
    [InlineData(RequisitionStatus.Signed, RequisitionStatus.Costing)]             // skip-jump
    [InlineData(RequisitionStatus.CustomerConfirm, RequisitionStatus.Costing)]    // skip-jump
    public async Task Rollback_NonWhitelistedTarget_Returns400(
        RequisitionStatus fromStatus, RequisitionStatus toStatus)
    {
        var reqId = await SeedRequisitionAsync(fromStatus);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/rollback-status",
                new { TargetStatus = toStatus, Reason = "forward jump or skip target attempt" });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                $"{fromStatus} → {toStatus} is not on the whitelist");
        }
        finally
        {
            await CleanupAsync(reqId);
        }
    }

    [Fact]
    public async Task Rollback_WritesAuditLog()
    {
        var reqId = await SeedRequisitionAsync(RequisitionStatus.Signed);
        var uniqueReason = $"rollback-audit-{Guid.NewGuid():N}";
        int? auditId = null;

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/rollback-status",
                new { TargetStatus = RequisitionStatus.MdFinalSign, Reason = uniqueReason });

            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var auditRow = await verifyDb.AdminAuditLogs.FirstOrDefaultAsync(a =>
                a.EntityType == "Requisition" &&
                a.EntityId == reqId &&
                a.Reason == uniqueReason);

            auditRow.Should().NotBeNull("an audit row must be written for the rollback");
            auditRow!.ActionType.Should().Be(AdminActionType.RollbackStatus);
            auditRow!.BeforeJson.Should().MatchRegex("\"Status\"\\s*:\\s*\"Signed\"", "before snapshot must capture original Signed status as string");
            auditRow.AfterJson.Should().NotBeNull();
            auditRow.AfterJson!.Should().MatchRegex("\"Status\"\\s*:\\s*\"MdFinalSign\"", "after snapshot must capture target MdFinalSign status as string");
            auditId = auditRow.Id;
        }
        finally
        {
            await CleanupAsync(reqId, auditId);
        }
    }

    [Fact]
    public async Task Rollback_MissingReason_Returns400()
    {
        var reqId = await SeedRequisitionAsync(RequisitionStatus.Signed);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/rollback-status",
                new { TargetStatus = RequisitionStatus.MdFinalSign, Reason = "" });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, "empty reason should be rejected");
        }
        finally
        {
            await CleanupAsync(reqId);
        }
    }

    [Fact]
    public async Task Rollback_NullBody_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

        // POST with no body — should hit the null-body guard
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/requisitions/1/rollback-status");
        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rollback_AsNonAdmin_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("ali@test.com", "Test@1234"));

        var resp = await _client.PostAsJsonAsync(
            "/api/admin/requisitions/1/rollback-status",
            new { TargetStatus = RequisitionStatus.MdFinalSign, Reason = "unauthorized attempt here" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Rollback_UnknownReqId_Returns404()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

        var resp = await _client.PostAsJsonAsync(
            "/api/admin/requisitions/9999999/rollback-status",
            new { TargetStatus = RequisitionStatus.MdFinalSign, Reason = "looking for nonexistent req" });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
