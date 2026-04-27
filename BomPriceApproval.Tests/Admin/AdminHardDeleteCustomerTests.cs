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

public class AdminHardDeleteCustomerTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> TokenAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    /// <summary>Seed a fresh, unique customer with an admin creator. No requisitions referencing it.</summary>
    private async Task<int> SeedFreshCustomerAsync(int? salesPersonId = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Users.FirstAsync(u => u.Email == "admin@test.com");
        var token = $"{Guid.NewGuid():N}".Substring(0, 8);
        var customer = new Customer
        {
            Code = $"DEL-{token}",
            Name = $"DeleteSmoke-{token}",
            Email = $"del-{token}@test.com",
            PhoneNumber = "+971500000000",
            Address = "Test Address",
            SalesPersonId = salesPersonId,
            CreatedByUserId = admin.Id,
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private async Task<int> SeedReqForCustomerAsync(int customerId, RequisitionStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sp = await db.Users.FirstAsync(u => u.Email == "ali@test.com");
        var req = new QuotationRequest
        {
            BranchId = sp.BranchId!.Value,
            SalesPersonId = sp.Id,
            CustomerId = customerId,
            CurrencyCode = "AED",
            Status = status,
        };
        db.QuotationRequests.Add(req);
        await db.SaveChangesAsync();
        return req.Id;
    }

    private async Task CleanupAsync(int customerId, params int[] reqIds)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var rid in reqIds)
        {
            var req = await db.QuotationRequests.FindAsync(rid);
            if (req is not null) db.QuotationRequests.Remove(req);
        }
        var auditRows = await db.AdminAuditLogs
            .Where(a => a.EntityType == "Customer" && a.EntityId == customerId)
            .ToListAsync();
        if (auditRows.Count > 0) db.AdminAuditLogs.RemoveRange(auditRows);
        var notifRows = await db.Notifications
            .Where(n => n.ReferenceType == nameof(NotificationType.CustomerDeleted) && n.ReferenceId == customerId)
            .ToListAsync();
        if (notifRows.Count > 0) db.Notifications.RemoveRange(notifRows);
        var customer = await db.Customers.FindAsync(customerId);
        if (customer is not null) db.Customers.Remove(customer);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task DeleteCustomer_NotFound_Returns404()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/admin/customers/9999999")
        {
            Content = JsonContent.Create(new { Reason = "looking for nothing" })
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteCustomer_AlreadyDeleted_Returns409()
    {
        var customerId = await SeedFreshCustomerAsync();
        try
        {
            // Manually mark as already deleted
            using (var setupScope = factory.Services.CreateScope())
            {
                var db = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var c = await db.Customers.FindAsync(customerId);
                c!.IsDeleted = true;
                c.DeletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/customers/{customerId}")
            {
                Content = JsonContent.Create(new { Reason = "duplicate delete attempt" })
            });

            resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally { await CleanupAsync(customerId); }
    }

    [Fact]
    public async Task DeleteCustomer_NonAdmin_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("ali@test.com", "Test@1234"));

        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/admin/customers/1")
        {
            Content = JsonContent.Create(new { Reason = "unauthorized SP attempt" })
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteCustomer_ReasonTooShort_Returns400()
    {
        var customerId = await SeedFreshCustomerAsync();
        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/customers/{customerId}")
            {
                Content = JsonContent.Create(new { Reason = "x" })
            });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally { await CleanupAsync(customerId); }
    }

    [Fact]
    public async Task DeleteCustomer_HasActiveWorkflowReqs_Returns409_WithBlockingList()
    {
        var customerId = await SeedFreshCustomerAsync();
        var reqId = await SeedReqForCustomerAsync(customerId, RequisitionStatus.BomPending);
        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/customers/{customerId}")
            {
                Content = JsonContent.Create(new { Reason = "blocked attempt" })
            });

            resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var blocking = doc.RootElement.GetProperty("blockingRequisitions");
            blocking.GetArrayLength().Should().Be(1);
            blocking[0].GetInt32().Should().Be(reqId);
        }
        finally { await CleanupAsync(customerId, reqId); }
    }

    [Fact]
    public async Task DeleteCustomer_OnlyApprovedRejectedReqs_Succeeds_AnonymizesInPlace()
    {
        var customerId = await SeedFreshCustomerAsync();
        var approvedReqId = await SeedReqForCustomerAsync(customerId, RequisitionStatus.Approved);
        var rejectedReqId = await SeedReqForCustomerAsync(customerId, RequisitionStatus.Rejected);
        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/customers/{customerId}")
            {
                Content = JsonContent.Create(new { Reason = "delete despite historical reqs" })
            });

            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // Verify anonymization persisted
            using var verifyScope = factory.Services.CreateScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Customers.FindAsync(customerId);
            c!.IsDeleted.Should().BeTrue();
            c.Name.Should().StartWith("[Deleted ");
            c.Email.Should().Be("");
            c.PhoneNumber.Should().Be("");
            c.Address.Should().Be("");
        }
        finally { await CleanupAsync(customerId, approvedReqId, rejectedReqId); }
    }

    [Fact]
    public async Task DeleteCustomer_HappyPath_FieldsAnonymized_FlagsSet_AuditWritten_NotifSent()
    {
        var customerId = await SeedFreshCustomerAsync();
        try
        {
            // Capture pre-state to compare
            string originalCode;
            using (var s = factory.Services.CreateScope())
            {
                var setupDb = s.ServiceProvider.GetRequiredService<AppDbContext>();
                originalCode = (await setupDb.Customers.FindAsync(customerId))!.Code;
            }

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/customers/{customerId}")
            {
                Content = JsonContent.Create(new { Reason = "smoke test happy path" })
            });

            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var verifyScope = factory.Services.CreateScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Customers.FindAsync(customerId);
            c.Should().NotBeNull("the customer row must be preserved (anonymize-in-place)");
            c!.IsDeleted.Should().BeTrue();
            c.DeletedAt.Should().NotBeNull();
            c.DeletedByUserId.Should().NotBeNull();
            c.Name.Should().Match("[Deleted ????-??-??]");
            c.Code.Should().Be($"[deleted-{customerId}]");
            c.Code.Should().NotBe(originalCode);
            c.Email.Should().Be("");
            c.PhoneNumber.Should().Be("");
            c.Address.Should().Be("");
            c.SalesPersonId.Should().BeNull();

            // Audit row written
            var audit = await db.AdminAuditLogs
                .FirstOrDefaultAsync(a => a.EntityType == "Customer" && a.EntityId == customerId);
            audit.Should().NotBeNull();
            audit!.ActionType.Should().Be(AdminActionType.HardDeleteCustomer);
            audit.Reason.Should().Be("smoke test happy path");
            audit.BeforeJson.Should().Contain(originalCode);
            audit.AfterJson.Should().Contain($"[deleted-{customerId}]");
            // Forever-decision: enums serialized as strings, not ints
            audit.AfterJson.Should().NotContain("\"IsDeleted\":1").And.NotContain("\"IsDeleted\":0");
        }
        finally { await CleanupAsync(customerId); }
    }

    [Fact]
    public async Task DeleteCustomer_GETListing_HidesDeletedRow()
    {
        var customerId = await SeedFreshCustomerAsync();
        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            // Pre-delete: customer is in the listing
            var preList = await _client.GetAsync("/api/customers");
            (await preList.Content.ReadAsStringAsync()).Should().Contain($"\"id\":{customerId}");

            // Delete
            var del = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/customers/{customerId}")
            {
                Content = JsonContent.Create(new { Reason = "listing-hide test" })
            });
            del.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // Post-delete: customer is hidden from the listing
            var postList = await _client.GetAsync("/api/customers");
            (await postList.Content.ReadAsStringAsync()).Should().NotContain($"\"id\":{customerId}");

            // GET-by-id also returns 404
            var getById = await _client.GetAsync($"/api/customers/{customerId}");
            getById.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally { await CleanupAsync(customerId); }
    }

    [Fact]
    public async Task DeleteCustomer_HistoricalReqDisplay_StillResolves_ShowsDeletedMarker()
    {
        var customerId = await SeedFreshCustomerAsync();
        var reqId = await SeedReqForCustomerAsync(customerId, RequisitionStatus.Approved);
        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var del = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/customers/{customerId}")
            {
                Content = JsonContent.Create(new { Reason = "historical-display test" })
            });
            del.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var reqResp = await _client.GetAsync($"/api/requisitions/{reqId}");
            reqResp.StatusCode.Should().Be(HttpStatusCode.OK,
                "historical req should still resolve via Customer FK navigation post-anonymize");
            var body = await reqResp.Content.ReadAsStringAsync();
            body.Should().Contain("[Deleted ", "anonymized customer name should surface in historical req detail");
        }
        finally { await CleanupAsync(customerId, reqId); }
    }
}
