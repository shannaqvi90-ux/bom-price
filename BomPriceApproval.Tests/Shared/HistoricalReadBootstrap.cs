using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BomPriceApproval.Tests.Shared;

/// <summary>
/// Shared bootstrap helpers for historical-read integration tests
/// (BomHistoricalReadTests + ApprovalHistoricalReadTests). Extracted to
/// remove ~110 lines of duplication. Use the extension methods on HttpClient.
/// </summary>
public static class HistoricalReadBootstrap
{
    public static async Task<string> LoginAsync(this HttpClient client, string email, string password)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    /// <summary>
    /// Drives a fresh requisition end-to-end up to MdReview status:
    /// SP creates items + requisition → BomCreator submits BOM → Accountant submits costing.
    /// Caller is responsible for clearing the auth header afterwards if needed.
    /// </summary>
    public static async Task<(int ReqId, int ItemId)> BootstrapThroughMdReviewAsync(this HttpClient client)
    {
        var spToken = await client.LoginAsync("ali@test.com", "Test@1234");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemDto>();

        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var rmResp = await client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "RM", Type = "RawMaterial", LastPurchasePrice = (decimal?)null });
        var rm = await rmResp.Content.ReadFromJsonAsync<ItemDto>();

        var customers = await client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        var reqResp = await client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fg!.Id, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();
        var requisitionId = created!.Id;

        var reqDetail = await client.GetFromJsonAsync<BootstrapRequisitionDetail>($"/api/requisitions/{requisitionId}");
        var riId = reqDetail!.Items[0].Id;

        var adminToken = await client.LoginAsync("admin@test.com", "Admin@1234");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var procResp = await client.PostAsJsonAsync("/api/processes",
            new { Name = $"P-{Guid.NewGuid():N}".Substring(0, 12), DisplayOrder = 1 });
        var proc = await procResp.Content.ReadFromJsonAsync<BootstrapProcess>();

        var bomToken = await client.LoginAsync("bob@test.com", "Test@1234");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);
        await client.PostAsync($"/api/bom/{requisitionId}/items/{riId}/start", null);
        await client.PutAsJsonAsync($"/api/bom/{requisitionId}/items/{riId}/lines", new
        {
            Lines = new[]
            {
                new { ProcessId = proc!.Id, RawMaterialItemId = rm!.Id, QtyPerKg = 0.85m, WastagePct = 2.0m }
            }
        });
        await client.PostAsync($"/api/bom/{requisitionId}/submit", null);

        var acctToken = await client.LoginAsync("sara@test.com", "Test@1234");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        var costing = await client.GetFromJsonAsync<BootstrapCostingReview>($"/api/costing/{requisitionId}");
        var bomLineId = costing!.Items.First(x => x.RequisitionItemId == riId).BomLines[0].BomLineId;
        await client.PostAsync($"/api/costing/{requisitionId}/items/{riId}/start", null);
        await client.PostAsJsonAsync($"/api/costing/{requisitionId}/items/{riId}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 1.00m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        client.DefaultRequestHeaders.Authorization = null;
        return (requisitionId, riId);
    }

    public static async Task<int> BootstrapApprovedRequisitionAsync(this HttpClient client)
    {
        var (reqId, riId) = await client.BootstrapThroughMdReviewAsync();
        var mdToken = await client.LoginAsync("md@test.com", "Test@1234");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);
        await client.PostAsJsonAsync($"/api/approvals/{reqId}/approve", new
        {
            Items = new[] { new { RequisitionItemId = riId, SalesPricePerKgAed = 2.50m } },
            Notes = (string?)null
        });
        client.DefaultRequestHeaders.Authorization = null;
        return reqId;
    }

    public static async Task<int> BootstrapRejectedRequisitionAsync(this HttpClient client)
    {
        var (reqId, _) = await client.BootstrapThroughMdReviewAsync();
        var mdToken = await client.LoginAsync("md@test.com", "Test@1234");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);
        await client.PostAsJsonAsync($"/api/approvals/{reqId}/reject", new
        {
            Notes = "Price too low"
        });
        client.DefaultRequestHeaders.Authorization = null;
        return reqId;
    }

    // ── Internal bootstrap-only DTOs ──
    private record BootstrapRequisitionItem(int Id, int ItemId, string ItemDescription, decimal ExpectedQty, int SortOrder);
    private record BootstrapRequisitionDetail(int Id, string RefNo, string Status, List<BootstrapRequisitionItem> Items);
    private record BootstrapProcess(int Id, string Name, int DisplayOrder, bool IsActive);
    private record BootstrapCostingBomLine(int BomLineId, int ProcessId, string ProcessName,
        int RawMaterialItemId, string RawMaterialDescription, decimal QtyPerKg, decimal WastagePct,
        object? LastCost);
    private record BootstrapCostingItem(int RequisitionItemId, int ItemId, string ItemDescription, decimal ExpectedQty,
        int? BomHeaderId, string CostStatus, object? Cost,
        List<BootstrapCostingBomLine> BomLines, object? Draft);
    private record BootstrapCostingReview(int RequisitionId, List<BootstrapCostingItem> Items);
}
