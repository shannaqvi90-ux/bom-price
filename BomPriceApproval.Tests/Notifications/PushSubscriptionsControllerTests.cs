using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.Tests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Notifications;

public class PushSubscriptionsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PushSubscriptionsControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<HttpClient> AuthedAs(string email, string password)
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        return client;
    }

    [Fact]
    public async Task Post_NoAuth_Returns401()
    {
        var resp = await _client.PostAsJsonAsync("/api/notifications/push-subscribe",
            new { endpoint = "https://web.push.apple.com/x", keys = new { p256dh = "p", auth = "a" } });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Delete_NoAuth_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/notifications/push-subscribe")
        {
            Content = JsonContent.Create(new { endpoint = "https://x" })
        };
        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_FirstSubscribe_Returns204AndCreatesRow()
    {
        var client = await AuthedAs("ali@test.com", "Test@1234");
        var endpoint = $"https://web.push.apple.com/{Guid.NewGuid():N}";

        var resp = await client.PostAsJsonAsync("/api/notifications/push-subscribe",
            new { endpoint, keys = new { p256dh = "p_first", auth = "a_first" }, userAgent = "iPhone test" });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sub = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint);
        sub.Should().NotBeNull();
        sub!.P256dh.Should().Be("p_first");
        sub.Auth.Should().Be("a_first");
        sub.UserAgent.Should().Be("iPhone test");
    }

    [Fact]
    public async Task Post_ReSubscribe_UpsertsExistingRow()
    {
        var client = await AuthedAs("ali@test.com", "Test@1234");
        var endpoint = $"https://web.push.apple.com/{Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/api/notifications/push-subscribe",
            new { endpoint, keys = new { p256dh = "old_p", auth = "old_a" } });

        var resp = await client.PostAsJsonAsync("/api/notifications/push-subscribe",
            new { endpoint, keys = new { p256dh = "new_p", auth = "new_a" }, userAgent = "Updated UA" });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subs = await db.PushSubscriptions.Where(s => s.Endpoint == endpoint).ToListAsync();
        subs.Should().HaveCount(1);
        subs[0].P256dh.Should().Be("new_p");
        subs[0].Auth.Should().Be("new_a");
        subs[0].UserAgent.Should().Be("Updated UA");
    }

    [Fact]
    public async Task Post_MissingKeys_Returns400()
    {
        var client = await AuthedAs("ali@test.com", "Test@1234");

        var resp = await client.PostAsJsonAsync("/api/notifications/push-subscribe",
            new { endpoint = "https://web.push.apple.com/missing-keys" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_OwnSubscription_Returns204AndRemoves()
    {
        var client = await AuthedAs("ali@test.com", "Test@1234");
        var endpoint = $"https://web.push.apple.com/{Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/api/notifications/push-subscribe",
            new { endpoint, keys = new { p256dh = "p", auth = "a" } });

        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/notifications/push-subscribe")
        {
            Content = JsonContent.Create(new { endpoint })
        };
        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sub = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint);
        sub.Should().BeNull();
    }

    [Fact]
    public async Task Delete_NonExistentSubscription_Returns204Idempotent()
    {
        var client = await AuthedAs("ali@test.com", "Test@1234");
        var endpoint = $"https://web.push.apple.com/never-existed-{Guid.NewGuid():N}";

        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/notifications/push-subscribe")
        {
            Content = JsonContent.Create(new { endpoint })
        };
        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_OtherUsersSub_LeavesRowIntact()
    {
        var aliClient = await AuthedAs("ali@test.com", "Test@1234");
        var bobClient = await AuthedAs("bob@test.com", "Test@1234");
        var endpoint = $"https://web.push.apple.com/{Guid.NewGuid():N}";

        await aliClient.PostAsJsonAsync("/api/notifications/push-subscribe",
            new { endpoint, keys = new { p256dh = "p", auth = "a" } });

        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/notifications/push-subscribe")
        {
            Content = JsonContent.Create(new { endpoint })
        };
        var resp = await bobClient.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sub = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint);
        sub.Should().NotBeNull();
    }
}
