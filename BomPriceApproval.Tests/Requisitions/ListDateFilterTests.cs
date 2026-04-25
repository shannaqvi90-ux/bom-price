using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

public class ListDateFilterTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
        return body.AccessToken;
    }

    // Seeds a minimal requisition as Sales (BomPending). Returns the new RefNo.
    private async Task<string> SeedAnyRequisitionAsSalesAsync()
    {
        var salesToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", salesToken);

        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items"))!;
        customers.Should().NotBeEmpty();
        items.Should().NotBeEmpty();

        var finishedGood = items.First(i => i.Type == "FinishedGood");

        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers[0].Id,
            CurrencyCode = "AED",
            Items = new[] { new { ItemId = finishedGood.Id, ExpectedQty = 5m } }
        });
        create.EnsureSuccessStatusCode();
        var created = (await create.Content.ReadFromJsonAsync<CreateResponse>())!;
        return created.RefNo;
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

    [Fact]
    public async Task List_WithFromFilter_PositiveMatch()
    {
        // Seed a fresh requisition (UpdatedAt = now)
        var refNo = await SeedAnyRequisitionAsSalesAsync();

        // Switch to MD (sees all branches)
        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var yesterday = DateTime.UtcNow.AddDays(-1).Date.ToString("yyyy-MM-dd");
        var tomorrow  = DateTime.UtcNow.AddDays(1).Date.ToString("yyyy-MM-dd");

        // from=yesterday → seeded requisition (UpdatedAt=now >= yesterday) should be present
        var respFrom = await _client.GetAsync($"/api/requisitions?from={yesterday}");
        respFrom.EnsureSuccessStatusCode();
        var itemsFrom = (await respFrom.Content.ReadFromJsonAsync<List<CountedListItem>>())!;
        itemsFrom.Select(i => i.RefNo).Should().Contain(refNo,
            "requisition updated today must appear when from=yesterday");

        // to=yesterday → seeded requisition (UpdatedAt=now > yesterday) should be excluded
        var respTo = await _client.GetAsync($"/api/requisitions?to={yesterday}");
        respTo.EnsureSuccessStatusCode();
        var itemsTo = (await respTo.Content.ReadFromJsonAsync<List<CountedListItem>>())!;
        itemsTo.Select(i => i.RefNo).Should().NotContain(refNo,
            "requisition updated today must not appear when to=yesterday");
    }

    [Fact]
    public async Task List_WithFromAndTo_NarrowsToWindow()
    {
        // Seed a fresh requisition (UpdatedAt = now)
        var refNo = await SeedAnyRequisitionAsSalesAsync();

        // Switch to MD (sees all branches)
        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var twoDaysAgo = DateTime.UtcNow.AddDays(-2).Date.ToString("yyyy-MM-dd");
        var yesterday  = DateTime.UtcNow.AddDays(-1).Date.ToString("yyyy-MM-dd");
        var tomorrow   = DateTime.UtcNow.AddDays(1).Date.ToString("yyyy-MM-dd");

        // Window that includes today (yesterday..tomorrow) → should be present
        var respInWindow = await _client.GetAsync($"/api/requisitions?from={yesterday}&to={tomorrow}");
        respInWindow.EnsureSuccessStatusCode();
        var itemsInWindow = (await respInWindow.Content.ReadFromJsonAsync<List<CountedListItem>>())!;
        itemsInWindow.Select(i => i.RefNo).Should().Contain(refNo,
            "window yesterday..tomorrow must include a requisition updated today");

        // Window that closes before today (twoDaysAgo..yesterday) → should be excluded
        var respBeforeWindow = await _client.GetAsync($"/api/requisitions?from={twoDaysAgo}&to={yesterday}");
        respBeforeWindow.EnsureSuccessStatusCode();
        var itemsBeforeWindow = (await respBeforeWindow.Content.ReadFromJsonAsync<List<CountedListItem>>())!;
        itemsBeforeWindow.Select(i => i.RefNo).Should().NotContain(refNo,
            "window twoDaysAgo..yesterday must exclude a requisition updated today");
    }

    private record LoginResponse(string AccessToken, string RefreshToken);
    private record CustomerShort(int Id, string Name);
    private record ItemShort(int Id, string Code, string Description, string Type);
    private record CreateResponse(int Id, string RefNo);
}
