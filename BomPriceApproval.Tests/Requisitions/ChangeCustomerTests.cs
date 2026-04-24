using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

public class ChangeCustomerTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    // Seeds a requisition at CostingPending. Returns reqId, original customer id, and a second customer id for swap.
    private async Task<(int ReqId, int OriginalCustomerId, int SwapCustomerId)> SeedRequisitionAtCostingPending()
    {
        // Sales creates req
        var sales = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sales.AccessToken);

        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items"))!;
        customers.Count.Should().BeGreaterThanOrEqualTo(2, "need two customers for swap");

        var original = customers[0];
        var swap = customers[1];

        var finishedGood = items.First(i => i.Type == "FinishedGood");
        var rawMaterial = items.First(i => i.Type == "RawMaterial");

        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = original.Id,
            CurrencyCode = "AED",
            Items = new[] { new { ItemId = finishedGood.Id, ExpectedQty = 10m } }
        });
        create.EnsureSuccessStatusCode();
        var reqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        // Fetch the requisition item id
        var detail = (await _client.GetFromJsonAsync<RequisitionDetailFull>($"/api/requisitions/{reqId}"))!;
        var reqItemId = detail.Items[0].Id;

        // Admin creates a process for BOM lines
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var processCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var processResp = await _client.PostAsJsonAsync("/api/processes", new { Name = processCode, DisplayOrder = 99 });
        processResp.EnsureSuccessStatusCode();
        var process = (await processResp.Content.ReadFromJsonAsync<ProcessShort>())!;

        // BomCreator walks BOM → CostingPending
        var bom = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bom.AccessToken);

        var startBom = await _client.PostAsync($"/api/bom/{reqId}/items/{reqItemId}/start", null);
        startBom.EnsureSuccessStatusCode();

        var saveBom = await _client.PutAsJsonAsync($"/api/bom/{reqId}/items/{reqItemId}/lines", new
        {
            Lines = new[] { new { ProcessId = process.Id, RawMaterialItemId = rawMaterial.Id, KgPerUnit = 1.5m, QtyPerKg = 1.5m, WastagePct = 2.0m } }
        });
        saveBom.EnsureSuccessStatusCode();

        var submitBom = await _client.PostAsync($"/api/bom/{reqId}/submit", null);
        submitBom.EnsureSuccessStatusCode();

        return (reqId, original.Id, swap.Id);
    }

    [Fact]
    public async Task ChangeCustomer_AsAccountant_InCostingPending_UpdatesCustomer()
    {
        var (reqId, _, swapId) = await SeedRequisitionAtCostingPending();

        var acct = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acct.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/customer", new
        {
            CustomerId = swapId,
            Reason = "Accountant correcting customer assignment"
        });

        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = (await _client.GetFromJsonAsync<RequisitionDetailFull>($"/api/requisitions/{reqId}"))!;
        detail.CustomerId.Should().Be(swapId);
    }

    [Fact]
    public async Task ChangeCustomer_SameCustomer_Returns400()
    {
        var (reqId, origId, _) = await SeedRequisitionAtCostingPending();

        var acct = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acct.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/customer", new
        {
            CustomerId = origId,
            Reason = (string?)null
        });

        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangeCustomer_NonExistentCustomer_Returns404()
    {
        var (reqId, _, _) = await SeedRequisitionAtCostingPending();

        var acct = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acct.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/customer", new
        {
            CustomerId = 999_999,
            Reason = (string?)null
        });

        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ChangeCustomer_NonExistentRequisition_Returns404()
    {
        var acct = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acct.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/999999/customer", new
        {
            CustomerId = 1,
            Reason = (string?)null
        });

        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ChangeCustomer_AsSales_Returns403()
    {
        var (reqId, _, swapId) = await SeedRequisitionAtCostingPending();

        var sales = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sales.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/customer", new
        {
            CustomerId = swapId,
            Reason = (string?)null
        });

        patch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChangeCustomer_OutsideCostingStates_Returns400()
    {
        // Sales creates req in BomPending — NOT in allowed costing states
        var sales = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sales.AccessToken);

        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items"))!;
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers.First().Id,
            CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First(i => i.Type == "FinishedGood").Id, ExpectedQty = 10m } }
        });
        create.EnsureSuccessStatusCode();
        var reqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        var acct = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acct.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/customer", new
        {
            CustomerId = customers[1].Id,
            Reason = (string?)null
        });

        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangeCustomer_AsAccountant_CrossBranch_Returns403()
    {
        // Seed a branch-1 requisition at CostingPending
        var (reqId, _, swapId) = await SeedRequisitionAtCostingPending();

        // Create a branch-2 accountant via admin
        var adminLogin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminLogin.AccessToken);

        var acct2Email = $"acct2cc-{Guid.NewGuid():N}"[..22] + "@test.com";
        var createUser = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "Branch2 Accountant CC",
            Email = acct2Email,
            Password = "Test@1234",
            Role = 3, // UserRole.Accountant
            BranchId = 2
        });
        createUser.EnsureSuccessStatusCode();

        // Branch-2 accountant attempts to change customer on branch-1 requisition → 403
        var acct2Login = await LoginAsync(acct2Email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acct2Login.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/customer", new
        {
            CustomerId = swapId,
            Reason = (string?)null
        });

        patch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetCustomerHistory_EmptyWhenNoChanges_Returns200()
    {
        var (reqId, _, _) = await SeedRequisitionAtCostingPending();

        var acct = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acct.AccessToken);

        var resp = await _client.GetAsync($"/api/requisitions/{reqId}/customer-history");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = (await resp.Content.ReadFromJsonAsync<List<HistoryEntry>>())!;
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCustomerHistory_AfterChange_ReturnsOneEntryWithDetails()
    {
        var (reqId, origId, swapId) = await SeedRequisitionAtCostingPending();

        var acct = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acct.AccessToken);

        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/customer", new
        {
            CustomerId = swapId,
            Reason = "Corrected subsidiary"
        });
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var history = (await _client.GetFromJsonAsync<List<HistoryEntry>>(
            $"/api/requisitions/{reqId}/customer-history"))!;

        history.Should().HaveCount(1);
        history[0].OldCustomerId.Should().Be(origId);
        history[0].NewCustomerId.Should().Be(swapId);
        history[0].ChangedByUserId.Should().Be(acct.UserId);
        history[0].Reason.Should().Be("Corrected subsidiary");
        history[0].OldCustomerName.Should().NotBeNullOrEmpty();
        history[0].NewCustomerName.Should().NotBeNullOrEmpty();
        history[0].ChangedByUserName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetCustomerHistory_OrdersEntriesDescendingByChangedAt()
    {
        var (reqId, _, swapId) = await SeedRequisitionAtCostingPending();

        var acct = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acct.AccessToken);

        // First change
        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        customers.Count.Should().BeGreaterThanOrEqualTo(3, "need three customers for two consecutive changes");
        var thirdId = customers[2].Id;

        var p1 = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/customer", new { CustomerId = swapId, Reason = (string?)null });
        p1.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Small delay to ensure distinct ChangedAt timestamps
        await Task.Delay(50);

        var p2 = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/customer", new { CustomerId = thirdId, Reason = (string?)null });
        p2.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var history = (await _client.GetFromJsonAsync<List<HistoryEntry>>(
            $"/api/requisitions/{reqId}/customer-history"))!;
        history.Should().HaveCount(2);
        history[0].ChangedAt.Should().BeAfter(history[1].ChangedAt, "newest first");
        history[0].NewCustomerId.Should().Be(thirdId);
        history[1].NewCustomerId.Should().Be(swapId);
    }

    // Private records
    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record CustomerShort(int Id, string Name);
    private record ItemShort(int Id, string Code, string Description, string Type);
    private record CreateResponse(int Id, string RefNo);
    private record ProcessShort(int Id, string Name);
    private record RequisitionItemShort(int Id, int ItemId, string ItemDescription, decimal ExpectedQty, int SortOrder);
    private record RequisitionDetailFull(int Id, int CustomerId, List<RequisitionItemShort> Items);
    private record HistoryEntry(
        int Id,
        int OldCustomerId, string OldCustomerName,
        int NewCustomerId, string NewCustomerName,
        int ChangedByUserId, string ChangedByUserName,
        DateTime ChangedAt, string? Reason);
}
