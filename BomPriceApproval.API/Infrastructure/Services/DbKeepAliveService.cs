using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Infrastructure.Services;

/// <summary>
/// Pings the database every ~4 minutes to prevent the Neon serverless compute
/// from scaling to zero, which causes the next user request to time out
/// (manifests as "Signing in..." hang or generic "Network Error" in browsers,
/// because /api/auth/login waits on a fresh DB connection that never opens).
///
/// Neon idles compute at ~5 minutes of inactivity by default. Pinging at 4
/// minutes keeps the compute warm without triggering wake-on-demand cycles.
///
/// First ping fires after a 30-second warm-up so the API has time to bind
/// the port + service the first inbound request.
///
/// On exception we log + continue — keepalive failure must never crash the
/// whole API. The next iteration retries.
/// </summary>
public class DbKeepAliveService(IServiceScopeFactory scopeFactory, ILogger<DbKeepAliveService> logger)
    : BackgroundService
{
    private static readonly TimeSpan WarmupDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PingInterval = TimeSpan.FromMinutes(4);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(WarmupDelay, stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                // SELECT 1 is the cheapest possible roundtrip — no table scan, no transaction.
                _ = await db.Database.ExecuteSqlRawAsync("SELECT 1", stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "DbKeepAlive ping failed (will retry next interval)");
            }

            try { await Task.Delay(PingInterval, stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }
}
