using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Notifications;

public class SalesGroupNotificationRoutingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    [Fact]
    public async Task ReqProgression_NotifiesOnlyOriginalSP_NotPeerInGroup()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        // Create group with 2 SPs (use ali for one + a fresh second SP)
        var grpResp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"NotifGrp-{Guid.NewGuid():N}".Substring(0, 18) });
        var grpId = (await grpResp.Content.ReadFromJsonAsync<GroupShort>())!.Id;

        var users = (await _client.GetFromJsonAsync<List<UserShort>>("/api/users"))!;
        var ali = users.First(u => u.Email == "ali@test.com");
        await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = grpId });

        var peerEmail = $"peer-{Guid.NewGuid():N}".Substring(0, 22) + "@test.com";
        var peerResp = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "Peer SP", Email = peerEmail, Password = "Test@1234", Role = 1, BranchId = 1
        });
        var peer = (await peerResp.Content.ReadFromJsonAsync<UserShort>())!;
        await _client.PutAsJsonAsync($"/api/users/{peer.Id}/group", new { GroupId = grpId });

        // Ali creates a req
        var aliLogin = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliLogin.AccessToken);
        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 1, CustomerId = customers.First().Id, CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        var reqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;

        // Status fan-out happens on req creation: BomCreators get notif. We're checking that the PEER SP doesn't get any req notif.
        // The peer should have ZERO notifs about this req.
        async Task<int> CountForReq(string email, string password)
        {
            var login = await LoginAsync(email, password);
            using var c = factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
            var notifs = (await c.GetFromJsonAsync<List<NotifShort>>("/api/notifications"))!;
            return notifs.Count(n => n.ReferenceType == "QuotationRequest" && n.ReferenceId == reqId);
        }

        (await CountForReq(peerEmail, "Test@1234")).Should().Be(0, "peer in same group should NOT receive req-progression notifs");

        // Cleanup
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        await _client.PutAsJsonAsync($"/api/users/{ali.Id}/group", new { GroupId = (int?)null });
        await _client.PutAsJsonAsync($"/api/users/{peer.Id}/group", new { GroupId = (int?)null });
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record GroupShort(int Id, string Name, bool IsActive);
    private record UserShort(int Id, string Email, string Name, string Role);
    private record CustomerShort(int Id, string Name);
    private record ItemShort(int Id, string Code, string Description, string Type, int BranchId);
    private record CreateResponse(int Id, string RefNo);
    private record NotifShort(int Id, string Message, string ReferenceType, int? ReferenceId);
}
