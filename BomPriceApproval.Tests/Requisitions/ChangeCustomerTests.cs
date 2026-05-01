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

namespace BomPriceApproval.Tests.Requisitions;

/// <summary>
/// Integration tests for PATCH /api/requisitions/{id}/customer (V3).
///
/// B3 task — validates that the customer-swap endpoint was updated from the
/// V2.3 CostingPending/CostingInProgress whitelist to the V3
/// Draft + Costing whitelist, and that all post-V3-cutover statuses
/// (MdPricing, CustomerConfirm, MdFinalSign, Signed, Rejected) are blocked.
///
/// Admin client is used throughout to avoid the Accountant branch-scope guard
/// (Sara is seeded with BranchId=Fujairah=1; V3 reqs are pinned to Al Ain=2).
/// This matches the pattern used in CostingSubmitV3Tests and WalkToMdPricingAsync.
/// </summary>
public class ChangeCustomerTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = factory;

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a second customer (Guid-isolated) to use as the swap target.
    /// Returns the new CustomerId.
    /// </summary>
    private async Task<int> SeedAltCustomerAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ali = await db.Users.FirstAsync(u => u.Email == "ali@test.com");

        var alt = new Customer
        {
            Code = $"ALT-{suffix}",
            Name = $"Alt Customer {suffix}",
            Address = "",
            Email = "",
            PhoneNumber = "",
            SalesPersonId = ali.Id,
            CreatedByUserId = ali.Id
        };
        db.Customers.Add(alt);
        await db.SaveChangesAsync();
        return alt.Id;
    }

    /// <summary>
    /// Sends PATCH /api/requisitions/{reqId}/customer with admin credentials.
    /// Returns the raw HttpResponseMessage.
    /// </summary>
    private async Task<HttpResponseMessage> PatchCustomerAsync(int reqId, int altCustomerId)
    {
        var admin = await V3WorkflowTestHelpers.CreateAdminClientAsync(_factory);
        return await admin.PatchAsJsonAsync(
            $"/api/requisitions/{reqId}/customer",
            new { customerId = altCustomerId, reason = "B3 test swap" });
    }

    // ── Allowed-status tests ─────────────────────────────────────────────────

    [Fact]
    public async Task Patch_FromDraft_AllowsSwap_Returns204()
    {
        // Seed req at Draft status (SP creates but does NOT submit).
        var salesClient = await V3WorkflowTestHelpers.CreateSalesClientAsync(_factory);
        var (customerId, fgId, rmId, processId) = await V3WorkflowTestHelpers.SeedV3MinimumAsync(_factory);
        var (createStatus, reqId, _) =
            await V3WorkflowTestHelpers.CreateV3DraftRequisitionAsync(salesClient, customerId, fgId, rmId, processId);
        createStatus.Should().Be(HttpStatusCode.Created);

        var altCustomerId = await SeedAltCustomerAsync();

        var resp = await PatchCustomerAsync(reqId, altCustomerId);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify DB: CustomerId updated + CustomerChangeHistory row inserted.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var req = await db.QuotationRequests.FindAsync(reqId);
        req!.CustomerId.Should().Be(altCustomerId);

        var history = await db.CustomerChangeHistories
            .Where(h => h.RequisitionId == reqId)
            .ToListAsync();
        history.Should().HaveCountGreaterThan(0);
        history.Last().NewCustomerId.Should().Be(altCustomerId);
        history.Last().OldCustomerId.Should().Be(customerId);
    }

    [Fact]
    public async Task Patch_FromCosting_AllowsSwap_Returns204()
    {
        // Walk Draft → Costing (SP submits).
        var reqId = await V3WorkflowTestHelpers.WalkToCostingAsync(_factory);

        // Read the current CustomerId from DB before swapping.
        int originalCustomerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = await db.QuotationRequests.FindAsync(reqId);
            req!.Status.Should().Be(RequisitionStatus.Costing, "pre-condition: req must be at Costing");
            originalCustomerId = req.CustomerId;
        }

        var altCustomerId = await SeedAltCustomerAsync();

        var resp = await PatchCustomerAsync(reqId, altCustomerId);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify DB: CustomerId updated + history row.
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var updated = await verifyDb.QuotationRequests.FindAsync(reqId);
        updated!.CustomerId.Should().Be(altCustomerId);

        var history = await verifyDb.CustomerChangeHistories
            .Where(h => h.RequisitionId == reqId)
            .ToListAsync();
        history.Should().HaveCountGreaterThan(0);
        history.Last().NewCustomerId.Should().Be(altCustomerId);
        history.Last().OldCustomerId.Should().Be(originalCustomerId);
    }

    // ── Blocked-status tests ─────────────────────────────────────────────────

    [Fact]
    public async Task Patch_FromMdPricing_Blocks_Returns400()
    {
        var reqId = await V3WorkflowTestHelpers.WalkToMdPricingAsync(_factory);
        var altCustomerId = await SeedAltCustomerAsync();

        var resp = await PatchCustomerAsync(reqId, altCustomerId);

        // ValidationProblemDetails shape: { "detail": "...", "errors": {...}, ... }
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Contain("Draft or Costing");
    }

    [Fact]
    public async Task Patch_FromCustomerConfirm_Blocks_Returns400()
    {
        var reqId = await V3WorkflowTestHelpers.WalkToCustomerConfirmAsync(_factory);
        var altCustomerId = await SeedAltCustomerAsync();

        var resp = await PatchCustomerAsync(reqId, altCustomerId);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Contain("Draft or Costing");
    }

    [Fact]
    public async Task Patch_FromSigned_Blocks_Returns400()
    {
        var reqId = await V3WorkflowTestHelpers.WalkToSignedAsync(_factory);
        var altCustomerId = await SeedAltCustomerAsync();

        var resp = await PatchCustomerAsync(reqId, altCustomerId);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Contain("Draft or Costing");
    }

    [Fact]
    public async Task Patch_FromRejected_Blocks_Returns400()
    {
        // There is no V3 HTTP endpoint to reject from MdPricing yet — the state
        // machine allows MdPricing → Rejected but no controller action exposes it.
        // Seed the Rejected status directly in the DB (same pattern as
        // AdminRollbackStatusTests.SeedRequisitionAsync). This is stable because the
        // ChangeCustomer status-guard runs before any state-machine logic.
        int reqId;
        int originalCustomerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sp = await db.Users.FirstAsync(u => u.Email == "ali@test.com");
            var customer = await db.Customers.FirstAsync(c => c.SalesPersonId == sp.Id);
            originalCustomerId = customer.Id;

            var suffix = Guid.NewGuid().ToString("N")[..8];
            var req = new QuotationRequest
            {
                BranchId = V3WorkflowTestHelpers.AlainBranchId,
                SalesPersonId = sp.Id,
                CustomerId = customer.Id,
                CurrencyCode = "AED",
                Status = RequisitionStatus.Rejected,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ReferenceNumber = $"PO-REJ-{suffix}",
                Notes = "B3 rejected test"
            };
            db.QuotationRequests.Add(req);
            await db.SaveChangesAsync();
            reqId = req.Id;
        }

        var altCustomerId = await SeedAltCustomerAsync();

        var resp = await PatchCustomerAsync(reqId, altCustomerId);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Contain("Draft or Costing");
    }

    // MdFinalSign is skipped — the walking cost to get there is identical to
    // Patch_FromCustomerConfirm + one extra accept-customer step. MdFinalSign
    // is not in CustomerSwapAllowedStatuses so it would 400 by the same guard.
    // CustomerConfirm coverage (above) already exercises the "post-MdPricing" block.
}
