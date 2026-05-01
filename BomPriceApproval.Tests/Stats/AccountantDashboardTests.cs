using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Stats;

public class AccountantDashboardTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private record LoginResponse(string AccessToken, string RefreshToken);

    private record DashboardV3Stats(
        int Costing,
        int AwaitingMd,
        int AwaitingCustomer,
        int SubmittedThisMonth);

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    private async Task<string> AccountantTokenAsync() => await LoginAsync("sara@test.com", "Test@1234");

    /// <summary>
    /// Seeds a minimal QuotationRequest row directly to DB with the given status and updatedAt.
    /// RefNo is auto-computed by Postgres (REQ-NNNN); no manual collision avoidance needed.
    /// Returns the seeded req ID for cleanup (if needed).
    /// </summary>
    private async Task<int> SeedReqAsync(RequisitionStatus status, DateTime? updatedAt = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Resolve a seed SalesPerson + Customer + Branch to satisfy FK constraints.
        // Alain branch = 2 (V3 active branch).
        var sp = db.Users.First(u => u.Email == "ali@test.com");
        var customer = db.Customers.First(c => !c.IsDeleted);
        var branch = db.Branches.First(b => b.IsActive);

        var req = new BomPriceApproval.API.Domain.Entities.QuotationRequest
        {
            CustomerId = customer.Id,
            SalesPersonId = sp.Id,
            BranchId = branch.Id,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = updatedAt ?? DateTime.UtcNow
        };

        db.QuotationRequests.Add(req);
        await db.SaveChangesAsync();
        return req.Id;
    }

    [Fact]
    public async Task Get_AsAccountant_ReturnsV3Fields()
    {
        var token = await AccountantTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/stats/accountant-dashboard");
        resp.EnsureSuccessStatusCode();

        var stats = (await resp.Content.ReadFromJsonAsync<DashboardV3Stats>())!;
        stats.Should().NotBeNull();
        stats.Costing.Should().BeGreaterThanOrEqualTo(0);
        stats.AwaitingMd.Should().BeGreaterThanOrEqualTo(0);
        stats.AwaitingCustomer.Should().BeGreaterThanOrEqualTo(0);
        stats.SubmittedThisMonth.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Get_AsAdmin_ReturnsV3Fields()
    {
        var token = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/stats/accountant-dashboard");
        resp.EnsureSuccessStatusCode();

        var stats = (await resp.Content.ReadFromJsonAsync<DashboardV3Stats>())!;
        stats.Should().NotBeNull();
        stats.Costing.Should().BeGreaterThanOrEqualTo(0);
        stats.AwaitingMd.Should().BeGreaterThanOrEqualTo(0);
        stats.AwaitingCustomer.Should().BeGreaterThanOrEqualTo(0);
        stats.SubmittedThisMonth.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Get_AsSalesPerson_Returns403()
    {
        var token = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/stats/accountant-dashboard");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var resp = await _client.GetAsync("/api/stats/accountant-dashboard");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Costing_CountsReqsInCostingStatus()
    {
        // Seed 1 req in Costing status
        await SeedReqAsync(RequisitionStatus.Costing);

        var token = await AccountantTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var before = (await (await _client.GetAsync("/api/stats/accountant-dashboard"))
            .Content.ReadFromJsonAsync<DashboardV3Stats>())!;

        // Seed one more and verify the count went up by exactly 1
        await SeedReqAsync(RequisitionStatus.Costing);

        var after = (await (await _client.GetAsync("/api/stats/accountant-dashboard"))
            .Content.ReadFromJsonAsync<DashboardV3Stats>())!;

        after.Costing.Should().Be(before.Costing + 1, "adding one Costing req should increment costing by 1");
    }

    [Fact]
    public async Task AwaitingMd_CountsMdPricingAndMdFinalSign()
    {
        var token = await AccountantTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var before = (await (await _client.GetAsync("/api/stats/accountant-dashboard"))
            .Content.ReadFromJsonAsync<DashboardV3Stats>())!;

        // Seed 1 MdPricing + 1 MdFinalSign
        await SeedReqAsync(RequisitionStatus.MdPricing);
        await SeedReqAsync(RequisitionStatus.MdFinalSign);

        var after = (await (await _client.GetAsync("/api/stats/accountant-dashboard"))
            .Content.ReadFromJsonAsync<DashboardV3Stats>())!;

        after.AwaitingMd.Should().Be(before.AwaitingMd + 2, "one MdPricing + one MdFinalSign should each count in awaitingMd");
    }

    [Fact]
    public async Task AwaitingCustomer_CountsCustomerConfirmStatus()
    {
        var token = await AccountantTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var before = (await (await _client.GetAsync("/api/stats/accountant-dashboard"))
            .Content.ReadFromJsonAsync<DashboardV3Stats>())!;

        await SeedReqAsync(RequisitionStatus.CustomerConfirm);

        var after = (await (await _client.GetAsync("/api/stats/accountant-dashboard"))
            .Content.ReadFromJsonAsync<DashboardV3Stats>())!;

        after.AwaitingCustomer.Should().Be(before.AwaitingCustomer + 1, "one CustomerConfirm req should increment awaitingCustomer by 1");
    }

    [Fact]
    public async Task SubmittedThisMonth_CountsReqsPassedCostingInCurrentMonth()
    {
        var token = await AccountantTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var before = (await (await _client.GetAsync("/api/stats/accountant-dashboard"))
            .Content.ReadFromJsonAsync<DashboardV3Stats>())!;

        // Seed 1 Signed req with UpdatedAt = now (current month) → should count
        await SeedReqAsync(RequisitionStatus.Signed, DateTime.UtcNow);

        var after = (await (await _client.GetAsync("/api/stats/accountant-dashboard"))
            .Content.ReadFromJsonAsync<DashboardV3Stats>())!;

        after.SubmittedThisMonth.Should().Be(before.SubmittedThisMonth + 1,
            "a Signed req updated this month should count in submittedThisMonth");
    }

    [Fact]
    public async Task SubmittedThisMonth_DoesNotCountPreviousMonth()
    {
        var token = await AccountantTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var before = (await (await _client.GetAsync("/api/stats/accountant-dashboard"))
            .Content.ReadFromJsonAsync<DashboardV3Stats>())!;

        // Seed 1 MdPricing req with UpdatedAt = last month → should NOT count
        var lastMonth = DateTime.UtcNow.AddMonths(-1);
        await SeedReqAsync(RequisitionStatus.MdPricing, lastMonth);

        var after = (await (await _client.GetAsync("/api/stats/accountant-dashboard"))
            .Content.ReadFromJsonAsync<DashboardV3Stats>())!;

        after.SubmittedThisMonth.Should().Be(before.SubmittedThisMonth,
            "a req updated last month should not count in submittedThisMonth");
    }

    [Fact]
    public async Task Cancelled_NotCountedInAnyBucket()
    {
        var token = await AccountantTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var before = (await (await _client.GetAsync("/api/stats/accountant-dashboard"))
            .Content.ReadFromJsonAsync<DashboardV3Stats>())!;

        await SeedReqAsync(RequisitionStatus.Cancelled, DateTime.UtcNow);

        var after = (await (await _client.GetAsync("/api/stats/accountant-dashboard"))
            .Content.ReadFromJsonAsync<DashboardV3Stats>())!;

        after.Costing.Should().Be(before.Costing, "Cancelled should not appear in Costing");
        after.AwaitingMd.Should().Be(before.AwaitingMd, "Cancelled should not appear in AwaitingMd");
        after.AwaitingCustomer.Should().Be(before.AwaitingCustomer, "Cancelled should not appear in AwaitingCustomer");
        after.SubmittedThisMonth.Should().Be(before.SubmittedThisMonth, "Cancelled should not appear in SubmittedThisMonth");
    }
}
