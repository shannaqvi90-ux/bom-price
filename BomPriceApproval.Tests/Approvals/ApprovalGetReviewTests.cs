using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Approvals;

public class ApprovalGetReviewTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    /// <summary>
    /// Bootstraps a requisition with <paramref name="itemCount"/> items, submits BOM for all,
    /// and then submits costing only for <paramref name="costedItemCount"/> of them.
    /// Returns the requisition id and all item ids (in order).
    /// </summary>
    private async Task<(int ReqId, int[] ItemIds)> BootstrapPartialCostingAsync(int itemCount, int costedItemCount)
    {
        // 1. SalesPerson creates finished goods + raw material
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);

        var fgIds = new List<int>();
        for (var i = 0; i < itemCount; i++)
        {
            var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
            var fgResp = await _client.PostAsJsonAsync("/api/items",
                new { Code = fgCode, Description = $"Finished Good {i}", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
            fgResp.StatusCode.Should().Be(HttpStatusCode.Created);
            var fg = await fgResp.Content.ReadFromJsonAsync<ItemDto>();
            fgIds.Add(fg!.Id);
        }

        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var rmResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "Raw Material", Type = "RawMaterial", LastPurchasePrice = (decimal?)null });
        rmResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var rm = await rmResp.Content.ReadFromJsonAsync<ItemDto>();

        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = fgIds.Select(id => new { ItemId = id, ExpectedQty = 100m }).ToArray(),
            CurrencyCode = "AED"
        });
        reqResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();
        var requisitionId = created!.Id;

        var reqDetail = await _client.GetFromJsonAsync<PartialRequisitionDetailDto>($"/api/requisitions/{requisitionId}");
        var requisitionItemIds = reqDetail!.Items.Select(i => i.Id).ToArray();

        // 2. Admin creates a process
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var procName = $"P-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await _client.PostAsJsonAsync("/api/processes", new { Name = procName, DisplayOrder = 1 });
        procResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var process = await procResp.Content.ReadFromJsonAsync<PartialProcessDto>();

        // 3. BomCreator starts BOM for each item and saves lines
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);

        foreach (var riId in requisitionItemIds)
        {
            await _client.PostAsync($"/api/bom/{requisitionId}/items/{riId}/start", null);
            await _client.PutAsJsonAsync($"/api/bom/{requisitionId}/items/{riId}/lines", new
            {
                Lines = new[]
                {
                    new { ProcessId = process!.Id, RawMaterialItemId = rm!.Id, QtyPerKg = 0.85m, WastagePct = 2.0m }
                }
            });
        }

        var submitBomResp = await _client.PostAsync($"/api/bom/{requisitionId}/submit", null);
        submitBomResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 4. Accountant submits costing only for the first costedItemCount items
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);

        var costingReview = await _client.GetFromJsonAsync<PartialCostingReviewDto>($"/api/costing/{requisitionId}");

        for (var i = 0; i < costedItemCount; i++)
        {
            var riId = requisitionItemIds[i];
            await _client.PostAsync($"/api/costing/{requisitionId}/items/{riId}/start", null);

            var bomLineId = costingReview!.Items.First(x => x.RequisitionItemId == riId).BomLines[0].BomLineId;
            var submitResp = await _client.PostAsJsonAsync(
                $"/api/costing/{requisitionId}/items/{riId}/submit", new
                {
                    RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 1.00m, CurrencyCode = "AED" } },
                    LandedCostType = "Percentage",
                    LandedCostValue = 0m,
                    FohAmount = 0m
                });
            submitResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        _client.DefaultRequestHeaders.Authorization = null;
        return (requisitionId, requisitionItemIds);
    }

    [Fact]
    public async Task GetReview_PartialCosting_Returns200WithReadyForReviewFalse()
    {
        var (reqId, itemIds) = await BootstrapPartialCostingAsync(itemCount: 2, costedItemCount: 1);

        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var resp = await _client.GetAsync($"/api/approvals/{reqId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<MdReviewDetailDto>();
        body.Should().NotBeNull();
        body!.ReadyForReview.Should().BeFalse();
        body.Items.Should().HaveCount(2);

        // One item has cost, one doesn't
        body.Items.Count(i => i.CostStatus == "Submitted").Should().Be(1);
        body.Items.Count(i => i.CostStatus == "NotStarted").Should().Be(1);

        var uncosted = body.Items.First(i => i.CostStatus == "NotStarted");
        uncosted.Cost.Should().BeNull();

        var costed = body.Items.First(i => i.CostStatus == "Submitted");
        costed.Cost.Should().NotBeNull();
    }

    [Fact]
    public async Task GetReview_AllCosted_Returns200WithReadyForReviewTrue()
    {
        var (reqId, itemIds) = await BootstrapPartialCostingAsync(itemCount: 2, costedItemCount: 2);

        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var resp = await _client.GetAsync($"/api/approvals/{reqId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<MdReviewDetailDto>();
        body.Should().NotBeNull();
        body!.ReadyForReview.Should().BeTrue();
        body.Items.Should().HaveCount(2);
        body.Items.Should().OnlyContain(i => i.CostStatus == "Submitted" && i.Cost != null);
    }

    [Fact]
    public async Task Approve_DivisionByZero_TotalCostZero_DoesNotThrow()
    {
        // Bootstrap: 1 item, fully costed with CostPerKg = 0 (zero total cost scenario)
        // We go through the normal workflow but with 0 cost to verify no DivisionByZeroException.
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "Zero Cost FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemDto>();

        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var rmResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "Zero Cost RM", Type = "RawMaterial", LastPurchasePrice = (decimal?)null });
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
        var procName = $"P-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await _client.PostAsJsonAsync("/api/processes", new { Name = procName, DisplayOrder = 1 });
        var process = await procResp.Content.ReadFromJsonAsync<PartialProcessDto>();

        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);
        await _client.PostAsync($"/api/bom/{requisitionId}/items/{riId}/start", null);
        await _client.PutAsJsonAsync($"/api/bom/{requisitionId}/items/{riId}/lines", new
        {
            Lines = new[] { new { ProcessId = process!.Id, RawMaterialItemId = rm!.Id, QtyPerKg = 0.85m, WastagePct = 2.0m } }
        });
        await _client.PostAsync($"/api/bom/{requisitionId}/submit", null);

        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{requisitionId}/items/{riId}/start", null);
        var costingReview = await _client.GetFromJsonAsync<PartialCostingReviewDto>($"/api/costing/{requisitionId}");
        var bomLineId = costingReview!.Items.First(x => x.RequisitionItemId == riId).BomLines[0].BomLineId;
        await _client.PostAsJsonAsync($"/api/costing/{requisitionId}/items/{riId}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 0m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        // Approve with any price — must not throw even though totalCost == 0
        var approveResp = await _client.PostAsJsonAsync($"/api/approvals/{requisitionId}/approve", new
        {
            Items = new[] { new { RequisitionItemId = riId, SalesPricePerKgAed = 1.00m } },
            Notes = "Zero cost test"
        });

        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);
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
    private record MdReviewItemCostDto(
        decimal RawMaterialCostPerKg, decimal LandedCostPerKg, decimal FohPerKg, decimal TotalCostPerKg,
        decimal MaterialCostPct, decimal LandedCostPct, decimal FohPct);
    private record MdReviewItemDetailDto(int RequisitionItemId, string ItemDescription, decimal ExpectedQty,
        string CostStatus, MdReviewItemCostDto? Cost);
    private record MdReviewDetailDto(string RefNo, string CustomerName,
        string CurrencyCode, decimal? ExchangeRate, bool ReadyForReview,
        List<MdReviewItemDetailDto> Items);
}
