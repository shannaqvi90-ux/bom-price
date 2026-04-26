using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Branches;

public class BranchesAdminCrudTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
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
        var resp = await _client.PostAsJsonAsync("/api/branches", new { Name = $"TestBranch-{Guid.NewGuid():N}".Substring(0, 25) });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_AsNonAdmin_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("ali@test.com", "Test@1234"));
        var resp = await _client.PostAsJsonAsync("/api/branches", new { Name = "Try" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_AsAdmin_TogglesIsActive()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
        var create = await _client.PostAsJsonAsync("/api/branches", new { Name = $"Toggle-{Guid.NewGuid():N}".Substring(0, 20) });
        create.EnsureSuccessStatusCode();
        var created = (await create.Content.ReadFromJsonAsync<BranchAdminResponse>())!;

        var upd = await _client.PutAsJsonAsync($"/api/branches/{created.Id}", new { Name = created.Name, IsActive = false });
        upd.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResp = await _client.GetAsync("/api/branches");
        var list = (await listResp.Content.ReadFromJsonAsync<List<BranchAdminResponse>>())!;
        list.First(b => b.Id == created.Id).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_BlocksWhenInUse()
    {
        // Branch 1 has users + reqs in seed → cannot soft-delete
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync("admin@test.com", "Admin@1234"));
        var resp = await _client.DeleteAsync("/api/branches/1");
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private record LoginResponse(string AccessToken);
    private record BranchAdminResponse(int Id, string Name, bool IsActive);
}
