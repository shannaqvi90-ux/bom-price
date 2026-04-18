using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Auth;

public class RefreshTokenRaceTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private record LoginBody(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);

    [Fact]
    public async Task Refresh_TwoConcurrentRequests_OneSucceedsOneIs401()
    {
        // Arrange: log in and capture the refresh token.
        var login = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = "ali@test.com", Password = "Test@1234" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await login.Content.ReadFromJsonAsync<LoginBody>();
        var refreshToken = body!.RefreshToken;

        // Act: fire two concurrent /refresh requests using the SAME refresh token.
        // Use a fresh HttpClient per task so HttpClient internal connection pooling
        // does not serialise the two requests.
        async Task<HttpResponseMessage> PostRefresh()
        {
            var c = factory.CreateClient();
            return await c.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = refreshToken });
        }

        var task1 = PostRefresh();
        var task2 = PostRefresh();
        var responses = await Task.WhenAll(task1, task2);

        // Assert: exactly one succeeds, exactly one is 401.
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var unauthorizedCount = responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized);

        successCount.Should().Be(1,
            "exactly one concurrent refresh must succeed (the one that wins the xmin race)");
        unauthorizedCount.Should().Be(1,
            "the losing concurrent refresh must receive 401 (token already consumed)");
    }
}
