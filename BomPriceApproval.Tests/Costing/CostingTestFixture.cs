using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BomPriceApproval.Tests.Costing;

/// <summary>
/// Shared helper for tests that need a requisition seeded through to <c>CostingPending</c>.
/// Exposed as a <c>static</c> method so multiple test fixtures can call it without inheritance
/// or class-level coupling. The body mirrors
/// <c>CostingTests.CreateRequisitionWithBomInCostingPendingAsync</c> exactly.
/// </summary>
internal static class CostingTestFixture
{
    public static async Task<(int RequisitionId, int RequisitionItemId)>
        CreateRequisitionWithBomInCostingPendingAsync(HttpClient client, string quoteCurrency = "AED")
    {
        // 1. SalesPerson creates items + requisition
        var spToken = await LoginAsync(client, "ali@test.com", "Test@1234");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "Test FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemDto>();
        var rmResp = await client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "Test RM", Type = "RawMaterial", LastPurchasePrice = (decimal?)null });
        var rm = await rmResp.Content.ReadFromJsonAsync<ItemDto>();

        var customers = await client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        var reqResp = await client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fg!.Id, ExpectedQty = 100m } },
            CurrencyCode = quoteCurrency
        });
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();
        var requisitionId = created!.Id;

        // Get requisitionItemId
        var reqDetail = await client.GetFromJsonAsync<RequisitionDetailDto>($"/api/requisitions/{requisitionId}");
        var requisitionItemId = reqDetail!.Items[0].Id;

        // 2. Admin creates a process
        var adminToken = await LoginAsync(client, "admin@test.com", "Admin@1234");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var procCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await client.PostAsJsonAsync("/api/processes", new { Name = procCode, DisplayOrder = 99 });
        var process = await procResp.Content.ReadFromJsonAsync<ProcessDto>();

        // 3. BomCreator starts, saves lines, submits BOM → CostingPending
        var bomToken = await LoginAsync(client, "bob@test.com", "Test@1234");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);
        await client.PostAsync($"/api/bom/{requisitionId}/items/{requisitionItemId}/start", null);
        await client.PutAsJsonAsync($"/api/bom/{requisitionId}/items/{requisitionItemId}/lines", new
        {
            Lines = new[]
            {
                new { ProcessId = process!.Id, RawMaterialItemId = rm!.Id, QtyPerKg = 0.85m, WastagePct = 2.0m }
            }
        });
        await client.PostAsync($"/api/bom/{requisitionId}/submit", null);

        client.DefaultRequestHeaders.Authorization = null;
        return (requisitionId, requisitionItemId);
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private record ItemDto(int Id);
    private record CustomerDto(int Id);
    private record ProcessDto(int Id);
    private record CreatedRequisition(int Id);
    private record RequisitionDetailDto(int Id, List<ItemDto> Items);
    private record LoginResponse(string AccessToken);
}
