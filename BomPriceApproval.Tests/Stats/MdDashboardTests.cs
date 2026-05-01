using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Stats;

public class MdDashboardTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private record LoginResponse(string AccessToken, string RefreshToken);

    private record MdDashboardStats(
        int ToPrice,
        int ToSign,
        int InFlight,
        int SignedToday);

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    private async Task<string> MdTokenAsync() => await LoginAsync("md@test.com", "Test@1234");

    /// <summary>
    /// Seeds a minimal QuotationRequest row directly to DB with the given status and updatedAt.
    /// Uses the same FK-resolution pattern as AccountantDashboardTests.
    /// </summary>
    private async Task<int> SeedReqAsync(RequisitionStatus status, DateTime? updatedAt = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
    public async Task Get_AsMd_ReturnsAllFourFields()
    {
        var token = await MdTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/stats/v3-dashboard");
        resp.EnsureSuccessStatusCode();

        var stats = (await resp.Content.ReadFromJsonAsync<MdDashboardStats>())!;
        stats.Should().NotBeNull();
        stats.ToPrice.Should().BeGreaterThanOrEqualTo(0);
        stats.ToSign.Should().BeGreaterThanOrEqualTo(0);
        stats.InFlight.Should().BeGreaterThanOrEqualTo(0);
        stats.SignedToday.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Get_AsAdmin_ReturnsAllFourFields()
    {
        var token = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/stats/v3-dashboard");
        resp.EnsureSuccessStatusCode();

        var stats = (await resp.Content.ReadFromJsonAsync<MdDashboardStats>())!;
        stats.Should().NotBeNull();
    }

    [Fact]
    public async Task Get_AsAccountant_Returns403()
    {
        var token = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/stats/v3-dashboard");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_AsSalesPerson_Returns403()
    {
        var token = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/stats/v3-dashboard");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var resp = await _client.GetAsync("/api/stats/v3-dashboard");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ToPrice_CountsMdPricingStatus()
    {
        var token = await MdTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var before = (await (await _client.GetAsync("/api/stats/v3-dashboard"))
            .Content.ReadFromJsonAsync<MdDashboardStats>())!;

        await SeedReqAsync(RequisitionStatus.MdPricing);
        await SeedReqAsync(RequisitionStatus.MdPricing);

        var after = (await (await _client.GetAsync("/api/stats/v3-dashboard"))
            .Content.ReadFromJsonAsync<MdDashboardStats>())!;

        after.ToPrice.Should().Be(before.ToPrice + 2,
            "two MdPricing reqs should increment toPrice by 2");
    }

    [Fact]
    public async Task ToSign_CountsMdFinalSignStatus()
    {
        var token = await MdTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var before = (await (await _client.GetAsync("/api/stats/v3-dashboard"))
            .Content.ReadFromJsonAsync<MdDashboardStats>())!;

        await SeedReqAsync(RequisitionStatus.MdFinalSign);

        var after = (await (await _client.GetAsync("/api/stats/v3-dashboard"))
            .Content.ReadFromJsonAsync<MdDashboardStats>())!;

        after.ToSign.Should().Be(before.ToSign + 1,
            "one MdFinalSign req should increment toSign by 1");
    }

    [Fact]
    public async Task InFlight_CountsCustomerConfirmAndCosting()
    {
        var token = await MdTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var before = (await (await _client.GetAsync("/api/stats/v3-dashboard"))
            .Content.ReadFromJsonAsync<MdDashboardStats>())!;

        // 3 CustomerConfirm + 0 Costing in this slice — assert combined +3
        await SeedReqAsync(RequisitionStatus.CustomerConfirm);
        await SeedReqAsync(RequisitionStatus.CustomerConfirm);
        await SeedReqAsync(RequisitionStatus.CustomerConfirm);

        var after = (await (await _client.GetAsync("/api/stats/v3-dashboard"))
            .Content.ReadFromJsonAsync<MdDashboardStats>())!;

        after.InFlight.Should().Be(before.InFlight + 3,
            "three CustomerConfirm reqs should increment inFlight by 3");
    }

    [Fact]
    public async Task SignedToday_CountsSignedReqsUpdatedToday()
    {
        var token = await MdTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var before = (await (await _client.GetAsync("/api/stats/v3-dashboard"))
            .Content.ReadFromJsonAsync<MdDashboardStats>())!;

        await SeedReqAsync(RequisitionStatus.Signed, DateTime.UtcNow);

        var after = (await (await _client.GetAsync("/api/stats/v3-dashboard"))
            .Content.ReadFromJsonAsync<MdDashboardStats>())!;

        after.SignedToday.Should().Be(before.SignedToday + 1,
            "one Signed req with UpdatedAt=now should increment signedToday by 1");
    }

    [Fact]
    public async Task SignedToday_DoesNotCountYesterday()
    {
        var token = await MdTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var before = (await (await _client.GetAsync("/api/stats/v3-dashboard"))
            .Content.ReadFromJsonAsync<MdDashboardStats>())!;

        var yesterday = DateTime.UtcNow.AddDays(-1);
        await SeedReqAsync(RequisitionStatus.Signed, yesterday);

        var after = (await (await _client.GetAsync("/api/stats/v3-dashboard"))
            .Content.ReadFromJsonAsync<MdDashboardStats>())!;

        after.SignedToday.Should().Be(before.SignedToday,
            "a Signed req updated yesterday should NOT count in signedToday");
    }

    /// <summary>
    /// End-to-end integration: seed the exact mix from the plan and verify all 4 buckets.
    /// 2 MdPricing + 1 MdFinalSign + 3 CustomerConfirm + 1 Signed today + 1 Signed yesterday
    /// → toPrice=2, toSign=1, inFlight=3, signedToday=1 (deltas).
    /// </summary>
    [Fact]
    public async Task PlanIntegrationScenario_AllFourBucketsCorrect()
    {
        var token = await MdTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var before = (await (await _client.GetAsync("/api/stats/v3-dashboard"))
            .Content.ReadFromJsonAsync<MdDashboardStats>())!;

        await SeedReqAsync(RequisitionStatus.MdPricing);
        await SeedReqAsync(RequisitionStatus.MdPricing);
        await SeedReqAsync(RequisitionStatus.MdFinalSign);
        await SeedReqAsync(RequisitionStatus.CustomerConfirm);
        await SeedReqAsync(RequisitionStatus.CustomerConfirm);
        await SeedReqAsync(RequisitionStatus.CustomerConfirm);
        await SeedReqAsync(RequisitionStatus.Signed, DateTime.UtcNow);
        await SeedReqAsync(RequisitionStatus.Signed, DateTime.UtcNow.AddDays(-1));

        var after = (await (await _client.GetAsync("/api/stats/v3-dashboard"))
            .Content.ReadFromJsonAsync<MdDashboardStats>())!;

        after.ToPrice.Should().Be(before.ToPrice + 2);
        after.ToSign.Should().Be(before.ToSign + 1);
        after.InFlight.Should().Be(before.InFlight + 3);
        after.SignedToday.Should().Be(before.SignedToday + 1);
    }
}
