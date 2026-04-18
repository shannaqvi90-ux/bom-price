using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Users;

public class UserTokenRevocationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(string AccessToken, string RefreshToken, int UserId)> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<LoginResult>();
        return (body!.AccessToken, body.RefreshToken, body.UserId);
    }

    private async Task<string> AdminTokenAsync() =>
        (await LoginAsync("admin@test.com", "Admin@1234")).AccessToken;

    private async Task<int> CreateUserAsync(string email, UserRole role, int? branchId)
    {
        var adminToken = await AdminTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var resp = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "Test User",
            Email = email,
            Password = "Test@1234",
            Role = (int)role,
            BranchId = branchId
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<CreatedUser>();
        return body!.Id;
    }

    [Fact]
    public async Task UpdateUserRole_RevokesActiveRefreshTokens()
    {
        var email = $"rev-role-{Guid.NewGuid():N}"[..28] + "@t.com";
        var userId = await CreateUserAsync(email, UserRole.SalesPerson, branchId: 1);

        var (_, refreshToken, _) = await LoginAsync(email, "Test@1234");

        // Admin changes the user's role
        var adminToken = await AdminTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var updateResp = await _client.PutAsJsonAsync($"/api/users/{userId}", new
        {
            Name = "Test User",
            Email = email,
            Role = UserRole.BomCreator,
            BranchId = 1,
            IsActive = true
        });
        updateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Old refresh token must now be rejected
        _client.DefaultRequestHeaders.Authorization = null;
        var refreshResp = await _client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = refreshToken });
        refreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateUserBranch_RevokesActiveRefreshTokens()
    {
        var email = $"rev-branch-{Guid.NewGuid():N}"[..28] + "@t.com";
        var userId = await CreateUserAsync(email, UserRole.SalesPerson, branchId: 1);

        var (_, refreshToken, _) = await LoginAsync(email, "Test@1234");

        var adminToken = await AdminTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var updateResp = await _client.PutAsJsonAsync($"/api/users/{userId}", new
        {
            Name = "Test User",
            Email = email,
            Role = UserRole.SalesPerson,
            BranchId = 2,
            IsActive = true
        });
        updateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        _client.DefaultRequestHeaders.Authorization = null;
        var refreshResp = await _client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = refreshToken });
        refreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateUser_SameRoleSameBranch_DoesNotRevokeTokens()
    {
        var email = $"rev-noop-{Guid.NewGuid():N}"[..28] + "@t.com";
        var userId = await CreateUserAsync(email, UserRole.SalesPerson, branchId: 1);

        var (_, refreshToken, _) = await LoginAsync(email, "Test@1234");

        var adminToken = await AdminTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var updateResp = await _client.PutAsJsonAsync($"/api/users/{userId}", new
        {
            Name = "Updated Name",   // only name changes
            Email = email,
            Role = UserRole.SalesPerson,
            BranchId = 1,
            IsActive = true
        });
        updateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Refresh token must still be valid
        _client.DefaultRequestHeaders.Authorization = null;
        var refreshResp = await _client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = refreshToken });
        refreshResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokeSessions_Admin_InvalidatesRefreshToken()
    {
        var email = $"revoke-{Guid.NewGuid():N}"[..28] + "@t.com";
        var userId = await CreateUserAsync(email, UserRole.SalesPerson, branchId: 1);

        var (_, refreshToken, _) = await LoginAsync(email, "Test@1234");

        var adminToken = await AdminTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var revokeResp = await _client.PostAsync($"/api/users/{userId}/revoke-sessions", content: null);
        revokeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Refresh token must now be rejected
        _client.DefaultRequestHeaders.Authorization = null;
        var refreshResp = await _client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = refreshToken });
        refreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokeSessions_NonAdmin_Forbidden()
    {
        var email = $"revoke-nonad-{Guid.NewGuid():N}"[..20] + "@t.com";
        var userId = await CreateUserAsync(email, UserRole.SalesPerson, branchId: 1);

        // Login as a non-admin and try to revoke sessions
        var (salesToken, _, _) = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", salesToken);
        var revokeResp = await _client.PostAsync($"/api/users/{userId}/revoke-sessions", content: null);
        revokeResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private record LoginResult(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record CreatedUser(int Id, string Name, string Email, string Role, int? BranchId, string? BranchName, bool IsActive);
}
