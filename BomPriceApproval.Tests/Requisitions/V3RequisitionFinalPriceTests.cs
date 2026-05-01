using System.Net.Http.Json;
using System.Text.Json;
using BomPriceApproval.Tests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

/// <summary>
/// V3 D-3 backend prereq: GET /api/requisitions/{id} embeds a finalPrice block
/// once the requisition is at MdFinalSign or Signed status. Earlier statuses
/// must return finalPrice = null.
/// </summary>
public class V3RequisitionFinalPriceTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = factory;

    [Fact]
    public async Task Get_StatusMdFinalSign_PopulatesFinalPriceBlock()
    {
        // Walk the V3 happy path up to MdFinalSign — uses the foreign-currency (USD)
        // payload from the helper, with MarginPerKg=1.5 and one FG of 5000kg.
        // PopulateBomCostAsync seeds TotalCostPerKg = 10 (per-kg-RM-cost) × 0.44 (qtyPerKg)
        // × (1 + 0/100) (wastage) = 4.4 per kg. Sale = 4.4 + 1.5 = 5.9 USD per kg;
        // sale per kg in AED depends on the seeded USD FX rate.
        var reqId = await V3WorkflowTestHelpers.WalkToMdFinalSignAsync(_factory);

        var sales = await V3WorkflowTestHelpers.CreateSalesClientAsync(_factory);
        var resp = await sales.GetAsync($"/api/requisitions/{reqId}");
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("MdFinalSign");

        // finalPrice should be populated — controller computes it for MdFinalSign + Signed.
        body.TryGetProperty("finalPrice", out var fp).Should().BeTrue();
        fp.ValueKind.Should().NotBe(JsonValueKind.Null);

        fp.GetProperty("currencyCode").GetString().Should().Be("USD");

        // RateSnapshot is captured at set-margin time from the active USD rate.
        // We don't assert the exact value (depends on seeded ExchangeRates), only that
        // it's populated for non-AED + drives SalePerKgAed > SalePerKg.
        var rate = fp.GetProperty("rateSnapshot").GetDecimal();
        rate.Should().BeGreaterThan(0m);

        var perFg = fp.GetProperty("perFg");
        perFg.GetArrayLength().Should().Be(1);

        var fg0 = perFg[0];
        var costPerKg = fg0.GetProperty("costPerKg").GetDecimal();
        var marginPerKg = fg0.GetProperty("marginPerKg").GetDecimal();
        marginPerKg.Should().Be(1.5m);
        costPerKg.Should().BeGreaterThan(0m); // PopulateBomCostAsync seeds non-zero cost
        fg0.GetProperty("salePerKg").GetDecimal().Should().Be(costPerKg + marginPerKg);
        fg0.GetProperty("expectedQty").GetDecimal().Should().Be(5000m);

        var salePerKgAed = fg0.GetProperty("salePerKgAed").GetDecimal();
        salePerKgAed.Should().Be((costPerKg + marginPerKg) * rate);

        var totalAed = fg0.GetProperty("totalAed").GetDecimal();
        totalAed.Should().Be(salePerKgAed * 5000m);

        fp.GetProperty("totalAed").GetDecimal().Should().Be(totalAed);
    }

    [Fact]
    public async Task Get_StatusCosting_FinalPriceIsNull()
    {
        // Walk only to Costing — finalPrice should NOT be populated yet.
        var reqId = await V3WorkflowTestHelpers.WalkToCostingAsync(_factory);

        var sales = await V3WorkflowTestHelpers.CreateSalesClientAsync(_factory);
        var resp = await sales.GetAsync($"/api/requisitions/{reqId}");
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Costing");
        body.TryGetProperty("finalPrice", out var fp).Should().BeTrue();
        fp.ValueKind.Should().Be(JsonValueKind.Null);
    }
}
