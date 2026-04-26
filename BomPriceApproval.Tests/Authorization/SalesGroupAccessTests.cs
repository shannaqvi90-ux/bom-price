using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Authorization;

public class SalesGroupAccessTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task<(int spId, string email)> CreateSpAsync(string namePrefix)
    {
        var email = $"{namePrefix}-{Guid.NewGuid():N}".Substring(0, 22) + "@test.com";
        var resp = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = $"SP {namePrefix}", Email = email, Password = "Test@1234", Role = 1, BranchId = 1
        });
        resp.EnsureSuccessStatusCode();
        var u = (await resp.Content.ReadFromJsonAsync<UserShort>())!;
        return (u.Id, email);
    }

    private async Task<int> SetupGroupAsync(string namePrefix, int[] spIds)
    {
        var grpResp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"{namePrefix}-{Guid.NewGuid():N}".Substring(0, 18) });
        grpResp.EnsureSuccessStatusCode();
        var grpId = (await grpResp.Content.ReadFromJsonAsync<GroupShort>())!.Id;
        foreach (var id in spIds)
            await _client.PutAsJsonAsync($"/api/users/{id}/group", new { GroupId = grpId });
        return grpId;
    }

    /// <summary>Creates a customer while authenticated as the current user (must already have auth header set).</summary>
    private async Task<int> CreateCustomerAsync(string prefix)
    {
        var resp = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = $"CUST-{Guid.NewGuid():N}".Substring(0, 12),
            Name = $"Customer {prefix}",
            Address = "",
            Email = "",
            PhoneNumber = ""
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CustShort>())!.Id;
    }

    [Fact]
    public async Task GroupMember_CanGet_PeerReqDetail()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var (spA_Id, spA_email) = await CreateSpAsync("acA");
        var (spB_Id, spB_email) = await CreateSpAsync("acB");
        await SetupGroupAsync("acGrp", new[] { spA_Id, spB_Id });

        // SP B creates a customer and a req using that customer
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var custId = await CreateCustomerAsync("acB");
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1, CustomerId = custId, CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        create.EnsureSuccessStatusCode();
        var spBReqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        // SP A GETs B's req detail — should succeed (group-mate)
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var get = await _client.GetAsync($"/api/requisitions/{spBReqId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NonGroupSP_CannotGet_OtherSPReqDetail_Returns403or404()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var (spA_Id, spA_email) = await CreateSpAsync("ngA");
        var (spB_Id, spB_email) = await CreateSpAsync("ngB");
        // No group — both solo

        // SP B creates a customer and a req
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var custId = await CreateCustomerAsync("ngB");
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1, CustomerId = custId, CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        create.EnsureSuccessStatusCode();
        var spBReqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        // SP A (solo, different SP) tries to GET B's req — should be 403 or 404
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var get = await _client.GetAsync($"/api/requisitions/{spBReqId}");
        get.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GroupMember_CanCreateReq_AgainstPeerCustomer()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var (spA_Id, spA_email) = await CreateSpAsync("crA");
        var (spB_Id, spB_email) = await CreateSpAsync("crB");
        await SetupGroupAsync("crGrp", new[] { spA_Id, spB_Id });

        // SP B creates a customer
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var custId = await CreateCustomerAsync("crB");

        // SP A creates a req against B's customer — should succeed
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1, CustomerId = custId, CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        // The new req has SalesPersonId = SP A (not SP B)
        var reqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;
        var detail = (await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}"))!;
        detail.SalesPersonId.Should().Be(spA_Id);
    }

    [Fact]
    public async Task GroupMember_CountMatchesGetAll_ForPeerReqs()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var (spA_Id, spA_email) = await CreateSpAsync("cmA");
        var (spB_Id, spB_email) = await CreateSpAsync("cmB");
        await SetupGroupAsync("cmGrp", new[] { spA_Id, spB_Id });

        // SP B creates a customer and 2 reqs
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var custId = await CreateCustomerAsync("cmB");
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        for (var i = 0; i < 2; i++)
        {
            var r = await _client.PostAsJsonAsync("/api/requisitions", new
            {
                BranchId = 1, CustomerId = custId, CurrencyCode = "AED",
                Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
            });
            r.EnsureSuccessStatusCode();
        }

        // SP A logs in — Count must match GetAll length, and must be > 0
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var countResp = (await _client.GetFromJsonAsync<CountResponse>("/api/requisitions/count"))!;
        var listResp = (await _client.GetFromJsonAsync<List<ReqListItem>>("/api/requisitions"))!;

        listResp.Count.Should().BeGreaterThan(0, "SP A should see SP B's reqs via group membership");
        countResp.Count.Should().Be(listResp.Count, "Count endpoint should match GetAll list length");
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record UserShort(int Id, string Email, string Name, string Role);
    private record GroupShort(int Id, string Name, bool IsActive);
    private record CustShort(int Id, string Code, string Name);
    private record ItemShort(int Id, string Code, string Description, string Type, int BranchId);
    private record CreateResponse(int Id, string RefNo);
    private record ReqDetail(int Id, int SalesPersonId);
    private record CountResponse(int Count);
    private record ReqListItem(int Id, string RefNo);
    private record CustomerShort(int Id, string Name);
}
