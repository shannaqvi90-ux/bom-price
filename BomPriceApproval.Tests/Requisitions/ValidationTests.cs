using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Requisitions;

public class ValidationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private async Task<int> CreateActiveFinishedGoodAsync(string adminToken)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var code = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var resp = await _client.PostAsJsonAsync("/api/items",
            new { Code = code, Description = "Test FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var item = await resp.Content.ReadFromJsonAsync<ItemDto>();
        return item!.Id;
    }

    private async Task<int> GetCustomerIdAsync()
    {
        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        return customers!.First().Id;
    }

    [Fact]
    public async Task Create_DuplicateItemIds_Returns400()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemId = await CreateActiveFinishedGoodAsync(sp);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[]
            {
                new { ItemId = itemId, ExpectedQty = 1m },
                new { ItemId = itemId, ExpectedQty = 2m },
            },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Detail.Should().Contain("Duplicate");
    }

    [Fact]
    public async Task Create_ZeroQty_Returns400()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemId = await CreateActiveFinishedGoodAsync(sp);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemId, ExpectedQty = 0m } },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Detail.Should().Contain("ExpectedQty");
    }

    [Fact]
    public async Task Create_NegativeQty_Returns400()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemId = await CreateActiveFinishedGoodAsync(sp);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemId, ExpectedQty = -1m } },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Detail.Should().Contain("ExpectedQty");
    }

    [Fact]
    public async Task Create_NonExistentItem_Returns400()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = 999999, ExpectedQty = 1m } },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Detail.ToLower().Should().Contain("unknown");
    }

    [Fact]
    public async Task Create_InactiveItem_Returns400()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@1234");
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemId = await CreateActiveFinishedGoodAsync(sp);

        // Note: this test deactivates the item and does not reactivate it.
        // Each test creates its own item, so this tombstoning is safe.
        // Deactivate via admin
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        var patch = await _client.PatchAsJsonAsync($"/api/items/{itemId}/status",
            new { IsActive = false });
        patch.IsSuccessStatusCode.Should().BeTrue();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemId, ExpectedQty = 1m } },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Detail.ToLower().Should().Contain("inactive");
    }

    [Fact]
    public async Task AddItem_DuplicateItem_Returns400()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemA = await CreateActiveFinishedGoodAsync(sp);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var createResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemA, ExpectedQty = 1m } },
            CurrencyCode = "AED"
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<CreatedRequisition>();

        var addResp = await _client.PostAsJsonAsync(
            $"/api/requisitions/{created!.Id}/items",
            new { ItemId = itemA, ExpectedQty = 2m });

        addResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await addResp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Detail.ToLower().Should().Contain("already");
    }

    private record RaceTestRequisitionDetailItem(int Id, int ItemId);
    private record RaceTestRequisitionDetail(int Id, List<RaceTestRequisitionDetailItem> Items);

    [Fact]
    public async Task AddItem_ParallelDuplicate_RejectsOneRequest()
    {
        // Seed: create requisition with item A; create a second item B to add.
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemA = await CreateActiveFinishedGoodAsync(sp);
        var itemB = await CreateActiveFinishedGoodAsync(sp);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var createResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemA, ExpectedQty = 1m } },
            CurrencyCode = "AED"
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<CreatedRequisition>();

        var endpoint = $"/api/requisitions/{created!.Id}/items";
        var payload = new { ItemId = itemB, ExpectedQty = 2m };

        async Task<HttpResponseMessage> PostAdd()
        {
            var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sp);
            return await _client.SendAsync(req);
        }

        // Kick off two requests in parallel.
        var task1 = PostAdd();
        var task2 = PostAdd();
        var responses = await Task.WhenAll(task1, task2);

        // At least one succeeded (2xx), at least one rejected (400 or 500).
        var successCount = responses.Count(r => (int)r.StatusCode >= 200 && (int)r.StatusCode < 300);
        var failCount = responses.Length - successCount;
        successCount.Should().Be(1, "exactly one request must succeed under a duplicate-add race");
        failCount.Should().Be(1, "exactly one request must be rejected");

        // Verify DB state: after the race, requisition has exactly 2 items (itemA + itemB, not 3).
        var detailResp = await _client.GetAsync($"/api/requisitions/{created.Id}");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await detailResp.Content.ReadFromJsonAsync<RaceTestRequisitionDetail>();
        detail!.Items.Count.Should().Be(2);
        detail.Items.Count(i => i.ItemId == itemB).Should().Be(1);
    }

    [Fact]
    public async Task AddItem_ZeroQty_Returns400()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemA = await CreateActiveFinishedGoodAsync(sp);
        var itemB = await CreateActiveFinishedGoodAsync(sp);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var createResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemA, ExpectedQty = 1m } },
            CurrencyCode = "AED"
        });
        var created = await createResp.Content.ReadFromJsonAsync<CreatedRequisition>();

        var addResp = await _client.PostAsJsonAsync(
            $"/api/requisitions/{created!.Id}/items",
            new { ItemId = itemB, ExpectedQty = 0m });

        addResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await addResp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Detail.Should().Contain("ExpectedQty");
    }

    [Fact]
    public async Task ValidationProblemDetails_ShapeAndContentType_AreCorrect()
    {
        // This test indirectly verifies the Validation fluent builder by triggering
        // the existing zero-qty check (will be migrated to the builder in Task 2).
        // Before Task 2: the current response is { message: "..." } → this test FAILS.
        // After Task 2: the response is a full ProblemDetails → this test PASSES.

        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemId = await CreateActiveFinishedGoodAsync(sp);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemId, ExpectedQty = 0m } },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Detail.Should().Contain("ExpectedQty");
        body.Errors.Should().ContainKey("Items[0].ExpectedQty");
    }

    [Fact]
    public async Task Create_ZeroQty_EmitsPerFieldError()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemId = await CreateActiveFinishedGoodAsync(sp);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemId, ExpectedQty = 0m } },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Errors.Should().ContainKey("Items[0].ExpectedQty");
        body.Errors["Items[0].ExpectedQty"][0].Should().Contain("greater than 0");
    }
}
