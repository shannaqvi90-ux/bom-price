using System.Security.Claims;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Admin;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminUsersController(AppDbContext db, AdminAuditLogger audit) : ControllerBase
{
    [HttpPost("users/{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordRequest? body)
    {
        if (body is null)
            return Validation.Detail("Request body is required").Return();
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
            return Validation.Detail("Reason is required (min 5 chars)")
                .Field("Reason", "Reason is required (min 5 chars).").Return();

        var user = await db.Users.Include(u => u.RefreshTokens).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        var before = new
        {
            user.Id,
            user.MustChangePassword,
            user.FailedLoginAttempts,
            user.LockedUntil,
            ActiveTokenCount = user.RefreshTokens.Count(t => !t.IsRevoked)
        };

        var temp = PasswordGenerator.Generate();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(temp);
        user.MustChangePassword = true;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        foreach (var tok in user.RefreshTokens.Where(t => !t.IsRevoked))
            tok.IsRevoked = true;

        var after = new
        {
            user.Id,
            user.MustChangePassword,
            user.FailedLoginAttempts,
            user.LockedUntil,
            ActiveTokenCount = 0
        };

        audit.Log(CurrentUserId, AdminActionType.ResetPassword, "User", id, body.Reason, before, after);
        await db.SaveChangesAsync();

        return Ok(new ResetPasswordResponse(temp));
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
}
