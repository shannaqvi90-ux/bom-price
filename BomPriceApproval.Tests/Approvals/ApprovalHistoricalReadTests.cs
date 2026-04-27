using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Approvals;

public class ApprovalHistoricalReadTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetReview_ReturnsCostsForApprovedRequisition()
    {
        var requisitionId = await _client.BootstrapApprovedRequisitionAsync();

        var mdToken = await _client.LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var resp = await _client.GetAsync($"/api/approvals/{requisitionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PartialApprovalReviewDto>();
        body!.Items.Should().HaveCount(1);
        body.Items[0].Cost.Should().NotBeNull();
        body.Items[0].Cost!.TotalCostPerKg.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetReview_ReturnsCostsForRejectedRequisition()
    {
        var requisitionId = await _client.BootstrapRejectedRequisitionAsync();

        var mdToken = await _client.LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var resp = await _client.GetAsync($"/api/approvals/{requisitionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PartialApprovalReviewDto>();
        body!.Items[0].Cost.Should().NotBeNull();
    }

    // ── Test-specific DTOs (kept here since only this file asserts on them) ──
    private record PartialApprovalReviewDto(string RefNo, string CustomerName, bool ReadyForReview, List<PartialApprovalItemDto> Items);
    private record PartialApprovalItemDto(int RequisitionItemId, PartialApprovalCostDto? Cost);
    private record PartialApprovalCostDto(decimal TotalCostPerKg, decimal MaterialCostPct, decimal LandedCostPct, decimal FohPct);
}
