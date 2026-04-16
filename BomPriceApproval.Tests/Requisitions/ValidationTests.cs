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
}

public record LoginResponse(string AccessToken, string RefreshToken);
public record ItemDto(int Id, string Code, string Description, string Type);
public record CustomerDto(int Id, string Name);
public record ErrorResponse(string Message);
public record CreatedRequisition(int Id, string RefNo);
