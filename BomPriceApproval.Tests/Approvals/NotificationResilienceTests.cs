using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Approvals;

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
        using var c = factory.CreateClient();
        var resp = await c.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResp>();
        return body!.AccessToken;
    }

    private void UseToken(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<(int reqId, int riId)> WalkToMdReviewAsync()
    {
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");

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

        UseToken(adminToken);
        var procCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await _client.PostAsJsonAsync("/api/processes", new { Name = procCode, DisplayOrder = 99 });
        procResp.EnsureSuccessStatusCode();
        var process = await procResp.Content.ReadFromJsonAsync<ProcessMin>();

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

        UseToken(acctToken);
        await _client.PostAsync($"/api/costing/{reqId}/items/{riId}/start", null);
        var review = await _client.GetFromJsonAsync<CostingReview>($"/api/costing/{reqId}");
        var bomLineId = review!.Items[0].BomLines[0].BomLineId;

        await _client.PostAsJsonAsync($"/api/costing/{reqId}/items/{riId}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 1.00m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        return (reqId, riId);
    }

    [Fact]
    public async Task Approve_ReturnsOk_EvenIfNotificationThrows()
    {
        var (reqId, riId) = await WalkToMdReviewAsync();

        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        UseToken(mdToken);

        var approveResp = await _client.PostAsJsonAsync($"/api/approvals/{reqId}/approve", new
        {
            Items = new[] { new { RequisitionItemId = riId, SalesPricePerKgAed = 10m } }
        });

        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        UseToken(spToken);
        var after = await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}");
        after!.Status.Should().Be("Approved");
    }

    [Fact]
    public async Task Reject_ReturnsOk_EvenIfNotificationThrows()
    {
        var (reqId, _) = await WalkToMdReviewAsync();

        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        UseToken(mdToken);

        var rejectResp = await _client.PostAsJsonAsync($"/api/approvals/{reqId}/reject",
            new { Notes = "needs more detail" });

        rejectResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        UseToken(spToken);
        var after = await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}");
        after!.Status.Should().Be("Rejected");
    }
}
