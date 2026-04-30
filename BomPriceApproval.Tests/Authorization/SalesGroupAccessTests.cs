using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.Tests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Authorization;

public class SalesGroupAccessTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task<(int spId, string email)> CreateSpAsync(string namePrefix, int branchId = 1)
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

    private async Task<int> SetupGroupAsync(string namePrefix, int[] spIds)
    {
        var grpResp = await _client.PostAsJsonAsync("/api/groups", new { Name = $"{namePrefix}-{Guid.NewGuid():N}".Substring(0, 18) });
        grpResp.EnsureSuccessStatusCode();
        var grpId = (await grpResp.Content.ReadFromJsonAsync<GroupShort>())!.Id;
        foreach (var id in spIds)
            await _client.PutAsJsonAsync($"/api/users/{id}/group", new { GroupId = grpId });
        return grpId;
    }

    /// <summary>Creates a customer while authenticated as the current user (must already have auth header set).</summary>
    private async Task<int> CreateCustomerAsync(string prefix)
    {
        var resp = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = $"CUST-{Guid.NewGuid():N}".Substring(0, 12),
            Name = $"Customer {prefix}",
            Address = "",
            Email = "",
            PhoneNumber = ""
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CustShort>())!.Id;
    }

    /// <summary>
    /// Direct DB seed of a Draft requisition for the given SP. V3 inline-BOM Create
    /// requires Alain branch + process + RM/FG; visibility tests only need an existing
    /// req row, so we seed directly.
    /// </summary>
    private async Task<int> SeedDraftReqForSpAsync(int spUserId, int customerId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sp = await db.Users.FirstAsync(u => u.Id == spUserId);
        var req = new QuotationRequest
        {
            BranchId = sp.BranchId ?? 1,
            SalesPersonId = spUserId,
            CustomerId = customerId,
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
    public async Task GroupMember_CanGet_PeerReqDetail()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var (spA_Id, spA_email) = await CreateSpAsync("acA");
        var (spB_Id, spB_email) = await CreateSpAsync("acB");
        await SetupGroupAsync("acGrp", new[] { spA_Id, spB_Id });

        // SP B creates a customer
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var custId = await CreateCustomerAsync("acB");

        var spBReqId = await SeedDraftReqForSpAsync(spB_Id, custId);

        // SP A GETs B's req detail — should succeed (group-mate)
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var get = await _client.GetAsync($"/api/requisitions/{spBReqId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NonGroupSP_CannotGet_OtherSPReqDetail_Returns403or404()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var (spA_Id, spA_email) = await CreateSpAsync("ngA");
        var (spB_Id, spB_email) = await CreateSpAsync("ngB");
        // No group — both solo

        // SP B creates a customer
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var custId = await CreateCustomerAsync("ngB");

        var spBReqId = await SeedDraftReqForSpAsync(spB_Id, custId);

        // SP A (solo, different SP) tries to GET B's req — should be 403 or 404
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var get = await _client.GetAsync($"/api/requisitions/{spBReqId}");
        get.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GroupMember_CanCreateReq_AgainstPeerCustomer()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        // Both SPs in branch 1 — V3 pins to Alain anyway
        var (spA_Id, spA_email) = await CreateSpAsync("crA");
        var (spB_Id, spB_email) = await CreateSpAsync("crB");
        await SetupGroupAsync("crGrp", new[] { spA_Id, spB_Id });

        // SP B creates a customer
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var custId = await CreateCustomerAsync("crB");

        // Seed an Alain FG + RM + ensure a process exists for the V3 inline payload
        var (fgId, rmId, processId) = await SeedAlainItemsAndProcessAsync();

        // SP A creates a V3 req against B's customer — should succeed
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            customerId = custId,
            quotationCurrency = "AED",
            finishedGoods = new[]
            {
                new
                {
                    itemId = fgId,
                    expectedQtyKg = 1m,
                    printing = false,
                    bomLines = new[]
                    {
                        new { processId, itemId = rmId, qtyPerKg = 0.5m, micron = "20" }
                    }
                }
            }
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        // The new req has SalesPersonId = SP A (not SP B)
        var reqId = (await create.Content.ReadFromJsonAsync<CreateResponse>())!.Id;
        // V3 GET shape exposes salesPerson as a nested { id, name } summary.
        var detail = (await _client.GetFromJsonAsync<JsonElement>($"/api/requisitions/{reqId}"));
        detail.GetProperty("salesPerson").GetProperty("id").GetInt32().Should().Be(spA_Id);
    }

    [Fact]
    public async Task GroupMember_CountMatchesGetAll_ForPeerReqs()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var (spA_Id, spA_email) = await CreateSpAsync("cmA");
        var (spB_Id, spB_email) = await CreateSpAsync("cmB");
        await SetupGroupAsync("cmGrp", new[] { spA_Id, spB_Id });

        // SP B creates a customer
        var spB = await LoginAsync(spB_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spB.AccessToken);
        var custId = await CreateCustomerAsync("cmB");

        // Seed 2 reqs for SP B directly via DB
        await SeedDraftReqForSpAsync(spB_Id, custId);
        await SeedDraftReqForSpAsync(spB_Id, custId);

        // SP A logs in — Count must match GetAll length, and must be > 0
        var spA = await LoginAsync(spA_email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spA.AccessToken);
        var countResp = (await _client.GetFromJsonAsync<CountResponse>("/api/requisitions/count"))!;
        var listResp = (await _client.GetFromJsonAsync<List<ReqListItem>>("/api/requisitions"))!;

        listResp.Count.Should().BeGreaterThan(0, "SP A should see SP B's reqs via group membership");
        countResp.Count.Should().Be(listResp.Count, "Count endpoint should match GetAll list length");
    }

    private async Task<(int FgId, int RmId, int ProcessId)> SeedAlainItemsAndProcessAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        var fg = new Item { Code = $"V3FG-{suffix}", Description = $"V3 FG {suffix}", Type = ItemType.FinishedGood, BranchId = V3WorkflowTestHelpers.AlainBranchId, IsActive = true };
        var rm = new Item { Code = $"V3RM-{suffix}", Description = $"V3 RM {suffix}", Type = ItemType.RawMaterial, BranchId = V3WorkflowTestHelpers.AlainBranchId, IsActive = true };
        db.Items.Add(fg);
        db.Items.Add(rm);
        var process = await db.Processes.FirstOrDefaultAsync(p => p.IsActive);
        if (process is null)
        {
            process = new Process { Name = $"Extrusion-{suffix}", DisplayOrder = 1, IsActive = true };
            db.Processes.Add(process);
        }
        await db.SaveChangesAsync();
        return (fg.Id, rm.Id, process.Id);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record UserShort(int Id, string Email, string Name, string Role);
    private record GroupShort(int Id, string Name, bool IsActive);
    private record CustShort(int Id, string Code, string Name);
    private record CreateResponse(int Id, string RefNo);
    private record CountResponse(int Count);
    private record ReqListItem(int Id, string RefNo);
}
