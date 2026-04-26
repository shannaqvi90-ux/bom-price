using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

public class BranchHistoryReadTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    [Fact]
    public async Task GetBranchHistory_NoChanges_ReturnsEmptyList()
    {
        var sara = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sara.AccessToken);

        var reqs = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions?status=BomPending"))!;
        if (!reqs.Any()) return; // skip if no seed BomPending exists in fresh container
        var reqId = reqs.First().Id;

        var hist = (await _client.GetFromJsonAsync<List<HistoryEntry>>($"/api/requisitions/{reqId}/branch-history"))!;
        hist.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBranchHistory_AfterTwoChanges_OrdersDesc()
    {
        // Step 1: create a branch-2 SP + branch-2 item via admin
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var sp2Email = $"sp2-{Guid.NewGuid():N}"[..24] + "@test.com";
        var createSp2 = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "SP Branch2", Email = sp2Email, Password = "Test@1234",
            Role = 1 /* SalesPerson */, BranchId = 2
        });
        createSp2.EnsureSuccessStatusCode();

        // Step 2: SP2 creates a branch-2 item and a customer
        var sp2 = await LoginAsync(sp2Email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp2.AccessToken);

        var itemCode = $"FG2-{Guid.NewGuid():N}"[..12];
        var createItem2 = await _client.PostAsJsonAsync("/api/items", new
        {
            Code = itemCode, Description = "Test FG Branch2", Type = "FinishedGood"
        });
        createItem2.EnsureSuccessStatusCode();
        var item2 = (await createItem2.Content.ReadFromJsonAsync<ItemShort>())!;

        var custCode = $"C2-{Guid.NewGuid():N}"[..12];
        var createCust = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = custCode, Name = "Test Customer B2", Address = "Al Ain",
            Email = $"{custCode}@test.com", PhoneNumber = "+97199000001"
        });
        createCust.EnsureSuccessStatusCode();
        var cust = (await createCust.Content.ReadFromJsonAsync<CustomerShort>())!;

        // Step 3: SP2 creates req at branch 2 with branch-2 item
        var createReq = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 2,
            CustomerId = cust.Id,
            CurrencyCode = "AED",
            Items = new[] { new { ItemId = item2.Id, ExpectedQty = 1m } }
        });
        createReq.EnsureSuccessStatusCode();
        var reqId = (await createReq.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        // Step 4: SP2 adds branch-1 item to req (no branch restriction on which item to add)
        // Use admin token to fetch branch-1 items (SP2's branch filter would return only branch-2 items)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var b1Items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        var item1 = b1Items.First();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp2.AccessToken);
        var addItem = await _client.PostAsJsonAsync($"/api/requisitions/{reqId}/items", new
        {
            ItemId = item1.Id, ExpectedQty = 1m
        });
        addItem.EnsureSuccessStatusCode();

        // Step 5: SP2 removes branch-2 item → only branch-1 item remains
        var detail = (await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}"))!;
        var b2ReqItem = detail.Items.First(i => i.ItemId == item2.Id);
        var removeItem = await _client.DeleteAsync($"/api/requisitions/{reqId}/items/{b2ReqItem.Id}");
        removeItem.EnsureSuccessStatusCode();

        // Step 6: Admin does branch change 2→1 (items now match branch 1)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var p1 = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/branch", new { BranchId = 1, Reason = "first move" });
        p1.StatusCode.Should().Be(HttpStatusCode.NoContent, "branch 1 items match, change 2→1 should succeed");

        await Task.Delay(50);

        // Step 7: SP2 re-adds branch-2 item and SP1 (via SP2 token, who now can't manage branch-1 req)
        // — use admin-created SP1 (ali) instead. ali is SP at branch 1, q.BranchId=1 now.
        var ali = await LoginAsync("ali@test.com", "Test@1234");
        // ali can't add to this req (SalesPersonId != ali's userId). Use SP2 — now q.BranchId=1, sp2.BranchId=2 → forbidden.
        // Reuse admin PATCH branch back — but need items to match first.
        // Items still has only branch-1 item. We need to swap to branch-2 item for 1→2 change.
        // SP2 can still add items since the branch check for SP is SalesPersonId, not BranchId.
        // SP2 is SalesPersonId on this req, so SP2 can add/remove items regardless of BranchId.
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp2.AccessToken);
        var addItem2Again = await _client.PostAsJsonAsync($"/api/requisitions/{reqId}/items", new
        {
            ItemId = item2.Id, ExpectedQty = 1m
        });
        addItem2Again.EnsureSuccessStatusCode();

        // Remove branch-1 item → only branch-2 item remains
        var detail2 = (await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}"))!;
        var b1ReqItem = detail2.Items.First(i => i.ItemId == item1.Id);
        var removeItem2 = await _client.DeleteAsync($"/api/requisitions/{reqId}/items/{b1ReqItem.Id}");
        removeItem2.EnsureSuccessStatusCode();

        // Step 8: Admin does branch change 1→2 (items now match branch 2)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var p2 = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/branch", new { BranchId = 2, Reason = "second move" });
        p2.StatusCode.Should().Be(HttpStatusCode.NoContent, "branch 2 items match, change 1→2 should succeed");

        // Step 9: Assert history ordered newest-first
        var hist = (await _client.GetFromJsonAsync<List<HistoryEntry>>($"/api/requisitions/{reqId}/branch-history"))!;
        hist.Should().HaveCount(2);
        hist[0].ChangedAt.Should().BeAfter(hist[1].ChangedAt, "newest first");
        hist[0].NewBranchId.Should().Be(2);
        hist[1].NewBranchId.Should().Be(1);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record CustomerShort(int Id, string Name);
    private record ItemShort(int Id, string Code, string Description, string Type, int BranchId);
    private record CreateResponse(int Id, string RefNo);
    private record ReqShort(int Id, string RefNo, string Status, int BranchId);
    private record RequisitionItemShort(int Id, int ItemId);
    private record ReqDetail(int Id, int BranchId, List<RequisitionItemShort> Items);
    private record HistoryEntry(int Id, int OldBranchId, string OldBranchName, int NewBranchId, string NewBranchName, int ChangedByUserId, string ChangedByUserName, DateTime ChangedAt, string? Reason);
}
