using System.Net;
using System.Net.Http.Headers;
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
/// V3 happy-path coverage for the new sales+BOM combined Create flow,
/// the Submit (Draft -> Costing) transition, and Cancel guard rails.
/// All seeded entities (customer + FG + RM) are Guid-isolated so tests
/// in the same class run independently.
/// </summary>
public class V3RequisitionWorkflowTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = factory;

    private async Task<string> LoginAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    /// <summary>
    /// Seeds the minimum entities needed for a V3 requisition (customer in Al Ain,
    /// one FG and one RM in Al Ain, and ensures at least one Process is present).
    /// All names/codes carry a Guid suffix to avoid collisions across tests.
    /// </summary>
    private async Task<(int CustomerId, int FgItemId, int RmItemId, int ProcessId)> SeedV3MinimumAsync()
    {
        const int alainBranchId = 2;
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Sales person seed user (ali) — used only as CreatedByUserId placeholder
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
            BranchId = alainBranchId,
            IsActive = true
        };
        var rm = new Item
        {
            Code = $"V3RM-{suffix}",
            Description = $"V3 RM {suffix}",
            Type = ItemType.RawMaterial,
            BranchId = alainBranchId,
            IsActive = true
        };
        db.Items.Add(fg);
        db.Items.Add(rm);

        var process = await db.Processes.FirstOrDefaultAsync(p => p.IsActive);
        if (process is null)
        {
            process = new Process { Name = "Extrusion", DisplayOrder = 1, IsActive = true };
            db.Processes.Add(process);
        }

        await db.SaveChangesAsync();

        return (customer.Id, fg.Id, rm.Id, process.Id);
    }

    /// <summary>
    /// Builds the V3 inline-BOM payload and POSTs it as ali (SalesPerson, Al Ain branch).
    /// Returns the parsed status code, requisition id, and the body string so callers
    /// can do their own assertions without re-reading the (already-consumed) stream.
    /// </summary>
    private async Task<(HttpStatusCode StatusCode, int RequisitionId, string Status)> CreateV3DraftRequisitionAsync(
        HttpClient client, int customerId, int fgItemId, int rmItemId, int processId)
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

        var resp = await client.PostAsJsonAsync("/api/requisitions", payload);
        if (resp.StatusCode != HttpStatusCode.Created)
            return (resp.StatusCode, 0, string.Empty);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return (resp.StatusCode, body.GetProperty("id").GetInt32(), body.GetProperty("status").GetString()!);
    }

    [Fact]
    public async Task Sales_CreatesRequisition_WithInlineBOM_StartsAsDraft()
    {
        var token = await LoginAsync("ali@test.com", "Test@1234");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (customerId, fgId, rmId, processId) = await SeedV3MinimumAsync();
        var (statusCode, reqId, status) = await CreateV3DraftRequisitionAsync(client, customerId, fgId, rmId, processId);

        statusCode.Should().Be(HttpStatusCode.Created);
        status.Should().Be("Draft");
        reqId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Sales_SubmitsRequisition_TransitionsToCosting()
    {
        var token = await LoginAsync("ali@test.com", "Test@1234");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (customerId, fgId, rmId, processId) = await SeedV3MinimumAsync();
        var (_, reqId, _) = await CreateV3DraftRequisitionAsync(client, customerId, fgId, rmId, processId);

        var submit = await client.PostAsync($"/api/requisitions/{reqId}/submit", content: null);
        submit.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await submit.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Costing");
    }

    [Fact]
    public async Task Submit_FromCostingStatus_ReturnsBadRequest()
    {
        var token = await LoginAsync("ali@test.com", "Test@1234");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (customerId, fgId, rmId, processId) = await SeedV3MinimumAsync();
        var (_, reqId, _) = await CreateV3DraftRequisitionAsync(client, customerId, fgId, rmId, processId);

        // First submit transitions Draft -> Costing
        var firstSubmit = await client.PostAsync($"/api/requisitions/{reqId}/submit", content: null);
        firstSubmit.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second submit must be rejected — already Costing
        var secondSubmit = await client.PostAsync($"/api/requisitions/{reqId}/submit", content: null);
        secondSubmit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cancel_DraftRequisition_TransitionsToCancelled()
    {
        var token = await LoginAsync("ali@test.com", "Test@1234");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (customerId, fgId, rmId, processId) = await SeedV3MinimumAsync();
        var (_, reqId, _) = await CreateV3DraftRequisitionAsync(client, customerId, fgId, rmId, processId);

        var cancel = await client.PostAsJsonAsync($"/api/requisitions/{reqId}/cancel",
            new { reason = "Customer withdrew" });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await cancel.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Cancelled");
    }

    [Fact]
    public async Task Cancel_WithoutReason_ReturnsBadRequest()
    {
        var token = await LoginAsync("ali@test.com", "Test@1234");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (customerId, fgId, rmId, processId) = await SeedV3MinimumAsync();
        var (_, reqId, _) = await CreateV3DraftRequisitionAsync(client, customerId, fgId, rmId, processId);

        // Reason "x" is below the 5-char minimum guard.
        var cancel = await client.PostAsJsonAsync($"/api/requisitions/{reqId}/cancel",
            new { reason = "x" });
        cancel.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
