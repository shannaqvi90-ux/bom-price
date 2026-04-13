using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Features.Notifications;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;

namespace BomPriceApproval.API.Infrastructure.Services;

public class NotificationService(AppDbContext db, IHubContext<NotificationHub> hub)
{
    public async Task SendAsync(int userId, string message, int referenceId, string referenceType)
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
}
