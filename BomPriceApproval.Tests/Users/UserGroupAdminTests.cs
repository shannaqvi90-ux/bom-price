using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Users;

public class UserGroupAdminTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task<int> CreateGroupAsync(string namePrefix)
    {
        var resp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"{namePrefix}-{Guid.NewGuid():N}".Substring(0, 18) });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<GroupShort>())!.Id;
    }

    /// <summary>Creates a throwaway SalesPerson via Admin so tests don't share seed-user state.</summary>
    private async Task<UserShort> CreateThrowawaySpAsync()
    {
        var email = $"sp-{Guid.NewGuid():N}@test.com";
        var resp = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "Throwaway SP", Email = email, Password = "Test@1234", Role = 1, BranchId = 1
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<UserShort>())!;
    }

    [Fact]
    public async Task SetGroup_AsAdmin_OnSP_Persists()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpId = await CreateGroupAsync("SetA");
        var sp = await CreateThrowawaySpAsync();

        var put = await _client.PutAsJsonAsync($"/api/users/{sp.Id}/group", new { GroupId = grpId });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetFromJsonAsync<UserGroupResponse>($"/api/users/{sp.Id}/group");
        get!.GroupId.Should().Be(grpId);
    }

    [Fact]
    public async Task SetGroup_AsAccountant_OnSP_Persists()
    {
        // Create the throwaway SP as Admin (POST /api/users requires Admin role)
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var grpId = await CreateGroupAsync("SetB");
        var sp = await CreateThrowawaySpAsync();

        // Now switch to Accountant to perform the group assignment
        var sara = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sara.AccessToken);

        var put = await _client.PutAsJsonAsync($"/api/users/{sp.Id}/group", new { GroupId = grpId });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SetGroup_AsSP_Returns403()
    {
        var ali = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ali.AccessToken);

        var put = await _client.PutAsJsonAsync($"/api/users/999/group", new { GroupId = 1 });
        put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SetGroup_OnNonSPUser_Returns400()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpId = await CreateGroupAsync("Reject");
        var users = (await _client.GetFromJsonAsync<List<UserShort>>("/api/users"))!;
        var bob = users.First(u => u.Email == "bob@test.com");  // BomCreator

        var put = await _client.PutAsJsonAsync($"/api/users/{bob.Id}/group", new { GroupId = grpId });
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ClearGroup_PassNullGroupId_Persists()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpId = await CreateGroupAsync("Clear");
        var sp = await CreateThrowawaySpAsync();

        await _client.PutAsJsonAsync($"/api/users/{sp.Id}/group", new { GroupId = grpId });
        var clear = await _client.PutAsJsonAsync($"/api/users/{sp.Id}/group", new { GroupId = (int?)null });
        clear.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetFromJsonAsync<UserGroupResponse>($"/api/users/{sp.Id}/group");
        get!.GroupId.Should().BeNull();
    }

    [Fact]
    public async Task SetGroup_InactiveGroup_Returns400()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpId = await CreateGroupAsync("Inactive");
        // Soft-delete the group
        var del = await _client.DeleteAsync($"/api/groups/{grpId}");
        del.EnsureSuccessStatusCode();

        var sp = await CreateThrowawaySpAsync();

        var put = await _client.PutAsJsonAsync($"/api/users/{sp.Id}/group", new { GroupId = grpId });
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record GroupShort(int Id, string Name, bool IsActive);
    private record UserShort(int Id, string Email, string Name, string Role);
    private record UserGroupResponse(int? GroupId, string? GroupName);
}
