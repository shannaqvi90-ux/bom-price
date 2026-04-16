using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using BomPriceApproval.Tests.Requisitions;

namespace BomPriceApproval.Tests.Approvals;

public class ApprovalValidationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private async Task<(int ReqId, int[] ItemIds)> BootstrapToMdReviewAsync(int itemCount)
    {
        // 1. SalesPerson creates itemCount finished goods + 1 raw material
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

        var reqDetail = await _client.GetFromJsonAsync<ApprovalRequisitionDetailDto>($"/api/requisitions/{requisitionId}");
        var requisitionItemIds = reqDetail!.Items.Select(i => i.Id).ToArray();

        // 2. Admin creates a process
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var procName = $"P-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await _client.PostAsJsonAsync("/api/processes", new { Name = procName, DisplayOrder = 1 });
        procResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var process = await procResp.Content.ReadFromJsonAsync<ApprovalProcessDto>();

        // 3. BomCreator starts BOM for each item, saves lines
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

        // 4. BomCreator submits BOM (single request-level call)
        var submitBomResp = await _client.PostAsync($"/api/bom/{requisitionId}/submit", null);
        submitBomResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 5. Accountant starts and submits costing for each item
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);

        foreach (var riId in requisitionItemIds)
        {
            await _client.PostAsync($"/api/costing/{requisitionId}/items/{riId}/start", null);

            var review = await _client.GetFromJsonAsync<ApprovalCostingReviewDto>($"/api/costing/{requisitionId}");
            var bomLineId = review!.Items.First(i => i.RequisitionItemId == riId).BomLines[0].BomLineId;

            var submitCostResp = await _client.PostAsJsonAsync(
                $"/api/costing/{requisitionId}/items/{riId}/submit", new
                {
                    RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 1.00m, CurrencyCode = "AED" } },
                    LandedCostType = "Percentage",
                    LandedCostValue = 0m,
                    FohAmount = 0m
                });
            submitCostResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        _client.DefaultRequestHeaders.Authorization = null;
        return (requisitionId, requisitionItemIds);
    }

    [Fact]
    public async Task Approve_ZeroPrice_Returns400()
    {
        var (reqId, itemIds) = await BootstrapToMdReviewAsync(itemCount: 1);
        var md = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", md);

        var resp = await _client.PostAsJsonAsync(
            $"/api/approvals/{reqId}/approve",
            new { Items = new[] { new { RequisitionItemId = itemIds[0], SalesPricePerKgAed = 0m } }, Notes = "" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.Should().Contain("SalesPrice");
    }

    [Fact]
    public async Task Approve_MissingItemInInput_Returns400()
    {
        var (reqId, itemIds) = await BootstrapToMdReviewAsync(itemCount: 2);
        var md = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", md);

        var resp = await _client.PostAsJsonAsync(
            $"/api/approvals/{reqId}/approve",
            new { Items = new[] { new { RequisitionItemId = itemIds[0], SalesPricePerKgAed = 10m } }, Notes = "" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.ToLower().Should().Contain("missing");
    }

    [Fact]
    public async Task Approve_DuplicateItemInInput_Returns400()
    {
        var (reqId, itemIds) = await BootstrapToMdReviewAsync(itemCount: 1);
        var md = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", md);

        var resp = await _client.PostAsJsonAsync(
            $"/api/approvals/{reqId}/approve",
            new
            {
                Items = new[]
                {
                    new { RequisitionItemId = itemIds[0], SalesPricePerKgAed = 10m },
                    new { RequisitionItemId = itemIds[0], SalesPricePerKgAed = 20m },
                },
                Notes = ""
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.ToLower().Should().Contain("duplicate");
    }

    [Fact]
    public async Task Approve_NegativeMargin_Succeeds()
    {
        // Documents the soft-warning policy: price < cost is allowed.
        var (reqId, itemIds) = await BootstrapToMdReviewAsync(itemCount: 1);
        var md = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", md);

        var resp = await _client.PostAsJsonAsync(
            $"/api/approvals/{reqId}/approve",
            new { Items = new[] { new { RequisitionItemId = itemIds[0], SalesPricePerKgAed = 0.01m } }, Notes = "" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Approve_OrphanItemInInput_Returns400()
    {
        var (reqId, itemIds) = await BootstrapToMdReviewAsync(itemCount: 1);
        var md = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", md);

        // Submit both the real item AND an orphan id that doesn't belong to this requisition
        var resp = await _client.PostAsJsonAsync(
            $"/api/approvals/{reqId}/approve",
            new
            {
                Items = new[]
                {
                    new { RequisitionItemId = itemIds[0], SalesPricePerKgAed = 10m },
                    new { RequisitionItemId = 999999, SalesPricePerKgAed = 20m },
                },
                Notes = ""
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.ToLower().Should().Contain("unknown");
    }

    // ── Private DTOs (non-colliding with Requisitions namespace records) ──
    private record ApprovalRequisitionItemDto(int Id, int ItemId, string ItemDescription, decimal ExpectedQty, int SortOrder);
    private record ApprovalRequisitionDetailDto(int Id, string RefNo, string Status, List<ApprovalRequisitionItemDto> Items);
    private record ApprovalProcessDto(int Id, string Name, int DisplayOrder, bool IsActive);
    private record ApprovalCostingBomLineDto(int BomLineId, int ProcessId, string ProcessName,
        int RawMaterialItemId, string RawMaterialDescription, decimal QtyPerKg, decimal WastagePct,
        object? LastCost);
    private record ApprovalCostingItemDto(int RequisitionItemId, int ItemId, string ItemDescription, decimal ExpectedQty,
        int? BomHeaderId, string CostStatus, object? Cost,
        List<ApprovalCostingBomLineDto> BomLines, object? Draft);
    private record ApprovalCostingReviewDto(int RequisitionId, List<ApprovalCostingItemDto> Items);
}
