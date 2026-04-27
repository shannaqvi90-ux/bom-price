using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Bom;

public class BomHistoricalReadTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetBom_ReturnsLinesForApprovedRequisition()
    {
        var requisitionId = await _client.BootstrapApprovedRequisitionAsync();

        var mdToken = await _client.LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var resp = await _client.GetAsync($"/api/bom/{requisitionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PartialBomReviewDto>();
        body!.RequisitionStatus.Should().Be("Approved");
        body.Items.Should().HaveCount(1);
        body.Items[0].Lines.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetBom_ReturnsLinesForRejectedRequisition()
    {
        var requisitionId = await _client.BootstrapRejectedRequisitionAsync();

        var mdToken = await _client.LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var resp = await _client.GetAsync($"/api/bom/{requisitionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PartialBomReviewDto>();
        body!.RequisitionStatus.Should().Be("Rejected");
        body.Items[0].Lines.Should().HaveCount(1);
    }

    // ── Test-specific DTOs (kept here since only this file asserts on them) ──
    private record PartialBomReviewDto(int RequisitionId, string RefNo, string RequisitionStatus, List<PartialBomItemDto> Items);
    private record PartialBomItemDto(int RequisitionItemId, string BomStatus, List<PartialBomLineDto> Lines);
    private record PartialBomLineDto(int Id, string ProcessName, decimal QtyPerKg, decimal WastagePct);
}
