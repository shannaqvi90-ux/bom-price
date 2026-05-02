using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Items;

public class ItemsListBranchAndTypeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> TokenAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    [Fact]
    public async Task SP_GetItems_DefaultsToFinishedGood_WhenNoTypeFilter()
    {
        var token = await TokenAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items"))!;
        items.Should().OnlyContain(i => i.Type == "FinishedGood",
            "SP defaults to FinishedGood when ?type= is not specified");
    }

    [Fact]
    public async Task SP_GetItems_HonorsExplicitRawMaterialFilter()
    {
        var token = await TokenAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // V3: SP needs RawMaterial visibility to populate BOM-line picker on the
        // combined req+BOM create page. Explicit ?type=RawMaterial must be honored.
        var rms = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?type=RawMaterial"))!;
        rms.Should().OnlyContain(i => i.Type == "RawMaterial");
    }

    [Fact]
    public async Task BomCreator_GetItems_DefaultIncludesAllTypes()
    {
        var token = await TokenAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items"))!;
        items.Select(i => i.Type).Distinct().Should().Contain(new[] { "FinishedGood", "RawMaterial" });
    }

    [Fact]
    public async Task GetItems_BranchIdFilter_RestrictsToThatBranch()
    {
        var token = await TokenAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var b1 = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1"))!;
        b1.Should().OnlyContain(i => i.BranchId == 1);

        var b2 = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=2"))!;
        b2.Should().OnlyContain(i => i.BranchId == 2);
    }

    private record LoginResponse(string AccessToken);
    private record ItemShort(int Id, string Code, string Description, string Type, int BranchId);
}
