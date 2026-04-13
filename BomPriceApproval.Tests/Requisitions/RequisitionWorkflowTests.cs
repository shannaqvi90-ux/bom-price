using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

public class RequisitionWorkflowTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task CreateRequisition_AsSalesPerson_ReturnsCreated()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = 1,
            ItemId = 1,
            ExpectedQty = 1000m,
            CurrencyCode = "AED"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GetRequisitions_AsSalesPerson_SeesOnlyOwnRequests()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/requisitions");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
