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

public class AdminAuditLogTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> TokenAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    [Fact]
    public async Task GetAuditLog_AsAdmin_ReturnsPagedList()
    {
        // Seed a throwaway audit row with a unique reason
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Users.FirstAsync(u => u.Role == UserRole.Admin);
        var uniqueReason = $"test-{Guid.NewGuid():N}";
        var row = new AdminAuditLog
        {
            AdminUserId = admin.Id,
            ActionType = AdminActionType.DeleteRequisition,
            EntityType = "Requisition",
            EntityId = 99999,
            Reason = uniqueReason,
            BeforeJson = "{}",
            CreatedAt = DateTime.UtcNow
        };
        db.AdminAuditLogs.Add(row);
        await db.SaveChangesAsync();

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
            var resp = await _client.GetAsync("/api/admin/audit-log?page=1&pageSize=50");
            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await resp.Content.ReadFromJsonAsync<AuditLogPagedResponse>();
            body!.Items.Should().NotBeEmpty();
            body.Items.Should().Contain(x => x.Reason == uniqueReason);
            body.Total.Should().BeGreaterThan(0);
        }
        finally
        {
            using var cleanupScope = factory.Services.CreateScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stale = await cleanupDb.AdminAuditLogs.FirstOrDefaultAsync(x => x.Id == row.Id);
            if (stale is not null)
            {
                cleanupDb.AdminAuditLogs.Remove(stale);
                await cleanupDb.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task GetAuditLog_AsNonAdmin_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("ali@test.com", "Test@1234"));
        var resp = await _client.GetAsync("/api/admin/audit-log");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAuditLog_FilterByActionType_NarrowsResults()
    {
        // Seed two rows with different ActionTypes, each with a unique reason
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Users.FirstAsync(u => u.Role == UserRole.Admin);

        var deleteReason = $"del-{Guid.NewGuid():N}";
        var rollbackReason = $"rbk-{Guid.NewGuid():N}";

        var deleteRow = new AdminAuditLog
        {
            AdminUserId = admin.Id,
            ActionType = AdminActionType.DeleteRequisition,
            EntityType = "Requisition",
            EntityId = 99998,
            Reason = deleteReason,
            BeforeJson = "{}",
            CreatedAt = DateTime.UtcNow
        };
        var rollbackRow = new AdminAuditLog
        {
            AdminUserId = admin.Id,
            ActionType = AdminActionType.RollbackStatus,
            EntityType = "Requisition",
            EntityId = 99997,
            Reason = rollbackReason,
            BeforeJson = "{}",
            CreatedAt = DateTime.UtcNow
        };
        db.AdminAuditLogs.AddRange(deleteRow, rollbackRow);
        await db.SaveChangesAsync();

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
            var resp = await _client.GetAsync("/api/admin/audit-log?actionType=DeleteRequisition&page=1&pageSize=100");
            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await resp.Content.ReadFromJsonAsync<AuditLogPagedResponse>();
            // The DeleteRequisition row must appear in filtered results
            body!.Items.Should().Contain(x => x.Reason == deleteReason);
            // The RollbackStatus row must NOT appear — proves filter actually narrows
            body.Items.Should().NotContain(x => x.Reason == rollbackReason);
        }
        finally
        {
            using var cleanupScope = factory.Services.CreateScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var staleDelete = await cleanupDb.AdminAuditLogs.FirstOrDefaultAsync(x => x.Id == deleteRow.Id);
            var staleRollback = await cleanupDb.AdminAuditLogs.FirstOrDefaultAsync(x => x.Id == rollbackRow.Id);
            if (staleDelete is not null) cleanupDb.AdminAuditLogs.Remove(staleDelete);
            if (staleRollback is not null) cleanupDb.AdminAuditLogs.Remove(staleRollback);
            if (staleDelete is not null || staleRollback is not null) await cleanupDb.SaveChangesAsync();
        }
    }
}
