using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Costing;

public class CostingTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private async Task<(int RequisitionId, int RequisitionItemId)> CreateRequisitionWithBomInCostingPendingAsync(string quoteCurrency = "AED")
    {
        // 1. SalesPerson creates items + requisition
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "Test FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemDto>();
        var rmResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "Test RM", Type = "RawMaterial", LastPurchasePrice = (decimal?)null });
        var rm = await rmResp.Content.ReadFromJsonAsync<ItemDto>();

        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fg!.Id, ExpectedQty = 100m } },
            CurrencyCode = quoteCurrency
        });
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();
        var requisitionId = created!.Id;

        // Get requisitionItemId
        var reqDetail = await _client.GetFromJsonAsync<RequisitionDetailDto>($"/api/requisitions/{requisitionId}");
        var requisitionItemId = reqDetail!.Items[0].Id;

        // 2. Admin creates a process
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var procCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await _client.PostAsJsonAsync("/api/processes", new { Name = procCode, DisplayOrder = 99 });
        var process = await procResp.Content.ReadFromJsonAsync<ProcessDto>();

        // 3. BomCreator starts, saves lines, submits BOM → CostingPending
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);
        await _client.PostAsync($"/api/bom/{requisitionId}/items/{requisitionItemId}/start", null);
        await _client.PutAsJsonAsync($"/api/bom/{requisitionId}/items/{requisitionItemId}/lines", new
        {
            Lines = new[]
            {
                new { ProcessId = process!.Id, RawMaterialItemId = rm!.Id, QtyPerKg = 0.85m, WastagePct = 2.0m }
            }
        });
        await _client.PostAsync($"/api/bom/{requisitionId}/submit", null);

        _client.DefaultRequestHeaders.Authorization = null;
        return (requisitionId, requisitionItemId);
    }

    private async Task<CostingReviewDto> GetCostingAsync(int requisitionId)
    {
        var resp = await _client.GetAsync($"/api/costing/{requisitionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<CostingReviewDto>())!;
    }

    [Fact]
    public async Task Start_TransitionsCostingPendingToCostingInProgress()
    {
        var (requisitionId, requisitionItemId) = await CreateRequisitionWithBomInCostingPendingAsync();

        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);

        var startResp = await _client.PostAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/start", null);
        startResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);
        var req = await _client.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{requisitionId}");
        req!.Status.Should().Be("CostingInProgress");
    }

    [Fact]
    public async Task SaveDraft_PersistsAndGetReturnsDraft()
    {
        var (requisitionId, requisitionItemId) = await CreateRequisitionWithBomInCostingPendingAsync();
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/start", null);

        var review = await GetCostingAsync(requisitionId);
        review.Items.Should().HaveCount(1);
        var bomLineId = review.Items[0].BomLines[0].BomLineId;

        var draftResp = await _client.PutAsJsonAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/draft", new
        {
            Lines = new[] { new { BomLineId = bomLineId, CostPerKg = 1.25m, CurrencyCode = "USD" } },
            LandedCostType = "Percentage",
            LandedCostValue = 5m,
            FohAmount = 0.12m
        });
        draftResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Status still CostingInProgress
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);
        var req = await _client.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{requisitionId}");
        req!.Status.Should().Be("CostingInProgress");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        var reloaded = await GetCostingAsync(requisitionId);
        var item = reloaded.Items[0];
        item.Draft.Should().NotBeNull();
        item.Draft!.Lines.Should().HaveCount(1);
        item.Draft.Lines[0].CostPerKg.Should().Be(1.25m);
        item.Draft.Lines[0].CurrencyCode.Should().Be("USD");
        item.Draft.LandedCostValue.Should().Be(5m);
    }

    [Fact]
    public async Task Submit_ConvertsCurrencyWritesLinesUpsertsLastCostAndMovesToMdReview()
    {
        var (requisitionId, requisitionItemId) = await CreateRequisitionWithBomInCostingPendingAsync(quoteCurrency: "AED");
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/start", null);

        var review = await GetCostingAsync(requisitionId);
        var bomLineId = review.Items[0].BomLines[0].BomLineId;

        // Submit with USD cost — seeded USD rate is 3.6725, quote AED = 1.0
        var submitResp = await _client.PostAsJsonAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 1.00m, CurrencyCode = "USD" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });
        submitResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Requisition → MdReview
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);
        var req = await _client.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{requisitionId}");
        req!.Status.Should().Be("MdReview");

        // Verify cost written + ItemLastCost upserted
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        var afterSubmit = await GetCostingAsync(requisitionId);
        var item = afterSubmit.Items[0];
        item.Cost.Should().NotBeNull();
        item.Cost!.RawMaterialCostTotal.Should().BeGreaterThan(0);
        item.BomLines[0].LastCost.Should().NotBeNull();
        item.BomLines[0].LastCost!.CostPerKg.Should().Be(1.00m);
        item.BomLines[0].LastCost!.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public async Task Submit_ReturnsBadRequest_WhenExchangeRateMissing()
    {
        var (requisitionId, requisitionItemId) = await CreateRequisitionWithBomInCostingPendingAsync(quoteCurrency: "AED");
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/start", null);

        var review = await GetCostingAsync(requisitionId);
        var bomLineId = review.Items[0].BomLines[0].BomLineId;

        // SAR rate not seeded → should fail
        var submitResp = await _client.PostAsJsonAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 5.0m, CurrencyCode = "SAR" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });
        submitResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Recosting_NewRequisitionDoesNotModifyPreviousBomCostLines()
    {
        // Submit costing on requisition A
        var (reqA, riA) = await CreateRequisitionWithBomInCostingPendingAsync();
        var acctToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{reqA}/items/{riA}/start", null);
        var reviewA = await GetCostingAsync(reqA);
        var bomLineIdA = reviewA.Items[0].BomLines[0].BomLineId;
        await _client.PostAsJsonAsync($"/api/costing/{reqA}/items/{riA}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineIdA, CostPerKg = 2.00m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        // Snapshot BomCost aggregate for A
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        var afterA = await GetCostingAsync(reqA);
        var totalA = afterA.Items[0].Cost!.TotalCostPerKg;

        // Submit costing on requisition B with a different cost for same raw material
        var (reqB, riB) = await CreateRequisitionWithBomInCostingPendingAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await _client.PostAsync($"/api/costing/{reqB}/items/{riB}/start", null);
        var reviewB = await GetCostingAsync(reqB);
        var bomLineIdB = reviewB.Items[0].BomLines[0].BomLineId;
        await _client.PostAsJsonAsync($"/api/costing/{reqB}/items/{riB}/submit", new
        {
            RawMaterialCosts = new[] { new { BomLineId = bomLineIdB, CostPerKg = 9.99m, CurrencyCode = "AED" } },
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        });

        // Requisition A total must be unchanged
        var reloadedA = await GetCostingAsync(reqA);
        reloadedA.Items[0].Cost!.TotalCostPerKg.Should().Be(totalA);
    }

    private async Task<(int RequisitionId, int RequisitionItemId, int[] BomLineIds)> BootstrapToCostingAsync(int bomLineCount)
    {
        // 1. SalesPerson creates items + requisition
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);

        var fgCode = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
        var fgResp = await _client.PostAsJsonAsync("/api/items",
            new { Code = fgCode, Description = "Test FG", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
        var fg = await fgResp.Content.ReadFromJsonAsync<ItemDto>();

        var rmIds = new List<int>();
        for (var i = 0; i < bomLineCount; i++)
        {
            var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
            var rmResp = await _client.PostAsJsonAsync("/api/items",
                new { Code = rmCode, Description = $"RM {i}", Type = "RawMaterial", LastPurchasePrice = (decimal?)null });
            var rm = await rmResp.Content.ReadFromJsonAsync<ItemDto>();
            rmIds.Add(rm!.Id);
        }

        var customers = await _client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        var reqResp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = new[] { new { ItemId = fg!.Id, ExpectedQty = 100m } },
            CurrencyCode = "AED"
        });
        var created = await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>();
        var requisitionId = created!.Id;

        var reqDetail = await _client.GetFromJsonAsync<RequisitionDetailDto>($"/api/requisitions/{requisitionId}");
        var requisitionItemId = reqDetail!.Items[0].Id;

        // 2. Admin creates a process
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var procName = $"P-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await _client.PostAsJsonAsync("/api/processes", new { Name = procName, DisplayOrder = 1 });
        var process = await procResp.Content.ReadFromJsonAsync<ProcessDto>();

        // 3. BomCreator starts, saves lines, submits BOM → CostingPending
        var bomToken = await LoginAsync("bob@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bomToken);
        await _client.PostAsync($"/api/bom/{requisitionId}/items/{requisitionItemId}/start", null);
        await _client.PutAsJsonAsync($"/api/bom/{requisitionId}/items/{requisitionItemId}/lines", new
        {
            Lines = rmIds.Select(rmId =>
                new { ProcessId = process!.Id, RawMaterialItemId = rmId, QtyPerKg = 0.85m, WastagePct = 2.0m })
                .ToArray()
        });
        await _client.PostAsync($"/api/bom/{requisitionId}/submit", null);

        // 4. Accountant starts costing → CostingInProgress
        var accToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accToken);
        await _client.PostAsync($"/api/costing/{requisitionId}/items/{requisitionItemId}/start", null);

        // 5. Fetch costing to extract bomLineIds
        var review = await _client.GetFromJsonAsync<CostingReviewDto>($"/api/costing/{requisitionId}");
        var bomLineIds = review!.Items[0].BomLines.Select(l => l.BomLineId).ToArray();

        _client.DefaultRequestHeaders.Authorization = null;
        return (requisitionId, requisitionItemId, bomLineIds);
    }

    [Fact]
    public async Task Submit_NegativeCost_Returns400()
    {
        var (reqId, itemId, bomLineIds) = await BootstrapToCostingAsync(bomLineCount: 1);

        var accToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accToken);

        var resp = await _client.PostAsJsonAsync(
            $"/api/costing/{reqId}/items/{itemId}/submit",
            new
            {
                RawMaterialCosts = new[] { new { BomLineId = bomLineIds[0], CostPerKg = -5m, CurrencyCode = "AED" } },
                LandedCostType = "Percentage",
                LandedCostValue = 5m,
                FohAmount = 1m
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<BomPriceApproval.Tests.Shared.ValidationProblemResponse>();
        body!.Detail.ToLower().Should().Contain("cost");
    }

    [Fact]
    public async Task Submit_UnknownBomLineId_Returns400()
    {
        var (reqId, itemId, _) = await BootstrapToCostingAsync(bomLineCount: 1);

        var accToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accToken);

        var resp = await _client.PostAsJsonAsync(
            $"/api/costing/{reqId}/items/{itemId}/submit",
            new
            {
                RawMaterialCosts = new[] { new { BomLineId = 999999, CostPerKg = 5m, CurrencyCode = "AED" } },
                LandedCostType = "Percentage",
                LandedCostValue = 5m,
                FohAmount = 1m
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<BomPriceApproval.Tests.Shared.ValidationProblemResponse>();
        body!.Detail.ToLower().Should().Contain("unknown");
    }

    [Fact]
    public async Task Submit_MissingLineCost_Returns400()
    {
        var (reqId, itemId, bomLineIds) = await BootstrapToCostingAsync(bomLineCount: 2);

        var accToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accToken);

        // Submit cost for only 1 of 2 BOM lines
        var resp = await _client.PostAsJsonAsync(
            $"/api/costing/{reqId}/items/{itemId}/submit",
            new
            {
                RawMaterialCosts = new[] { new { BomLineId = bomLineIds[0], CostPerKg = 5m, CurrencyCode = "AED" } },
                LandedCostType = "Percentage",
                LandedCostValue = 5m,
                FohAmount = 1m
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<BomPriceApproval.Tests.Shared.ValidationProblemResponse>();
        body!.Detail.Should().Contain("Missing cost");
    }

    // ── Multi-item concurrency helper ──────────────────────────────────────────

    /// <summary>
    /// Seeds a requisition with <paramref name="itemCount"/> requisition items,
    /// builds a BOM for each, submits the BOM, and transitions to CostingInProgress.
    /// Returns (requisitionId, array of (requisitionItemId, bomLineId) per item).
    /// </summary>
    private async Task<(int RequisitionId, (int RequisitionItemId, int BomLineId)[] Items)>
        BootstrapMultiItemToCostingInProgressAsync(int itemCount)
    {
        var client = factory.CreateClient();

        // Login helpers scoped to this client
        async Task<string> Login(string email, string pw)
        {
            var r = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = pw });
            return (await r.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        }

        void Auth(string token) =>
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // 1. Create FG items (one per requisition item) + one shared RM
        var spToken = await Login("ali@test.com", "Test@1234");
        Auth(spToken);

        var fgIds = new List<int>();
        for (var i = 0; i < itemCount; i++)
        {
            var code = $"FG-{Guid.NewGuid():N}".Substring(0, 10);
            var resp = await client.PostAsJsonAsync("/api/items",
                new { Code = code, Description = $"FG {i}", Type = "FinishedGood", LastPurchasePrice = (decimal?)null });
            fgIds.Add((await resp.Content.ReadFromJsonAsync<ItemDto>())!.Id);
        }

        var rmCode = $"RM-{Guid.NewGuid():N}".Substring(0, 10);
        var rmResp = await client.PostAsJsonAsync("/api/items",
            new { Code = rmCode, Description = "Shared RM", Type = "RawMaterial", LastPurchasePrice = (decimal?)null });
        var rmId = (await rmResp.Content.ReadFromJsonAsync<ItemDto>())!.Id;

        var customers = await client.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        var reqResp = await client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customers!.First().Id,
            Items = fgIds.Select(fgId => new { ItemId = fgId, ExpectedQty = 100m }).ToArray(),
            CurrencyCode = "AED"
        });
        var requisitionId = (await reqResp.Content.ReadFromJsonAsync<CreatedRequisition>())!.Id;

        var reqDetail = await client.GetFromJsonAsync<RequisitionDetailDto>($"/api/requisitions/{requisitionId}");
        var riIds = reqDetail!.Items.Select(i => i.Id).ToArray();

        // 2. Admin creates a process
        var adminToken = await Login("admin@test.com", "Admin@1234");
        Auth(adminToken);
        var procCode = $"P-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await client.PostAsJsonAsync("/api/processes", new { Name = procCode, DisplayOrder = 1 });
        var processId = (await procResp.Content.ReadFromJsonAsync<ProcessDto>())!.Id;

        // 3. BomCreator builds + submits BOM for every item
        var bomToken = await Login("bob@test.com", "Test@1234");
        Auth(bomToken);
        foreach (var riId in riIds)
        {
            await client.PostAsync($"/api/bom/{requisitionId}/items/{riId}/start", null);
            await client.PutAsJsonAsync($"/api/bom/{requisitionId}/items/{riId}/lines", new
            {
                Lines = new[] { new { ProcessId = processId, RawMaterialItemId = rmId, QtyPerKg = 0.85m, WastagePct = 2.0m } }
            });
        }
        await client.PostAsync($"/api/bom/{requisitionId}/submit", null);

        // 4. Accountant starts costing
        var accToken = await Login("sara@test.com", "Test@1234");
        Auth(accToken);
        await client.PostAsync($"/api/costing/{requisitionId}/items/{riIds[0]}/start", null);

        // 5. Fetch bomLineIds per item
        var review = await client.GetFromJsonAsync<CostingReviewDto>($"/api/costing/{requisitionId}");
        var itemData = riIds
            .Select(riId =>
            {
                var ci = review!.Items.First(x => x.RequisitionItemId == riId);
                return (riId, ci.BomLines[0].BomLineId);
            }).ToArray();

        client.DefaultRequestHeaders.Authorization = null;
        return (requisitionId, itemData);
    }

    private object DefaultSubmitBody(int bomLineId) => new
    {
        RawMaterialCosts = new[] { new { BomLineId = bomLineId, CostPerKg = 2.0m, CurrencyCode = "AED" } },
        LandedCostType = "Percentage",
        LandedCostValue = 0m,
        FohAmount = 0m
    };

    // ── Concurrency tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitItem_SingleItemReq_TransitionsToMdReview()
    {
        var (reqId, items) = await BootstrapMultiItemToCostingInProgressAsync(itemCount: 1);
        var (riId, bomLineId) = items[0];

        var client = factory.CreateClient();
        var token = await LoginAsync(client, "sara@test.com", "Test@1234");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await client.PostAsJsonAsync(
            $"/api/costing/{reqId}/items/{riId}/submit",
            DefaultSubmitBody(bomLineId));
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        var spClient = factory.CreateClient();
        var spToken = await LoginAsync(spClient, "ali@test.com", "Test@1234");
        spClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", spToken);
        var req = await spClient.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{reqId}");
        req!.Status.Should().Be("MdReview");
    }

    [Fact]
    public async Task SubmitItem_ConcurrentLastTwoItems_BothTriggerMdReview()
    {
        // Seed: 3-item req with item 1 already costed sequentially.
        var (reqId, items) = await BootstrapMultiItemToCostingInProgressAsync(itemCount: 3);
        var (ri0, bl0) = items[0];
        var (ri1, bl1) = items[1];
        var (ri2, bl2) = items[2];

        // Pre-submit item 0 sequentially.
        var seqClient = factory.CreateClient();
        var seqToken = await LoginAsync(seqClient, "sara@test.com", "Test@1234");
        seqClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", seqToken);
        var pre = await seqClient.PostAsJsonAsync(
            $"/api/costing/{reqId}/items/{ri0}/submit", DefaultSubmitBody(bl0));
        pre.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        // Items 1 and 2 are submitted concurrently — each with its own HttpClient.
        var task1 = Task.Run(async () =>
        {
            var c = factory.CreateClient();
            var t = await LoginAsync(c, "sara@test.com", "Test@1234");
            c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", t);
            return await c.PostAsJsonAsync($"/api/costing/{reqId}/items/{ri1}/submit", DefaultSubmitBody(bl1));
        });
        var task2 = Task.Run(async () =>
        {
            var c = factory.CreateClient();
            var t = await LoginAsync(c, "sara@test.com", "Test@1234");
            c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", t);
            return await c.PostAsJsonAsync($"/api/costing/{reqId}/items/{ri2}/submit", DefaultSubmitBody(bl2));
        });

        var results = await Task.WhenAll(task1, task2);
        foreach (var r in results)
            r.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        // Final state: requisition must be MdReview, exactly 3 BomCost rows.
        var spClient = factory.CreateClient();
        var spToken = await LoginAsync(spClient, "ali@test.com", "Test@1234");
        spClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", spToken);
        var req = await spClient.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{reqId}");
        req!.Status.Should().Be("MdReview");

        var accClient = factory.CreateClient();
        var accToken = await LoginAsync(accClient, "sara@test.com", "Test@1234");
        accClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accToken);
        var review = await accClient.GetFromJsonAsync<CostingReviewDto>($"/api/costing/{reqId}");
        review!.Items.Count(i => i.Cost is not null).Should().Be(3);
    }

    [Fact]
    public async Task SubmitItem_MultipleConcurrent_NoDuplicatedCosts()
    {
        // One-item requisition; fire 5 parallel submits of the same item.
        var (reqId, items) = await BootstrapMultiItemToCostingInProgressAsync(itemCount: 1);
        var (riId, bomLineId) = items[0];

        var tasks = Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
        {
            var c = factory.CreateClient();
            var t = await LoginAsync(c, "sara@test.com", "Test@1234");
            c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", t);
            return await c.PostAsJsonAsync(
                $"/api/costing/{reqId}/items/{riId}/submit", DefaultSubmitBody(bomLineId));
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        // At least one must succeed.
        results.Should().Contain(r => r.StatusCode == System.Net.HttpStatusCode.NoContent);

        // Final state: exactly 1 BomCost row (no duplicates), status is MdReview.
        var accClient = factory.CreateClient();
        var accToken = await LoginAsync(accClient, "sara@test.com", "Test@1234");
        accClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accToken);
        var review = await accClient.GetFromJsonAsync<CostingReviewDto>($"/api/costing/{reqId}");
        review!.Items.Count(i => i.Cost is not null).Should().Be(1);

        var spClient = factory.CreateClient();
        var spToken = await LoginAsync(spClient, "ali@test.com", "Test@1234");
        spClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", spToken);
        var req = await spClient.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{reqId}");
        req!.Status.Should().Be("MdReview");
    }

    [Fact]
    public async Task SubmitItem_ConcurrentDifferentReqsSharingRawMaterial_NoDeadlock()
    {
        // Two independent requisitions whose BOMs both reference a shared raw material,
        // but in OPPOSITE list positions to maximize the chance that both transactions
        // enter the ItemLastCost upsert with the shared item concurrently.
        // Pre-fix: 23505 unique_violation from the TOCTOU read-then-insert race.
        // Post-fix: INSERT … ON CONFLICT atomically serializes concurrent first-INSERTs.

        // ── Arrange: create shared and per-req raw materials + FG items ────────────
        var spClient = factory.CreateClient();
        var spToken = await LoginAsync(spClient, "ali@test.com", "Test@1234");
        spClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", spToken);

        async Task<int> CreateItem(string prefix, string type)
        {
            var code = $"{prefix}-{Guid.NewGuid():N}".Substring(0, 10);
            var resp = await spClient.PostAsJsonAsync("/api/items",
                new { Code = code, Description = $"{prefix} {code}", Type = type, LastPurchasePrice = (decimal?)null });
            resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
            var item = await resp.Content.ReadFromJsonAsync<ItemDto>();
            return item!.Id;
        }

        var fgA = await CreateItem("FG", "FinishedGood");
        var fgB = await CreateItem("FG", "FinishedGood");
        var rmShared = await CreateItem("RM", "RawMaterial");
        var rmA = await CreateItem("RM", "RawMaterial");
        var rmB = await CreateItem("RM", "RawMaterial");

        var customers = await spClient.GetFromJsonAsync<List<CustomerDto>>("/api/customers");
        var customerId = customers!.First().Id;

        async Task<(int ReqId, int RiId)> CreateReq(int fgId)
        {
            var resp = await spClient.PostAsJsonAsync("/api/requisitions", new
            {
                CustomerId = customerId,
                Items = new[] { new { ItemId = fgId, ExpectedQty = 100m } },
                CurrencyCode = "AED"
            });
            resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
            var created = await resp.Content.ReadFromJsonAsync<CreatedRequisition>();
            var detail = await spClient.GetFromJsonAsync<RequisitionDetailDto>($"/api/requisitions/{created!.Id}");
            return (created.Id, detail!.Items[0].Id);
        }

        var (reqA, riA) = await CreateReq(fgA);
        var (reqB, riB) = await CreateReq(fgB);

        // ── Admin seeds a Process for BOM lines ──
        var adminClient = factory.CreateClient();
        var adminToken = await LoginAsync(adminClient, "admin@test.com", "Admin@1234");
        adminClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
        var procCode = $"PROC-{Guid.NewGuid():N}".Substring(0, 12);
        var procResp = await adminClient.PostAsJsonAsync("/api/processes", new { Name = procCode, DisplayOrder = 99 });
        procResp.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        var process = await procResp.Content.ReadFromJsonAsync<ProcessDto>();

        // ── BomCreator builds TWO-line BOMs, reversed order of the shared RM ──
        //    reqA lines: [rmA, rmShared]   reqB lines: [rmShared, rmB]
        var bomClient = factory.CreateClient();
        var bomToken = await LoginAsync(bomClient, "bob@test.com", "Test@1234");
        bomClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bomToken);

        async Task BuildAndSubmitBom(int reqId, int riId, int firstRm, int secondRm)
        {
            await bomClient.PostAsync($"/api/bom/{reqId}/items/{riId}/start", null);
            var saveResp = await bomClient.PutAsJsonAsync($"/api/bom/{reqId}/items/{riId}/lines", new
            {
                Lines = new[]
                {
                    new { ProcessId = process!.Id, RawMaterialItemId = firstRm,  QtyPerKg = 0.50m, WastagePct = 1.0m },
                    new { ProcessId = process!.Id, RawMaterialItemId = secondRm, QtyPerKg = 0.50m, WastagePct = 1.0m }
                }
            });
            saveResp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
            (await bomClient.PostAsync($"/api/bom/{reqId}/submit", null))
                .StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
        }

        await BuildAndSubmitBom(reqA, riA, rmA, rmShared);
        await BuildAndSubmitBom(reqB, riB, rmShared, rmB);

        // ── Accountant starts costing on both, then submits concurrently ──
        async Task<(int RiId, List<CostingBomLineDto> Lines)> StartAndLoad(HttpClient c, int reqId, int riId)
        {
            (await c.PostAsync($"/api/costing/{reqId}/items/{riId}/start", null))
                .StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
            var review = await c.GetFromJsonAsync<CostingReviewDto>($"/api/costing/{reqId}");
            return (riId, review!.Items.First(x => x.RequisitionItemId == riId).BomLines);
        }

        var startClient = factory.CreateClient();
        var startToken = await LoginAsync(startClient, "sara@test.com", "Test@1234");
        startClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", startToken);

        var (_, linesA) = await StartAndLoad(startClient, reqA, riA);
        var (_, linesB) = await StartAndLoad(startClient, reqB, riB);

        object SubmitBody(List<CostingBomLineDto> lines) => new
        {
            RawMaterialCosts = lines
                .Select(l => new { BomLineId = l.BomLineId, CostPerKg = 3.0m, CurrencyCode = "AED" })
                .ToArray(),
            LandedCostType = "Percentage",
            LandedCostValue = 0m,
            FohAmount = 0m
        };

        // Fire the two submits concurrently on fresh authenticated clients.
        async Task<HttpClient> AuthedClient()
        {
            var c = factory.CreateClient();
            var t = await LoginAsync(c, "sara@test.com", "Test@1234");
            c.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", t);
            return c;
        }

        var submitClientA = await AuthedClient();
        var submitClientB = await AuthedClient();

        Task<HttpResponseMessage> DoSubmit(HttpClient c, int reqId, int riId, List<CostingBomLineDto> lines) =>
            c.PostAsJsonAsync($"/api/costing/{reqId}/items/{riId}/submit", SubmitBody(lines));

        var taskA = Task.Run(() => DoSubmit(submitClientA, reqA, riA, linesA));
        var taskB = Task.Run(() => DoSubmit(submitClientB, reqB, riB, linesB));
        var results = await Task.WhenAll(taskA, taskB);

        // ── Assert: both submits succeed, neither deadlocked into 500 ──
        foreach (var r in results)
            r.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        // Both requisitions should be MdReview (single-item reqs, so one submit each = complete).
        var verifyClient = factory.CreateClient();
        var verifyToken = await LoginAsync(verifyClient, "ali@test.com", "Test@1234");
        verifyClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", verifyToken);
        (await verifyClient.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{reqA}"))!
            .Status.Should().Be("MdReview");
        (await verifyClient.GetFromJsonAsync<RequisitionDto>($"/api/requisitions/{reqB}"))!
            .Status.Should().Be("MdReview");
    }

    // ── DTOs ──
    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record ItemDto(int Id, string Code, string Description, string Type, int BranchId, bool IsActive, decimal? LastPurchasePrice);
    private record CustomerDto(int Id, string Code, string Name);
    private record ProcessDto(int Id, string Name, int DisplayOrder, bool IsActive);
    private record CreatedRequisition(int Id, string RefNo);
    private record RequisitionItemDto(int Id, int ItemId, string ItemDescription, decimal ExpectedQty, int SortOrder);
    private record RequisitionDetailDto(int Id, string RefNo, string Status, List<RequisitionItemDto> Items);
    private record RequisitionDto(int Id, string RefNo, string Status);
    private record LastCostDto(decimal CostPerKg, string CurrencyCode, DateTime UpdatedAt);
    private record CostingBomLineDto(int BomLineId, int ProcessId, string ProcessName,
        int RawMaterialItemId, string RawMaterialDescription, decimal QtyPerKg, decimal WastagePct,
        LastCostDto? LastCost);
    private record CostingDraftLineDto(int BomLineId, decimal CostPerKg, string CurrencyCode);
    private record CostingDraftDto(List<CostingDraftLineDto> Lines, string LandedCostType,
        decimal LandedCostValue, decimal FohAmount);
    private record CostingSummaryDto(int Id, decimal RawMaterialCostTotal, string LandedCostType,
        decimal LandedCostValue, decimal FohAmount, decimal TotalCostPerKg, DateTime? SubmittedAt);
    private record CostingItemDto(int RequisitionItemId, int ItemId, string ItemDescription, decimal ExpectedQty,
        int? BomHeaderId, string CostStatus, CostingSummaryDto? Cost,
        List<CostingBomLineDto> BomLines, CostingDraftDto? Draft);
    private record CostingReviewDto(int RequisitionId, List<CostingItemDto> Items);
}
