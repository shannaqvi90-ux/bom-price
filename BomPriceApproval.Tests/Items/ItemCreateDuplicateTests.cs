using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Items;

public class ItemCreateDuplicateTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private async Task<(HttpResponseMessage Response, ItemDto? Body)> PostItemAsync(
        string token, string code, int type = 1)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await client.PostAsJsonAsync("/api/items", new
        {
            Code = code,
            Description = $"Item {code}",
            Type = type,
            LastPurchasePrice = (decimal?)null
        });
        ItemDto? body = null;
        if (resp.IsSuccessStatusCode)
            body = await resp.Content.ReadFromJsonAsync<ItemDto>();
        return (resp, body);
    }

    [Fact]
    public async Task CreateItem_DuplicateCodeSameBranch_Returns400()
    {
        var code = $"DUPPOST-{Guid.NewGuid():N}"[..16];
        var aliToken = await LoginAsync("ali@test.com", "Test@1234");

        var (first, _) = await PostItemAsync(aliToken, code);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var (second, _) = await PostItemAsync(aliToken, code);
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await second.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        problem!.Errors.Should().ContainKey("Code");
    }

    [Fact]
    public async Task CreateItem_DuplicateCodeDifferentBranch_Returns201()
    {
        var code = $"XBRPOST-{Guid.NewGuid():N}"[..16];

        // Create branch-2 SalesPerson via admin
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var spEmail = $"sp2-{Guid.NewGuid():N}"[..12] + "@test.com";
        var createUser = await adminClient.PostAsJsonAsync("/api/users", new
        {
            Name = "Branch2 SP for dup test",
            Email = spEmail,
            Password = "Test@1234",
            Role = 1, // SalesPerson
            BranchId = 2
        });
        createUser.StatusCode.Should().Be(HttpStatusCode.Created);

        var sp2Token = await LoginAsync(spEmail, "Test@1234");

        // Same code in branch 1 (ali) and branch 2 (sp2) — both should succeed
        var (resp1, _) = await PostItemAsync(await LoginAsync("ali@test.com", "Test@1234"), code);
        resp1.StatusCode.Should().Be(HttpStatusCode.Created);

        var (resp2, _) = await PostItemAsync(sp2Token, code);
        resp2.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateItem_ConcurrentDuplicateInsert_OneSucceedsOneFails()
    {
        var code = $"CONCUR-{Guid.NewGuid():N}"[..16];
        var aliToken = await LoginAsync("ali@test.com", "Test@1234");

        // Fire two requests simultaneously with the same code
        var task1 = PostItemAsync(aliToken, code);
        var task2 = PostItemAsync(aliToken, code);
        var results = await Task.WhenAll(task1, task2);

        var statuses = results.Select(r => r.Response.StatusCode).ToList();

        // Exactly one must succeed (201) and one must fail (400)
        statuses.Should().Contain(HttpStatusCode.Created);
        statuses.Should().Contain(HttpStatusCode.BadRequest);

        // The failing response must include a Code field error
        var failedResp = results.First(r => r.Response.StatusCode == HttpStatusCode.BadRequest).Response;
        var problem = await failedResp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        problem!.Errors.Should().ContainKey("Code");
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record ItemDto(int Id, string Code, string Description, string Type, int BranchId, bool IsActive, decimal? LastPurchasePrice);
    private record ValidationProblemResponse(string Detail, Dictionary<string, string[]> Errors);
}
