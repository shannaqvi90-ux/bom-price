using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Customers;

public class CustomersListGroupScopingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
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

    [Fact]
    public async Task SP_InGroup_SeesPeerCreatedCustomers()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpResp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"CustGrp-{Guid.NewGuid():N}".Substring(0, 18) });
        var grpId = (await grpResp.Content.ReadFromJsonAsync<GroupShort>())!.Id;

        var (spA_Id, spA_email) = await CreateSpAsync("cgA");
        var (spB_Id, spB_email) = await CreateSpAsync("cgB");
        await _client.PutAsJsonAsync($"/api/users/{spA_Id}/group", new { GroupId = grpId });
        await _client.PutAsJsonAsync($"/api/users/{spB_Id}/group", new { GroupId = grpId });

        // SP B creates a customer
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var custResp = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = $"CUST-{Guid.NewGuid():N}".Substring(0, 12),
            Name = "Test Customer for Group",
            Address = "", Email = "", PhoneNumber = ""
        });
        custResp.EnsureSuccessStatusCode();
        var custId = (await custResp.Content.ReadFromJsonAsync<CustShort>())!.Id;

        // SP A logs in — should see B's customer
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var list = (await _client.GetFromJsonAsync<List<CustShort>>("/api/customers"))!;
        list.Any(c => c.Id == custId).Should().BeTrue("group peer customers visible");
    }

    [Fact]
    public async Task SP_NoGroup_OnlySeesOwnCustomers()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var (spA_Id, spA_email) = await CreateSpAsync("scA");
        var (spB_Id, spB_email) = await CreateSpAsync("scB");

        // SP B creates a customer
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var custResp = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = $"CUST-{Guid.NewGuid():N}".Substring(0, 12),
            Name = "Solo Test Customer",
            Address = "", Email = "", PhoneNumber = ""
        });
        custResp.EnsureSuccessStatusCode();
        var custId = (await custResp.Content.ReadFromJsonAsync<CustShort>())!.Id;

        // SP A — solo, doesn't see B's customer
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var list = (await _client.GetFromJsonAsync<List<CustShort>>("/api/customers"))!;
        list.Any(c => c.Id == custId).Should().BeFalse("solo SP only sees own customers");
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record UserShort(int Id, string Email, string Name, string Role);
    private record GroupShort(int Id, string Name, bool IsActive);
    private record CustShort(int Id, string Code, string Name);
}
