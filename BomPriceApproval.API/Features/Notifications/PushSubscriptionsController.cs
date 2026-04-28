using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Notifications;

public record PushKeysDto([Required] string p256dh, [Required] string auth);

public record PushSubscribeRequest(
    [Required] string endpoint,
    [Required] PushKeysDto keys,
    string? userAgent
);

public record PushUnsubscribeRequest([Required] string endpoint);

[ApiController]
[Route("api/notifications/push-subscribe")]
[Authorize]
public class PushSubscriptionsController(AppDbContext db) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    public async Task<IActionResult> Subscribe([FromBody] PushSubscribeRequest req)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (string.IsNullOrWhiteSpace(req.endpoint) ||
            req.keys is null ||
            string.IsNullOrWhiteSpace(req.keys.p256dh) ||
            string.IsNullOrWhiteSpace(req.keys.auth))
        {
            ModelState.AddModelError("endpoint", "endpoint, keys.p256dh, and keys.auth are required");
            return ValidationProblem(ModelState);
        }

        var userId = CurrentUserId;
        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == req.endpoint);
        if (existing is null)
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                UserId = userId,
                Endpoint = req.endpoint,
                P256dh = req.keys.p256dh,
                Auth = req.keys.auth,
                UserAgent = req.userAgent,
                CreatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.UserId = userId;
            existing.P256dh = req.keys.p256dh;
            existing.Auth = req.keys.auth;
            existing.UserAgent = req.userAgent;
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Unsubscribe([FromBody] PushUnsubscribeRequest req)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var userId = CurrentUserId;
        var sub = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == req.endpoint && s.UserId == userId);
        if (sub is not null)
        {
            db.PushSubscriptions.Remove(sub);
            await db.SaveChangesAsync();
        }
        return NoContent();
    }
}
