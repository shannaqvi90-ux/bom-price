using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Costing;

/// <summary>
/// Regression guard: when the Accountant submits costing for the LAST item of a
/// requisition, the requisition must auto-transition from <c>CostingInProgress</c> to
/// <c>MdReview</c>. The mobile V2.1 flow depends on this for its
/// "all items submitted → toast → pop to pending list" UX.
/// </summary>
public class CostingLastItemTransitionTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    [Fact]
    public async Task Costing_Submit_LastItem_TransitionsRequisitionToMdReview()
    {
        // Arrange: build a single-item requisition through to CostingPending.
        var (requisitionId, requisitionItemId) =
            await CostingTestFixture.CreateRequisitionWithBomInCostingPendingAsync(_client);

        // Act: Accountant starts costing, then submits.
        var accountantToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accountantToken);

        var startResp = await _client.PostAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/start", null);
        startResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Pull the costing review to grab the BOM line id we need to submit cost against.
        var review = await _client.GetFromJsonAsync<CostingReviewDto>($"/api/costing/{requisitionId}");
        var bomLineId = review!.Items.Single().BomLines.Single().BomLineId;

        var submitResp = await _client.PostAsJsonAsync(
            $"/api/costing/{requisitionId}/items/{requisitionItemId}/submit",
            new
            {
                RawMaterialCosts = new[]
                {
                    new { BomLineId = bomLineId, CostPerKg = 1.25m, CurrencyCode = "AED" }
                },
                LandedCostType = "Percentage",
                LandedCostValue = 0m,
                FohAmount = 0m,
            });
        submitResp.IsSuccessStatusCode.Should().BeTrue(
            $"submit should return a 2xx response (got {(int)submitResp.StatusCode})");

        // Assert: any authenticated role can read the requisition and see status MdReview.
        var mdToken = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var reqDetail = await _client.GetFromJsonAsync<RequisitionDetailDto>($"/api/requisitions/{requisitionId}");
        reqDetail!.Status.Should().Be("MdReview");
    }

    private record LoginResponse(string AccessToken, string RefreshToken);
    private record CostingReviewDto(int RequisitionId, List<CostingItemDto> Items);
    private record CostingItemDto(int RequisitionItemId, List<BomLineDto> BomLines);
    private record BomLineDto(int BomLineId);
    private record RequisitionDetailDto(int Id, string Status);
}
