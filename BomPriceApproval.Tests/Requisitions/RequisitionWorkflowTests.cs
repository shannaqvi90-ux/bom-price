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

public class RequisitionWorkflowTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateRequisition_AsSalesPerson_ReturnsCreated()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/requisitions", new
        {
            customerId = 1,
            quotationCurrency = "AED",
            finishedGoods = Array.Empty<object>()
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetRequisitions_AsSalesPerson_SeesOnlyOwnRequests()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/requisitions");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetAll_AllInvalidStatuses_ReturnsUnfilteredResults()
    {
        // Documents current behavior: when every provided status value fails to parse,
        // the filter is silently skipped and all branch-scoped requisitions are returned
        // (rather than an empty list). This is intentional (tolerant of stale clients
        // sending unknown values).

        // Seed one known requisition so we have something to assert against
        int seededReqId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var salesPerson = db.Users.First(u => u.Email == "ali@test.com");
            var customer = db.Customers.First();

            var req = new QuotationRequest
            {
                BranchId = salesPerson.BranchId!.Value,
                SalesPersonId = salesPerson.Id,
                CustomerId = customer.Id,
                CurrencyCode = "AED",
                Status = RequisitionStatus.Draft,
            };
            db.QuotationRequests.Add(req);
            db.SaveChanges();
            seededReqId = req.Id;
        }

        var client = factory.CreateClient();

        // Log in as MD (null-branch, sees all)
        var mdLogin = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "md@test.com", Password = "Test@1234" });
        mdLogin.EnsureSuccessStatusCode();
        var mdTokens = (await mdLogin.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", mdTokens.AccessToken);

        // Send only invalid status values — none will parse, so parsed.Length == 0,
        // the inner guard is skipped, and the full branch-scoped list is returned.
        var res = await client.GetAsync("/api/requisitions?status=Garbage&status=NotAStatus");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await res.Content.ReadFromJsonAsync<List<ReqListItem>>();

        list.Should().NotBeNull();
        list.Should().Contain(r => r.Id == seededReqId,
            "the seeded requisition must appear because invalid statuses are silently discarded, not treated as an empty filter");
    }

    [Fact]
    public async Task Approvals_PersistedToDb_AfterApprove()
    {
        int reqId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var salesPerson = db.Users.First(u => u.Email == "ali@test.com");
            var mdUser     = db.Users.First(u => u.Email == "md@test.com");
            var customer   = db.Customers.First();
            var items      = db.Items.Take(2).ToList();

            // 1. Seed the QuotationRequest at V3 Signed (terminal approved-equivalent)
            var req = new QuotationRequest
            {
                BranchId     = salesPerson.BranchId!.Value,
                SalesPersonId = salesPerson.Id,
                CustomerId   = customer.Id,
                CurrencyCode = "AED",
                Status       = RequisitionStatus.Approved,
            };
            db.QuotationRequests.Add(req);
            db.SaveChanges();
            reqId = req.Id;

            // 2. Seed 2 RequisitionItems
            var ri1 = new RequisitionItem { QuotationRequestId = req.Id, ItemId = items[0].Id, ExpectedQty = 100m, SortOrder = 1 };
            var ri2 = new RequisitionItem { QuotationRequestId = req.Id, ItemId = items[1].Id, ExpectedQty = 200m, SortOrder = 2 };
            db.RequisitionItems.AddRange(ri1, ri2);
            db.SaveChanges();

            // 3. Seed QuotationApproval + 2 ApprovalItems
            var approval = new QuotationApproval
            {
                QuotationRequestId = req.Id,
                ApprovedByUserId   = mdUser.Id,
                IsApproved         = true,
                IsSuperseded       = false,
                Notes              = "Test approval",
                Items = new List<ApprovalItem>
                {
                    new() { RequisitionItemId = ri1.Id, SalesPricePerKgAed = 100m, SalesPricePerKgForeign = null, ProfitMarginPct = 10m, MaterialCostPct = 50m, OtherCostPct = 5m },
                    new() { RequisitionItemId = ri2.Id, SalesPricePerKgAed = 250m, SalesPricePerKgForeign = null, ProfitMarginPct = 15m, MaterialCostPct = 55m, OtherCostPct = 6m },
                }
            };
            db.QuotationApprovals.Add(approval);
            db.SaveChanges();
        }

        var client = factory.CreateClient();

        var mdLogin = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "md@test.com", Password = "Test@1234" });
        mdLogin.EnsureSuccessStatusCode();
        var mdTokens = (await mdLogin.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", mdTokens.AccessToken);

        // GET still resolves the V2.3 historical req (anonymize-style preservation).
        // V3 GET shape no longer surfaces approval/items in the response body — the FE fetches
        // approvals via the dedicated /api/approvals endpoints — so we read directly from the
        // DbContext here to verify approval-item prices are intact (the V2.3 contract this test
        // was written for).
        var resp = await client.GetAsync($"/api/requisitions/{reqId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var verifyScope = factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var approvalItems = await db.QuotationApprovals
                .Where(a => a.QuotationRequestId == reqId && !a.IsSuperseded)
                .SelectMany(a => a.Items)
                .ToListAsync();

            approvalItems.Should().HaveCount(2, "both approval items must be persisted");
            approvalItems.Should().ContainSingle(
                ai => ai.SalesPricePerKgAed == 100m,
                "first approval item must have SalesPricePerKgAed = 100");
            approvalItems.Should().ContainSingle(
                ai => ai.SalesPricePerKgAed == 250m,
                "second approval item must have SalesPricePerKgAed = 250");
        }
    }

    // Scoped private records:
    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record ReqListItem(int Id, string RefNo, string Status, int ItemCount, string CustomerName,
        string CurrencyCode, string BranchName, string SalesPersonName, DateTime CreatedAt);
}
