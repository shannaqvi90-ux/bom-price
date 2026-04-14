using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Bom;

public class BomWithCostTests(WebApplicationFactory<Program> factory)
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

    [Fact]
    public async Task GetBom_ReturnsCostColumnsAfterCostingSubmitted_ForeignCurrency()
    {
        // 1. SalesPerson creates requisition (AED quote currency)
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", spToken);

        var items = await _client.GetFromJsonAsync<List<ItemDto>>("/api/items");
        var finishedGood = items!.First(i => i.Type == "FinishedGood");
        var rawMaterial = items!.First(i => i.Type == "RawMaterial");
        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");

        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            ItemId = finishedGood.Id,
            ExpectedQty = 100m,
            CurrencyCode = "AED"
        });
        reqResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();
        var requisitionId = created!.Id;

        // 2. Admin creates a process
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);
        var processCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var processResp = await _client.PostAsJsonAsync("/api/processes",
            new { Name = processCode, DisplayOrder = 99 });
        processResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var process = await processResp.Content.ReadFromJsonAsync<ProcessDto>();

        // 3. BomCreator starts and submits BOM (0.85 kg, 2% wastage)
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", bomToken);

        await _client.PostAsync($"/api/bom/{requisitionId}/start", null);
        var submitBomResp = await _client.PostAsJsonAsync($"/api/bom/{requisitionId}/submit", new
        {
            Lines = new[]
            {
                new
                {
                    ProcessId = process!.Id,
                    RawMaterialItemId = rawMaterial.Id,
                    QtyPerKg = 0.85m,
                    WastagePct = 2.0m
                }
            }
        });
        submitBomResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 4. Accountant uses the seeded USD rate (3.6725 AED/USD — created by seed data)
        var saraToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", saraToken);

        // 5. Accountant starts costing and gets BOM line id
        var startCostResp = await _client.PostAsync($"/api/costing/{requisitionId}/start", null);
        startCostResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var bomForCosting = await _client.GetFromJsonAsync<BomDetailDto>($"/api/bom/{requisitionId}");
        var bomLineId = bomForCosting!.Lines[0].Id;

        // 6. Accountant submits costing at 4.2 USD/kg
        var submitCostResp = await _client.PostAsJsonAsync(
            $"/api/costing/{requisitionId}/submit", new
            {
                RawMaterialCosts = new[]
                {
                    new { BomLineId = bomLineId, CostPerKg = 4.2m, CurrencyCode = "USD" }
                },
                LandedCostType = "Percentage",
                LandedCostValue = 5m,
                FohAmount = 0.5m
            });
        submitCostResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 7. BomCreator reads BOM — cost columns must reflect frozen rates
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", bomToken);
        var bomWithCost = await _client.GetFromJsonAsync<BomDetailDto>(
            $"/api/bom/{requisitionId}");

        bomWithCost!.Lines.Should().HaveCount(1);
        var line = bomWithCost.Lines[0];

        line.CostPerKg.Should().Be(4.2m);
        line.CurrencyCode.Should().Be("USD");
        // 4.2 USD × 3.6725 AED/USD = 15.4245 AED
        line.CostPerKgInAed.Should().BeApproximately(4.2m * 3.6725m, 0.0001m);
        // 15.4245 × 0.85 × 1.02 ≈ 13.3937
        line.ContributionAed.Should().BeApproximately(4.2m * 3.6725m * 0.85m * 1.02m, 0.001m);
    }

    [Fact]
    public async Task GetBom_CostColumnsAreNull_BeforeCostingSubmitted()
    {
        // SalesPerson creates requisition
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", spToken);

        var items = await _client.GetFromJsonAsync<List<ItemDto>>("/api/items");
        var finishedGood = items!.First(i => i.Type == "FinishedGood");
        var rawMaterial = items!.First(i => i.Type == "RawMaterial");
        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");

        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            ItemId = finishedGood.Id,
            ExpectedQty = 50m,
            CurrencyCode = "AED"
        });
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();
        var requisitionId = created!.Id;

        // Admin creates a process
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);
        var processCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var processResp = await _client.PostAsJsonAsync("/api/processes",
            new { Name = processCode, DisplayOrder = 99 });
        var process = await processResp.Content.ReadFromJsonAsync<ProcessDto>();

        // BomCreator submits BOM (no costing yet)
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", bomToken);

        await _client.PostAsync($"/api/bom/{requisitionId}/start", null);
        await _client.PostAsJsonAsync($"/api/bom/{requisitionId}/submit", new
        {
            Lines = new[]
            {
                new
                {
                    ProcessId = process!.Id,
                    RawMaterialItemId = rawMaterial.Id,
                    QtyPerKg = 1.0m,
                    WastagePct = 0m
                }
            }
        });

        // GET /api/bom before costing — cost columns must be null
        var bom = await _client.GetFromJsonAsync<BomDetailDto>($"/api/bom/{requisitionId}");
        var line = bom!.Lines[0];

        line.CostPerKg.Should().BeNull();
        line.CurrencyCode.Should().BeNull();
        line.CostPerKgInAed.Should().BeNull();
        line.ContributionAed.Should().BeNull();
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role,
        int UserId, string Name, int? BranchId);
    private record ItemDto(int Id, string Code, string Description, string Type,
        int BranchId, bool IsActive, decimal? LastPurchasePrice);
    private record CustomerDto(int Id, string Code, string Name);
    private record CreatedRequisition(int Id, string RefNo);
    private record ProcessDto(int Id, string Name, int DisplayOrder, bool IsActive);
    private record BomLineDto(
        int Id, int ProcessId, string ProcessName,
        int RawMaterialItemId, string RawMaterialDescription,
        decimal QtyPerKg, decimal WastagePct,
        decimal? CostPerKg, string? CurrencyCode,
        decimal? CostPerKgInAed, decimal? ContributionAed);
    private record BomDetailDto(int Id, int QuotationRequestId, string RefNo,
        string ItemDescription, List<BomLineDto> Lines,
        decimal TotalCostPerKg, DateTime? SubmittedAt);
}
