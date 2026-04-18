using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using BomPriceApproval.Tests.Shared;

namespace BomPriceApproval.Tests.Requisitions;

public class ResubmitTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private record AuthLoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record ItemMin(int Id, string Code, string Description, string Type);
    private record CustomerMin(int Id, string Name);
    private record ProcessMin(int Id, string Name, int DisplayOrder, bool IsActive);
    private record CreatedReq(int Id, string RefNo);
    private record RiMin(int Id, int ItemId);
    private record ReqDetailMin(int Id, string RefNo, string Status, List<RiMin> Items);
    private record BomLineMin(int Id);
    private record BomItemMin(int RequisitionItemId, List<BomLineMin> Lines);
    private record BomReviewMin(int RequisitionId, string RefNo, string RequisitionStatus, List<BomItemMin> Items);
    private record ApprovalSummaryMin(bool IsApproved, string? Notes, DateTime ApprovedAt);
    private record ReqDetailWithApproval(int Id, string RefNo, string Status, ApprovalSummaryMin? Approval);
    private record CreatedUser(int Id);

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<AuthLoginResponse>();
        return body!.AccessToken;
    }

    private void UseToken(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<int> CreateActiveFinishedGoodAsync()
    {
        var code = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var resp = await _client.PostAsJsonAsync("/api/items",
            new { Code = code, Description = "Test FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        resp.EnsureSuccessStatusCode();
        var item = await resp.Content.ReadFromJsonAsync<ItemMin>();
        return item!.Id;
    }

    private async Task<int> CreateActiveRawMaterialAsync()
    {
        var code = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var resp = await _client.PostAsJsonAsync("/api/items",
            new { Code = code, Description = "Test RM", Type = "RawMaterial", LastPurchasePrice = 5m });
        resp.EnsureSuccessStatusCode();
        var item = await resp.Content.ReadFromJsonAsync<ItemMin>();
        return item!.Id;
    }

    private async Task<int> EnsureProcessAsync()
    {
        var processes = await _client.GetFromJsonAsync<List<ProcessMin>>("/api/processes");
        if (processes is { Count: > 0 }) return processes[0].Id;
        var code = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var resp = await _client.PostAsJsonAsync("/api/processes",
            new { Name = code, DisplayOrder = 99 });
        resp.EnsureSuccessStatusCode();
        var p = await resp.Content.ReadFromJsonAsync<ProcessMin>();
        return p!.Id;
    }

    private async Task<(string spToken, int reqId)> CreateRejectedRequisitionAsync(string notes)
    {
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        var mdToken = await LoginAsync("md@test.com", "Test@1234");

        // SalesPerson has a branch — use spToken for item creation (admin has null BranchId and is rejected by POST /api/items)
        UseToken(spToken);
        var fgId = await CreateActiveFinishedGoodAsync();
        var rmId = await CreateActiveRawMaterialAsync();
        var customers = await _client.GetFromJsonAsync<List<CustomerMin>>("/api/customers");
        var createResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fgId, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<CreatedReq>();
        var reqId = created!.Id;

        var detail = await _client.GetFromJsonAsync<ReqDetailMin>($"/api/requisitions/{reqId}");
        var riId = detail!.Items[0].Id;

        // Admin needed to create/get process
        UseToken(adminToken);
        var processId = await EnsureProcessAsync();

        UseToken(bomToken);
        (await _client.PostAsync($"/api/bom/{reqId}/items/{riId}/start", null)).IsSuccessStatusCode.Should().BeTrue();

        (await _client.PutAsJsonAsync($"/api/bom/{reqId}/items/{riId}/lines", new
        {
            Lines = new[] { new { ProcessId = processId, RawMaterialItemId = rmId, QtyPerKg = 1.0m, WastagePct = 0m } }
        })).IsSuccessStatusCode.Should().BeTrue();

        (await _client.PostAsync($"/api/bom/{reqId}/submit", null)).IsSuccessStatusCode.Should().BeTrue();

        UseToken(acctToken);
        (await _client.PostAsync($"/api/costing/{reqId}/items/{riId}/start", null)).IsSuccessStatusCode.Should().BeTrue();

        var bomReview = await _client.GetFromJsonAsync<BomReviewMin>($"/api/bom/{reqId}");
        var bomLineId = bomReview!.Items.First(i => i.RequisitionItemId == riId).Lines[0].Id;

        (await _client.PostAsJsonAsync(
            $"/api/costing/{reqId}/items/{riId}/submit", new
            {
                RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 5m, CurrencyCode = "AED" } },
                LandedCostType = "Percentage",
                LandedCostValue = 0m,
                FohAmount = 0m
            })).IsSuccessStatusCode.Should().BeTrue();

        UseToken(mdToken);
        (await _client.PostAsJsonAsync($"/api/approvals/{reqId}/reject", new { Notes = notes }))
            .IsSuccessStatusCode.Should().BeTrue();

        return (spToken, reqId);
    }

    private async Task<(string token, int userId)> CreateSecondSalesPersonAsync()
    {
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        UseToken(adminToken);
        var email = $"sp{Guid.NewGuid():N}@t.com";
        var password = "Test@1234";
        var resp = await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "Other Sales",
            Email = email,
            Password = password,
            Role = "SalesPerson",
            BranchId = 1
        });
        resp.EnsureSuccessStatusCode();
        var created = await resp.Content.ReadFromJsonAsync<CreatedUser>();
        var token = await LoginAsync(email, password);
        return (token, created!.Id);
    }

    [Fact]
    public async Task GetDetail_OnRejectedRequisition_ReturnsNotes()
    {
        var (spToken, reqId) = await CreateRejectedRequisitionAsync("Margin too low");
        UseToken(spToken);

        var resp = await _client.GetAsync($"/api/requisitions/{reqId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<ReqDetailWithApproval>();
        body!.Status.Should().Be("Rejected");
        body.Approval.Should().NotBeNull();
        body.Approval!.IsApproved.Should().BeFalse();
        body.Approval.Notes.Should().Be("Margin too low");
    }

    [Fact]
    public async Task Resubmit_RejectedRequisition_TransitionsToBomPending_AndSupersedesApproval()
    {
        var (spToken, reqId) = await CreateRejectedRequisitionAsync("Try again");
        UseToken(spToken);
        var newFgId = await CreateActiveFinishedGoodAsync();

        var resp = await _client.PostAsJsonAsync($"/api/requisitions/{reqId}/resubmit",
            new { Items = new[] { new { ItemId = newFgId, ExpectedQty = 250m } } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await _client.GetFromJsonAsync<ReqDetailWithApproval>($"/api/requisitions/{reqId}");
        detail!.Status.Should().Be("BomPending");
        detail.Approval.Should().BeNull("the superseded approval must be filtered out of the current-approval projection");
    }

    [Fact]
    public async Task Resubmit_StatusNotRejected_ReturnsValidationProblem_StatusField()
    {
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        UseToken(spToken);
        var itemId = await CreateActiveFinishedGoodAsync();
        var customers = await _client.GetFromJsonAsync<List<CustomerMin>>("/api/customers");

        var createResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = itemId, ExpectedQty = 1m } },
            CurrencyCode = "AED"
        });
        var created = await createResp.Content.ReadFromJsonAsync<CreatedReq>();

        var resp = await _client.PostAsJsonAsync($"/api/requisitions/{created!.Id}/resubmit",
            new { Items = new[] { new { ItemId = itemId, ExpectedQty = 2m } } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Errors.Should().ContainKey("Status");
    }

    [Fact]
    public async Task Resubmit_EmptyItems_ReturnsValidationProblem_ItemsField()
    {
        var (spToken, reqId) = await CreateRejectedRequisitionAsync("Reason");
        UseToken(spToken);

        var resp = await _client.PostAsJsonAsync($"/api/requisitions/{reqId}/resubmit",
            new { Items = Array.Empty<object>() });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Errors.Should().ContainKey("Items");
    }

    [Fact]
    public async Task Resubmit_NonOwnerSameBranch_Forbidden()
    {
        var (_, reqId) = await CreateRejectedRequisitionAsync("R1");

        var (otherSpToken, _) = await CreateSecondSalesPersonAsync();

        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        UseToken(spToken);
        var anyFg = await CreateActiveFinishedGoodAsync();

        UseToken(otherSpToken);
        var resp = await _client.PostAsJsonAsync($"/api/requisitions/{reqId}/resubmit",
            new { Items = new[] { new { ItemId = anyFg, ExpectedQty = 1m } } });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
