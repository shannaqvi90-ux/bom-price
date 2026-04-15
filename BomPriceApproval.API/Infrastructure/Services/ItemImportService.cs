using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using ClosedXML.Excel;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace BomPriceApproval.API.Infrastructure.Services;

public record ImportResult(int Imported, int Skipped, List<string> Errors);

public class ItemImportService(AppDbContext db)
{
    public async Task<ImportResult> ImportExcelAsync(Stream stream, int branchId)
    {
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheet(1);
        var rows = ws.RangeUsed().RowsUsed().Skip(1);

        var items = rows.Select(row => (
            Code: row.Cell(1).GetString().Trim(),
            Description: row.Cell(2).GetString().Trim(),
            TypeStr: row.Cell(3).GetString().Trim(),
            LastPurchasePrice: row.Cell(4).TryGetValue<decimal>(out var p) ? p : (decimal?)null
        )).ToList();

        return await ImportItemsAsync(items, branchId);
    }

    public async Task<ImportResult> ImportCsvAsync(Stream stream, int branchId)
    {
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        var records = csv.GetRecords<CsvRow>().ToList();
        var items = records.Select(r => (
            r.Code, r.Description, TypeStr: r.Type,
            LastPurchasePrice: (decimal?)r.LastPurchasePrice
        ));
        return await ImportItemsAsync(items, branchId);
    }

    private async Task<ImportResult> ImportItemsAsync(
        IEnumerable<(string Code, string Description, string TypeStr, decimal? LastPurchasePrice)> items,
        int branchId)
    {
        int imported = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var (code, description, typeStr, price) in items)
        {
            if (!Enum.TryParse<ItemType>(typeStr, ignoreCase: true, out var type))
            {
                errors.Add($"Invalid type '{typeStr}' for item code '{code}'");
                skipped++;
                continue;
            }

            if (await db.Items.AnyAsync(i => i.Code == code && i.BranchId == branchId))
            {
                skipped++;
                continue;
            }

            db.Items.Add(new Item
            {
                Code = code, Description = description, Type = type,
                BranchId = branchId, LastPurchasePrice = price
            });
            imported++;
        }

        await db.SaveChangesAsync();
        return new ImportResult(imported, skipped, errors);
    }

    public static byte[] GenerateTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Items");
        ws.Cell(1, 1).Value = "Code";
        ws.Cell(1, 2).Value = "Description";
        ws.Cell(1, 3).Value = "Type";
        ws.Cell(1, 4).Value = "LastPurchasePrice";
        ws.Row(1).Style.Font.Bold = true;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private class CsvRow
    {
        public string Code { get; set; } = "";
        public string Description { get; set; } = "";
        public string Type { get; set; } = "";
        public decimal LastPurchasePrice { get; set; }
    }
}
