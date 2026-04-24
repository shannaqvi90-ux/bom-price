using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Bom;

public class BomHistoricalReadTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private async Task<(int ReqId, int ItemId)> BootstrapThroughMdReviewAsync()
    {
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemDto>();

        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var rmResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "RM", Type = "RawMaterial", LastPurchasePrice = (decimal?)null });
        var rm = await rmResp.Content.ReadFromJsonAsync<ItemDto>();

        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fg!.Id, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();
        var requisitionId = created!.Id;

        var reqDetail = await _client.GetFromJsonAsync<PartialRequisitionDetailDto>($"/api/requisitions/{requisitionId}");
        var riId = reqDetail!.Items[0].Id;

        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var procResp = await _client.PostAsJsonAsync("/api/processes",
            new { Name = $"P-{Guid.NewGuid():N}".Substring(0, 12), DisplayOrder = 1 });
        var proc = await procResp.Content.ReadFromJsonAsync<PartialProcessDto>();

        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);
        await _client.PostAsync($"/api/bom/{requisitionId}/items/{riId}/start", null);
        await _client.PutAsJsonAsync($"/api/bom/{requisitionId}/items/{riId}/lines", new
        {
            Lines = new[]
            {
                new { ProcessId = proc!.Id, RawMaterialItemId = rm!.Id, QtyPerKg = 0.85m, WastagePct = 2.0m }
            }
        });
        await _client.PostAsync($"/api/bom/{requisitionId}/submit", null);

        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        var costing = await _client.GetFromJsonAsync<PartialCostingReviewDto>($"/api/costing/{requisitionId}");
        var bomLineId = costing!.Items.First(x => x.RequisitionItemId == riId).BomLines[0].BomLineId;
        await _client.PostAsync($"/api/costing/{requisitionId}/items/{riId}/start", null);
        await _client.PostAsJsonAsync($"/api/costing/{requisitionId}/items/{riId}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 1.00m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        _client.DefaultRequestHeaders.Authorization = null;
        return (requisitionId, riId);
    }

    private async Task<int> BootstrapApprovedRequisitionAsync()
    {
        var (reqId, riId) = await BootstrapThroughMdReviewAsync();
        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);
        await _client.PostAsJsonAsync($"/api/approvals/{reqId}/approve", new
        {
            Items = new[] { new { RequisitionItemId = riId, SalesPricePerKgAed = 2.50m } },
            Notes = (string?)null
        });
        _client.DefaultRequestHeaders.Authorization = null;
        return reqId;
    }

    private async Task<int> BootstrapRejectedRequisitionAsync()
    {
        var (reqId, _) = await BootstrapThroughMdReviewAsync();
        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);
        await _client.PostAsJsonAsync($"/api/approvals/{reqId}/reject", new
        {
            Notes = "Price too low"
        });
        _client.DefaultRequestHeaders.Authorization = null;
        return reqId;
    }

    [Fact]
    public async Task GetBom_ReturnsLinesForApprovedRequisition()
    {
        var requisitionId = await BootstrapApprovedRequisitionAsync();

        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var resp = await _client.GetAsync($"/api/bom/{requisitionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PartialBomReviewDto>();
        body!.RequisitionStatus.Should().Be("Approved");
        body.Items.Should().HaveCount(1);
        body.Items[0].Lines.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetBom_ReturnsLinesForRejectedRequisition()
    {
        var requisitionId = await BootstrapRejectedRequisitionAsync();

        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var resp = await _client.GetAsync($"/api/bom/{requisitionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PartialBomReviewDto>();
        body!.RequisitionStatus.Should().Be("Rejected");
        body.Items[0].Lines.Should().HaveCount(1);
    }

    // ── Private DTOs ──
    private record PartialRequisitionItemDto(int Id, int ItemId, string ItemDescription, decimal ExpectedQty, int SortOrder);
    private record PartialRequisitionDetailDto(int Id, string RefNo, string Status, List<PartialRequisitionItemDto> Items);
    private record PartialProcessDto(int Id, string Name, int DisplayOrder, bool IsActive);
    private record PartialCostingBomLineDto(int BomLineId, int ProcessId, string ProcessName,
        int RawMaterialItemId, string RawMaterialDescription, decimal QtyPerKg, decimal WastagePct,
        object? LastCost);
    private record PartialCostingItemDto(int RequisitionItemId, int ItemId, string ItemDescription, decimal ExpectedQty,
        int? BomHeaderId, string CostStatus, object? Cost,
        List<PartialCostingBomLineDto> BomLines, object? Draft);
    private record PartialCostingReviewDto(int RequisitionId, List<PartialCostingItemDto> Items);
    private record PartialBomReviewDto(int RequisitionId, string RefNo, string RequisitionStatus, List<PartialBomItemDto> Items);
    private record PartialBomItemDto(int RequisitionItemId, string BomStatus, List<PartialBomLineDto> Lines);
    private record PartialBomLineDto(int Id, string ProcessName, decimal QtyPerKg, decimal WastagePct);
}
