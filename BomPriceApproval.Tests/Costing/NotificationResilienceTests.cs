using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Costing;

public class NotificationResilienceTests(ThrowingNotificationFactory factory)
    : IClassFixture<ThrowingNotificationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private record LoginResp(string AccessToken);
    private record ItemMin(int Id);
    private record CustomerMin(int Id);
    private record ProcessMin(int Id);
    private record CreatedReq(int Id, string RefNo);
    private record RiMin(int Id);
    private record ReqDetail(int Id, string Status, List<RiMin> Items);
    private record CostingBomLineMin(int BomLineId);
    private record CostingItemMin(int RequisitionItemId, List<CostingBomLineMin> BomLines);
    private record CostingReview(int RequisitionId, List<CostingItemMin> Items);

    private async Task<string> LoginAsync(string email, string password)
    {
        // Fresh client for login so the test's bearer token doesn't conflict.
        using var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResp>();
        return body!.AccessToken;
    }

    private void UseToken(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task SubmitItem_LastItem_ReturnsSuccess_EvenIfNotificationThrows()
    {
        // Arrange — walk the workflow to CostingInProgress on a single-item requisition.
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        UseToken(spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        fgResp.EnsureSuccessStatusCode();
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemMin>();
        var rmResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "RM", Type = "RawMaterial", LastPurchasePrice = 5m });
        rmResp.EnsureSuccessStatusCode();
        var rm = await rmResp.Content.ReadFromJsonAsync<ItemMin>();

        var customers = await _client.GetFromJsonAsync<List<CustomerMin>>("/api/customers");
        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fg!.Id, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });
        reqResp.EnsureSuccessStatusCode();
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedReq>();
        var reqId = created!.Id;

        var reqDetail = await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}");
        var riId = reqDetail!.Items[0].Id;

        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        UseToken(adminToken);
        var procCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await _client.PostAsJsonAsync("/api/processes", new { Name = procCode, DisplayOrder = 99 });
        procResp.EnsureSuccessStatusCode();
        var process = await procResp.Content.ReadFromJsonAsync<ProcessMin>();

        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        UseToken(bomToken);
        await _client.PostAsync($"/api/bom/{reqId}/items/{riId}/start", null);
        await _client.PutAsJsonAsync($"/api/bom/{reqId}/items/{riId}/lines", new
        {
            Lines = new[]
            {
                new { ProcessId = process!.Id, RawMaterialItemId = rm!.Id, QtyPerKg = 0.85m, WastagePct = 2.0m }
            }
        });
        await _client.PostAsync($"/api/bom/{reqId}/submit", null);

        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        UseToken(acctToken);
        await _client.PostAsync($"/api/costing/{reqId}/items/{riId}/start", null);
        var review = await _client.GetFromJsonAsync<CostingReview>($"/api/costing/{reqId}");
        var bomLineId = review!.Items[0].BomLines[0].BomLineId;

        // Act — submit the last (only) item's costing. With a throwing NotificationService,
        // this previously surfaced as a 500; after the fix, it must still return 204.
        var submitResp = await _client.PostAsJsonAsync($"/api/costing/{reqId}/items/{riId}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 1.00m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        // Assert — response is 2xx and the state change (→ MdReview) did commit.
        submitResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        UseToken(spToken);
        var req = await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}");
        req!.Status.Should().Be("MdReview");
    }
}
