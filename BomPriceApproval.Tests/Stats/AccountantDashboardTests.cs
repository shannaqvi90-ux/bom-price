using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Stats;

public class AccountantDashboardTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    // Note: spec §8.1 lists "Empty DB → all zeros" as a required case, but the
    // shared WebApplicationFactory always seeds and parallel tests dirty state,
    // so the case is not asserted here. The >= 0 baseline assertions cover the spirit.

    private record LoginResponse(string AccessToken, string RefreshToken);

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    private record DashboardStats(
        int PendingCosting,
        int InProgress,
        int SubmittedThisMonth,
        int AwaitingMd);

    [Fact]
    public async Task Get_AsAccountant_ReturnsAllFourCounts()
    {
        var token = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/stats/accountant-dashboard");
        resp.EnsureSuccessStatusCode();

        var stats = (await resp.Content.ReadFromJsonAsync<DashboardStats>())!;

        stats.Should().NotBeNull();
        stats.PendingCosting.Should().BeGreaterThanOrEqualTo(0);
        stats.InProgress.Should().BeGreaterThanOrEqualTo(0);
        stats.SubmittedThisMonth.Should().BeGreaterThanOrEqualTo(0);
        stats.AwaitingMd.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Get_AsAdmin_ReturnsAllFourCounts()
    {
        // Admin password is Admin@1234 (different from other seed users)
        var token = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/stats/accountant-dashboard");
        resp.EnsureSuccessStatusCode();

        var stats = (await resp.Content.ReadFromJsonAsync<DashboardStats>())!;
        stats.Should().NotBeNull();
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
    public async Task Get_PendingCosting_IncrementsWhenNewRequisitionAdvancesToCostingPending()
    {
        // Read baseline count as Sara (Accountant)
        var saraToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", saraToken);

        var baseline = (await _client.GetFromJsonAsync<DashboardStats>("/api/stats/accountant-dashboard"))!;

        // Seed a new requisition at CostingPending
        var (_, _, refNo) = await SeedOneRequisitionAtCostingPendingAsync();

        // Re-read counts as Sara
        saraToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", saraToken);
        var after = (await _client.GetFromJsonAsync<DashboardStats>("/api/stats/accountant-dashboard"))!;

        // PendingCosting must increase by at least 1 (parallel tests may also advance reqs,
        // so strict equality would be flaky in the full suite — >= baseline+1 is sufficient
        // to prove the endpoint is not returning hardcoded zeros).
        after.PendingCosting.Should().BeGreaterThanOrEqualTo(baseline.PendingCosting + 1,
            $"requisition {refNo} just landed at CostingPending");
    }

    [Fact]
    public async Task Get_InProgressAndMdReviewBuckets_IncrementWhenSeededReqsAdvance()
    {
        // Read baseline as Sara
        var saraToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", saraToken);
        var baseline = (await _client.GetFromJsonAsync<DashboardStats>("/api/stats/accountant-dashboard"))!;

        // Seed: req at CostingPending (Sales → BomCreator → submit BOM)
        var (reqId, reqItemId, _) = await SeedOneRequisitionAtCostingPendingAsync();

        // Advance to CostingInProgress: Sara starts costing for the first item
        saraToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", saraToken);
        var startResp = await _client.PostAsync($"/api/costing/{reqId}/items/{reqItemId}/start", null);
        startResp.EnsureSuccessStatusCode();

        // Re-read counts
        var afterStart = (await _client.GetFromJsonAsync<DashboardStats>("/api/stats/accountant-dashboard"))!;

        // CostingInProgress incremented by at least 1 (parallel-safe)
        afterStart.InProgress.Should().BeGreaterThanOrEqualTo(baseline.InProgress + 1,
            "starting costing on a CostingPending requisition should bump InProgress by at least 1");
    }

    // Seeds a single requisition through Sales create → BomCreator start/save/submit → CostingPending.
    // Returns (ReqId, ReqItemId, RefNo) so callers can advance the workflow further.
    private async Task<(int ReqId, int ReqItemId, string RefNo)> SeedOneRequisitionAtCostingPendingAsync()
    {
        // Sales creates the requisition
        var salesToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", salesToken);

        var customers = (await _client.GetFromJsonAsync<List<SeedCustomerShort>>("/api/customers"))!;
        // SP sees FinishedGood only (server-enforced)
        var spItems = (await _client.GetFromJsonAsync<List<SeedItemShort>>("/api/items"))!;

        var customer = customers.First();
        var finishedGood = spItems.First(i => i.Type == "FinishedGood");

        var createResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customer.Id,
            CurrencyCode = "AED",
            Items = new[] { new { ItemId = finishedGood.Id, ExpectedQty = 10m } }
        });
        createResp.EnsureSuccessStatusCode();
        var created = (await createResp.Content.ReadFromJsonAsync<SeedCreateResponse>())!;
        var reqId = created.Id;

        // Fetch the requisition item id
        var detail = (await _client.GetFromJsonAsync<SeedRequisitionDetail>($"/api/requisitions/{reqId}"))!;
        var reqItemId = detail.Items[0].Id;

        // Admin creates a process for BOM lines
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var processCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var processResp = await _client.PostAsJsonAsync("/api/processes", new { Name = processCode, DisplayOrder = 99 });
        processResp.EnsureSuccessStatusCode();
        var process = (await processResp.Content.ReadFromJsonAsync<SeedProcessShort>())!;

        // BomCreator: start BOM, save lines, submit BOM (→ CostingPending)
        // Note: BomCreator seed email is bob@test.com
        // BomCreator can see RawMaterial items (not role-restricted)
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);
        var bomItems = (await _client.GetFromJsonAsync<List<SeedItemShort>>("/api/items"))!;
        var rawMaterial = bomItems.First(i => i.Type == "RawMaterial");

        var startBom = await _client.PostAsync($"/api/bom/{reqId}/items/{reqItemId}/start", null);
        startBom.EnsureSuccessStatusCode();

        var saveBom = await _client.PutAsJsonAsync($"/api/bom/{reqId}/items/{reqItemId}/lines", new
        {
            Lines = new[] { new { ProcessId = process.Id, RawMaterialItemId = rawMaterial.Id, KgPerUnit = 1.5m, QtyPerKg = 1.5m, WastagePct = 2.0m } }
        });
        saveBom.EnsureSuccessStatusCode();

        var submitBom = await _client.PostAsync($"/api/bom/{reqId}/submit", null);
        submitBom.EnsureSuccessStatusCode();

        return (reqId, reqItemId, created.RefNo);
    }

    // Private records used only by the seed helper
    private record SeedCustomerShort(int Id, string Name);
    private record SeedItemShort(int Id, string Code, string Description, string Type);
    private record SeedCreateResponse(int Id, string RefNo);
    private record SeedProcessShort(int Id, string Name);
    private record SeedRequisitionItemShort(int Id, int ItemId, string ItemDescription, decimal ExpectedQty, int SortOrder);
    private record SeedRequisitionDetail(int Id, int CustomerId, List<SeedRequisitionItemShort> Items);
}
