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
/// Round-trips a V3 req through Create → GET and asserts the V3 response shape
/// (nested finishedGoods[].bomLines[] + customer/salesPerson summaries) so the
/// bom-web V3Requisition contract stays in sync with the backend projection.
/// </summary>
public class V3RequisitionGetShapeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = factory;

    [Fact]
    public async Task Get_AfterV3Create_ReturnsV3ShapeWithNestedBomLines()
    {
        var sales = await V3WorkflowTestHelpers.CreateSalesClientAsync(_factory);

        // Seed customer + FG + 2 RMs in Al Ain so we can assert two bomLines round-trip
        var suffix = Guid.NewGuid().ToString("N")[..8];
        int customerId, fgItemId, rm1Id, rm2Id, processId;
        string customerCode, fgCode, rm1Code, rm2Code;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ali = await db.Users.FirstAsync(u => u.Email == "ali@test.com");

            var seedCustomer = new Customer
            {
                Code = $"V3SHAPE-{suffix}",
                Name = $"V3 Shape Cust {suffix}",
                Address = "",
                Email = "",
                PhoneNumber = "",
                SalesPersonId = ali.Id,
                CreatedByUserId = ali.Id
            };
            db.Customers.Add(seedCustomer);

            var fg = new Item
            {
                Code = $"SHAPE-FG-{suffix}",
                Description = $"Shape FG {suffix}",
                Type = ItemType.FinishedGood,
                BranchId = V3WorkflowTestHelpers.AlainBranchId,
                IsActive = true
            };
            var rm1 = new Item
            {
                Code = $"SHAPE-RM1-{suffix}",
                Description = $"Shape RM1 {suffix}",
                Type = ItemType.RawMaterial,
                BranchId = V3WorkflowTestHelpers.AlainBranchId,
                IsActive = true
            };
            var rm2 = new Item
            {
                Code = $"SHAPE-RM2-{suffix}",
                Description = $"Shape RM2 {suffix}",
                Type = ItemType.RawMaterial,
                BranchId = V3WorkflowTestHelpers.AlainBranchId,
                IsActive = true
            };
            db.Items.AddRange(fg, rm1, rm2);

            var process = await db.Processes.FirstOrDefaultAsync(p => p.IsActive);
            if (process is null)
            {
                process = new Process { Name = $"Extrusion-{suffix}", DisplayOrder = 1, IsActive = true };
                db.Processes.Add(process);
            }

            await db.SaveChangesAsync();

            customerId = seedCustomer.Id;
            fgItemId = fg.Id;
            rm1Id = rm1.Id;
            rm2Id = rm2.Id;
            processId = process.Id;
            customerCode = seedCustomer.Code;
            fgCode = fg.Code;
            rm1Code = rm1.Code;
            rm2Code = rm2.Code;
        }

        const string notesText = "V3 GET shape — round-trip note";

        // Create V3 req: 1 FG with 2 BOM lines
        var createPayload = new
        {
            customerId,
            quotationCurrency = "USD",
            referenceNumber = "PO-V3-SHAPE",
            notes = notesText,
            finishedGoods = new[]
            {
                new
                {
                    itemId = fgItemId,
                    expectedQtyKg = 5000m,
                    printing = true,
                    bomLines = new[]
                    {
                        new { processId, itemId = rm1Id, qtyPerKg = 0.40m, micron = "20" },
                        new { processId, itemId = rm2Id, qtyPerKg = 0.60m, micron = "25" }
                    }
                }
            }
        };

        var createResp = await sales.PostAsJsonAsync("/api/requisitions", createPayload);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var createBody = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var reqId = createBody.GetProperty("id").GetInt32();
        reqId.Should().BeGreaterThan(0);

        // GET and assert V3 shape
        var getResp = await sales.GetAsync($"/api/requisitions/{reqId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await getResp.Content.ReadFromJsonAsync<JsonElement>();

        // Top-level
        detail.GetProperty("id").GetInt32().Should().Be(reqId);
        detail.GetProperty("status").GetString().Should().Be("Draft");
        detail.GetProperty("currencyCode").GetString().Should().Be("USD");
        detail.GetProperty("notes").GetString().Should().Be(notesText);
        detail.GetProperty("refNo").GetString().Should().NotBeNullOrEmpty();

        // Customer summary { id, name, code }
        var customer = detail.GetProperty("customer");
        customer.GetProperty("id").GetInt32().Should().Be(customerId);
        customer.GetProperty("code").GetString().Should().Be(customerCode);
        customer.GetProperty("name").GetString().Should().Contain(suffix);

        // SalesPerson summary { id, name }
        var salesPerson = detail.GetProperty("salesPerson");
        salesPerson.GetProperty("id").GetInt32().Should().BeGreaterThan(0);
        salesPerson.GetProperty("name").GetString().Should().NotBeNullOrEmpty();

        // FinishedGoods array — exactly 1 entry
        var finishedGoods = detail.GetProperty("finishedGoods");
        finishedGoods.GetArrayLength().Should().Be(1);

        var fg0 = finishedGoods[0];
        fg0.GetProperty("id").GetInt32().Should().BeGreaterThan(0);
        fg0.GetProperty("expectedQty").GetDecimal().Should().Be(5000m);
        fg0.GetProperty("hasPrinting").GetBoolean().Should().BeTrue();

        var fgItem = fg0.GetProperty("item");
        fgItem.GetProperty("id").GetInt32().Should().Be(fgItemId);
        fgItem.GetProperty("code").GetString().Should().Be(fgCode);
        fgItem.GetProperty("description").GetString().Should().Contain(suffix);

        // BomLines: 2 entries, ordered (process display order then id) — both lines share the
        // same process so they should fall back to id-ascending order, matching insert order.
        fg0.GetProperty("bomLines").ValueKind.Should().Be(JsonValueKind.Array);
        var bomLines = fg0.GetProperty("bomLines");
        bomLines.GetArrayLength().Should().Be(2);

        var line0 = bomLines[0];
        line0.GetProperty("qtyPerKg").GetDecimal().Should().Be(0.40m);
        line0.GetProperty("micron").GetString().Should().Be("20");
        line0.GetProperty("item").GetProperty("code").GetString().Should().Be(rm1Code);

        var line1 = bomLines[1];
        line1.GetProperty("qtyPerKg").GetDecimal().Should().Be(0.60m);
        line1.GetProperty("micron").GetString().Should().Be("25");
        line1.GetProperty("item").GetProperty("code").GetString().Should().Be(rm2Code);

        // Costs is null when no costing has been submitted yet (Draft state)
        fg0.GetProperty("costs").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_HistoricalV2ReqWithoutBomHeader_ReturnsBomLinesNullAndCostsNull()
    {
        // Seed a V2.3-style legacy req that has a RequisitionItem WITHOUT a BomHeader.
        // The V3 GET shape must emit bomLines: null + costs: null in this case (advisor's
        // null-safety call). Direct DB seed avoids going through V3 Create which always
        // creates a BomHeader.
        int reqId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ali = await db.Users.FirstAsync(u => u.Email == "ali@test.com");
            var customer = await db.Customers.FirstAsync();
            var item = await db.Items.FirstAsync();

            var req = new QuotationRequest
            {
                BranchId = ali.BranchId!.Value,
                SalesPersonId = ali.Id,
                CustomerId = customer.Id,
                CurrencyCode = "AED",
                Status = RequisitionStatus.BomPending  // legacy V2.3 status
            };
            db.QuotationRequests.Add(req);
            await db.SaveChangesAsync();

            db.RequisitionItems.Add(new RequisitionItem
            {
                QuotationRequestId = req.Id,
                ItemId = item.Id,
                ExpectedQty = 100m,
                SortOrder = 1
            });
            await db.SaveChangesAsync();
            reqId = req.Id;
        }

        var md = await V3WorkflowTestHelpers.CreateMdClientAsync(_factory);
        var resp = await md.GetAsync($"/api/requisitions/{reqId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var fgs = detail.GetProperty("finishedGoods");
        fgs.GetArrayLength().Should().Be(1);
        var fg0 = fgs[0];
        fg0.GetProperty("bomLines").ValueKind.Should().Be(JsonValueKind.Null);
        fg0.GetProperty("costs").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
