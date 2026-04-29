using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BomPriceApproval.Tests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

/// <summary>
/// V3 happy-path coverage for the new sales+BOM combined Create flow,
/// the Submit (Draft -> Costing) transition, and Cancel guard rails.
/// All seeded entities (customer + FG + RM) are Guid-isolated so tests
/// in the same class run independently. Helpers live in
/// <see cref="V3WorkflowTestHelpers"/> so other V3 test classes can reuse them.
/// </summary>
public class V3RequisitionWorkflowTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = factory;

    [Fact]
    public async Task Sales_CreatesRequisition_WithInlineBOM_StartsAsDraft()
    {
        var client = await V3WorkflowTestHelpers.CreateSalesClientAsync(_factory);

        var (customerId, fgId, rmId, processId) = await V3WorkflowTestHelpers.SeedV3MinimumAsync(_factory);
        var (statusCode, reqId, status) =
            await V3WorkflowTestHelpers.CreateV3DraftRequisitionAsync(client, customerId, fgId, rmId, processId);

        statusCode.Should().Be(HttpStatusCode.Created);
        status.Should().Be("Draft");
        reqId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Sales_SubmitsRequisition_TransitionsToCosting()
    {
        var client = await V3WorkflowTestHelpers.CreateSalesClientAsync(_factory);

        var (customerId, fgId, rmId, processId) = await V3WorkflowTestHelpers.SeedV3MinimumAsync(_factory);
        var (_, reqId, _) =
            await V3WorkflowTestHelpers.CreateV3DraftRequisitionAsync(client, customerId, fgId, rmId, processId);

        var submit = await client.PostAsync($"/api/requisitions/{reqId}/submit", content: null);
        submit.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await submit.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Costing");
    }

    [Fact]
    public async Task Submit_FromCostingStatus_ReturnsBadRequest()
    {
        var client = await V3WorkflowTestHelpers.CreateSalesClientAsync(_factory);

        var (customerId, fgId, rmId, processId) = await V3WorkflowTestHelpers.SeedV3MinimumAsync(_factory);
        var (_, reqId, _) =
            await V3WorkflowTestHelpers.CreateV3DraftRequisitionAsync(client, customerId, fgId, rmId, processId);

        // First submit transitions Draft -> Costing
        var firstSubmit = await client.PostAsync($"/api/requisitions/{reqId}/submit", content: null);
        firstSubmit.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second submit must be rejected — already Costing
        var secondSubmit = await client.PostAsync($"/api/requisitions/{reqId}/submit", content: null);
        secondSubmit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cancel_DraftRequisition_TransitionsToCancelled()
    {
        var client = await V3WorkflowTestHelpers.CreateSalesClientAsync(_factory);

        var (customerId, fgId, rmId, processId) = await V3WorkflowTestHelpers.SeedV3MinimumAsync(_factory);
        var (_, reqId, _) =
            await V3WorkflowTestHelpers.CreateV3DraftRequisitionAsync(client, customerId, fgId, rmId, processId);

        var cancel = await client.PostAsJsonAsync($"/api/requisitions/{reqId}/cancel",
            new { reason = "Customer withdrew" });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await cancel.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Cancelled");
    }

    [Fact]
    public async Task Cancel_WithoutReason_ReturnsBadRequest()
    {
        var client = await V3WorkflowTestHelpers.CreateSalesClientAsync(_factory);

        var (customerId, fgId, rmId, processId) = await V3WorkflowTestHelpers.SeedV3MinimumAsync(_factory);
        var (_, reqId, _) =
            await V3WorkflowTestHelpers.CreateV3DraftRequisitionAsync(client, customerId, fgId, rmId, processId);

        // Reason "x" is below the 5-char minimum guard.
        var cancel = await client.PostAsJsonAsync($"/api/requisitions/{reqId}/cancel",
            new { reason = "x" });
        cancel.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
