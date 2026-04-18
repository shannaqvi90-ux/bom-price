using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Users;

public class UserCreateTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAdminAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = "admin@test.com", Password = "Admin@1234" });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private async Task<HttpResponseMessage> CreateUserAsync(string token, string email)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/users")
        {
            Content = JsonContent.Create(new
            {
                Name = "Test User",
                Email = email,
                Password = "Test@1234",
                Role = "SalesPerson",
                BranchId = 1
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(req);
    }

    [Fact]
    public async Task CreateUser_DuplicateEmail_Returns400()
    {
        var token = await LoginAdminAsync();
        var email = $"dup-{Guid.NewGuid():N}@example.com";

        var first = await CreateUserAsync(token, email);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await CreateUserAsync(token, email);
        ((int)second.StatusCode).Should().BeOneOf(new[] { 400, 409 }, "duplicate email must be rejected");
    }

    [Fact]
    public async Task CreateUser_EmailCaseVariant_AlsoFails()
    {
        var token = await LoginAdminAsync();
        var uniquePart = Guid.NewGuid().ToString("N");
        var lower = $"user-{uniquePart}@example.com";
        var upper = $"USER-{uniquePart}@EXAMPLE.COM";

        var first = await CreateUserAsync(token, lower);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await CreateUserAsync(token, upper);
        ((int)second.StatusCode).Should().BeOneOf(new[] { 400, 409 },
            "a case variant of an existing email must be rejected");
    }

    [Fact]
    public async Task CreateUser_ConcurrentDuplicate_OneSucceedsOneFails()
    {
        var token = await LoginAdminAsync();
        var email = $"race-{Guid.NewGuid():N}@example.com";

        async Task<HttpResponseMessage> Post()
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/users")
            {
                Content = JsonContent.Create(new
                {
                    Name = "Race User",
                    Email = email,
                    Password = "Test@1234",
                    Role = "SalesPerson",
                    BranchId = 1
                })
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await _client.SendAsync(req);
        }

        var responses = await Task.WhenAll(Post(), Post());

        var successCount = responses.Count(r => (int)r.StatusCode >= 200 && (int)r.StatusCode < 300);
        var failCount = responses.Length - successCount;

        successCount.Should().Be(1, "exactly one concurrent create must succeed");
        failCount.Should().Be(1, "exactly one concurrent create must be rejected");
    }
}
