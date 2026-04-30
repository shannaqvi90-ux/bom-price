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

    // Returns (Id, server-generated Code). descSuffix is used only for Description.
    private async Task<(int Id, string Code)> CreateItemAsAliAsync(string descSuffix)
    {
        var token = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.PostAsJsonAsync("/api/items", new
        {
            Description = $"Test Item {descSuffix}",
            Type = 1, // ItemType.RawMaterial
            LastPurchasePrice = (decimal?)null
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<ItemDto>();
        return (body!.Id, body.Code);
    }

    [Fact]
    public async Task EditItem_AsAdmin_Succeeds()
    {
        var (id, code) = await CreateItemAsAliAsync($"admin-{Guid.NewGuid():N}");

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
        var (id, code) = await CreateItemAsAliAsync($"acc-{Guid.NewGuid():N}"); // ali is branch 1, sara is also branch 1

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
    public async Task EditItem_AsAccountant_NotInUserBranches_Returns403()
    {
        // V2.3-A: Accountant authorization is driven by the M:N UserBranches table,
        // NOT User.BranchId. The seed Sara has UserBranches=[1,2] post-V23a auto-assignment,
        // so we provision a fresh Accountant explicitly scoped to branch 1 only.

        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Create a SalesPerson at branch 2, create item there
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
            Description = "Branch 2 Item",
            Type = 1,
            LastPurchasePrice = (decimal?)null
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var b2Item = await createResp.Content.ReadFromJsonAsync<ItemDto>();

        // Provision a fresh Accountant scoped to UserBranches=[1] only (branch 2 EXCLUDED).
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var accEmail = $"acc1-{Guid.NewGuid():N}"[..12] + "@test.com";
        var newAccResp = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "Branch1 Accountant",
            Email = accEmail,
            Password = "Test@1234",
            Role = 3, // UserRole.Accountant
            BranchId = 1
        });
        newAccResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Login once to capture UserId, then SetBranches as admin.
        var firstAccLogin = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = accEmail, Password = "Test@1234" });
        var loginBody = await firstAccLogin.Content.ReadFromJsonAsync<LoginResponse>();
        var newAccUserId = loginBody!.UserId;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var setBranchesResp = await _client.PutAsJsonAsync(
            $"/api/users/{newAccUserId}/branches",
            new { BranchIds = new[] { 1 } });
        setBranchesResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // This Accountant has UserBranches=[1] — must get 403 on branch 2 item.
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);
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
        // Create two items; both get distinct auto-generated codes from the server
        var (_, codeA) = await CreateItemAsAliAsync($"dup-a-{Guid.NewGuid():N}");
        var (idB, _) = await CreateItemAsAliAsync($"dup-b-{Guid.NewGuid():N}");

        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Try to rename item B to use item A's server-generated code — must 409
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
        var (id, _) = await CreateItemAsAliAsync($"deact-{Guid.NewGuid():N}");

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
        var (id, _) = await CreateItemAsAliAsync($"react-{Guid.NewGuid():N}");

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
