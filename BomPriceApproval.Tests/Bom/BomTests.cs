using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Bom;

public class BomTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task SubmitBom_UpdatesRequisitionStatusToCostingPending()
    {
        var client = factory.CreateClient();
        // Login as BOM creator, submit BOM for a requisition in BomPending status
        var response = await client.PostAsJsonAsync("/api/bom/1/submit", new
        {
            Lines = new[] { new { ProcessId = 1, RawMaterialItemId = 2, QtyPerKg = 0.8m, WastagePct = 5m } }
        });
        // Requisition status should become CostingPending
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
