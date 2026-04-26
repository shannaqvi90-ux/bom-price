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

    [Fact]
    public async Task SetGroup_AsAdmin_OnSP_Persists()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpId = await CreateGroupAsync("SetA");
        var users = (await _client.GetFromJsonAsync<List<UserShort>>("/api/users"))!;
        var ali = users.First(u => u.Email == "ali@test.com");

        var put = await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = grpId });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetFromJsonAsync<UserGroupResponse>($"/api/users/{ali.Id}/group");
        get!.GroupId.Should().Be(grpId);

        // Cleanup
        await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = (int?)null });
    }

    [Fact]
    public async Task SetGroup_AsAccountant_OnSP_Persists()
    {
        var sara = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sara.AccessToken);

        var grpId = await CreateGroupAsync("SetB");
        var users = (await _client.GetFromJsonAsync<List<UserShort>>("/api/users"))!;
        var ali = users.First(u => u.Email == "ali@test.com");

        var put = await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = grpId });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Cleanup
        await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = (int?)null });
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
        var users = (await _client.GetFromJsonAsync<List<UserShort>>("/api/users"))!;
        var ali = users.First(u => u.Email == "ali@test.com");

        await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = grpId });
        var clear = await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = (int?)null });
        clear.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetFromJsonAsync<UserGroupResponse>($"/api/users/{ali.Id}/group");
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

        var users = (await _client.GetFromJsonAsync<List<UserShort>>("/api/users"))!;
        var ali = users.First(u => u.Email == "ali@test.com");

        var put = await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = grpId });
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record GroupShort(int Id, string Name, bool IsActive);
    private record UserShort(int Id, string Email, string Name, string Role);
    private record UserGroupResponse(int? GroupId, string? GroupName);
}
