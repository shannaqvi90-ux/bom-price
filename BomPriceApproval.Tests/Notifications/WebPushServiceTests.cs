using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using SubEntity = BomPriceApproval.API.Domain.Entities.PushSubscription;

namespace BomPriceApproval.Tests.Notifications;

public class WebPushServiceTests
{
    private const string TestPublicKey = "BNxPP9PhIxBjaHv4WdpFrApT7ot3YTeNW0z_uG44VZh3MqcJVDmZ-2I2qRtm6gwKfL0wvtmgrrHpLgSsOQE0aHs";
    private const string TestPrivateKey = "9Q9vdo8gx6JpVvEjtRHsZS0vJjtv1IabO_cERWDFVvw";
    private const string TestSubject = "mailto:test@example.com";

    private static IConfiguration Cfg(string? pub, string? priv, string? subj) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "WebPush:VapidPublicKey", pub },
            { "WebPush:VapidPrivateKey", priv },
            { "WebPush:Subject", subj },
        }).Build();

    [Fact]
    public void IsConfigured_FalseWhenAllKeysMissing()
    {
        var svc = new WebPushService(Cfg("", "", ""), NullLogger<WebPushService>.Instance);
        Assert.False(svc.IsConfigured);
    }

    [Fact]
    public void IsConfigured_FalseWhenPublicKeyMissing()
    {
        var svc = new WebPushService(Cfg("", TestPrivateKey, TestSubject), NullLogger<WebPushService>.Instance);
        Assert.False(svc.IsConfigured);
    }

    [Fact]
    public void IsConfigured_FalseWhenPrivateKeyMissing()
    {
        var svc = new WebPushService(Cfg(TestPublicKey, "", TestSubject), NullLogger<WebPushService>.Instance);
        Assert.False(svc.IsConfigured);
    }

    [Fact]
    public void IsConfigured_FalseWhenSubjectMissing()
    {
        var svc = new WebPushService(Cfg(TestPublicKey, TestPrivateKey, ""), NullLogger<WebPushService>.Instance);
        Assert.False(svc.IsConfigured);
    }

    [Fact]
    public void IsConfigured_TrueWithAllKeysPresent()
    {
        var svc = new WebPushService(Cfg(TestPublicKey, TestPrivateKey, TestSubject), NullLogger<WebPushService>.Instance);
        Assert.True(svc.IsConfigured);
    }

    [Fact]
    public async Task SendAsync_NoOpWhenNotConfigured()
    {
        var svc = new WebPushService(Cfg(null, null, null), NullLogger<WebPushService>.Instance);
        var sub = new SubEntity { Endpoint = "https://x", P256dh = "p", Auth = "a" };
        // Should not throw — VAPID disabled means silent no-op
        await svc.SendAsync(sub, "title", "body");
    }
}
