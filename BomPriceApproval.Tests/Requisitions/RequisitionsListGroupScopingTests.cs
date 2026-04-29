using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Requisitions;

public class RequisitionsListGroupScopingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task<(int spId, string email)> CreateSpAsync(string namePrefix, int branchId)
    {
        var email = $"{namePrefix}-{Guid.NewGuid():N}".Substring(0, 22) + "@test.com";
        var resp = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = $"SP {namePrefix}", Email = email, Password = "Test@1234", Role = 1, BranchId = branchId
        });
        resp.EnsureSuccessStatusCode();
        var u = (await resp.Content.ReadFromJsonAsync<UserShort>())!;
        return (u.Id, email);
    }

    /// <summary>
    /// Direct DB seed of a Draft requisition for the given SP. V3 inline-BOM Create
    /// is heavyweight (process + RM + Alain pin); these tests assert visibility
    /// (SalesAuthorization) which is orthogonal, so a simple DB-direct seed is enough.
    /// </summary>
    private async Task<int> SeedDraftReqForSpAsync(int spUserId, int branchId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customer = new Customer
        {
            Code = $"GS-{Guid.NewGuid():N}".Substring(0, 14),
            Name = $"Group Scope Customer {Guid.NewGuid():N}".Substring(0, 30),
            Address = "",
            Email = "",
            PhoneNumber = "",
            SalesPersonId = spUserId,
            CreatedByUserId = spUserId
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var req = new QuotationRequest
        {
            BranchId = branchId,
            SalesPersonId = spUserId,
            CustomerId = customer.Id,
            CurrencyCode = "AED",
            Status = RequisitionStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.QuotationRequests.Add(req);
        await db.SaveChangesAsync();
        return req.Id;
    }

    [Fact]
    public async Task SP_NoGroup_OnlySeesOwnReqs()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var (spA_Id, spA_email) = await CreateSpAsync("noGrpA", 1);
        var (spB_Id, _) = await CreateSpAsync("noGrpB", 1);

        var spBReqId = await SeedDraftReqForSpAsync(spB_Id, branchId: 1);

        // SP A logs in — should NOT see B's req
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var list = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions"))!;
        list.Any(r => r.Id == spBReqId).Should().BeFalse("solo SP only sees own");
    }

    [Fact]
    public async Task SP_InGroupWithPeer_SeesPeersReqs()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        // Create group
        var grpResp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"GrpScope-{Guid.NewGuid():N}".Substring(0, 18) });
        grpResp.EnsureSuccessStatusCode();
        var grpId = (await grpResp.Content.ReadFromJsonAsync<GroupShort>())!.Id;

        // Create 2 SPs and put both in group
        var (spA_Id, spA_email) = await CreateSpAsync("grpA", 1);
        var (spB_Id, _) = await CreateSpAsync("grpB", 1);
        await _client.PutAsJsonAsync($"/api/users/{spA_Id}/group", new { GroupId = grpId });
        await _client.PutAsJsonAsync($"/api/users/{spB_Id}/group", new { GroupId = grpId });

        var spBReqId = await SeedDraftReqForSpAsync(spB_Id, branchId: 1);

        // SP A logs in — should now SEE B's req
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var list = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions"))!;
        list.Any(r => r.Id == spBReqId).Should().BeTrue("group peers share req visibility");
    }

    [Fact]
    public async Task SP_RemovedFromGroup_LosesPeerVisibility()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpResp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"GrpCut-{Guid.NewGuid():N}".Substring(0, 18) });
        grpResp.EnsureSuccessStatusCode();
        var grpId = (await grpResp.Content.ReadFromJsonAsync<GroupShort>())!.Id;

        var (spA_Id, spA_email) = await CreateSpAsync("cutA", 1);
        var (spB_Id, _) = await CreateSpAsync("cutB", 1);
        await _client.PutAsJsonAsync($"/api/users/{spA_Id}/group", new { GroupId = grpId });
        await _client.PutAsJsonAsync($"/api/users/{spB_Id}/group", new { GroupId = grpId });

        var spBReqId = await SeedDraftReqForSpAsync(spB_Id, branchId: 1);

        // Remove A from group
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        await _client.PutAsJsonAsync($"/api/users/{spA_Id}/group", new { GroupId = (int?)null });

        // SP A re-checks list — no longer sees B
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var list = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions"))!;
        list.Any(r => r.Id == spBReqId).Should().BeFalse("Q11 clean cut");
    }

    [Fact]
    public async Task SP_InGroupCrossBranch_SeesCrossBranchPeerReqs()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var grpResp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"Cross-{Guid.NewGuid():N}".Substring(0, 18) });
        grpResp.EnsureSuccessStatusCode();
        var grpId = (await grpResp.Content.ReadFromJsonAsync<GroupShort>())!.Id;

        var (spB1_Id, _) = await CreateSpAsync("br1", 1);
        var (spB2_Id, spB2_email) = await CreateSpAsync("br2", 2);
        await _client.PutAsJsonAsync($"/api/users/{spB1_Id}/group", new { GroupId = grpId });
        await _client.PutAsJsonAsync($"/api/users/{spB2_Id}/group", new { GroupId = grpId });

        var br1ReqId = await SeedDraftReqForSpAsync(spB1_Id, branchId: 1);

        // SP-branch2 logs in and should see branch-1 req via group
        var spB2 = await LoginAsync(spB2_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB2.AccessToken);
        var list = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions"))!;
        list.Any(r => r.Id == br1ReqId).Should().BeTrue("Q5 group is branch-agnostic");
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record UserShort(int Id, string Email, string Name, string Role);
    private record GroupShort(int Id, string Name, bool IsActive);
    private record ReqShort(int Id, string RefNo, string Status, int BranchId);
}
