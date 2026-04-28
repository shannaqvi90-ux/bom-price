using System.Text.Json;
using WebPush;
using SubEntity = BomPriceApproval.API.Domain.Entities.PushSubscription;

namespace BomPriceApproval.API.Infrastructure.Services;

public class WebPushService
{
    private readonly WebPushClient _client;
    private readonly VapidDetails? _vapid;
    private readonly ILogger<WebPushService> _logger;

    public bool IsConfigured => _vapid is not null;

    public WebPushService(IConfiguration cfg, ILogger<WebPushService> logger)
    {
        _logger = logger;
        _client = new WebPushClient();
        var publicKey = cfg["WebPush:VapidPublicKey"];
        var privateKey = cfg["WebPush:VapidPrivateKey"];
        var subject = cfg["WebPush:Subject"];
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey) || string.IsNullOrWhiteSpace(subject))
        {
            _logger.LogWarning("WebPush VAPID config missing — push notifications disabled for this run.");
            _vapid = null;
        }
        else
        {
            _vapid = new VapidDetails(subject, publicKey, privateKey);
        }
    }

    public virtual async Task SendAsync(SubEntity sub, string title, string body, CancellationToken ct = default)
    {
        if (_vapid is null)
        {
            _logger.LogDebug("Skipping web push (VAPID not configured).");
            return;
        }
        var pushSub = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
        var payload = JsonSerializer.Serialize(new { title, body });
        await _client.SendNotificationAsync(pushSub, payload, _vapid);
    }
}
