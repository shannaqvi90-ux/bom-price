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

public class AdminReassignSpTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> TokenAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    /// <summary>Seeds a throwaway SalesPerson with a Guid-isolated email. Returns the created User.</summary>
    private async Task<User> SeedThrowawaySpAsync(string suffix, bool isActive = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Use branch from ali's user (all SPs need a valid BranchId for req seeding)
        var ali = await db.Users.FirstAsync(u => u.Email == "ali@test.com");

        var user = new User
        {
            Name = $"Throwaway SP {suffix}",
            Email = $"sp-{suffix}@throwaway.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test@1234"),
            Role = UserRole.SalesPerson,
            BranchId = ali.BranchId,
            IsActive = isActive,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Seeds a minimal throwaway requisition owned by the given SP and returns its Id.</summary>
    private async Task<int> SeedRequisitionAsync(int spId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sp = await db.Users.FindAsync(spId);
        // Pick any customer, or create a minimal one if none exists for this SP
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.SalesPersonId == spId)
                       ?? await db.Customers.FirstAsync(); // fallback to any customer

        var req = new QuotationRequest
        {
            BranchId = sp!.BranchId!.Value,
            SalesPersonId = spId,
            CustomerId = customer.Id,
            CurrencyCode = "AED",
            Status = RequisitionStatus.BomPending,
        };
        db.QuotationRequests.Add(req);
        await db.SaveChangesAsync();
        return req.Id;
    }

    private async Task CleanupAsync(int? reqId, int[]? spIds = null, int[]? auditIds = null)
    {
        using var cleanupScope = factory.Services.CreateScope();
        var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (auditIds is not null)
        {
            foreach (var aid in auditIds)
            {
                var row = await cleanupDb.AdminAuditLogs.FindAsync(aid);
                if (row is not null) cleanupDb.AdminAuditLogs.Remove(row);
            }
        }

        if (reqId.HasValue)
        {
            var staleReq = await cleanupDb.QuotationRequests.FindAsync(reqId.Value);
            if (staleReq is not null) cleanupDb.QuotationRequests.Remove(staleReq);
        }

        if (spIds is not null)
        {
            foreach (var uid in spIds)
            {
                var staleUser = await cleanupDb.Users.FindAsync(uid);
                if (staleUser is not null) cleanupDb.Users.Remove(staleUser);
            }
        }

        await cleanupDb.SaveChangesAsync();
    }

    // ── Test 1 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReassignSp_HappyPath_Returns200AndUpdates()
    {
        var sp1 = await SeedThrowawaySpAsync(Guid.NewGuid().ToString("N"));
        var sp2 = await SeedThrowawaySpAsync(Guid.NewGuid().ToString("N"));
        var reqId = await SeedRequisitionAsync(sp1.Id);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/reassign-sp",
                new { NewSalesPersonId = sp2.Id, Reason = "reassign happy path test" });

            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            // Behavioral: verify DB was updated
            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await verifyDb.QuotationRequests.FindAsync(reqId);
            req!.SalesPersonId.Should().Be(sp2.Id, "the SalesPersonId should have been updated to sp2");
        }
        finally
        {
            // Cleanup audit rows first, then req, then users
            using var cleanupScope = factory.Services.CreateScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var auditRows = await cleanupDb.AdminAuditLogs
                .Where(a => a.EntityType == "Requisition" && a.EntityId == reqId).ToListAsync();
            if (auditRows.Count > 0) cleanupDb.AdminAuditLogs.RemoveRange(auditRows);
            var staleReq = await cleanupDb.QuotationRequests.FindAsync(reqId);
            if (staleReq is not null) cleanupDb.QuotationRequests.Remove(staleReq);
            await cleanupDb.SaveChangesAsync();

            using var userCleanup = factory.Services.CreateScope();
            var userDb = userCleanup.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var uid in new[] { sp1.Id, sp2.Id })
            {
                var u = await userDb.Users.FindAsync(uid);
                if (u is not null) userDb.Users.Remove(u);
            }
            await userDb.SaveChangesAsync();
        }
    }

    // ── Test 2 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReassignSp_NonSpTarget_Returns400()
    {
        var sp1 = await SeedThrowawaySpAsync(Guid.NewGuid().ToString("N"));
        var reqId = await SeedRequisitionAsync(sp1.Id);

        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var accountant = await db.Users.FirstAsync(u => u.Role == UserRole.Accountant && u.IsActive);

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/reassign-sp",
                new { NewSalesPersonId = accountant.Id, Reason = "trying non-SP target" });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "target user is an Accountant, not a SalesPerson");
        }
        finally
        {
            await CleanupAsync(reqId, spIds: new[] { sp1.Id });
        }
    }

    // ── Test 3 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReassignSp_InactiveTarget_Returns400()
    {
        var sp1 = await SeedThrowawaySpAsync(Guid.NewGuid().ToString("N"), isActive: true);
        var sp2Inactive = await SeedThrowawaySpAsync(Guid.NewGuid().ToString("N"), isActive: false);
        var reqId = await SeedRequisitionAsync(sp1.Id);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/reassign-sp",
                new { NewSalesPersonId = sp2Inactive.Id, Reason = "trying inactive SP target" });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "target SP is inactive, should be rejected");
        }
        finally
        {
            await CleanupAsync(reqId, spIds: new[] { sp1.Id, sp2Inactive.Id });
        }
    }

    // ── Test 4 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReassignSp_WritesAuditWithOldSpId()
    {
        var sp1 = await SeedThrowawaySpAsync(Guid.NewGuid().ToString("N"));
        var sp2 = await SeedThrowawaySpAsync(Guid.NewGuid().ToString("N"));
        var reqId = await SeedRequisitionAsync(sp1.Id);
        var uniqueReason = $"reassign-audit-{Guid.NewGuid():N}";
        int? auditId = null;

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/reassign-sp",
                new { NewSalesPersonId = sp2.Id, Reason = uniqueReason });

            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            // Behavioral: verify audit row written with correct SP ids
            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var auditRow = await verifyDb.AdminAuditLogs.FirstOrDefaultAsync(a =>
                a.EntityType == "Requisition" &&
                a.EntityId == reqId &&
                a.Reason == uniqueReason);

            auditRow.Should().NotBeNull("an audit row must be written for the reassign");
            auditRow!.ActionType.Should().Be(AdminActionType.ReassignSp);
            // Use typed JSON parsing to avoid false positives from substring matches
            // (e.g. id=12 would be found inside id=123 with a naive Contains check)
            var before = System.Text.Json.JsonDocument.Parse(auditRow.BeforeJson).RootElement;
            before.GetProperty("OldSalesPersonId").GetInt32().Should().Be(sp1.Id,
                "BeforeJson must record the old SP id");

            auditRow.AfterJson.Should().NotBeNull();
            var after = System.Text.Json.JsonDocument.Parse(auditRow.AfterJson!).RootElement;
            after.GetProperty("NewSalesPersonId").GetInt32().Should().Be(sp2.Id,
                "AfterJson must record the new SP id");
            auditId = auditRow.Id;
        }
        finally
        {
            await CleanupAsync(reqId,
                spIds: new[] { sp1.Id, sp2.Id },
                auditIds: auditId.HasValue ? new[] { auditId.Value } : null);
        }
    }

    // ── Test 5 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReassignSp_MissingReason_Returns400()
    {
        var sp1 = await SeedThrowawaySpAsync(Guid.NewGuid().ToString("N"));
        var sp2 = await SeedThrowawaySpAsync(Guid.NewGuid().ToString("N"));
        var reqId = await SeedRequisitionAsync(sp1.Id);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/requisitions/{reqId}/reassign-sp",
                new { NewSalesPersonId = sp2.Id, Reason = "" });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, "empty reason should be rejected");
        }
        finally
        {
            await CleanupAsync(reqId, spIds: new[] { sp1.Id, sp2.Id });
        }
    }

    // ── Test 6 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReassignSp_NullBody_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

        // POST with no body — should hit the null-body guard
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/requisitions/1/reassign-sp");
        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Test 7 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReassignSp_AsNonAdmin_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("ali@test.com", "Test@1234"));

        var resp = await _client.PostAsJsonAsync(
            "/api/admin/requisitions/1/reassign-sp",
            new { NewSalesPersonId = 1, Reason = "unauthorized attempt here" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Test 8 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReassignSp_UnknownReqId_Returns404()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

        // Use ali's id as a valid SP target so validation passes the SP check
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ali = await db.Users.FirstAsync(u => u.Email == "ali@test.com");

        var resp = await _client.PostAsJsonAsync(
            "/api/admin/requisitions/9999999/reassign-sp",
            new { NewSalesPersonId = ali.Id, Reason = "looking for nonexistent req" });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
