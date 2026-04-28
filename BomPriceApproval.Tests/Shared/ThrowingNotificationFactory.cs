using BomPriceApproval.API.Features.Notifications;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace BomPriceApproval.Tests.Shared;

public class ThrowingNotificationService(
    AppDbContext db,
    IHubContext<NotificationHub> hub,
    WebPushService webPush,
    ILogger<NotificationService> logger)
    : NotificationService(db, hub, webPush, logger)
{
    public override Task SendAsync(int userId, string message, int referenceId, string referenceType)
        => throw new InvalidOperationException("Simulated notification failure");
}

public class ThrowingNotificationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<NotificationService>();
            services.AddScoped<NotificationService, ThrowingNotificationService>();
        });
    }
}
