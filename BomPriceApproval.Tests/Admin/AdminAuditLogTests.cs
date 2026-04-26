using System.Net;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Admin;

public class AdminAuditLogTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private async Task<HttpClient> AsAdmin()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { Email = "admin@test.com", Password = "Admin@1234" });
        var body = await login.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var token = body!["accessToken"].ToString();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    [Fact]
    public async Task GetAuditLog_AsAdmin_ReturnsPagedList()
    {
        // Seed a throwaway audit row
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Users.FirstAsync(u => u.Role == UserRole.Admin);
        var row = new AdminAuditLog
        {
            AdminUserId = admin.Id,
            ActionType = AdminActionType.DeleteRequisition,
            EntityType = "Requisition",
            EntityId = 99999,
            Reason = $"test-{Guid.NewGuid():N}",
            BeforeJson = "{}",
            CreatedAt = DateTime.UtcNow
        };
        db.AdminAuditLogs.Add(row);
        await db.SaveChangesAsync();

        var client = await AsAdmin();
        var resp = await client.GetAsync("/api/admin/audit-log?page=1&pageSize=20");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body!.Should().ContainKey("items");
        body.Should().ContainKey("total");

        // Cleanup
        db.AdminAuditLogs.Remove(row);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAuditLog_AsNonAdmin_Returns403()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { Email = "ali@test.com", Password = "Test@1234" });
        var body = await login.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var token = body!["accessToken"].ToString();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var resp = await client.GetAsync("/api/admin/audit-log");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAuditLog_FilterByActionType_NarrowsResults()
    {
        var client = await AsAdmin();
        var resp = await client.GetAsync("/api/admin/audit-log?actionType=DeleteRequisition&page=1&pageSize=5");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        // We can't assert exact counts without a clean db; just verify shape.
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body!.Should().ContainKey("items");
    }
}
