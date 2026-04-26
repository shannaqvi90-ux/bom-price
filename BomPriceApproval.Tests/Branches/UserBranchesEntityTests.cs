using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Branches;

public class UserBranchesEntityTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    [Fact]
    public async Task SeedMigration_AssignedSara_ToBothBranches()
    {
        // Sara is the seeded Accountant; the V23a migration should have assigned her to all active branches.
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var users = (await _client.GetFromJsonAsync<List<UserShort>>("/api/users"))!;
        var sara = users.First(u => u.Email == "sara@test.com");

        var saraBranches = (await _client.GetFromJsonAsync<List<int>>($"/api/users/{sara.Id}/branches"))!;
        saraBranches.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record UserShort(int Id, string Email, string Name, string Role);
}
