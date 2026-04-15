using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Items;

public class PurchaseLedgerImportTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private static byte[] BuildLedgerXlsx(params (string Code, DateTime Date, decimal Price)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Ledger");
        ws.Cell(1, 1).Value = "SKU";
        ws.Cell(1, 2).Value = "PurchaseDate";
        ws.Cell(1, 3).Value = "Rate";
        for (int i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].Code;
            ws.Cell(i + 2, 2).Value = rows[i].Date;
            ws.Cell(i + 2, 3).Value = rows[i].Price;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task LedgerHeaders_ReturnsColumnNames()
    {
        var token = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var bytes = BuildLedgerXlsx(("SOME-CODE", new DateTime(2026, 1, 1), 12m));
        using var form = new MultipartFormDataContent();
        var fc = new ByteArrayContent(bytes);
        fc.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fc, "file", "ledger.xlsx");

        var resp = await _client.PostAsync("/api/items/import/ledger/headers", form);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<HeadersResponse>();
        body!.Headers.Should().BeEquivalentTo(new[] { "SKU", "PurchaseDate", "Rate" });
    }

    [Fact]
    public async Task LedgerImport_PicksMostRecentPricePerItem()
    {
        // Login as SalesPerson (branchId=1) to create a test item
        var spToken = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spToken);

        var itemCode = $"LGR-{Guid.NewGuid():N}".Substring(0, 20);
        await _client.PostAsJsonAsync("/api/items", new
        {
            Code = itemCode, Description = "Ledger Test Item",
            Type = 0, // ItemType.FinishedGood
            LastPurchasePrice = (decimal?)null
        });

        // Switch to admin for import (only admin can call ledger endpoint)
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var bytes = BuildLedgerXlsx(
            (itemCode, new DateTime(2026, 1, 1), 10m),
            (itemCode, new DateTime(2026, 3, 1), 15m),   // <-- most recent
            (itemCode, new DateTime(2026, 2, 1), 12m),
            ("UNKNOWN-CODE", new DateTime(2026, 3, 5), 99m)
        );

        using var form = new MultipartFormDataContent();
        var fc = new ByteArrayContent(bytes);
        fc.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fc, "file", "ledger.xlsx");
        form.Add(new StringContent("SKU"), "itemCodeColumn");
        form.Add(new StringContent("PurchaseDate"), "dateColumn");
        form.Add(new StringContent("Rate"), "unitPriceColumn");
        form.Add(new StringContent("1"), "branchId");

        var resp = await _client.PostAsync("/api/items/import/ledger", form);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await resp.Content.ReadFromJsonAsync<ImportSummary>();
        result!.Updated.Should().Be(1);
        result.Skipped.Should().Be(1);
        result.UnmatchedCodes.Should().Contain("UNKNOWN-CODE");

        // Verify the saved LastPurchasePrice via GET /api/items (admin sees all branches)
        var items = await _client.GetFromJsonAsync<List<ItemDto>>("/api/items");
        items!.First(i => i.Code == itemCode).LastPurchasePrice.Should().Be(15m);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record HeadersResponse(List<string> Headers);
    private record ImportSummary(int Updated, int Skipped, List<string> UnmatchedCodes);
    private record ItemDto(int Id, string Code, string Description, string Type, int BranchId, bool IsActive, decimal? LastPurchasePrice);
}
