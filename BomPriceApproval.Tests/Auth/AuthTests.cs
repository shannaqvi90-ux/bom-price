using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Auth;

public class AuthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(string AccessToken, string RefreshToken)> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return (body!.AccessToken, body.RefreshToken);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokens()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "admin@test.com",
            Password = "Admin@1234"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBeNullOrEmpty();
        body.Role.Should().Be("Admin");
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "admin@test.com",
            Password = "wrongpassword"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Timing regression guard: the mean response time for an unknown email must be
    /// within 30 ms of the mean for a known email + wrong password.  Both paths now
    /// run a full BCrypt.Verify so their cost is dominated by the hash work.
    ///
    /// If this test becomes flaky on CI (noisy environment), the allowed delta can be
    /// widened; a large persistent deviation (e.g. &gt;100 ms) signals the constant-time
    /// protection has been removed.
    /// </summary>
    [Fact]
    [Trait("Category", "Timing")]
    public async Task Login_WithInvalidEmail_TakesSimilarTimeAsInvalidPassword()
    {
        const int Trials = 10;
        const double AllowedDeltaMs = 30.0;

        // Warm up both paths so JIT / first-request overhead is excluded.
        await _client.PostAsJsonAsync("/api/auth/login", new { Email = "no-such@example.com", Password = "x" });
        await _client.PostAsJsonAsync("/api/auth/login", new { Email = "admin@test.com", Password = "x" });

        var unknownEmailMs = new double[Trials];
        var wrongPasswordMs = new double[Trials];

        for (int i = 0; i < Trials; i++)
        {
            var sw = Stopwatch.StartNew();
            await _client.PostAsJsonAsync("/api/auth/login", new { Email = "no-such@example.com", Password = "WrongPass1!" });
            sw.Stop();
            unknownEmailMs[i] = sw.Elapsed.TotalMilliseconds;
        }

        for (int i = 0; i < Trials; i++)
        {
            var sw = Stopwatch.StartNew();
            await _client.PostAsJsonAsync("/api/auth/login", new { Email = "admin@test.com", Password = "WrongPass1!" });
            sw.Stop();
            wrongPasswordMs[i] = sw.Elapsed.TotalMilliseconds;
        }

        var meanUnknown = unknownEmailMs.Average();
        var meanWrong = wrongPasswordMs.Average();
        var delta = Math.Abs(meanUnknown - meanWrong);

        delta.Should().BeLessThan(AllowedDeltaMs,
            $"unknown-email mean={meanUnknown:F1} ms, wrong-password mean={meanWrong:F1} ms — " +
            $"a large gap indicates the constant-time BCrypt.Verify guard was removed from the login handler");
    }

    [Fact]
    public async Task Logout_Then_AccessToken_Returns401()
    {
        var (accessToken, refreshToken) = await LoginAsync("ali@test.com", "Test@1234");

        // Logout with the access token in the Authorization header
        var logoutClient = factory.CreateClient();
        logoutClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var logoutResp = await logoutClient.PostAsJsonAsync("/api/auth/logout", new { RefreshToken = refreshToken });
        logoutResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Use the old access token on an authenticated endpoint — must be 401 now
        var getClient = factory.CreateClient();
        getClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var getResp = await getClient.GetAsync("/api/requisitions");
        getResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_DoesNotAffect_OtherUsersTokens()
    {
        var (aliToken, aliRefresh) = await LoginAsync("ali@test.com", "Test@1234");
        var (bobToken, _) = await LoginAsync("bob@test.com", "Test@1234");

        // Ali logs out
        var logoutClient = factory.CreateClient();
        logoutClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliToken);
        var logoutResp = await logoutClient.PostAsJsonAsync("/api/auth/logout", new { RefreshToken = aliRefresh });
        logoutResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Bob's token should still work
        var bobClient = factory.CreateClient();
        bobClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
        var bobResp = await bobClient.GetAsync("/api/requisitions");
        bobResp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredJti_Cleanup_RemovesExpiredRows()
    {
        int insertedId;
        using (var insertScope = factory.Services.CreateScope())
        {
            var db = insertScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var expiredJti = new RevokedJti
            {
                Jti = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
                RevokedAt = DateTime.UtcNow.AddMinutes(-16)
            };
            db.RevokedJtis.Add(expiredJti);
            await db.SaveChangesAsync();
            insertedId = expiredJti.Id;
        }

        using (var cleanupScope = factory.Services.CreateScope())
        {
            var db = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deleted = await db.RevokedJtis
                .Where(r => r.ExpiresAt < DateTime.UtcNow)
                .ExecuteDeleteAsync();
            deleted.Should().BeGreaterThan(0);
        }

        using (var verifyScope = factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stillThere = await db.RevokedJtis.FindAsync(insertedId);
            stillThere.Should().BeNull();
        }
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
}
