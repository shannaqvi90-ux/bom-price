using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

public class ChangeBranchTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    /// <summary>Seeds a requisition in branch 1 in CostingPending status (BOM submitted).</summary>
    private async Task<int> SeedRequisitionAtCostingPendingInBranch1()
    {
        var sales = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sales.AccessToken);

        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        var rawMaterials = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=RawMaterial"))!;
        // SP can't see RawMaterials — fetch as admin for BOM seed
        var adminLogin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminLogin.AccessToken);
        rawMaterials = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=RawMaterial"))!;

        // Reset to sales auth
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sales.AccessToken);

        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1,
            CustomerId = customers.First().Id,
            CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        create.EnsureSuccessStatusCode();
        var reqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        var detail = (await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}"))!;
        var reqItemId = detail.Items[0].Id;

        // Admin creates a process for BOM lines
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminLogin.AccessToken);
        var processCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var processResp = await _client.PostAsJsonAsync("/api/processes", new { Name = processCode, DisplayOrder = 99 });
        processResp.EnsureSuccessStatusCode();
        var process = (await processResp.Content.ReadFromJsonAsync<ProcessShort>())!;

        // BomCreator (bob, branch 1) walks BOM → CostingPending
        var bom = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bom.AccessToken);

        var startBom = await _client.PostAsync($"/api/bom/{reqId}/items/{reqItemId}/start", null);
        startBom.EnsureSuccessStatusCode();

        var saveBom = await _client.PutAsJsonAsync($"/api/bom/{reqId}/items/{reqItemId}/lines", new
        {
            Lines = new[] { new { ProcessId = process.Id, RawMaterialItemId = rawMaterials.First().Id, KgPerUnit = 1.5m, QtyPerKg = 1.5m, WastagePct = 2.0m } }
        });
        saveBom.EnsureSuccessStatusCode();

        var submitBom = await _client.PostAsync($"/api/bom/{reqId}/submit", null);
        submitBom.EnsureSuccessStatusCode();

        return reqId;
    }

    [Fact]
    public async Task ChangeBranch_AsAccountant_InCostingPending_UpdatesBranch_AndWritesHistory()
    {
        var reqId = await SeedRequisitionAtCostingPendingInBranch1();

        var sara = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sara.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/branch", new
        {
            BranchId = 2,
            Reason = "Order belongs to Alain"
        });
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest, "items still belong to branch 1; user must remove them first");
    }

    [Fact]
    public async Task ChangeBranch_StrictBlock_OnItemMismatch_Returns400_WithItemList()
    {
        var reqId = await SeedRequisitionAtCostingPendingInBranch1();

        var sara = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sara.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/branch", new
        {
            BranchId = 2,
            Reason = "Wrong branch"
        });
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await patch.Content.ReadAsStringAsync();
        body.Should().Contain("branch 2", "error message lists items not in target branch");
    }

    [Fact]
    public async Task ChangeBranch_AsSP_Returns403()
    {
        var reqId = await SeedRequisitionAtCostingPendingInBranch1();
        var sales = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sales.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/branch", new { BranchId = 2 });
        patch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChangeBranch_AsBomCreator_Returns403()
    {
        var reqId = await SeedRequisitionAtCostingPendingInBranch1();
        var bom = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bom.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/branch", new { BranchId = 2 });
        patch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChangeBranch_StatusBeyondCostingPending_Returns400()
    {
        var reqId = await SeedRequisitionAtCostingPendingInBranch1();

        var sara = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sara.AccessToken);
        var detail = (await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}"))!;
        var reqItemId = detail.Items[0].Id;
        var startCosting = await _client.PostAsync($"/api/costing/{reqId}/items/{reqItemId}/start", null);
        startCosting.EnsureSuccessStatusCode();

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/branch", new { BranchId = 2 });
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest, "branch change blocked from CostingInProgress onward");
    }

    [Fact]
    public async Task ChangeBranch_SameBranch_Returns400()
    {
        var reqId = await SeedRequisitionAtCostingPendingInBranch1();
        var sara = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sara.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/branch", new { BranchId = 1 });
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangeBranch_AccountantNotAssignedToReqBranch_Returns403()
    {
        var reqId = await SeedRequisitionAtCostingPendingInBranch1();

        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var email = $"acct2only-{Guid.NewGuid():N}"[..22] + "@test.com";
        var createUser = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "Branch2 Only", Email = email, Password = "Test@1234", Role = 3, BranchId = (int?)null
        });
        createUser.EnsureSuccessStatusCode();
        var created = (await createUser.Content.ReadFromJsonAsync<UserShort>())!;
        await _client.PutAsJsonAsync($"/api/users/{created.Id}/branches", new { BranchIds = new[] { 2 } });

        var login = await LoginAsync(email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/branch", new { BranchId = 2 });
        patch.StatusCode.Should().Be(HttpStatusCode.Forbidden, "branch-2-only Accountant cannot act on branch-1 req");
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record CustomerShort(int Id, string Name);
    private record ItemShort(int Id, string Code, string Description, string Type, int BranchId);
    private record CreateResponse(int Id, string RefNo);
    private record ProcessShort(int Id, string Name);
    private record RequisitionItemShort(int Id, int ItemId, string ItemDescription, decimal ExpectedQty, int SortOrder);
    private record ReqDetail(int Id, int BranchId, List<RequisitionItemShort> Items);
    private record UserShort(int Id, string Email, string Name, string Role);
}
