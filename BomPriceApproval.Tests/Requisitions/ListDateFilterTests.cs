using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

public class ListDateFilterTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
        return body.AccessToken;
    }

    private record CountedListItem(int Id, string RefNo, string Status);

    [Fact]
    public async Task List_WithFromFilter_ReturnsOnlyItemsUpdatedOnOrAfter()
    {
        var token = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var future = DateTime.UtcNow.AddDays(1).Date;
        var fromParam = future.ToString("yyyy-MM-dd");

        var resp = await _client.GetAsync($"/api/requisitions?from={fromParam}");
        resp.EnsureSuccessStatusCode();
        var items = (await resp.Content.ReadFromJsonAsync<List<CountedListItem>>())!;

        items.Should().BeEmpty("no seeded requisitions exist with UpdatedAt in the future");
    }

    [Fact]
    public async Task List_WithToFilter_ReturnsOnlyItemsUpdatedBefore()
    {
        var token = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var farPast = new DateTime(2000, 1, 1).ToString("yyyy-MM-dd");

        var resp = await _client.GetAsync($"/api/requisitions?to={farPast}");
        resp.EnsureSuccessStatusCode();
        var items = (await resp.Content.ReadFromJsonAsync<List<CountedListItem>>())!;

        items.Should().BeEmpty("nothing exists with UpdatedAt before 2000-01-01");
    }

    [Fact]
    public async Task List_WithoutDateFilters_BackwardsCompatible()
    {
        var token = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/requisitions");
        resp.EnsureSuccessStatusCode();
        var items = (await resp.Content.ReadFromJsonAsync<List<CountedListItem>>())!;

        items.Should().NotBeNull();
    }

    private record LoginResponse(string AccessToken, string RefreshToken);
}
