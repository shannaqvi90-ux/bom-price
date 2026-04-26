using System.Net;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Auth;

public class LoginMustChangePasswordTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Login_NormalUser_ReturnsMustChangePasswordFalse()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { Email = "ali@test.com", Password = "Test@1234" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await login.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body!["mustChangePassword"].ToString().Should().Be("False");
    }

    [Fact]
    public async Task Login_FlaggedUser_ReturnsMustChangePasswordTrue()
    {
        // Create a throwaway user with the flag set
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = $"forced-{Guid.NewGuid():N}@test.com";
        var user = new User
        {
            Email = email,
            Name = "Forced User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Temp123!"),
            Role = UserRole.SalesPerson,
            BranchId = 1,
            IsActive = true,
            MustChangePassword = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = "Temp123!" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await login.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body!["mustChangePassword"].ToString().Should().Be("True");

        // Cleanup
        db.Users.Remove(user);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ChangePassword_ClearsMustChangePasswordFlag()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = $"forced-{Guid.NewGuid():N}@test.com";
        var user = new User
        {
            Email = email,
            Name = "Forced User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Temp123!"),
            Role = UserRole.SalesPerson,
            BranchId = 1,
            IsActive = true,
            MustChangePassword = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = "Temp123!" });
        var loginBody = await login.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var token = loginBody!["accessToken"].ToString();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var change = await client.PostAsJsonAsync("/api/auth/change-password", new { CurrentPassword = "Temp123!", NewPassword = "NewPass456!" });
        change.StatusCode.Should().Be(HttpStatusCode.OK);
        var changeBody = await change.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        changeBody!["mustChangePassword"].ToString().Should().Be("False");

        // Verify in DB — use a fresh scope to avoid stale tracked entity
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var refreshed = await verifyDb.Users.FindAsync(user.Id);
        refreshed!.MustChangePassword.Should().BeFalse();

        // Cleanup
        verifyDb.Users.Remove(refreshed);
        await verifyDb.SaveChangesAsync();
    }
}
