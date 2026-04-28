using System.Net;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Features.Notifications;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebPushException = WebPush.WebPushException;

namespace BomPriceApproval.API.Infrastructure.Services;

public class NotificationService(
    AppDbContext db,
    IHubContext<NotificationHub> hub,
    WebPushService webPush,
    ILogger<NotificationService> logger)
{
    private const string PushTitle = "FPF Quotations";

    public virtual async Task SendAsync(int userId, string message, int referenceId, string referenceType)
    {
        var notification = new Notification
        {
            UserId = userId, Message = message,
            ReferenceId = referenceId, ReferenceType = referenceType
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        await hub.Clients.Group($"user_{userId}").SendAsync("ReceiveNotification", new
        {
            notification.Id, notification.Message,
            notification.ReferenceId, notification.ReferenceType,
            notification.CreatedAt, notification.IsRead
        });

        await FanOutWebPushAsync(new[] { userId }, message);
    }

    /// <summary>
    /// Batched notification fan-out. Inserts one Notification row per distinct
    /// recipient in a single SaveChangesAsync call (closes the N+1 fan-out
    /// problem in admin override + workflow notifications), then pushes
    /// SignalR per recipient. No-op when userIds is empty after deduplication.
    /// </summary>
    public virtual async Task SendToUsersAsync(
        IEnumerable<int> userIds,
        string message,
        int referenceId,
        string referenceType,
        CancellationToken ct = default)
    {
        var distinctIds = userIds.Distinct().ToList();
        if (distinctIds.Count == 0) return;

        var notifications = distinctIds.Select(uid => new Notification
        {
            UserId = uid, Message = message,
            ReferenceId = referenceId, ReferenceType = referenceType
        }).ToList();

        db.Notifications.AddRange(notifications);
        await db.SaveChangesAsync(ct);

        // Push per-user. SignalR Group send is per-connection-group so each
        // recipient has their own group; no way to fan out in a single call.
        foreach (var notification in notifications)
        {
            await hub.Clients.Group($"user_{notification.UserId}").SendAsync(
                "ReceiveNotification",
                new
                {
                    notification.Id, notification.Message,
                    notification.ReferenceId, notification.ReferenceType,
                    notification.CreatedAt, notification.IsRead
                },
                ct);
        }

        await FanOutWebPushAsync(distinctIds, message, ct);
    }

    /// <summary>
    /// Best-effort web push fan-out. Failure NEVER breaks the SignalR + DB
    /// notification flow — exceptions are logged and swallowed. 410 Gone /
    /// 404 NotFound responses (per RFC 8030) auto-delete the dead subscription.
    /// </summary>
    private async Task FanOutWebPushAsync(IEnumerable<int> userIds, string body, CancellationToken ct = default)
    {
        if (!webPush.IsConfigured) return;

        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;

        var subs = await db.PushSubscriptions
            .Where(s => ids.Contains(s.UserId))
            .ToListAsync(ct);
        if (subs.Count == 0) return;

        var dead = new List<BomPriceApproval.API.Domain.Entities.PushSubscription>();
        foreach (var sub in subs)
        {
            try
            {
                await webPush.SendAsync(sub, PushTitle, body, ct);
                sub.LastUsedAt = DateTime.UtcNow;
            }
            catch (WebPushException ex) when (
                ex.StatusCode == HttpStatusCode.Gone ||
                ex.StatusCode == HttpStatusCode.NotFound)
            {
                dead.Add(sub);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Web push failed for sub {SubId}", sub.Id);
            }
        }

        if (dead.Count > 0) db.PushSubscriptions.RemoveRange(dead);
        await db.SaveChangesAsync(ct);
    }
}
