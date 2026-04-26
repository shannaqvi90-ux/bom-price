using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.Tests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Admin;

public class AdminResetPasswordTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> TokenAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    /// <summary>Seeds a throwaway user with specified fields and returns its Id.</summary>
    private async Task<int> SeedUserAsync(
        string? email = null,
        int failedLoginAttempts = 0,
        DateTime? lockedUntil = null,
        bool mustChangePassword = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User
        {
            Name = "Throwaway User",
            Email = email ?? $"throwaway-{Guid.NewGuid():N}@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!"),
            Role = UserRole.SalesPerson,
            BranchId = 1,
            IsActive = true,
            FailedLoginAttempts = failedLoginAttempts,
            LockedUntil = lockedUntil,
            MustChangePassword = mustChangePassword,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task CleanupUserAsync(int userId, bool includeAudit = false, string? uniqueReason = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (includeAudit && uniqueReason is not null)
        {
            var auditRows = await db.AdminAuditLogs
                .Where(a => a.EntityType == "User" && a.EntityId == userId && a.Reason == uniqueReason)
                .ToListAsync();
            if (auditRows.Count > 0) db.AdminAuditLogs.RemoveRange(auditRows);
        }
        else if (includeAudit)
        {
            var auditRows = await db.AdminAuditLogs
                .Where(a => a.EntityType == "User" && a.EntityId == userId)
                .ToListAsync();
            if (auditRows.Count > 0) db.AdminAuditLogs.RemoveRange(auditRows);
        }

        var tokens = await db.RefreshTokens.Where(t => t.UserId == userId).ToListAsync();
        if (tokens.Count > 0) db.RefreshTokens.RemoveRange(tokens);

        var user = await db.Users.FindAsync(userId);
        if (user is not null) db.Users.Remove(user);

        await db.SaveChangesAsync();
    }

    // ──────────────────────────────────────────────────────────────
    // Test 1: Happy path — returns temp password, sets flags
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_HappyPath_ReturnsTempPasswordAndFlagsUser()
    {
        var userId = await SeedUserAsync(
            failedLoginAttempts: 3,
            lockedUntil: DateTime.UtcNow.AddHours(1),
            mustChangePassword: false);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/users/{userId}/reset-password",
                new { Reason = "user locked out" });

            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var tempPassword = doc.RootElement.GetProperty("tempPassword").GetString();
            tempPassword.Should().NotBeNullOrEmpty();
            tempPassword!.Length.Should().Be(12, "PasswordGenerator.Generate() produces 12-char passwords");

            // Behavioral: verify DB state updated
            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var refreshed = await verifyDb.Users.FindAsync(userId);

            refreshed!.MustChangePassword.Should().BeTrue();
            refreshed.FailedLoginAttempts.Should().Be(0);
            refreshed.LockedUntil.Should().BeNull();
            BCrypt.Net.BCrypt.Verify(tempPassword, refreshed.PasswordHash).Should().BeTrue(
                "the stored hash must match the returned temp password");
        }
        finally
        {
            await CleanupUserAsync(userId, includeAudit: true);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 2: All refresh tokens revoked
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_RevokesAllRefreshTokens()
    {
        var userId = await SeedUserAsync();

        // Seed 2 active refresh tokens for the user
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RefreshTokens.AddRange(
                new RefreshToken
                {
                    Token = $"tok-{Guid.NewGuid():N}",
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    IsRevoked = false,
                    UserId = userId,
                },
                new RefreshToken
                {
                    Token = $"tok-{Guid.NewGuid():N}",
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    IsRevoked = false,
                    UserId = userId,
                });
            await db.SaveChangesAsync();
        }

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/users/{userId}/reset-password",
                new { Reason = "revoke tokens test" });

            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokens = await verifyDb.RefreshTokens.Where(t => t.UserId == userId).ToListAsync();
            tokens.Should().NotBeEmpty();
            tokens.All(t => t.IsRevoked).Should().BeTrue("all refresh tokens must be revoked after reset-password");
        }
        finally
        {
            await CleanupUserAsync(userId, includeAudit: true);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 3: Audit log does NOT contain the temp password
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_AuditLogDoesNotContainTempPassword()
    {
        var userId = await SeedUserAsync();
        var uniqueReason = $"audit-no-pw-{Guid.NewGuid():N}";

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/users/{userId}/reset-password",
                new { Reason = uniqueReason });

            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var tempPassword = doc.RootElement.GetProperty("tempPassword").GetString()!;

            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var auditRow = await verifyDb.AdminAuditLogs.FirstOrDefaultAsync(a =>
                a.EntityType == "User" &&
                a.EntityId == userId &&
                a.Reason == uniqueReason);

            auditRow.Should().NotBeNull("audit row must be written for reset-password");
            auditRow!.ActionType.Should().Be(AdminActionType.ResetPassword);

            auditRow.BeforeJson.Should().NotContain(tempPassword,
                "temp password must NEVER appear in BeforeJson (critical security requirement)");
            auditRow.AfterJson.Should().NotBeNull();
            auditRow.AfterJson!.Should().NotContain(tempPassword,
                "temp password must NEVER appear in AfterJson (critical security requirement)");
        }
        finally
        {
            await CleanupUserAsync(userId, includeAudit: true, uniqueReason: uniqueReason);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 4: Non-admin returns 403
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_AsNonAdmin_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("ali@test.com", "Test@1234"));

        var resp = await _client.PostAsJsonAsync(
            "/api/admin/users/1/reset-password",
            new { Reason = "unauthorized reset attempt" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ──────────────────────────────────────────────────────────────
    // Test 5: Unknown user ID returns 404
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_UnknownUserId_Returns404()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

        var resp = await _client.PostAsJsonAsync(
            "/api/admin/users/9999999/reset-password",
            new { Reason = "looking for nonexistent user" });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ──────────────────────────────────────────────────────────────
    // Test 6: Missing reason returns 400
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_MissingReason_Returns400()
    {
        var userId = await SeedUserAsync();

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/users/{userId}/reset-password",
                new { Reason = "" });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, "empty reason should be rejected");
        }
        finally
        {
            await CleanupUserAsync(userId);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 7: Null body returns 400
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_NullBody_Returns400()
    {
        var userId = await SeedUserAsync();

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            // POST with no body content
            var request = new HttpRequestMessage(HttpMethod.Post,
                $"/api/admin/users/{userId}/reset-password");
            var resp = await _client.SendAsync(request);

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "null body should be rejected before any DB lookup");
        }
        finally
        {
            await CleanupUserAsync(userId);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 8: Temp password actually works for login (end-to-end)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_TempPasswordPassesValidator()
    {
        var email = $"throwaway-{Guid.NewGuid():N}@test.com";
        var userId = await SeedUserAsync(email: email);

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resetResp = await _client.PostAsJsonAsync(
                $"/api/admin/users/{userId}/reset-password",
                new { Reason = "end-to-end login test" });

            resetResp.StatusCode.Should().Be(HttpStatusCode.OK);

            using var resetDoc = JsonDocument.Parse(await resetResp.Content.ReadAsStringAsync());
            var tempPassword = resetDoc.RootElement.GetProperty("tempPassword").GetString()!;

            // Try to login with the temp password
            var loginResp = await _client.PostAsJsonAsync(
                "/api/auth/login",
                new { Email = email, Password = tempPassword });

            loginResp.StatusCode.Should().Be(HttpStatusCode.OK,
                "the generated temp password must be accepted by the login endpoint");

            using var loginDoc = JsonDocument.Parse(await loginResp.Content.ReadAsStringAsync());
            var mustChange = loginDoc.RootElement.GetProperty("mustChangePassword").GetBoolean();
            mustChange.Should().BeTrue("login response must indicate mustChangePassword=true after reset");
        }
        finally
        {
            await CleanupUserAsync(userId, includeAudit: true);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Test 9: Audit captures user state change (typed JSON assertions)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_AuditCapturesUserStateChange()
    {
        var userId = await SeedUserAsync(
            failedLoginAttempts: 2,
            lockedUntil: DateTime.UtcNow.AddHours(2),
            mustChangePassword: false);

        var uniqueReason = $"audit-state-change-{Guid.NewGuid():N}";

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await TokenAsync("admin@test.com", "Admin@1234"));

            var resp = await _client.PostAsJsonAsync(
                $"/api/admin/users/{userId}/reset-password",
                new { Reason = uniqueReason });

            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            using var verifyScope = factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var auditRow = await verifyDb.AdminAuditLogs.FirstOrDefaultAsync(a =>
                a.EntityType == "User" &&
                a.EntityId == userId &&
                a.Reason == uniqueReason);

            auditRow.Should().NotBeNull("audit row must exist");

            // Parse BeforeJson
            using var beforeDoc = JsonDocument.Parse(auditRow!.BeforeJson);
            var beforeMustChange = beforeDoc.RootElement.GetProperty("MustChangePassword").GetBoolean();
            beforeMustChange.Should().BeFalse("BeforeJson must capture original MustChangePassword=false");

            // Parse AfterJson
            auditRow.AfterJson.Should().NotBeNull();
            using var afterDoc = JsonDocument.Parse(auditRow.AfterJson!);
            var afterMustChange = afterDoc.RootElement.GetProperty("MustChangePassword").GetBoolean();
            afterMustChange.Should().BeTrue("AfterJson must show MustChangePassword=true");

            var afterFailedAttempts = afterDoc.RootElement.GetProperty("FailedLoginAttempts").GetInt32();
            afterFailedAttempts.Should().Be(0, "AfterJson must show FailedLoginAttempts=0");

            // LockedUntil=null is omitted by WhenWritingNull serializer option — absence means null
            var hasLockedUntil = afterDoc.RootElement.TryGetProperty("LockedUntil", out var afterLockedUntil);
            if (hasLockedUntil)
                afterLockedUntil.ValueKind.Should().Be(JsonValueKind.Null,
                    "if LockedUntil appears in AfterJson it must be null after reset");
        }
        finally
        {
            await CleanupUserAsync(userId, includeAudit: true, uniqueReason: uniqueReason);
        }
    }
}
