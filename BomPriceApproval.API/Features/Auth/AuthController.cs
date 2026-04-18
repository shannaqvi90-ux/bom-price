using System.IdentityModel.Tokens.Jwt;
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
public class AuthController(AppDbContext db, TokenService tokenService, IConfiguration config) : ControllerBase
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
            return Unauthorized(new { message = "Invalid credentials" });
        }

        if (user.LockedUntil is not null && user.LockedUntil > DateTime.UtcNow)
        {
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

        return Ok(new LoginResponse(accessToken, refreshTokenValue, user.Role.ToString(), user.Id, user.Name, user.BranchId));
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
        await db.SaveChangesAsync();

        return Ok(new LoginResponse(
            tokenService.GenerateAccessToken(token.User),
            newRefresh,
            token.User.Role.ToString(),
            token.User.Id,
            token.User.Name,
            token.User.BranchId));
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
        return NoContent();
    }
}
