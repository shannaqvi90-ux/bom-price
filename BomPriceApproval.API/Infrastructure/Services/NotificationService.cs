using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Features.Notifications;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;

namespace BomPriceApproval.API.Infrastructure.Services;

public class NotificationService(AppDbContext db, IHubContext<NotificationHub> hub)
{
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
    }
}
