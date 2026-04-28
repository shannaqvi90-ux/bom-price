using System.Net;
using BomPriceApproval.API;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebPush;
using Xunit;
using DomSub = BomPriceApproval.API.Domain.Entities.PushSubscription;

namespace BomPriceApproval.Tests.Notifications;

public class TestWebPushService : WebPushService
{
    public List<(DomSub Sub, string Title, string Body)> Calls { get; } = new();
    public Func<DomSub, Task>? OnSendAsync { get; set; }

    public TestWebPushService(IConfiguration cfg, ILogger<WebPushService> logger) : base(cfg, logger) { }

    public override async Task SendAsync(DomSub sub, string title, string body, CancellationToken ct = default)
    {
        Calls.Add((sub, title, body));
        if (OnSendAsync is not null)
            await OnSendAsync(sub);
    }
}

public sealed class WebPushTestFactory : WebApplicationFactory<Program>
{
    public TestWebPushService TestPushService { get; } = new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "WebPush:VapidPublicKey", "BNxPP9PhIxBjaHv4WdpFrApT7ot3YTeNW0z_uG44VZh3MqcJVDmZ-2I2qRtm6gwKfL0wvtmgrrHpLgSsOQE0aHs" },
            { "WebPush:VapidPrivateKey", "9Q9vdo8gx6JpVvEjtRHsZS0vJjtv1IabO_cERWDFVvw" },
            { "WebPush:Subject", "mailto:test@example.com" },
        }).Build(),
        NullLogger<WebPushService>.Instance);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<WebPushService>();
            services.AddSingleton<WebPushService>(_ => TestPushService);
        });
    }
}

public class NotificationServiceWebPushTests : IClassFixture<WebPushTestFactory>
{
    private readonly WebPushTestFactory _factory;

    public NotificationServiceWebPushTests(WebPushTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<int> SeedSubscriptionAsync(int userId, string endpoint)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sub = new DomSub
        {
            UserId = userId,
            Endpoint = endpoint,
            P256dh = "p",
            Auth = "a",
            CreatedAt = DateTime.UtcNow,
        };
        db.PushSubscriptions.Add(sub);
        await db.SaveChangesAsync();
        return sub.Id;
    }

    private async Task<int> GetTestUserIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == "ali@test.com");
        return user.Id;
    }

    [Fact]
    public async Task SendAsync_FansOutWebPush_OnHappyPath()
    {
        var userId = await GetTestUserIdAsync();
        var endpoint = $"https://web.push.apple.com/{Guid.NewGuid():N}";
        var subId = await SeedSubscriptionAsync(userId, endpoint);

        _factory.TestPushService.Calls.Clear();
        _factory.TestPushService.OnSendAsync = null;

        using (var scope = _factory.Services.CreateScope())
        {
            var notif = scope.ServiceProvider.GetRequiredService<NotificationService>();
            await notif.SendAsync(userId, "Test message body", 1, "Test");
        }

        _factory.TestPushService.Calls.Should().HaveCount(1);
        _factory.TestPushService.Calls[0].Sub.Endpoint.Should().Be(endpoint);
        _factory.TestPushService.Calls[0].Title.Should().Be("FPF Quotations");
        _factory.TestPushService.Calls[0].Body.Should().Be("Test message body");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sub = await db.PushSubscriptions.FindAsync(subId);
            sub!.LastUsedAt.Should().NotBeNull();
            db.PushSubscriptions.Remove(sub);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SendAsync_RemovesSub_When410Gone()
    {
        var userId = await GetTestUserIdAsync();
        var endpoint = $"https://web.push.apple.com/{Guid.NewGuid():N}";
        var subId = await SeedSubscriptionAsync(userId, endpoint);

        _factory.TestPushService.Calls.Clear();
        _factory.TestPushService.OnSendAsync = _ =>
            throw new WebPushException("Subscription expired", null!,
                new HttpResponseMessage(HttpStatusCode.Gone));

        using (var scope = _factory.Services.CreateScope())
        {
            var notif = scope.ServiceProvider.GetRequiredService<NotificationService>();
            await notif.SendAsync(userId, "Test message", 1, "Test");
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.PushSubscriptions.FindAsync(subId)).Should().BeNull();
        }
    }

    [Fact]
    public async Task SendAsync_RemovesSub_When404NotFound()
    {
        var userId = await GetTestUserIdAsync();
        var endpoint = $"https://web.push.apple.com/{Guid.NewGuid():N}";
        var subId = await SeedSubscriptionAsync(userId, endpoint);

        _factory.TestPushService.Calls.Clear();
        _factory.TestPushService.OnSendAsync = _ =>
            throw new WebPushException("Not found", null!,
                new HttpResponseMessage(HttpStatusCode.NotFound));

        using (var scope = _factory.Services.CreateScope())
        {
            var notif = scope.ServiceProvider.GetRequiredService<NotificationService>();
            await notif.SendAsync(userId, "Test message", 1, "Test");
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.PushSubscriptions.FindAsync(subId)).Should().BeNull();
        }
    }

    [Fact]
    public async Task SendAsync_SwallowsGenericException_AndPreservesSub()
    {
        var userId = await GetTestUserIdAsync();
        var endpoint = $"https://web.push.apple.com/{Guid.NewGuid():N}";
        var subId = await SeedSubscriptionAsync(userId, endpoint);

        _factory.TestPushService.Calls.Clear();
        _factory.TestPushService.OnSendAsync = _ =>
            throw new TaskCanceledException("Network timeout");

        using (var scope = _factory.Services.CreateScope())
        {
            var notif = scope.ServiceProvider.GetRequiredService<NotificationService>();
            await notif.SendAsync(userId, "Test message", 1, "Test");
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sub = await db.PushSubscriptions.FindAsync(subId);
            sub.Should().NotBeNull();
            db.PushSubscriptions.Remove(sub!);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SendAsync_NoOpsWhenUserHasNoSub()
    {
        var userId = await GetTestUserIdAsync();
        _factory.TestPushService.Calls.Clear();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var leftover = await db.PushSubscriptions.Where(s => s.UserId == userId).ToListAsync();
            db.PushSubscriptions.RemoveRange(leftover);
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var notif = scope.ServiceProvider.GetRequiredService<NotificationService>();
            await notif.SendAsync(userId, "Test message", 1, "Test");
        }

        _factory.TestPushService.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SendToUsersAsync_FansOutToAllUserSubs()
    {
        var aliId = await GetTestUserIdAsync();
        int bobId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            bobId = (await db.Users.FirstAsync(u => u.Email == "bob@test.com")).Id;
        }

        // Clean up any leftover subs for ali/bob from prior tests
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var leftover = await db.PushSubscriptions
                .Where(s => s.UserId == aliId || s.UserId == bobId)
                .ToListAsync();
            db.PushSubscriptions.RemoveRange(leftover);
            await db.SaveChangesAsync();
        }

        var aliEndpoint = $"https://web.push.apple.com/ali-{Guid.NewGuid():N}";
        var bobEndpoint = $"https://web.push.apple.com/bob-{Guid.NewGuid():N}";
        var aliSubId = await SeedSubscriptionAsync(aliId, aliEndpoint);
        var bobSubId = await SeedSubscriptionAsync(bobId, bobEndpoint);

        _factory.TestPushService.Calls.Clear();
        _factory.TestPushService.OnSendAsync = null;

        using (var scope = _factory.Services.CreateScope())
        {
            var notif = scope.ServiceProvider.GetRequiredService<NotificationService>();
            await notif.SendToUsersAsync(new[] { aliId, bobId }, "Multi-user msg", 1, "Test");
        }

        _factory.TestPushService.Calls.Should().HaveCount(2);
        _factory.TestPushService.Calls.Select(c => c.Sub.Endpoint).Should()
            .BeEquivalentTo(new[] { aliEndpoint, bobEndpoint });

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.PushSubscriptions.Remove((await db.PushSubscriptions.FindAsync(aliSubId))!);
            db.PushSubscriptions.Remove((await db.PushSubscriptions.FindAsync(bobSubId))!);
            await db.SaveChangesAsync();
        }
    }
}
