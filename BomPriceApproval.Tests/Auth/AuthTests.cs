using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Auth;

public class AuthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

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

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
}
