using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Shared;

/// <summary>
/// Shared helpers for V3 workflow integration tests. Centralizes seed users,
/// authenticated-client construction, the V3 Create payload, and the multi-stage
/// "walks" used by approval-split + workflow happy-path tests.
///
/// V3 reqs are pinned to Al Ain (BranchId=2) by RequisitionsController.Create —
/// items must live there or Create rejects them.
/// </summary>
public static class V3WorkflowTestHelpers
{
    /// <summary>Al Ain branch id (must match the seed in AppDbContext.HasData).</summary>
    public const int AlainBranchId = 2;

    // ─── Token / authenticated-client helpers ───────────────────────────────

    public static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    public static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory, string email, string password)
    {
        var client = factory.CreateClient();
        var token = await LoginAsync(client, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static Task<HttpClient> CreateSalesClientAsync(WebApplicationFactory<Program> factory)
        => CreateAuthenticatedClientAsync(factory, "ali@test.com", "Test@1234");

    public static Task<HttpClient> CreateAccountantClientAsync(WebApplicationFactory<Program> factory)
        => CreateAuthenticatedClientAsync(factory, "sara@test.com", "Test@1234");

    public static Task<HttpClient> CreateMdClientAsync(WebApplicationFactory<Program> factory)
        => CreateAuthenticatedClientAsync(factory, "md@test.com", "Test@1234");

    /// <summary>
    /// Admin client — used for V3 walks that hit Costing endpoints. Sara (sara@test.com)
    /// is seeded with BranchId=Fujairah(1) which CostingController treats as a binding
    /// constraint via the JWT branchId claim, so an Accountant token can't submit a
    /// V3 req pinned to Alain. Admin accepts the Costing role check + has null BranchId.
    /// </summary>
    public static Task<HttpClient> CreateAdminClientAsync(WebApplicationFactory<Program> factory)
        => CreateAuthenticatedClientAsync(factory, "admin@test.com", "Admin@1234");

    // ─── Seeding ────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds the minimum entities needed for a V3 requisition (customer in Al Ain,
    /// one FG and one RM in Al Ain, and ensures at least one Process is present).
    /// All names/codes carry a Guid suffix to avoid collisions across tests.
    /// </summary>
    public static async Task<(int CustomerId, int FgItemId, int RmItemId, int ProcessId)> SeedV3MinimumAsync(
        WebApplicationFactory<Program> factory)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ali = await db.Users.FirstAsync(u => u.Email == "ali@test.com");

        var customer = new Customer
        {
            Code = $"V3CUST-{suffix}",
            Name = $"V3 Customer {suffix}",
            Address = "",
            Email = "",
            PhoneNumber = "",
            SalesPersonId = ali.Id,
            CreatedByUserId = ali.Id
        };
        db.Customers.Add(customer);

        var fg = new Item
        {
            Code = $"V3FG-{suffix}",
            Description = $"V3 FG {suffix}",
            Type = ItemType.FinishedGood,
            BranchId = AlainBranchId,
            IsActive = true
        };
        var rm = new Item
        {
            Code = $"V3RM-{suffix}",
            Description = $"V3 RM {suffix}",
            Type = ItemType.RawMaterial,
            BranchId = AlainBranchId,
            IsActive = true
        };
        db.Items.Add(fg);
        db.Items.Add(rm);

        var process = await db.Processes.FirstOrDefaultAsync(p => p.IsActive);
        if (process is null)
        {
            process = new Process { Name = $"Extrusion-{suffix}", DisplayOrder = 1, IsActive = true };
            db.Processes.Add(process);
        }

        await db.SaveChangesAsync();

        return (customer.Id, fg.Id, rm.Id, process.Id);
    }

    /// <summary>
    /// Clears the MD's seeded signature path so signature-related tests can
    /// assert the "no signature on file" branch independent of execution order.
    /// Does NOT delete the on-disk file (cheap to leave, harmless to re-overwrite).
    /// </summary>
    public static async Task ClearMdSignatureAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var md = await db.Users.FirstAsync(u => u.Email == "md@test.com");
        md.SignatureImagePath = null;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Sara's seeded BranchId is Fujairah (1). For V3 (Alain-only) + the V3
    /// Costing.Submit notification fan-out (which queries UserBranches for
    /// branch-scoped accountants), she needs a UserBranches row for Alain.
    /// Idempotent — safe to call from multiple tests.
    /// </summary>
    public static async Task EnsureAccountantInAlainAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sara = await db.Users.FirstAsync(u => u.Email == "sara@test.com");
        var exists = await db.UserBranches.AnyAsync(ub => ub.UserId == sara.Id && ub.BranchId == AlainBranchId);
        if (!exists)
        {
            db.UserBranches.Add(new UserBranch { UserId = sara.Id, BranchId = AlainBranchId });
            await db.SaveChangesAsync();
        }
    }

    // ─── Workflow walks ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds the V3 inline-BOM payload and POSTs it as ali (SalesPerson).
    /// V3 controller pins the req to Al Ain regardless of caller's BranchId.
    /// Returns the parsed status code, requisition id, and the body status string.
    /// </summary>
    public static async Task<(HttpStatusCode StatusCode, int RequisitionId, string Status)> CreateV3DraftRequisitionAsync(
        HttpClient salesClient, int customerId, int fgItemId, int rmItemId, int processId)
    {
        var payload = new
        {
            customerId,
            quotationCurrency = "USD",
            referenceNumber = "PO-9941",
            notes = "Test V3 happy path",
            finishedGoods = new[]
            {
                new
                {
                    itemId = fgItemId,
                    expectedQtyKg = 5000m,
                    printing = true,
                    bomLines = new[]
                    {
                        new { processId, itemId = rmItemId, qtyPerKg = 0.44m, micron = "20" }
                    }
                }
            }
        };

        var resp = await salesClient.PostAsJsonAsync("/api/requisitions", payload);
        if (resp.StatusCode != HttpStatusCode.Created)
            return (resp.StatusCode, 0, string.Empty);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return (resp.StatusCode, body.GetProperty("id").GetInt32(), body.GetProperty("status").GetString()!);
    }

    /// <summary>
    /// Walks Draft → Costing: SP creates requisition via V3 inline payload, then
    /// SP submits to transition to Costing status. Returns the new req id.
    /// </summary>
    public static async Task<int> WalkToCostingAsync(WebApplicationFactory<Program> factory)
    {
        var salesClient = await CreateSalesClientAsync(factory);
        var (customerId, fgId, rmId, processId) = await SeedV3MinimumAsync(factory);
        var (createStatus, reqId, _) = await CreateV3DraftRequisitionAsync(salesClient, customerId, fgId, rmId, processId);
        if (createStatus != HttpStatusCode.Created)
            throw new InvalidOperationException($"V3 Create failed with {createStatus}");

        var submit = await salesClient.PostAsync($"/api/requisitions/{reqId}/submit", content: null);
        submit.EnsureSuccessStatusCode();
        return reqId;
    }

    /// <summary>
    /// Direct DB seed of BomCost + BomCostLine for every FG on the requisition,
    /// so the V3 Costing.Submit "all FGs costed" check passes. The V3 Phase A
    /// HTTP API doesn't expose a "set cost" endpoint yet (Phase B), and going
    /// direct keeps test setup small and stable. Sets SubmittedByUserId to sara.
    /// </summary>
    public static async Task<int> PopulateBomCostAsync(WebApplicationFactory<Program> factory, int reqId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sara = await db.Users.FirstAsync(u => u.Email == "sara@test.com");

        var boms = await db.BomHeaders
            .Include(b => b.Lines)
            .Where(b => b.RequisitionItem.QuotationRequestId == reqId)
            .ToListAsync();

        foreach (var bom in boms)
        {
            // Replace any existing cost so the helper is re-runnable.
            var existingCost = await db.BomCosts.FirstOrDefaultAsync(c => c.BomHeaderId == bom.Id);
            if (existingCost is not null) db.BomCosts.Remove(existingCost);
            var existingLines = await db.BomCostLines.Where(l => l.BomHeaderId == bom.Id).ToListAsync();
            if (existingLines.Count > 0) db.BomCostLines.RemoveRange(existingLines);

            const decimal costPerKg = 10m;
            decimal rmTotal = 0m;
            foreach (var line in bom.Lines)
            {
                rmTotal += costPerKg * line.QtyPerKg * (1 + line.WastagePct / 100m);
                db.BomCostLines.Add(new BomCostLine
                {
                    BomHeaderId = bom.Id,
                    BomLineId = line.Id,
                    CostPerKg = costPerKg,
                    CurrencyCode = "AED",
                    CostPerKgInQuoteCurrency = costPerKg,
                    CostPerKgInAed = costPerKg
                });
            }

            db.BomCosts.Add(new BomCost
            {
                BomHeaderId = bom.Id,
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

            bom.TotalCostPerKg = rmTotal;
        }

        await db.SaveChangesAsync();
        return reqId;
    }

    /// <summary>
    /// Walks Draft → Costing → MdPricing: WalkToCostingAsync + PopulateBomCostAsync +
    /// accountant Submit. Returns the req id at MdPricing status.
    /// </summary>
    public static async Task<int> WalkToMdPricingAsync(WebApplicationFactory<Program> factory)
    {
        await EnsureAccountantInAlainAsync(factory);
        var reqId = await WalkToCostingAsync(factory);
        await PopulateBomCostAsync(factory, reqId);

        // Use admin (BranchId=null) for the Costing.Submit call: V3 reqs are pinned
        // to Alain, but sara's seeded JWT branchId claim is Fujairah(1) which trips
        // CostingController's branch guard. Admin role is accepted by the endpoint.
        var admin = await CreateAdminClientAsync(factory);
        var submit = await admin.PostAsync($"/api/costing/{reqId}/submit", content: null);
        submit.EnsureSuccessStatusCode();
        return reqId;
    }

    /// <summary>
    /// Walks to CustomerConfirm: WalkToMdPricingAsync + MD set-margin (one entry per FG).
    /// Returns the req id at CustomerConfirm status.
    /// </summary>
    public static async Task<int> WalkToCustomerConfirmAsync(WebApplicationFactory<Program> factory)
    {
        var reqId = await WalkToMdPricingAsync(factory);

        var anyClient = await CreateSalesClientAsync(factory);
        var reqItemIds = await GetReqItemIdsAsync(anyClient, reqId);

        var md = await CreateMdClientAsync(factory);
        var setMargin = await md.PostAsJsonAsync($"/api/approvals/{reqId}/set-margin", new
        {
            notes = "Initial margin",
            items = reqItemIds.Select(id => new { requisitionItemId = id, marginPerKg = 1.5m }).ToArray()
        });
        setMargin.EnsureSuccessStatusCode();
        return reqId;
    }

    /// <summary>
    /// Walks to MdFinalSign: WalkToCustomerConfirmAsync + SP accept-customer.
    /// Returns the req id at MdFinalSign status.
    /// </summary>
    public static async Task<int> WalkToMdFinalSignAsync(WebApplicationFactory<Program> factory)
    {
        var reqId = await WalkToCustomerConfirmAsync(factory);

        var sales = await CreateSalesClientAsync(factory);
        var accept = await sales.PostAsJsonAsync($"/api/approvals/{reqId}/accept-customer",
            new { customerFeedback = "Customer agreed on the call" });
        accept.EnsureSuccessStatusCode();
        return reqId;
    }

    // ─── Inspection ─────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the requisition detail from /api/requisitions/{id} and returns the
    /// list of RequisitionItem ids in their original order. Caller must already
    /// be authenticated (any role with read access).
    /// </summary>
    public static async Task<List<int>> GetReqItemIdsAsync(HttpClient anyAuthenticatedClient, int reqId)
    {
        var resp = await anyAuthenticatedClient.GetAsync($"/api/requisitions/{reqId}");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var ids = new List<int>();
        if (body.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
                ids.Add(item.GetProperty("id").GetInt32());
        }
        return ids;
    }
}
