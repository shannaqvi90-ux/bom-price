using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Bom;

public class BomSaveLinesTests(WebApplicationFactory<Program> factory)
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
    public async Task SaveLines_ReplacesLinesWithoutChangingStatus()
    {
        // 1. SalesPerson creates items and requisition
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "Test Finished Good", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        fgResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var finishedGood = await fgResp.Content.ReadFromJsonAsync<ItemDto>();

        var rmResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "Test Raw Material", Type = "RawMaterial", LastPurchasePrice = (decimal?)null });
        rmResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var rawMaterial = await rmResp.Content.ReadFromJsonAsync<ItemDto>();

        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        var customerId = customers!.First().Id;

        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = finishedGood!.Id, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });
        reqResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();
        var requisitionId = created!.Id;

        // Get requisitionItemId
        var reqDetail = await _client.GetFromJsonAsync<RequisitionDetailDto>($"/api/requisitions/{requisitionId}");
        var requisitionItemId = reqDetail!.Items[0].Id;

        // 2. Admin creates a process
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var processCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var processResp = await _client.PostAsJsonAsync("/api/processes",
            new { Name = processCode, DisplayOrder = 99 });
        processResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var process = await processResp.Content.ReadFromJsonAsync<ProcessDto>();

        // 3. BomCreator starts BOM for item (→ BomInProgress)
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);

        var startResp = await _client.PostAsync($"/api/bom/{requisitionId}/items/{requisitionItemId}/start", null);
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. BomCreator saves lines via PUT
        var saveResp = await _client.PutAsJsonAsync($"/api/bom/{requisitionId}/items/{requisitionItemId}/lines", new
        {
            Lines = new[]
            {
                new { ProcessId = process!.Id, RawMaterialItemId = rawMaterial!.Id, QtyPerKg = 0.85m, WastagePct = 2.0m }
            }
        });
        saveResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 5. Verify lines persisted and status still BomInProgress
        var bom = await _client.GetFromJsonAsync<BomReviewDto>($"/api/bom/{requisitionId}");
        bom!.Items.Should().HaveCount(1);
        bom.Items[0].Lines.Should().HaveCount(1);
        bom.Items[0].Lines[0].QtyPerKg.Should().Be(0.85m);

        // Requisition should still be BomInProgress (not transitioned)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);
        var req = await _client.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{requisitionId}");
        req!.Status.Should().Be("BomInProgress");
    }

    [Fact]
    public async Task SaveLines_Returns400_WhenStatusIsNotBomInProgress()
    {
        // SalesPerson creates requisition (BomPending — not started yet)
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "Test Finished Good", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        fgResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var finishedGood = await fgResp.Content.ReadFromJsonAsync<ItemDto>();

        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");

        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = finishedGood!.Id, ExpectedQty = 50m } },
            CurrencyCode = "AED"
        });
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();

        // Get requisitionItemId
        var reqDetail = await _client.GetFromJsonAsync<RequisitionDetailDto>($"/api/requisitions/{created!.Id}");
        var requisitionItemId = reqDetail!.Items[0].Id;

        // BomCreator tries to PUT lines without calling /start first
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);

        var saveResp = await _client.PutAsJsonAsync($"/api/bom/{created.Id}/items/{requisitionItemId}/lines",
            new { Lines = Array.Empty<object>() });
        saveResp.StatusCode.Should().Be(HttpStatusCode.BadRequest); // status is BomPending, not BomInProgress
    }

    [Fact]
    public async Task SaveLines_ZeroQty_Returns400()
    {
        // 1. SalesPerson creates items and requisition
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "Test Finished Good", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        fgResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var finishedGood = await fgResp.Content.ReadFromJsonAsync<ItemDto>();

        var rmResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "Test Raw Material", Type = "RawMaterial", LastPurchasePrice = (decimal?)null });
        rmResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var rawMaterial = await rmResp.Content.ReadFromJsonAsync<ItemDto>();

        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        var customerId = customers!.First().Id;

        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = finishedGood!.Id, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });
        reqResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();

        var reqDetail = await _client.GetFromJsonAsync<RequisitionDetailDto>($"/api/requisitions/{created!.Id}");
        var requisitionItemId = reqDetail!.Items[0].Id;

        // 2. Admin creates a process
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var processCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var processResp = await _client.PostAsJsonAsync("/api/processes",
            new { Name = processCode, DisplayOrder = 99 });
        processResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var process = await processResp.Content.ReadFromJsonAsync<ProcessDto>();

        // 3. BomCreator starts BOM for item (→ BomInProgress)
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);

        await _client.PostAsync($"/api/bom/{created.Id}/items/{requisitionItemId}/start", null);

        var saveResp = await _client.PutAsJsonAsync(
            $"/api/bom/{created.Id}/items/{requisitionItemId}/lines",
            new
            {
                Lines = new[]
                {
                    new { ProcessId = process!.Id, RawMaterialItemId = rawMaterial!.Id, QtyPerKg = 0m, WastagePct = 2.0m }
                }
            });

        saveResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await saveResp.Content.ReadFromJsonAsync<BomPriceApproval.Tests.Shared.ValidationProblemResponse>();
        body!.Detail.Should().Contain("QtyPerKg");
    }

    [Fact]
    public async Task SaveLines_NegativeWastage_Returns400()
    {
        // 1. SalesPerson creates items and requisition
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "Test Finished Good", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        fgResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var finishedGood = await fgResp.Content.ReadFromJsonAsync<ItemDto>();

        var rmResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "Test Raw Material", Type = "RawMaterial", LastPurchasePrice = (decimal?)null });
        rmResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var rawMaterial = await rmResp.Content.ReadFromJsonAsync<ItemDto>();

        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        var customerId = customers!.First().Id;

        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = finishedGood!.Id, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });
        reqResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();

        var reqDetail = await _client.GetFromJsonAsync<RequisitionDetailDto>($"/api/requisitions/{created!.Id}");
        var requisitionItemId = reqDetail!.Items[0].Id;

        // 2. Admin creates a process
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var processCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var processResp = await _client.PostAsJsonAsync("/api/processes",
            new { Name = processCode, DisplayOrder = 99 });
        processResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var process = await processResp.Content.ReadFromJsonAsync<ProcessDto>();

        // 3. BomCreator starts BOM for item (→ BomInProgress)
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);

        await _client.PostAsync($"/api/bom/{created.Id}/items/{requisitionItemId}/start", null);

        var saveResp = await _client.PutAsJsonAsync(
            $"/api/bom/{created.Id}/items/{requisitionItemId}/lines",
            new
            {
                Lines = new[]
                {
                    new { ProcessId = process!.Id, RawMaterialItemId = rawMaterial!.Id, QtyPerKg = 1m, WastagePct = -5m }
                }
            });

        saveResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await saveResp.Content.ReadFromJsonAsync<BomPriceApproval.Tests.Shared.ValidationProblemResponse>();
        body!.Detail.ToLower().Should().Contain("wastage");
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record ItemDto(int Id, string Code, string Description, string Type, int BranchId, bool IsActive, decimal? LastPurchasePrice);
    private record CustomerDto(int Id, string Code, string Name);
    private record CreatedRequisition(int Id, string RefNo);
    private record ProcessDto(int Id, string Name, int DisplayOrder, bool IsActive);
    private record RequisitionItemDto(int Id, int ItemId, string ItemDescription, decimal ExpectedQty, int SortOrder);
    private record RequisitionDetailDto(int Id, string RefNo, string Status, List<RequisitionItemDto> Items);
    private record RequisitionDto(int Id, string RefNo, string Status);
    private record BomLineDto(int Id, int ProcessId, string ProcessName, int RawMaterialItemId, string RawMaterialDescription, decimal QtyPerKg, decimal WastagePct);
    private record BomItemDto(int RequisitionItemId, int ItemId, string ItemDescription, decimal ExpectedQty, int SortOrder,
        int? BomHeaderId, string BomStatus, List<BomLineDto> Lines, decimal TotalCostPerKg, DateTime? SubmittedAt);
    private record BomReviewDto(int RequisitionId, string RefNo, string RequisitionStatus, List<BomItemDto> Items);
}
