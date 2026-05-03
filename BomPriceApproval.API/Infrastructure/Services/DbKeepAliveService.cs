using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Infrastructure.Services;

/// <summary>
/// Pings the database every ~2 minutes to prevent Neon serverless compute
/// from scaling to zero, which causes the next user request to time out
/// (manifests as "Signing in..." hang or generic "Network Error" in browsers,
/// because /api/auth/login waits on a fresh DB connection that never opens).
///
/// Neon idles compute at as little as ~3 minutes of inactivity in practice.
/// Pinging at 2 minutes gives a safety margin without flooding the DB.
///
/// First ping fires after a 5-second warm-up so the API binds its port + the
/// first user request doesn't race the keepalive on a fresh deploy.
///
/// Every successful ping logs at Information so we can verify in Fly logs
/// that the service is firing. On exception we log + continue — keepalive
/// failure must never crash the whole API.
/// </summary>
public class DbKeepAliveService(IServiceScopeFactory scopeFactory, ILogger<DbKeepAliveService> logger)
    : BackgroundService
{
    private static readonly TimeSpan WarmupDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PingInterval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "DbKeepAlive starting — warmup {WarmupSeconds}s, then SELECT 1 every {IntervalMinutes}m",
            WarmupDelay.TotalSeconds, PingInterval.TotalMinutes);

        try { await Task.Delay(WarmupDelay, stoppingToken); }
        catch (TaskCanceledException) { return; }

        var pingCount = 0L;
        while (!stoppingToken.IsCancellationRequested)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                _ = await db.Database.ExecuteSqlRawAsync("SELECT 1", stoppingToken);
                sw.Stop();
                pingCount++;
                logger.LogInformation(
                    "DbKeepAlive ping #{PingCount} ok in {ElapsedMs}ms",
                    pingCount, sw.ElapsedMilliseconds);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                sw.Stop();
                logger.LogWarning(ex,
                    "DbKeepAlive ping failed after {ElapsedMs}ms (will retry next interval)",
                    sw.ElapsedMilliseconds);
            }

            try { await Task.Delay(PingInterval, stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }
}
