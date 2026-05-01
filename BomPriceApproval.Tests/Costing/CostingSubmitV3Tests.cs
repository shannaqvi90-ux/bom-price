using System.Net;
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

namespace BomPriceApproval.Tests.Costing;

/// <summary>
/// Integration tests for POST /api/costing/{reqId}/submit (V3).
///
/// Validation audit (B2 task) confirmed the controller already has:
///   (a) Wrong-status guard: CanTransition check → 400
///   (b) Missing-cost guard: Any(ri => BomHeader is null || Cost is null) → 400
///   (d) Correct status transition: Costing → MdPricing
///   (e) MD notification fan-out via SendToUsersAsync
///
/// Tests below cover all three observable failure modes + the happy path.
/// Admin client is used for all submit calls because sara (Accountant) is seeded
/// with BranchId=Fujairah (1) and V3 reqs are pinned to Al Ain (BranchId=2).
/// AccountantAuthorizedForBranchAsync rejects that mismatch, so Admin (cross-branch)
/// is the correct role for these tests — consistent with WalkToMdPricingAsync.
/// </summary>
public class CostingSubmitV3Tests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = factory;

    // ── Test 1: wrong-status returns 400 ────────────────────────────────────

    /// <summary>
    /// A req in Draft status cannot be submitted for costing — state machine
    /// does not allow Draft → MdPricing, so the controller must 400.
    /// </summary>
    [Fact]
    public async Task Submit_FromNonCostingStatus_Returns400()
    {
        // Seed a req and leave it in Draft (do NOT call SP submit).
        var salesClient = await V3WorkflowTestHelpers.CreateSalesClientAsync(_factory);
        var (customerId, fgId, rmId, processId) = await V3WorkflowTestHelpers.SeedV3MinimumAsync(_factory);
        var (createStatus, reqId, status) =
            await V3WorkflowTestHelpers.CreateV3DraftRequisitionAsync(salesClient, customerId, fgId, rmId, processId);

        createStatus.Should().Be(HttpStatusCode.Created);
        status.Should().Be("Draft"); // confirm it is still Draft

        // Submit costing from Draft — must reject.
        var admin = await V3WorkflowTestHelpers.CreateAdminClientAsync(_factory);
        var resp = await admin.PostAsync($"/api/costing/{reqId}/submit", content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Test 2: missing BomCost on one FG returns 400 ───────────────────────

    /// <summary>
    /// A Costing-status req where ONE FG has no BomCost must be rejected with
    /// "All FGs must have cost data before submit". Here we create a two-FG req,
    /// populate cost only for the first FG, and leave the second uncovered.
    /// </summary>
    [Fact]
    public async Task Submit_FgWithoutBomCost_Returns400()
    {
        // Seed two FGs (two separate V3 minimum seeds sharing the same customer+RM).
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var seedScope = _factory.Services.CreateScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ali = await seedDb.Users.FirstAsync(u => u.Email == "ali@test.com");

        var customer = new Customer
        {
            Code = $"CST2FG-{suffix}",
            Name = $"Two-FG Customer {suffix}",
            Address = "", Email = "", PhoneNumber = "",
            SalesPersonId = ali.Id,
            CreatedByUserId = ali.Id
        };
        seedDb.Customers.Add(customer);

        var fg1 = new Item
        {
            Code = $"FG1-{suffix}", Description = $"FG1 {suffix}",
            Type = ItemType.FinishedGood, BranchId = V3WorkflowTestHelpers.AlainBranchId, IsActive = true
        };
        var fg2 = new Item
        {
            Code = $"FG2-{suffix}", Description = $"FG2 {suffix}",
            Type = ItemType.FinishedGood, BranchId = V3WorkflowTestHelpers.AlainBranchId, IsActive = true
        };
        var rm = new Item
        {
            Code = $"RM2FG-{suffix}", Description = $"RM {suffix}",
            Type = ItemType.RawMaterial, BranchId = V3WorkflowTestHelpers.AlainBranchId, IsActive = true
        };
        seedDb.Items.AddRange(fg1, fg2, rm);

        var process = await seedDb.Processes.FirstOrDefaultAsync(p => p.IsActive);
        if (process is null)
        {
            process = new Process { Name = $"Extrusion-{suffix}", DisplayOrder = 1, IsActive = true };
            seedDb.Processes.Add(process);
        }

        await seedDb.SaveChangesAsync();

        // Create a two-FG V3 draft requisition directly via API.
        var salesClient = await V3WorkflowTestHelpers.CreateSalesClientAsync(_factory);
        var twoFgPayload = new
        {
            customerId = customer.Id,
            quotationCurrency = "USD",
            referenceNumber = $"PO-2FG-{suffix}",
            notes = "Two-FG missing-cost test",
            finishedGoods = new[]
            {
                new
                {
                    itemId = fg1.Id, expectedQtyKg = 1000m, printing = false,
                    bomLines = new[] { new { processId = process.Id, itemId = rm.Id, qtyPerKg = 0.5m, micron = "30" } }
                },
                new
                {
                    itemId = fg2.Id, expectedQtyKg = 2000m, printing = false,
                    bomLines = new[] { new { processId = process.Id, itemId = rm.Id, qtyPerKg = 0.6m, micron = "25" } }
                }
            }
        };

        var createResp = await salesClient.PostAsJsonAsync("/api/requisitions", twoFgPayload);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var createBody = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var reqId = createBody.GetProperty("id").GetInt32();

        // SP submits Draft → Costing.
        var spSubmit = await salesClient.PostAsync($"/api/requisitions/{reqId}/submit", content: null);
        spSubmit.EnsureSuccessStatusCode();

        // Populate BomCost for ONLY the first FG (leave FG2's BomHeader.Cost null).
        using var costScope = _factory.Services.CreateScope();
        var costDb = costScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sara = await costDb.Users.FirstAsync(u => u.Email == "sara@test.com");

        var allBoms = await costDb.BomHeaders
            .Include(b => b.Lines)
            .Where(b => b.RequisitionItem.QuotationRequestId == reqId)
            .ToListAsync();

        allBoms.Should().HaveCount(2); // ensure both FGs have BomHeaders

        // Only seed BomCost for the first BomHeader.
        var firstBom = allBoms[0];
        const decimal costPerKg = 10m;
        decimal rmTotal = firstBom.Lines.Sum(l => costPerKg * l.QtyPerKg * (1 + l.WastagePct / 100m));

        foreach (var line in firstBom.Lines)
        {
            costDb.BomCostLines.Add(new BomCostLine
            {
                BomHeaderId = firstBom.Id,
                BomLineId = line.Id,
                CostPerKg = costPerKg,
                CurrencyCode = "AED",
                CostPerKgInQuoteCurrency = costPerKg,
                CostPerKgInAed = costPerKg
            });
        }

        costDb.BomCosts.Add(new BomCost
        {
            BomHeaderId = firstBom.Id,
            RawMaterialCostTotal = rmTotal,
            LandedCostType = LandedCostType.FixedValue,
            LandedCostValue = 0m,
            FohAmount = 0m,
            TotalCostPerKg = rmTotal,
            SubmittedByUserId = sara.Id,
            FohPerKg = 0m,
            TransportPerKg = 0m,
            CommissionPerKg = 0m
        });
        firstBom.TotalCostPerKg = rmTotal;

        await costDb.SaveChangesAsync();
        // Second BomHeader intentionally has no BomCost.

        // Attempt costing submit — must reject because FG2 has no cost data.
        var admin = await V3WorkflowTestHelpers.CreateAdminClientAsync(_factory);
        var resp = await admin.PostAsync($"/api/costing/{reqId}/submit", content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should()
            .Contain("All FGs must have cost data before submit");
    }

    // ── Test 3: happy path → 200 + transitions to MdPricing ─────────────────

    /// <summary>
    /// When all FGs have cost data and the req is in Costing status, the submit
    /// endpoint must return 200 with status="MdPricing".
    ///
    /// Note: the full Draft→Signed happy path is also verified in
    /// V3ApprovalSplitTests.FullHappyPath_DraftToSigned (line 49 calls the same
    /// endpoint). This test provides a focused assertion on the costing submit
    /// response body and DB state, independent of the approval split surface.
    /// </summary>
    [Fact]
    public async Task Submit_AllFgsCovered_Returns200_AndTransitionsToMdPricing()
    {
        await V3WorkflowTestHelpers.EnsureAccountantInAlainAsync(_factory);

        // Walk to Costing status (Draft → submit by SP).
        var reqId = await V3WorkflowTestHelpers.WalkToCostingAsync(_factory);

        // Populate BomCost for all FGs via the shared DB-seed helper.
        await V3WorkflowTestHelpers.PopulateBomCostAsync(_factory, reqId);

        // Submit — admin client (cross-branch) to avoid Fujairah/Alain mismatch.
        var admin = await V3WorkflowTestHelpers.CreateAdminClientAsync(_factory);
        var resp = await admin.PostAsync($"/api/costing/{reqId}/submit", content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetInt32().Should().Be(reqId);
        body.GetProperty("status").GetString().Should().Be("MdPricing");

        // Verify DB state — req must be at MdPricing.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var req = await db.QuotationRequests.FindAsync(reqId);
        req.Should().NotBeNull();
        req!.Status.Should().Be(RequisitionStatus.MdPricing);
    }
}
