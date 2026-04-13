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
        var rows = ws.RangeUsed().RowsUsed().Skip(1); // skip header

        var items = rows.Select(row => new
        {
            Code = row.Cell(1).GetString().Trim(),
            Description = row.Cell(2).GetString().Trim(),
            TypeStr = row.Cell(3).GetString().Trim()
        }).ToList();

        return await ImportItemsAsync(items.Select(i => (i.Code, i.Description, i.TypeStr)), branchId);
    }

    public async Task<ImportResult> ImportCsvAsync(Stream stream, int branchId)
    {
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        var records = csv.GetRecords<dynamic>().ToList();

        var items = records.Select(r => (
            Code: (string)r.Code,
            Description: (string)r.Description,
            TypeStr: (string)r.Type
        ));

        return await ImportItemsAsync(items, branchId);
    }

    private async Task<ImportResult> ImportItemsAsync(IEnumerable<(string Code, string Description, string TypeStr)> items, int branchId)
    {
        int imported = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var (code, description, typeStr) in items)
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

            db.Items.Add(new Item { Code = code, Description = description, Type = type, BranchId = branchId });
            imported++;
        }

        await db.SaveChangesAsync();
        return new ImportResult(imported, skipped, errors);
    }
}
