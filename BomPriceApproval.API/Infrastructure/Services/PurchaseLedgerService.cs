using BomPriceApproval.API.Infrastructure.Data;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Infrastructure.Services;

public record LedgerImportSummary(int Updated, int Skipped, List<string> UnmatchedCodes);

public class PurchaseLedgerService(AppDbContext db)
{
    public List<string> ExtractHeaders(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);
        var headerRow = ws.FirstRowUsed();
        return headerRow.CellsUsed().Select(c => c.GetString().Trim()).ToList();
    }

    public async Task<LedgerImportSummary> ImportAsync(
        Stream stream, string itemCodeColumn, string dateColumn, string unitPriceColumn, int branchId)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);

        var headerRow = ws.FirstRowUsed();
        var headerToIndex = headerRow.CellsUsed()
            .ToDictionary(c => c.GetString().Trim(), c => c.Address.ColumnNumber);

        if (!headerToIndex.TryGetValue(itemCodeColumn, out var codeCol))
            throw new InvalidOperationException($"Column '{itemCodeColumn}' not found");
        if (!headerToIndex.TryGetValue(dateColumn, out var dateCol))
            throw new InvalidOperationException($"Column '{dateColumn}' not found");
        if (!headerToIndex.TryGetValue(unitPriceColumn, out var priceCol))
            throw new InvalidOperationException($"Column '{unitPriceColumn}' not found");

        // Parse rows (skip header)
        var rows = ws.RangeUsed().RowsUsed().Skip(1).Select(row => new
        {
            Code = row.Cell(codeCol).GetString().Trim(),
            Date = ParseDate(row.Cell(dateCol)),
            Price = row.Cell(priceCol).TryGetValue<decimal>(out var p) ? p : 0m
        }).Where(r => !string.IsNullOrEmpty(r.Code) && r.Date != DateTime.MinValue && r.Price > 0);

        // Group by item code, pick most recent row per code
        var latestByCode = rows
            .GroupBy(r => r.Code)
            .Select(g => g.OrderByDescending(r => r.Date).First())
            .ToList();

        int updated = 0, skipped = 0;
        var unmatched = new List<string>();

        foreach (var entry in latestByCode)
        {
            var item = await db.Items.FirstOrDefaultAsync(i => i.Code == entry.Code && i.BranchId == branchId);
            if (item is null)
            {
                unmatched.Add(entry.Code);
                skipped++;
                continue;
            }
            item.LastPurchasePrice = entry.Price;
            updated++;
        }

        await db.SaveChangesAsync();
        return new LedgerImportSummary(updated, skipped, unmatched);
    }

    private static DateTime ParseDate(ClosedXML.Excel.IXLCell cell)
    {
        if (cell.TryGetValue<DateTime>(out var dt)) return dt;
        if (DateTime.TryParse(cell.GetString(), out var parsed)) return parsed;
        // ClosedXML stores dates as doubles after round-trip; try double → DateTime
        if (cell.TryGetValue<double>(out var d) && d > 0)
        {
            try { return DateTime.FromOADate(d); } catch { }
        }
        return DateTime.MinValue;
    }
}
