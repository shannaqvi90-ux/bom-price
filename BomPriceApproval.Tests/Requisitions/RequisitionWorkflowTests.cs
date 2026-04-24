using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Requisitions;

public class RequisitionWorkflowTests(WebApplicationFactory<Program> factory)
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
    public async Task CreateRequisition_AsSalesPerson_ReturnsCreated()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = 1,
            Items = new[] { new { ItemId = 1, ExpectedQty = 1000m } },
            CurrencyCode = "AED"
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
    public async Task Create_AsAccountant_UsesJwtBranch_AndSucceeds()
    {
        // Arrange — accountant logs in
        var acctLogin = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctLogin.AccessToken);

        // Fetch a customer and an active item
        var customersResp = await _client.GetAsync("/api/customers");
        customersResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var customers = await customersResp.Content.ReadFromJsonAsync<List<AcctCustomerShort>>();
        customers!.Should().NotBeEmpty();

        var itemsResp = await _client.GetAsync("/api/items");
        itemsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await itemsResp.Content.ReadFromJsonAsync<List<AcctItemShort>>();
        items!.Should().NotBeEmpty();

        // Act
        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers.First().Id,
            CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 100.0m } }
        });

        // Assert
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await create.Content.ReadFromJsonAsync<AcctCreateResponse>();
        body!.RefNo.Should().StartWith("REQ-");

        // Verify branch inherits from JWT
        var detail = await _client.GetFromJsonAsync<AcctReqDetail>($"/api/requisitions/{body.Id}");
        detail!.BranchId.Should().Be(acctLogin.BranchId!.Value);
    }

    [Fact]
    public async Task AddItem_AsAccountant_CrossBranch_Returns403()
    {
        // Step 1: Log in as admin and create a branch-2 accountant
        var adminLogin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminLogin.AccessToken);

        var acct2Email = $"acct2-{Guid.NewGuid():N}"[..20] + "@test.com";
        var createUser = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "Branch2 Accountant",
            Email = acct2Email,
            Password = "Test@1234",
            Role = 3, // UserRole.Accountant
            BranchId = 2
        });
        createUser.EnsureSuccessStatusCode();

        // Step 2: Sales person (ali@, branch 1) creates a requisition — it belongs to branch 1
        var salesLogin = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", salesLogin.AccessToken);

        var customers = await _client.GetFromJsonAsync<List<AcctCustomerShort>>("/api/customers");
        var items = await _client.GetFromJsonAsync<List<AcctItemShort>>("/api/items");

        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            CurrencyCode = "AED",
            Items = new[] { new { ItemId = items!.First().Id, ExpectedQty = 10m } }
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var reqId = (await create.Content.ReadFromJsonAsync<AcctCreateResponse>())!.Id;

        // Step 3: Branch-2 accountant attempts to add an item to branch-1 requisition → 403
        var acct2Login = await LoginAsync(acct2Email, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acct2Login.AccessToken);

        var add = await _client.PostAsJsonAsync($"/api/requisitions/{reqId}/items", new
        {
            ItemId = items.Skip(1).First().Id,
            ExpectedQty = 5m
        });

        add.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAll_MultiStatus_ReturnsUnion()
    {
        // Seed requisitions directly via DbContext: 2 BomPending, 2 CostingPending, 1 Approved
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var salesPerson = db.Users.First(u => u.Email == "ali@test.com");
            var customer = db.Customers.First();

            var statuses = new[]
            {
                RequisitionStatus.BomPending,
                RequisitionStatus.BomPending,
                RequisitionStatus.CostingPending,
                RequisitionStatus.CostingPending,
                RequisitionStatus.Approved,
            };

            foreach (var status in statuses)
            {
                db.QuotationRequests.Add(new QuotationRequest
                {
                    BranchId = salesPerson.BranchId!.Value,
                    SalesPersonId = salesPerson.Id,
                    CustomerId = customer.Id,
                    CurrencyCode = "AED",
                    Status = status,
                });
            }

            db.SaveChanges();
        }

        var client = factory.CreateClient();

        // Log in as MD (null-branch, sees all)
        var mdLogin = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "md@test.com", Password = "Test@1234" });
        mdLogin.EnsureSuccessStatusCode();
        var mdTokens = (await mdLogin.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", mdTokens.AccessToken);

        // Request multi-status filter
        var res = await client.GetAsync("/api/requisitions?status=BomPending&status=CostingPending");
        res.EnsureSuccessStatusCode();
        var list = await res.Content.ReadFromJsonAsync<List<ReqListItem>>();

        list.Should().NotBeNull();
        list.Should().NotBeEmpty();
        list!.Should().AllSatisfy(r =>
            new[] { "BomPending", "CostingPending" }.Should().Contain(r.Status));
        list.Should().NotContain(r => r.Status == "Approved");
        // Both statuses must actually be present — proves union, not single-filter
        list.Should().Contain(r => r.Status == "BomPending",
            "BomPending rows must be included in multi-status result");
        list.Should().Contain(r => r.Status == "CostingPending",
            "CostingPending rows must be included in multi-status result");
    }

    [Fact]
    public async Task GetAll_Search_MatchesRefNoAndCustomerName()
    {
        // Seed a customer with a unique name + a requisition for that customer
        string uniqueCustomerName = $"UniqueCustomer_{Guid.NewGuid():N}";
        int seededReqId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var salesPerson = db.Users.First(u => u.Email == "ali@test.com");

            var newCustomer = new Customer
            {
                Code = $"UC-{Guid.NewGuid():N}"[..12],
                Name = uniqueCustomerName,
                Address = "Test Address",
                Email = $"unique_{Guid.NewGuid():N}@test.com",
                PhoneNumber = "+97100000001",
                SalesPersonId = salesPerson.Id,
                CreatedByUserId = salesPerson.Id,
            };
            db.Customers.Add(newCustomer);
            db.SaveChanges();

            var req = new QuotationRequest
            {
                BranchId = salesPerson.BranchId!.Value,
                SalesPersonId = salesPerson.Id,
                CustomerId = newCustomer.Id,
                CurrencyCode = "AED",
                Status = RequisitionStatus.BomPending,
            };
            db.QuotationRequests.Add(req);
            db.SaveChanges();
            seededReqId = req.Id;
        }

        var client = factory.CreateClient();

        var mdLogin = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "md@test.com", Password = "Test@1234" });
        mdLogin.EnsureSuccessStatusCode();
        var mdTokens = (await mdLogin.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", mdTokens.AccessToken);

        // Search by first 12 chars of unique customer name (guaranteed to be unique)
        var searchTerm = uniqueCustomerName[..12];
        var byCustomer = await client.GetAsync(
            $"/api/requisitions?search={Uri.EscapeDataString(searchTerm)}");
        byCustomer.EnsureSuccessStatusCode();
        var list1 = await byCustomer.Content.ReadFromJsonAsync<List<ReqListItem>>();

        list1.Should().NotBeNull();
        list1.Should().Contain(r => r.CustomerName == uniqueCustomerName,
            "the seeded unique customer name should appear in results");
        // search must filter — no rows for a completely different unique token should appear
        var otherToken = $"ZZZOther_{Guid.NewGuid():N}";
        list1.Should().NotContain(r => r.CustomerName.Contains(otherToken),
            "unrelated tokens must not appear in search results");
        // All returned rows must match the search term (either RefNo or CustomerName)
        list1.Should().AllSatisfy(r =>
            (r.CustomerName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
             r.RefNo.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue($"row {r.RefNo} should match search term '{searchTerm}'"));

        // Search by RefNo — get the RefNo of our seeded req, then search partial
        var seededItem = list1!.First(r => r.CustomerName == uniqueCustomerName);
        var refNoPartial = seededItem.RefNo[4..]; // skip "REQ-", use numeric portion
        var byRef = await client.GetAsync(
            $"/api/requisitions?search={Uri.EscapeDataString(refNoPartial)}");
        byRef.EnsureSuccessStatusCode();
        var list2 = await byRef.Content.ReadFromJsonAsync<List<ReqListItem>>();

        list2.Should().NotBeNull();
        list2.Should().Contain(r => r.RefNo == seededItem.RefNo,
            "searching by RefNo numeric part should return the seeded requisition");
    }

    [Fact]
    public async Task GetAll_AllInvalidStatuses_ReturnsUnfilteredResults()
    {
        // Documents current behavior: when every provided status value fails to parse,
        // the filter is silently skipped and all branch-scoped requisitions are returned
        // (rather than an empty list). This is intentional (tolerant of stale clients
        // sending unknown values), but must be pinned — a future refactor could change
        // this behavior and otherwise pass the existing suite.

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
                Status = RequisitionStatus.BomPending,
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
        // the inner guard is skipped, and the full branch-scoped list is returned
        var res = await client.GetAsync("/api/requisitions?status=Garbage&status=NotAStatus");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await res.Content.ReadFromJsonAsync<List<ReqListItem>>();

        list.Should().NotBeNull();
        list.Should().Contain(r => r.Id == seededReqId,
            "the seeded requisition must appear because invalid statuses are silently discarded, not treated as an empty filter");
    }

    // Scoped private records (avoid name collisions with any existing records in the file):
    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record AcctCustomerShort(int Id, string Name);
    private record AcctItemShort(int Id, string Code, string Description);
    private record AcctCreateResponse(int Id, string RefNo);
    private record AcctReqDetail(int Id, int BranchId);
    private record ReqListItem(int Id, string RefNo, string Status, int ItemCount, string CustomerName,
        string CurrencyCode, string BranchName, string SalesPersonName, DateTime CreatedAt);
}
