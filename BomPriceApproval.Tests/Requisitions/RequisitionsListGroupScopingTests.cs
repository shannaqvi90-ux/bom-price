using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

public class RequisitionsListGroupScopingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task<(int spId, string email)> CreateSpAsync(string namePrefix, int branchId)
    {
        var email = $"{namePrefix}-{Guid.NewGuid():N}".Substring(0, 22) + "@test.com";
        var resp = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = $"SP {namePrefix}", Email = email, Password = "Test@1234", Role = 1, BranchId = branchId
        });
        resp.EnsureSuccessStatusCode();
        var u = (await resp.Content.ReadFromJsonAsync<UserShort>())!;
        return (u.Id, email);
    }

    /// <summary>Creates a customer as the currently authenticated SP and returns its Id.</summary>
    private async Task<int> CreateCustomerAsSpAsync()
    {
        var code = $"C-{Guid.NewGuid():N}".Substring(0, 14);
        var resp = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = code, Name = $"Customer {code}", Address = "Test", Email = $"{code}@test.com", PhoneNumber = "0000"
        });
        resp.EnsureSuccessStatusCode();
        var c = (await resp.Content.ReadFromJsonAsync<CustomerShort>())!;
        return c.Id;
    }

    [Fact]
    public async Task SP_NoGroup_OnlySeesOwnReqs()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var (spA_Id, spA_email) = await CreateSpAsync("noGrpA", 1);
        var (spB_Id, spB_email) = await CreateSpAsync("noGrpB", 1);

        // SP B creates a customer + req
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var custId = await CreateCustomerAsSpAsync();
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        items.Should().NotBeEmpty("seed includes branch-1 finished goods");
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1, CustomerId = custId, CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        create.EnsureSuccessStatusCode();
        var spBReqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        // SP A logs in — should NOT see B's req
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var list = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions"))!;
        list.Any(r => r.Id == spBReqId).Should().BeFalse("solo SP only sees own");
    }

    [Fact]
    public async Task SP_InGroupWithPeer_SeesPeersReqs()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        // Create group
        var grpResp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"GrpScope-{Guid.NewGuid():N}".Substring(0, 18) });
        grpResp.EnsureSuccessStatusCode();
        var grpId = (await grpResp.Content.ReadFromJsonAsync<GroupShort>())!.Id;

        // Create 2 SPs and put both in group
        var (spA_Id, spA_email) = await CreateSpAsync("grpA", 1);
        var (spB_Id, spB_email) = await CreateSpAsync("grpB", 1);
        await _client.PutAsJsonAsync($"/api/users/{spA_Id}/group", new { GroupId = grpId });
        await _client.PutAsJsonAsync($"/api/users/{spB_Id}/group", new { GroupId = grpId });

        // SP B creates a customer + req
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var custId = await CreateCustomerAsSpAsync();
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        items.Should().NotBeEmpty("seed includes branch-1 finished goods");
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1, CustomerId = custId, CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        create.EnsureSuccessStatusCode();
        var spBReqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        // SP A logs in — should now SEE B's req
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var list = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions"))!;
        list.Any(r => r.Id == spBReqId).Should().BeTrue("group peers share req visibility");
    }

    [Fact]
    public async Task SP_RemovedFromGroup_LosesPeerVisibility()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpResp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"GrpCut-{Guid.NewGuid():N}".Substring(0, 18) });
        grpResp.EnsureSuccessStatusCode();
        var grpId = (await grpResp.Content.ReadFromJsonAsync<GroupShort>())!.Id;

        var (spA_Id, spA_email) = await CreateSpAsync("cutA", 1);
        var (spB_Id, spB_email) = await CreateSpAsync("cutB", 1);
        await _client.PutAsJsonAsync($"/api/users/{spA_Id}/group", new { GroupId = grpId });
        await _client.PutAsJsonAsync($"/api/users/{spB_Id}/group", new { GroupId = grpId });

        // SP B creates a customer + req
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var custId = await CreateCustomerAsSpAsync();
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        items.Should().NotBeEmpty("seed includes branch-1 finished goods");
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1, CustomerId = custId, CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        create.EnsureSuccessStatusCode();
        var spBReqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        // Remove A from group
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        await _client.PutAsJsonAsync($"/api/users/{spA_Id}/group", new { GroupId = (int?)null });

        // SP A re-checks list — no longer sees B
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var list = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions"))!;
        list.Any(r => r.Id == spBReqId).Should().BeFalse("Q11 clean cut");
    }

    [Fact]
    public async Task SP_InGroupCrossBranch_SeesCrossBranchPeerReqs()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpResp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"Cross-{Guid.NewGuid():N}".Substring(0, 18) });
        grpResp.EnsureSuccessStatusCode();
        var grpId = (await grpResp.Content.ReadFromJsonAsync<GroupShort>())!.Id;

        var (spB1_Id, spB1_email) = await CreateSpAsync("br1", 1);
        var (spB2_Id, spB2_email) = await CreateSpAsync("br2", 2);
        await _client.PutAsJsonAsync($"/api/users/{spB1_Id}/group", new { GroupId = grpId });
        await _client.PutAsJsonAsync($"/api/users/{spB2_Id}/group", new { GroupId = grpId });

        // SP-branch1 creates a customer + branch-1 req
        var spB1 = await LoginAsync(spB1_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB1.AccessToken);
        var custId = await CreateCustomerAsSpAsync();
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        items.Should().NotBeEmpty("seed includes branch-1 finished goods");
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1, CustomerId = custId, CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        create.EnsureSuccessStatusCode();
        var br1ReqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        // SP-branch2 logs in and should see branch-1 req via group
        var spB2 = await LoginAsync(spB2_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB2.AccessToken);
        var list = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions"))!;
        list.Any(r => r.Id == br1ReqId).Should().BeTrue("Q5 group is branch-agnostic");
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record UserShort(int Id, string Email, string Name, string Role);
    private record GroupShort(int Id, string Name, bool IsActive);
    private record CustomerShort(int Id, string Name);
    private record ItemShort(int Id, string Code, string Description, string Type, int BranchId);
    private record CreateResponse(int Id, string RefNo);
    private record ReqShort(int Id, string RefNo, string Status, int BranchId);
}
