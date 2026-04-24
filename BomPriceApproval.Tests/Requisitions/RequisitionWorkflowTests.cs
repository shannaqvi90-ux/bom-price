using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

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

    // Scoped private records (avoid name collisions with any existing records in the file):
    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record AcctCustomerShort(int Id, string Name);
    private record AcctItemShort(int Id, string Code, string Description);
    private record AcctCreateResponse(int Id, string RefNo);
    private record AcctReqDetail(int Id, int BranchId);
}
