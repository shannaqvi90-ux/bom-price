using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Notifications;

public class NotificationCascadeOnBranchChangeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    [Fact]
    public async Task BranchChange_NotifiesSP_BothBranchesBomCreatorAndAccountant_AllMDs()
    {
        // Step 1: Seed a branch-2 FinishedGood item directly via DbContext
        int branch2ItemId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = new Item
            {
                Code = $"B2-NOTIF-{Guid.NewGuid():N}"[..18],
                Description = "Branch-2 test item for notification cascade",
                Type = ItemType.FinishedGood,
                BranchId = 2,
                IsActive = true
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            branch2ItemId = item.Id;
        }

        // Step 2: Ali (SP, branch-1) creates a branch-1 req with a branch-1 item
        var sales = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sales.AccessToken);

        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        var b1Items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        b1Items.Should().NotBeEmpty("branch-1 seed items must exist");

        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1,
            CustomerId = customers.First().Id,
            CurrencyCode = "AED",
            Items = new[] { new { ItemId = b1Items.First().Id, ExpectedQty = 1m } }
        });
        create.EnsureSuccessStatusCode();
        var reqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        // Step 3: Sara (Accountant, assigned to both branches) adds the branch-2 item
        // AddItem does NOT validate item-branch membership — only checks item exists + active
        var sara = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sara.AccessToken);

        var addItem = await _client.PostAsJsonAsync($"/api/requisitions/{reqId}/items",
            new { ItemId = branch2ItemId, ExpectedQty = 1m });
        addItem.EnsureSuccessStatusCode();

        // Step 4: Sara removes the branch-1 item (count is now 2 — last-item rule doesn't apply)
        // Item removal is only allowed in BomPending status, so do this before advancing status.
        var detail = (await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{reqId}"))!;
        var branch1Item = detail.Items.First(i => i.ItemId == b1Items.First().Id);
        var branch2ReqItem = detail.Items.First(i => i.ItemId == branch2ItemId);
        var removeItem = await _client.DeleteAsync($"/api/requisitions/{reqId}/items/{branch1Item.Id}");
        removeItem.EnsureSuccessStatusCode();

        // Step 5: Bob (BomCreator, branch-1) starts BOM on the remaining item → advances to BomInProgress.
        // This prevents the req from appearing in ?status=BomPending queries in other tests.
        // Branch change is allowed in BomInProgress (controller allows BomPending/BomInProgress/CostingPending).
        var bob = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bob.AccessToken);
        var startBom = await _client.PostAsync($"/api/bom/{reqId}/items/{branch2ReqItem.Id}/start", null);
        startBom.EnsureSuccessStatusCode();

        // Step 6: Sara patches branch 1 → 2 (all remaining items belong to branch-2 — passes validation)
        // Re-set Sara's token — _client auth was overwritten by bob's login in step 5.
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sara.AccessToken);
        var patch = await _client.PatchAsJsonAsync($"/api/requisitions/{reqId}/branch",
            new { BranchId = 2, Reason = "test cascade" });
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent, "branch change should succeed when all items match target branch");

        // Step 6: Verify each expected recipient received a notification
        async Task<int> CountForReq(string email, string password)
        {
            var login = await LoginAsync(email, password);
            using var c = factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
            var notifs = (await c.GetFromJsonAsync<List<NotifShort>>("/api/notifications"))!;
            return notifs.Count(n => n.ReferenceType == "QuotationRequest" && n.ReferenceId == reqId && n.Message.Contains("Branch"));
        }

        (await CountForReq("ali@test.com", "Test@1234")).Should().BeGreaterThan(0, "SP (ali) must receive branch change notification");
        (await CountForReq("md@test.com", "Test@1234")).Should().BeGreaterThan(0, "MD must receive branch change notification");
        (await CountForReq("bob@test.com", "Test@1234")).Should().BeGreaterThan(0, "old-branch BomCreator (bob, branch 1) must receive notification");
        (await CountForReq("sara@test.com", "Test@1234")).Should().BeGreaterThan(0, "Accountant (sara) assigned to both branches must receive notification");
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record CustomerShort(int Id, string Name);
    private record ItemShort(int Id, string Code, string Description, string Type, int BranchId);
    private record CreateResponse(int Id, string RefNo);
    private record RequisitionItemShort(int Id, int ItemId);
    private record ReqDetail(int Id, int BranchId, List<RequisitionItemShort> Items);
    private record NotifShort(int Id, string Message, string ReferenceType, int? ReferenceId);
}
