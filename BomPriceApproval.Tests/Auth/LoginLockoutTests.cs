using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Auth;

public class LoginLockoutTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    // ── helpers ──────────────────────────────────────────────────────────

    private async Task<string> CreateLockedUserAsync(string email)
    {
        const string password = "Test@1234";
        await CreateUserViaApiAsync(email);

        // 5 wrong attempts to trigger lockout
        for (int i = 0; i < 5; i++)
            await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = "Wrong@999" });

        return password;
    }

    private async Task CreateUserViaApiAsync(string email)
    {
        var adminToken = await AdminTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var resp = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "Lockout Test User",
            Email = email,
            Password = "Test@1234",
            Role = (int)UserRole.SalesPerson,
            BranchId = 1
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created, $"seeding user {email} failed");
        _client.DefaultRequestHeaders.Authorization = null;
    }

    private async Task<string> AdminTokenAsync()
    {
        // Reset lockout so accumulated failed attempts from other tests never block admin.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admin = db.Users.First(u => u.Email == "admin@test.com");
            admin.FailedLoginAttempts = 0;
            admin.LockedUntil = null;
            db.SaveChanges();
        }

        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = "admin@test.com", Password = "Admin@1234" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<LoginResult>();
        return body!.AccessToken;
    }

    // ── tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_After5FailedAttempts_IsLocked()
    {
        var email = $"lck-{Guid.NewGuid():N}"[..28] + "@t.com";
        await CreateUserViaApiAsync(email);

        // 5 wrong attempts
        for (int i = 0; i < 5; i++)
        {
            var r = await _client.PostAsJsonAsync("/api/auth/login",
                new { Email = email, Password = "Wrong@999" });
            r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // 6th attempt with correct password should return 400 locked, not 200
        var locked = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = "Test@1234" });

        locked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await locked.Content.ReadFromJsonAsync<ValidationProblemResult>();
        body!.Detail.Should().Contain("locked");
        body.Errors.Should().ContainKey("Email");
    }

    [Fact]
    public async Task Login_AfterLockoutExpires_Succeeds()
    {
        var email = $"lck-exp-{Guid.NewGuid():N}"[..24] + "@t.com";
        await CreateUserViaApiAsync(email);

        // Put LockedUntil in the past directly via EF
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.First(u => u.Email == email);
            user.FailedLoginAttempts = 5;
            user.LockedUntil = DateTime.UtcNow.AddMinutes(-1); // already expired
            db.SaveChanges();
        }

        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = "Test@1234" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_SuccessResets_FailedAttempts()
    {
        var email = $"lck-rst-{Guid.NewGuid():N}"[..24] + "@t.com";
        await CreateUserViaApiAsync(email);

        // 3 wrong attempts
        for (int i = 0; i < 3; i++)
            await _client.PostAsJsonAsync("/api/auth/login",
                new { Email = email, Password = "Wrong@999" });

        // Correct login clears the counter
        var ok = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = "Test@1234" });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify DB state
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.Users.First(u => u.Email == email);
        user.FailedLoginAttempts.Should().Be(0);
        user.LockedUntil.Should().BeNull();
    }

    // ── private DTOs ──────────────────────────────────────────────────────

    private record LoginResult(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record ValidationProblemResult(string Detail, Dictionary<string, string[]> Errors);
}
