using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

public class RequisitionsListBranchScopingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    [Fact]
    public async Task Sara_AssignedToBothBranches_SeesBothBranchesReqs()
    {
        var sara = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sara.AccessToken);

        var list = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions"))!;
        list.Select(r => r.BranchId).Distinct().Should().Contain(new[] { 1, 2 },
            "Sara is assigned to both branches via UserBranches");
    }

    [Fact]
    public async Task Accountant_AssignedToBranch1Only_DoesNotSeeBranch2Reqs()
    {
        // Create a branch-1-only Accountant via admin
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var email = $"acct1only-{Guid.NewGuid():N}"[..22] + "@test.com";
        var createUser = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "Branch1 Only Accountant",
            Email = email,
            Password = "Test@1234",
            Role = 3,
            BranchId = (int?)null
        });
        createUser.EnsureSuccessStatusCode();
        var created = (await createUser.Content.ReadFromJsonAsync<UserShort>())!;

        // Replace UserBranches: assign only to branch 1
        var setBranches = await _client.PutAsJsonAsync($"/api/users/{created.Id}/branches", new { BranchIds = new[] { 1 } });
        setBranches.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // List reqs as branch-1-only Accountant
        var login = await LoginAsync(email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var list = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions"))!;
        list.Should().OnlyContain(r => r.BranchId == 1);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record UserShort(int Id, string Email, string Name, string Role);
    private record ReqShort(int Id, string RefNo, string Status, int BranchId);
}
