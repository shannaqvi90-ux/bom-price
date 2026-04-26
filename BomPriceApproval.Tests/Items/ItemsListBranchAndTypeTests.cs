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
    public async Task SP_GetItems_ExcludesRawMaterial_RegardlessOfTypeParam()
    {
        var token = await TokenAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items"))!;
        items.Should().OnlyContain(i => i.Type == "FinishedGood");

        // Even if SP explicitly asks for RawMaterial, server still excludes
        var withParam = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?type=RawMaterial"))!;
        withParam.Should().OnlyContain(i => i.Type == "FinishedGood",
            "SP role server-enforces RawMaterial exclusion as defense-in-depth");
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
