using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

public class ValidationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private async Task<int> CreateActiveFinishedGoodAsync(string adminToken)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var code = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var resp = await _client.PostAsJsonAsync("/api/items",
            new { Code = code, Description = "Test FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var item = await resp.Content.ReadFromJsonAsync<ItemDto>();
        return item!.Id;
    }

    private async Task<int> GetCustomerIdAsync()
    {
        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        return customers!.First().Id;
    }

    [Fact]
    public async Task Create_DuplicateItemIds_Returns400()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemId = await CreateActiveFinishedGoodAsync(sp);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[]
            {
                new { ItemId = itemId, ExpectedQty = 1m },
                new { ItemId = itemId, ExpectedQty = 2m },
            },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.Should().Contain("Duplicate");
    }

    [Fact]
    public async Task Create_ZeroQty_Returns400()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemId = await CreateActiveFinishedGoodAsync(sp);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemId, ExpectedQty = 0m } },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.Should().Contain("ExpectedQty");
    }

    [Fact]
    public async Task Create_NegativeQty_Returns400()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemId = await CreateActiveFinishedGoodAsync(sp);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemId, ExpectedQty = -1m } },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_NonExistentItem_Returns400()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = 999999, ExpectedQty = 1m } },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.ToLower().Should().Contain("unknown");
    }

    [Fact]
    public async Task Create_InactiveItem_Returns400()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemId = await CreateActiveFinishedGoodAsync(sp);

        // Deactivate via admin
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        var patch = await _client.PatchAsJsonAsync($"/api/items/{itemId}/status",
            new { IsActive = false });
        patch.IsSuccessStatusCode.Should().BeTrue();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemId, ExpectedQty = 1m } },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddItem_DuplicateItem_Returns400()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemA = await CreateActiveFinishedGoodAsync(sp);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var createResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemA, ExpectedQty = 1m } },
            CurrencyCode = "AED"
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<CreatedRequisition>();

        var addResp = await _client.PostAsJsonAsync(
            $"/api/requisitions/{created!.Id}/items",
            new { ItemId = itemA, ExpectedQty = 2m });

        addResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddItem_ZeroQty_Returns400()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemA = await CreateActiveFinishedGoodAsync(sp);
        var itemB = await CreateActiveFinishedGoodAsync(sp);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var createResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemA, ExpectedQty = 1m } },
            CurrencyCode = "AED"
        });
        var created = await createResp.Content.ReadFromJsonAsync<CreatedRequisition>();

        var addResp = await _client.PostAsJsonAsync(
            $"/api/requisitions/{created!.Id}/items",
            new { ItemId = itemB, ExpectedQty = 0m });

        addResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

public record LoginResponse(string AccessToken, string RefreshToken);
public record ItemDto(int Id, string Code, string Description, string Type);
public record CustomerDto(int Id, string Name);
public record ErrorResponse(string Message);
public record CreatedRequisition(int Id, string RefNo);
