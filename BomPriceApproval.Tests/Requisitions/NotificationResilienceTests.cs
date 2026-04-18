using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Requisitions;

public class NotificationResilienceTests(ThrowingNotificationFactory factory)
    : IClassFixture<ThrowingNotificationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private record LoginResp(string AccessToken);
    private record ItemMin(int Id);
    private record CustomerMin(int Id);
    private record CreatedReq(int Id, string RefNo);
    private record ReqDetail(int Id, string Status);

    private async Task<string> LoginAsync(string email, string password)
    {
        using var c = factory.CreateClient();
        var resp = await c.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResp>();
        return body!.AccessToken;
    }

    private void UseToken(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task Create_ReturnsCreated_EvenIfNotificationThrows()
    {
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        UseToken(spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        fgResp.EnsureSuccessStatusCode();
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemMin>();

        var customers = await _client.GetFromJsonAsync<List<CustomerMin>>("/api/customers");

        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fg!.Id, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });

        reqResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedReq>();

        var detail = await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{created!.Id}");
        detail!.Status.Should().Be("BomPending");
    }
}
