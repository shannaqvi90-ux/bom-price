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

public class RequisitionsListBranchScopingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    // Ensures the test sees at least one requisition in the given branch,
    // regardless of test execution order or DB seed state. Without this guard,
    // the assertion below depends on whichever prior test happened to create
    // a branch-2 req — flaky in fresh CI DBs.
    private async Task EnsureRequisitionExistsInBranchAsync(int branchId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.QuotationRequests.AnyAsync(r => r.BranchId == branchId))
            return;

        var sp = await db.Users.FirstAsync(u => u.Email == "ali@test.com");
        var customer = await db.Customers.FirstAsync(c => c.SalesPersonId == sp.Id);
        var item = await db.Items.FirstAsync(i => i.BranchId == branchId);

        var req = new QuotationRequest
        {
            CustomerId = customer.Id,
            SalesPersonId = sp.Id,
            BranchId = branchId,
            Status = RequisitionStatus.BomPending,
            CreatedAt = DateTime.UtcNow
        };
        db.QuotationRequests.Add(req);
        await db.SaveChangesAsync();

        db.RequisitionItems.Add(new RequisitionItem
        {
            QuotationRequestId = req.Id,
            ItemId = item.Id,
            ExpectedQty = 100m
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Sara_AssignedToBothBranches_SeesBothBranchesReqs()
    {
        // Self-seed reqs in both branches to remove order dependence.
        await EnsureRequisitionExistsInBranchAsync(branchId: 1);
        await EnsureRequisitionExistsInBranchAsync(branchId: 2);

        var sara = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sara.AccessToken);

        var list = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions"))!;
        list.Select(r => r.BranchId).Distinct().Should().Contain(new[] { 1, 2 },
            "Sara is assigned to both branches via UserBranches");
    }

    [Fact]
    public async Task Accountant_AssignedToBranch1Only_DoesNotSeeBranch2Reqs()
    {
        // Create a branch-1-only Accountant via admin
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var email = $"acct1only-{Guid.NewGuid():N}"[..22] + "@test.com";
        var createUser = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "Branch1 Only Accountant",
            Email = email,
            Password = "Test@1234",
            Role = 3,
            BranchId = (int?)null
        });
        createUser.EnsureSuccessStatusCode();
        var created = (await createUser.Content.ReadFromJsonAsync<UserShort>())!;

        // Replace UserBranches: assign only to branch 1
        var setBranches = await _client.PutAsJsonAsync($"/api/users/{created.Id}/branches", new { BranchIds = new[] { 1 } });
        setBranches.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // List reqs as branch-1-only Accountant
        var login = await LoginAsync(email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var list = (await _client.GetFromJsonAsync<List<ReqShort>>("/api/requisitions"))!;
        list.Should().OnlyContain(r => r.BranchId == 1);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record UserShort(int Id, string Email, string Name, string Role);
    private record ReqShort(int Id, string RefNo, string Status, int BranchId);
}
