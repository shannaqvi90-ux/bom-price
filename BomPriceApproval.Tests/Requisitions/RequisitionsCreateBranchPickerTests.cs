using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

public class RequisitionsCreateBranchPickerTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    [Fact]
    public async Task Create_AsSP_WithExplicitBranchId_PersistsThatBranch()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp.AccessToken);

        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=2&type=FinishedGood"))!;
        items.Should().NotBeEmpty("seed includes branch-2 finished goods");

        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 2,
            CustomerId = customers.First().Id,
            CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = (await create.Content.ReadFromJsonAsync<CreateResponse>())!;
        var detail = (await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{created.Id}"))!;
        detail.BranchId.Should().Be(2);
    }

    [Fact]
    public async Task Create_AsSP_WithItemsFromOtherBranch_Returns400()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp.AccessToken);

        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        var b1Items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;

        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            BranchId = 2,
            CustomerId = customers.First().Id,
            CurrencyCode = "AED",
            Items = new[] { new { ItemId = b1Items.First().Id, ExpectedQty = 1m } }
        });
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_AsSP_WithoutBranchId_TransitionFallbackUsesUserBranchId()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234"); // ali has BranchId = 1
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp.AccessToken);

        var customers = (await _client.GetFromJsonAsync<List<CustomerShort>>("/api/customers"))!;
        var items = (await _client.GetFromJsonAsync<List<ItemShort>>("/api/items?branchId=1&type=FinishedGood"))!;

        var create = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            // BranchId intentionally omitted
            CustomerId = customers.First().Id,
            CurrencyCode = "AED",
            Items = new[] { new { ItemId = items.First().Id, ExpectedQty = 1m } }
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = (await create.Content.ReadFromJsonAsync<CreateResponse>())!;
        var detail = (await _client.GetFromJsonAsync<ReqDetail>($"/api/requisitions/{created.Id}"))!;
        detail.BranchId.Should().Be(1, "fallback to ali's User.BranchId");
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record CustomerShort(int Id, string Name);
    private record ItemShort(int Id, string Code, string Description, string Type, int BranchId);
    private record CreateResponse(int Id, string RefNo);
    private record ReqDetail(int Id, int BranchId);
}
