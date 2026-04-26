using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Users;

public class UserBranchesAdminTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    [Fact]
    public async Task GetBranches_AsAdmin_ForSara_ReturnsBoth()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var users = (await _client.GetFromJsonAsync<List<UserShort>>("/api/users"))!;
        var sara = users.First(u => u.Email == "sara@test.com");

        var branches = (await _client.GetFromJsonAsync<List<int>>($"/api/users/{sara.Id}/branches"))!;
        branches.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async Task SetBranches_ReplacesEntireSet()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var email = $"acctset-{Guid.NewGuid():N}"[..22] + "@test.com";
        var create = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "Set Test", Email = email, Password = "Test@1234", Role = 3, BranchId = (int?)null
        });
        create.EnsureSuccessStatusCode();
        var u = (await create.Content.ReadFromJsonAsync<UserShort>())!;

        // Initially no branches
        var initial = (await _client.GetFromJsonAsync<List<int>>($"/api/users/{u.Id}/branches"))!;
        initial.Should().BeEmpty();

        // Set [1] only
        var set1 = await _client.PutAsJsonAsync($"/api/users/{u.Id}/branches", new { BranchIds = new[] { 1 } });
        set1.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after1 = (await _client.GetFromJsonAsync<List<int>>($"/api/users/{u.Id}/branches"))!;
        after1.Should().BeEquivalentTo(new[] { 1 });

        // Replace with [2] — [1] gone
        var set2 = await _client.PutAsJsonAsync($"/api/users/{u.Id}/branches", new { BranchIds = new[] { 2 } });
        set2.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after2 = (await _client.GetFromJsonAsync<List<int>>($"/api/users/{u.Id}/branches"))!;
        after2.Should().BeEquivalentTo(new[] { 2 });
    }

    [Fact]
    public async Task SetBranches_NonAdmin_Returns403()
    {
        var sales = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sales.AccessToken);

        var resp = await _client.PutAsJsonAsync("/api/users/999/branches", new { BranchIds = new[] { 1 } });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SetBranches_OnNonAccountantUser_Returns400()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var users = (await _client.GetFromJsonAsync<List<UserShort>>("/api/users"))!;
        var bom = users.First(u => u.Email == "bob@test.com");

        var resp = await _client.PutAsJsonAsync($"/api/users/{bom.Id}/branches", new { BranchIds = new[] { 1 } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record UserShort(int Id, string Email, string Name, string Role);
}
