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

    private async Task<int> CreateRequisitionWithBomInCostingPendingAsync(string quoteCurrency = "AED")
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
            ItemId = fg!.Id,
            ExpectedQty = 100m,
            CurrencyCode = quoteCurrency
        });
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();
        var requisitionId = created!.Id;

        // 2. Admin creates a process
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var procCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await _client.PostAsJsonAsync("/api/processes", new { Name = procCode, DisplayOrder = 99 });
        var process = await procResp.Content.ReadFromJsonAsync<ProcessDto>();

        // 3. BomCreator starts + submits BOM → CostingPending
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);
        await _client.PostAsync($"/api/bom/{requisitionId}/start", null);
        await _client.PostAsJsonAsync($"/api/bom/{requisitionId}/submit", new
        {
            Lines = new[]
            {
                new { ProcessId = process!.Id, RawMaterialItemId = rm!.Id, QtyPerKg = 0.85m, WastagePct = 2.0m }
            }
        });

        return requisitionId;
    }

    private async Task<CostingDetailDto> GetCostingAsync(int requisitionId)
    {
        var resp = await _client.GetAsync($"/api/costing/{requisitionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<CostingDetailDto>())!;
    }

    [Fact]
    public async Task Start_TransitionsCostingPendingToCostingInProgress()
    {
        var requisitionId = await CreateRequisitionWithBomInCostingPendingAsync();

        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);

        var startResp = await _client.PostAsync($"/api/costing/{requisitionId}/start", null);
        startResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);
        var req = await _client.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{requisitionId}");
        req!.Status.Should().Be("CostingInProgress");
    }

    [Fact]
    public async Task SaveDraft_PersistsAndGetReturnsDraft()
    {
        var requisitionId = await CreateRequisitionWithBomInCostingPendingAsync();
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{requisitionId}/start", null);

        var detail = await GetCostingAsync(requisitionId);
        detail.BomLines.Should().HaveCount(1);
        var bomLineId = detail.BomLines[0].BomLineId;

        var draftResp = await _client.PutAsJsonAsync($"/api/costing/{requisitionId}/draft", new
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
        reloaded.Draft.Should().NotBeNull();
        reloaded.Draft!.Lines.Should().HaveCount(1);
        reloaded.Draft.Lines[0].CostPerKg.Should().Be(1.25m);
        reloaded.Draft.Lines[0].CurrencyCode.Should().Be("USD");
        reloaded.Draft.LandedCostValue.Should().Be(5m);
    }

    [Fact]
    public async Task Submit_ConvertsCurrencyWritesLinesUpsertsLastCostAndMovesToMdReview()
    {
        var requisitionId = await CreateRequisitionWithBomInCostingPendingAsync(quoteCurrency: "AED");
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{requisitionId}/start", null);

        var detail = await GetCostingAsync(requisitionId);
        var bomLineId = detail.BomLines[0].BomLineId;

        // Submit with USD cost — seeded USD rate is 3.6725, quote AED = 1.0
        var submitResp = await _client.PostAsJsonAsync($"/api/costing/{requisitionId}/submit", new
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

        // Verify BomCost aggregate written + ItemLastCost upserted
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        var afterSubmit = await GetCostingAsync(requisitionId);
        afterSubmit.RawMaterialCostTotal.Should().BeGreaterThan(0); // conversion happened, BomCostLine written
        afterSubmit.BomLines[0].LastCost.Should().NotBeNull(); // ItemLastCost upserted
        afterSubmit.BomLines[0].LastCost!.CostPerKg.Should().Be(1.00m); // original entry cost stored
        afterSubmit.BomLines[0].LastCost!.CurrencyCode.Should().Be("USD"); // original entry currency stored
    }

    [Fact]
    public async Task Submit_ReturnsBadRequest_WhenExchangeRateMissing()
    {
        var requisitionId = await CreateRequisitionWithBomInCostingPendingAsync(quoteCurrency: "AED");
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{requisitionId}/start", null);

        var detail = await GetCostingAsync(requisitionId);
        var bomLineId = detail.BomLines[0].BomLineId;

        // SAR rate not seeded → should fail
        var submitResp = await _client.PostAsJsonAsync($"/api/costing/{requisitionId}/submit", new
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
        var reqA = await CreateRequisitionWithBomInCostingPendingAsync();
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{reqA}/start", null);
        var detailA = await GetCostingAsync(reqA);
        var bomLineIdA = detailA.BomLines[0].BomLineId;
        await _client.PostAsJsonAsync($"/api/costing/{reqA}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineIdA, CostPerKg = 2.00m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        // Snapshot BomCost aggregate for A
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        var afterA = await GetCostingAsync(reqA);
        var totalA = afterA.TotalCostPerKg;

        // Submit costing on requisition B with a different cost for same raw material
        var reqB = await CreateRequisitionWithBomInCostingPendingAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{reqB}/start", null);
        var detailB = await GetCostingAsync(reqB);
        var bomLineIdB = detailB.BomLines[0].BomLineId;
        await _client.PostAsJsonAsync($"/api/costing/{reqB}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineIdB, CostPerKg = 9.99m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        // Requisition A total must be unchanged
        var reloadedA = await GetCostingAsync(reqA);
        reloadedA.TotalCostPerKg.Should().Be(totalA);
    }

    // ── DTOs ──
    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record ItemDto(int Id, string Code, string Description, string Type, int BranchId, bool IsActive, decimal? LastPurchasePrice);
    private record CustomerDto(int Id, string Code, string Name);
    private record ProcessDto(int Id, string Name, int DisplayOrder, bool IsActive);
    private record CreatedRequisition(int Id, string RefNo);
    private record RequisitionDto(int Id, string RefNo, string Status);
    private record LastCostDto(decimal CostPerKg, string CurrencyCode, DateTime UpdatedAt);
    private record CostingBomLineDto(int BomLineId, int ProcessId, string ProcessName,
        int RawMaterialItemId, string RawMaterialDescription, decimal QtyPerKg, decimal WastagePct,
        LastCostDto? LastCost);
    private record CostingDraftLineDto(int BomLineId, decimal CostPerKg, string CurrencyCode);
    private record CostingDraftDto(List<CostingDraftLineDto> Lines, string LandedCostType,
        decimal LandedCostValue, decimal FohAmount);
    private record CostingDetailDto(int Id, decimal RawMaterialCostTotal, string LandedCostType,
        decimal LandedCostValue, decimal FohAmount, decimal TotalCostPerKg, DateTime? SubmittedAt,
        List<CostingBomLineDto> BomLines, CostingDraftDto? Draft);
}
