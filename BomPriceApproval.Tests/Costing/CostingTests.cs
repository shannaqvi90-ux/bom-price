using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Costing;

public class CostingTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private async Task<(int RequisitionId, int RequisitionItemId)> CreateRequisitionWithBomInCostingPendingAsync(string quoteCurrency = "AED")
    {
        // 1. SalesPerson creates items + requisition
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "Test FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemDto>();
        var rmResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "Test RM", Type = "RawMaterial", LastPurchasePrice = (decimal?)null });
        var rm = await rmResp.Content.ReadFromJsonAsync<ItemDto>();

        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fg!.Id, ExpectedQty = 100m } },
            CurrencyCode = quoteCurrency
        });
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();
        var requisitionId = created!.Id;

        // Get requisitionItemId
        var reqDetail = await _client.GetFromJsonAsync<RequisitionDetailDto>($"/api/requisitions/{requisitionId}");
        var requisitionItemId = reqDetail!.Items[0].Id;

        // 2. Admin creates a process
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var procCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await _client.PostAsJsonAsync("/api/processes", new { Name = procCode, DisplayOrder = 99 });
        var process = await procResp.Content.ReadFromJsonAsync<ProcessDto>();

        // 3. BomCreator starts, saves lines, submits BOM → CostingPending
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);
        await _client.PostAsync($"/api/bom/{requisitionId}/items/{requisitionItemId}/start", null);
        await _client.PutAsJsonAsync($"/api/bom/{requisitionId}/items/{requisitionItemId}/lines", new
        {
            Lines = new[]
            {
                new { ProcessId = process!.Id, RawMaterialItemId = rm!.Id, QtyPerKg = 0.85m, WastagePct = 2.0m }
            }
        });
        await _client.PostAsync($"/api/bom/{requisitionId}/submit", null);

        _client.DefaultRequestHeaders.Authorization = null;
        return (requisitionId, requisitionItemId);
    }

    private async Task<CostingReviewDto> GetCostingAsync(int requisitionId)
    {
        var resp = await _client.GetAsync($"/api/costing/{requisitionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<CostingReviewDto>())!;
    }

    [Fact]
    public async Task Start_TransitionsCostingPendingToCostingInProgress()
    {
        var (requisitionId, requisitionItemId) = await CreateRequisitionWithBomInCostingPendingAsync();

        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);

        var startResp = await _client.PostAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/start", null);
        startResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);
        var req = await _client.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{requisitionId}");
        req!.Status.Should().Be("CostingInProgress");
    }

    [Fact]
    public async Task SaveDraft_PersistsAndGetReturnsDraft()
    {
        var (requisitionId, requisitionItemId) = await CreateRequisitionWithBomInCostingPendingAsync();
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/start", null);

        var review = await GetCostingAsync(requisitionId);
        review.Items.Should().HaveCount(1);
        var bomLineId = review.Items[0].BomLines[0].BomLineId;

        var draftResp = await _client.PutAsJsonAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/draft", new
        {
            Lines = new[] { new { BomLineId = bomLineId, CostPerKg = 1.25m, CurrencyCode = "USD" } },
            LandedCostType = "Percentage",
            LandedCostValue = 5m,
            FohAmount = 0.12m
        });
        draftResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Status still CostingInProgress
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);
        var req = await _client.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{requisitionId}");
        req!.Status.Should().Be("CostingInProgress");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        var reloaded = await GetCostingAsync(requisitionId);
        var item = reloaded.Items[0];
        item.Draft.Should().NotBeNull();
        item.Draft!.Lines.Should().HaveCount(1);
        item.Draft.Lines[0].CostPerKg.Should().Be(1.25m);
        item.Draft.Lines[0].CurrencyCode.Should().Be("USD");
        item.Draft.LandedCostValue.Should().Be(5m);
    }

    [Fact]
    public async Task Submit_ConvertsCurrencyWritesLinesUpsertsLastCostAndMovesToMdReview()
    {
        var (requisitionId, requisitionItemId) = await CreateRequisitionWithBomInCostingPendingAsync(quoteCurrency: "AED");
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/start", null);

        var review = await GetCostingAsync(requisitionId);
        var bomLineId = review.Items[0].BomLines[0].BomLineId;

        // Submit with USD cost — seeded USD rate is 3.6725, quote AED = 1.0
        var submitResp = await _client.PostAsJsonAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 1.00m, CurrencyCode = "USD" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });
        submitResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Requisition → MdReview
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);
        var req = await _client.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{requisitionId}");
        req!.Status.Should().Be("MdReview");

        // Verify cost written + ItemLastCost upserted
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        var afterSubmit = await GetCostingAsync(requisitionId);
        var item = afterSubmit.Items[0];
        item.Cost.Should().NotBeNull();
        item.Cost!.RawMaterialCostTotal.Should().BeGreaterThan(0);
        item.BomLines[0].LastCost.Should().NotBeNull();
        item.BomLines[0].LastCost!.CostPerKg.Should().Be(1.00m);
        item.BomLines[0].LastCost!.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public async Task Submit_ReturnsBadRequest_WhenExchangeRateMissing()
    {
        var (requisitionId, requisitionItemId) = await CreateRequisitionWithBomInCostingPendingAsync(quoteCurrency: "AED");
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/start", null);

        var review = await GetCostingAsync(requisitionId);
        var bomLineId = review.Items[0].BomLines[0].BomLineId;

        // SAR rate not seeded → should fail
        var submitResp = await _client.PostAsJsonAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 5.0m, CurrencyCode = "SAR" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });
        submitResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Recosting_NewRequisitionDoesNotModifyPreviousBomCostLines()
    {
        // Submit costing on requisition A
        var (reqA, riA) = await CreateRequisitionWithBomInCostingPendingAsync();
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{reqA}/items/{riA}/start", null);
        var reviewA = await GetCostingAsync(reqA);
        var bomLineIdA = reviewA.Items[0].BomLines[0].BomLineId;
        await _client.PostAsJsonAsync($"/api/costing/{reqA}/items/{riA}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineIdA, CostPerKg = 2.00m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        // Snapshot BomCost aggregate for A
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        var afterA = await GetCostingAsync(reqA);
        var totalA = afterA.Items[0].Cost!.TotalCostPerKg;

        // Submit costing on requisition B with a different cost for same raw material
        var (reqB, riB) = await CreateRequisitionWithBomInCostingPendingAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{reqB}/items/{riB}/start", null);
        var reviewB = await GetCostingAsync(reqB);
        var bomLineIdB = reviewB.Items[0].BomLines[0].BomLineId;
        await _client.PostAsJsonAsync($"/api/costing/{reqB}/items/{riB}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineIdB, CostPerKg = 9.99m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        // Requisition A total must be unchanged
        var reloadedA = await GetCostingAsync(reqA);
        reloadedA.Items[0].Cost!.TotalCostPerKg.Should().Be(totalA);
    }

    private async Task<(int RequisitionId, int RequisitionItemId, int[] BomLineIds)> BootstrapToCostingAsync(int bomLineCount)
    {
        // 1. SalesPerson creates items + requisition
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "Test FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemDto>();

        var rmIds = new List<int>();
        for (var i = 0; i < bomLineCount; i++)
        {
            var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
            var rmResp = await _client.PostAsJsonAsync("/api/items",
                new { Code = rmCode, Description = $"RM {i}", Type = "RawMaterial", LastPurchasePrice = (decimal?)null });
            var rm = await rmResp.Content.ReadFromJsonAsync<ItemDto>();
            rmIds.Add(rm!.Id);
        }

        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fg!.Id, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();
        var requisitionId = created!.Id;

        var reqDetail = await _client.GetFromJsonAsync<RequisitionDetailDto>($"/api/requisitions/{requisitionId}");
        var requisitionItemId = reqDetail!.Items[0].Id;

        // 2. Admin creates a process
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var procName = $"P-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await _client.PostAsJsonAsync("/api/processes", new { Name = procName, DisplayOrder = 1 });
        var process = await procResp.Content.ReadFromJsonAsync<ProcessDto>();

        // 3. BomCreator starts, saves lines, submits BOM → CostingPending
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);
        await _client.PostAsync($"/api/bom/{requisitionId}/items/{requisitionItemId}/start", null);
        await _client.PutAsJsonAsync($"/api/bom/{requisitionId}/items/{requisitionItemId}/lines", new
        {
            Lines = rmIds.Select(rmId =>
                new { ProcessId = process!.Id, RawMaterialItemId = rmId, QtyPerKg = 0.85m, WastagePct = 2.0m })
                .ToArray()
        });
        await _client.PostAsync($"/api/bom/{requisitionId}/submit", null);

        // 4. Accountant starts costing → CostingInProgress
        var accToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accToken);
        await _client.PostAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/start", null);

        // 5. Fetch costing to extract bomLineIds
        var review = await _client.GetFromJsonAsync<CostingReviewDto>($"/api/costing/{requisitionId}");
        var bomLineIds = review!.Items[0].BomLines.Select(l => l.BomLineId).ToArray();

        _client.DefaultRequestHeaders.Authorization = null;
        return (requisitionId, requisitionItemId, bomLineIds);
    }

    [Fact]
    public async Task Submit_NegativeCost_Returns400()
    {
        var (reqId, itemId, bomLineIds) = await BootstrapToCostingAsync(bomLineCount: 1);

        var accToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accToken);

        var resp = await _client.PostAsJsonAsync(
            $"/api/costing/{reqId}/items/{itemId}/submit",
            new
            {
                RawMaterialCosts = new[] { new { BomLineId = bomLineIds[0], CostPerKg = -5m, CurrencyCode = "AED" } },
                LandedCostType = "Percentage",
                LandedCostValue = 5m,
                FohAmount = 1m
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<BomPriceApproval.Tests.Requisitions.ErrorResponse>();
        body!.Message.ToLower().Should().Contain("cost");
    }

    [Fact]
    public async Task Submit_UnknownBomLineId_Returns400()
    {
        var (reqId, itemId, _) = await BootstrapToCostingAsync(bomLineCount: 1);

        var accToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accToken);

        var resp = await _client.PostAsJsonAsync(
            $"/api/costing/{reqId}/items/{itemId}/submit",
            new
            {
                RawMaterialCosts = new[] { new { BomLineId = 999999, CostPerKg = 5m, CurrencyCode = "AED" } },
                LandedCostType = "Percentage",
                LandedCostValue = 5m,
                FohAmount = 1m
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<BomPriceApproval.Tests.Requisitions.ErrorResponse>();
        body!.Message.ToLower().Should().Contain("unknown");
    }

    [Fact]
    public async Task Submit_MissingLineCost_Returns400()
    {
        var (reqId, itemId, bomLineIds) = await BootstrapToCostingAsync(bomLineCount: 2);

        var accToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accToken);

        // Submit cost for only 1 of 2 BOM lines
        var resp = await _client.PostAsJsonAsync(
            $"/api/costing/{reqId}/items/{itemId}/submit",
            new
            {
                RawMaterialCosts = new[] { new { BomLineId = bomLineIds[0], CostPerKg = 5m, CurrencyCode = "AED" } },
                LandedCostType = "Percentage",
                LandedCostValue = 5m,
                FohAmount = 1m
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<BomPriceApproval.Tests.Requisitions.ErrorResponse>();
        body!.Message.Should().Contain("Missing cost");
    }

    // ── DTOs ──
    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record ItemDto(int Id, string Code, string Description, string Type, int BranchId, bool IsActive, decimal? LastPurchasePrice);
    private record CustomerDto(int Id, string Code, string Name);
    private record ProcessDto(int Id, string Name, int DisplayOrder, bool IsActive);
    private record CreatedRequisition(int Id, string RefNo);
    private record RequisitionItemDto(int Id, int ItemId, string ItemDescription, decimal ExpectedQty, int SortOrder);
    private record RequisitionDetailDto(int Id, string RefNo, string Status, List<RequisitionItemDto> Items);
    private record RequisitionDto(int Id, string RefNo, string Status);
    private record LastCostDto(decimal CostPerKg, string CurrencyCode, DateTime UpdatedAt);
    private record CostingBomLineDto(int BomLineId, int ProcessId, string ProcessName,
        int RawMaterialItemId, string RawMaterialDescription, decimal QtyPerKg, decimal WastagePct,
        LastCostDto? LastCost);
    private record CostingDraftLineDto(int BomLineId, decimal CostPerKg, string CurrencyCode);
    private record CostingDraftDto(List<CostingDraftLineDto> Lines, string LandedCostType,
        decimal LandedCostValue, decimal FohAmount);
    private record CostingSummaryDto(int Id, decimal RawMaterialCostTotal, string LandedCostType,
        decimal LandedCostValue, decimal FohAmount, decimal TotalCostPerKg, DateTime? SubmittedAt);
    private record CostingItemDto(int RequisitionItemId, int ItemId, string ItemDescription, decimal ExpectedQty,
        int? BomHeaderId, string CostStatus, CostingSummaryDto? Cost,
        List<CostingBomLineDto> BomLines, CostingDraftDto? Draft);
    private record CostingReviewDto(int RequisitionId, List<CostingItemDto> Items);
}
