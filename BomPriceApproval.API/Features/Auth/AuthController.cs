using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController(
    AppDbContext db,
    TokenService tokenService,
    IConfiguration config,
    ILogger<AuthController> logger) : ControllerBase
{
    // Computed once at class load; used to absorb timing when the email is not found,
    // preventing user enumeration via response-time difference (~3 ms vs ~93 ms).
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("never-matches-anything");

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.IsActive);

        if (user is null)
        {
            BCrypt.Net.BCrypt.Verify(req.Password, DummyHash); // constant-time; result discarded
            logger.LogInformation("[Audit] Login failed: unknown email {Email}",
                normalizedEmail);
            return Unauthorized(new { message = "Invalid credentials" });
        }

        if (user.LockedUntil is not null && user.LockedUntil > DateTime.UtcNow)
        {
            logger.LogWarning("[Audit] Login rejected: account locked {UserId} {Email} LockedUntil={LockedUntil}",
                user.Id, user.Email, user.LockedUntil);
            return Validation
                .Detail("Account temporarily locked due to too many failed login attempts. Try again later.")
                .Field("Email", "Account locked.")
                .Return();
        }

        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= 5)
                user.LockedUntil = DateTime.UtcNow.AddMinutes(15);
            await db.SaveChangesAsync();
            logger.LogWarning("[Audit] Login failed: wrong password {UserId} {Email} Attempts={Attempts} Locked={Locked}",
                user.Id, user.Email, user.FailedLoginAttempts, user.LockedUntil is not null);
            return Unauthorized(new { message = "Invalid credentials" });
        }

        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;

        var accessToken = tokenService.GenerateAccessToken(user);
        var refreshTokenValue = tokenService.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            Token = refreshTokenValue,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(int.Parse(config["Jwt:RefreshTokenExpiryDays"]!))
        };
        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync();

        logger.LogInformation("[Audit] Login success {UserId} {Email} Role={Role} BranchId={BranchId}",
            user.Id, user.Email, user.Role, user.BranchId);

        return Ok(new LoginResponse(accessToken, refreshTokenValue, user.Role.ToString(), user.Id, user.Name, user.BranchId, user.MustChangePassword));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest req)
    {
        var token = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == req.RefreshToken
                && !t.IsRevoked
                && t.ExpiresAt > DateTime.UtcNow
                && t.User.IsActive);

        if (token is null) return Unauthorized(new { message = "Invalid refresh token" });

        token.IsRevoked = true;
        var newRefresh = tokenService.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            Token = newRefresh,
            UserId = token.UserId,
            ExpiresAt = DateTime.UtcNow.AddDays(int.Parse(config["Jwt:RefreshTokenExpiryDays"]!))
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another concurrent refresh already consumed this token.
            // Treat as token-already-used: 401.
            logger.LogWarning("[Audit] Refresh race lost — token already consumed {UserId}",
                token.UserId);
            return Unauthorized(new { message = "Invalid refresh token" });
        }

        logger.LogInformation("[Audit] Token refreshed {UserId}", token.UserId);

        return Ok(new LoginResponse(
            tokenService.GenerateAccessToken(token.User),
            newRefresh,
            token.User.Role.ToString(),
            token.User.Id,
            token.User.Name,
            token.User.BranchId,
            token.User.MustChangePassword));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest req)
    {
        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var expClaim = User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
        if (jti is not null && long.TryParse(expClaim, out var exp))
        {
            db.RevokedJtis.Add(new RevokedJti
            {
                Jti = jti,
                ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime,
                RevokedAt = DateTime.UtcNow
            });
        }

        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == req.RefreshToken);
        if (token is not null) token.IsRevoked = true;

        await db.SaveChangesAsync();

        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        logger.LogInformation("[Audit] Logout UserId={UserId} Jti={Jti}",
            userIdClaim ?? "unknown", jti ?? "none");

        return NoContent();
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdStr, out var userId))
            return Unauthorized(new { message = "Invalid token" });

        var user = await db.Users.FindAsync(userId);
        if (user is null || !user.IsActive)
            return Unauthorized(new { message = "User not found" });

        if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
        {
            logger.LogWarning("[Audit] ChangePassword failed: wrong current password UserId={UserId}", userId);
            return Validation.Detail("Current password is incorrect.")
                .Field("CurrentPassword", "Incorrect current password.")
                .Return();
        }

        var pwdError = PasswordValidator.Validate(req.NewPassword);
        if (pwdError is not null)
            return Validation.Detail(pwdError).Field("NewPassword", pwdError).Return();

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        user.MustChangePassword = false;
        await db.SaveChangesAsync();

        logger.LogInformation("[Audit] ChangePassword success UserId={UserId}", userId);

        return Ok(new { mustChangePassword = false });
    }
}
