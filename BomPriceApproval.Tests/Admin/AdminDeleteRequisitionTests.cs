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
}
