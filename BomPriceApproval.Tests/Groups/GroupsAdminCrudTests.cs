using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Groups;

public class GroupsAdminCrudTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> TokenAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    [Fact]
    public async Task Create_AsAdmin_Returns201()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
        var resp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"GrpA-{Guid.NewGuid():N}".Substring(0, 18) });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_AsAccountant_Returns201()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("sara@test.com", "Test@1234"));
        var resp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"GrpB-{Guid.NewGuid():N}".Substring(0, 18) });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_AsSP_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("ali@test.com", "Test@1234"));
        var resp = await _client.PostAsJsonAsync("/api/groups", new { Name = "Try" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_AsBomCreator_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("bob@test.com", "Test@1234"));
        var resp = await _client.PostAsJsonAsync("/api/groups", new { Name = "Try" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_AsAdmin_TogglesIsActive()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
        var create = await _client.PostAsJsonAsync("/api/groups", new { Name = $"Toggle-{Guid.NewGuid():N}".Substring(0, 18) });
        create.EnsureSuccessStatusCode();
        var created = (await create.Content.ReadFromJsonAsync<GroupAdminResponse>())!;

        var upd = await _client.PutAsJsonAsync($"/api/groups/{created.Id}", new { Name = created.Name, IsActive = false });
        upd.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResp = await _client.GetAsync("/api/groups");
        var list = (await listResp.Content.ReadFromJsonAsync<List<GroupAdminResponse>>())!;
        list.First(g => g.Id == created.Id).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_BlocksWhenInUse()
    {
        // Create group via API
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
        var create = await _client.PostAsJsonAsync("/api/groups", new { Name = $"InUse-{Guid.NewGuid():N}".Substring(0, 18) });
        create.EnsureSuccessStatusCode();
        var grp = (await create.Content.ReadFromJsonAsync<GroupAdminResponse>())!;

        // Assign ali to the group directly via DB (PUT /api/users/{id}/group is Task 4)
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ali = await db.Users.FirstAsync(u => u.Email == "ali@test.com");
        ali.GroupId = grp.Id;
        await db.SaveChangesAsync();

        try
        {
            // Attempt delete → 409 Conflict
            var delResp = await _client.DeleteAsync($"/api/groups/{grp.Id}");
            delResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            // Cleanup: clear Ali's group so other tests aren't polluted
            ali.GroupId = null;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Delete_NoMembers_SoftDeletes()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
        var create = await _client.PostAsJsonAsync("/api/groups", new { Name = $"Del-{Guid.NewGuid():N}".Substring(0, 18) });
        create.EnsureSuccessStatusCode();
        var grp = (await create.Content.ReadFromJsonAsync<GroupAdminResponse>())!;

        var del = await _client.DeleteAsync($"/api/groups/{grp.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = (await _client.GetFromJsonAsync<List<GroupAdminResponse>>("/api/groups"))!;
        list.First(g => g.Id == grp.Id).IsActive.Should().BeFalse();
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record GroupAdminResponse(int Id, string Name, bool IsActive);
}
