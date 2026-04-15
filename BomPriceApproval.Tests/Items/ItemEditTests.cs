// BomPriceApproval.Tests/Items/ItemEditTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Items;

public class ItemEditTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private async Task<int> CreateItemAsAliAsync(string code)
    {
        var token = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.PostAsJsonAsync("/api/items", new
        {
            Code = code,
            Description = $"Test Item {code}",
            Type = 1, // ItemType.RawMaterial
            LastPurchasePrice = (decimal?)null
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<ItemDto>();
        return body!.Id;
    }

    [Fact]
    public async Task EditItem_AsAdmin_Succeeds()
    {
        var code = $"ADM-{Guid.NewGuid():N}"[..12];
        var id = await CreateItemAsAliAsync(code);

        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var resp = await _client.PutAsJsonAsync($"/api/items/{id}", new
        {
            Code = code,
            Description = "Updated Description",
            Type = "RawMaterial",
            LastPurchasePrice = 5.25m
        });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var items = await _client.GetFromJsonAsync<List<ItemDto>>("/api/items?includeInactive=true");
        items!.First(i => i.Id == id).Description.Should().Be("Updated Description");
        items!.First(i => i.Id == id).LastPurchasePrice.Should().Be(5.25m);
    }

    [Fact]
    public async Task EditItem_AsAccountant_OwnBranch_Succeeds()
    {
        var code = $"ACC-{Guid.NewGuid():N}"[..12];
        var id = await CreateItemAsAliAsync(code); // ali is branch 1, sara is also branch 1

        var saraToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", saraToken);

        var resp = await _client.PutAsJsonAsync($"/api/items/{id}", new
        {
            Code = code,
            Description = "Sara Updated",
            Type = "RawMaterial",
            LastPurchasePrice = (decimal?)null
        });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task EditItem_AsAccountant_CrossBranch_Returns403()
    {
        // Create a SalesPerson at branch 2, create item there
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var spEmail = $"sp2-{Guid.NewGuid():N}"[..12] + "@test.com";
        await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "Branch2 SP",
            Email = spEmail,
            Password = "Test@1234",
            Role = 1, // UserRole.SalesPerson
            BranchId = 2
        });

        var sp2Token = await LoginAsync(spEmail, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp2Token);
        var createResp = await _client.PostAsJsonAsync("/api/items", new
        {
            Code = $"B2-{Guid.NewGuid():N}"[..12],
            Description = "Branch 2 Item",
            Type = 1,
            LastPurchasePrice = (decimal?)null
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var b2Item = await createResp.Content.ReadFromJsonAsync<ItemDto>();

        // Sara is branch 1 Accountant — should get 403 on branch 2 item
        var saraToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", saraToken);

        var resp = await _client.PutAsJsonAsync($"/api/items/{b2Item!.Id}", new
        {
            Code = b2Item.Code,
            Description = "Forbidden Update",
            Type = "RawMaterial",
            LastPurchasePrice = (decimal?)null
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EditItem_DuplicateCode_Returns409()
    {
        var codeA = $"DUP-A-{Guid.NewGuid():N}"[..14];
        var codeB = $"DUP-B-{Guid.NewGuid():N}"[..14];
        await CreateItemAsAliAsync(codeA);
        var idB = await CreateItemAsAliAsync(codeB);

        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Try to rename item B to use item A's code
        var resp = await _client.PutAsJsonAsync($"/api/items/{idB}", new
        {
            Code = codeA,
            Description = "Duplicate Code Attempt",
            Type = "RawMaterial",
            LastPurchasePrice = (decimal?)null
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeactivateItem_ItemDisappearsFromDefaultList()
    {
        var code = $"DEACT-{Guid.NewGuid():N}"[..14];
        var id = await CreateItemAsAliAsync(code);

        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var patchResp = await _client.PatchAsJsonAsync($"/api/items/{id}/status", new { IsActive = false });
        patchResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Default GET (active only) — item gone
        var active = await _client.GetFromJsonAsync<List<ItemDto>>("/api/items");
        active!.Should().NotContain(i => i.Id == id);

        // includeInactive=true — item present
        var all = await _client.GetFromJsonAsync<List<ItemDto>>("/api/items?includeInactive=true");
        all!.Should().Contain(i => i.Id == id && !i.IsActive);
    }

    [Fact]
    public async Task ReactivateItem_ItemAppearsInDefaultList()
    {
        var code = $"REACT-{Guid.NewGuid():N}"[..14];
        var id = await CreateItemAsAliAsync(code);

        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        await _client.PatchAsJsonAsync($"/api/items/{id}/status", new { IsActive = false });
        await _client.PatchAsJsonAsync($"/api/items/{id}/status", new { IsActive = true });

        var active = await _client.GetFromJsonAsync<List<ItemDto>>("/api/items");
        active!.Should().Contain(i => i.Id == id && i.IsActive);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record ItemDto(int Id, string Code, string Description, string Type, int BranchId, bool IsActive, decimal? LastPurchasePrice);
}
