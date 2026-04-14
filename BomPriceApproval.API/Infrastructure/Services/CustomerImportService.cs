using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using ClosedXML.Excel;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace BomPriceApproval.API.Infrastructure.Services;

public class CustomerImportService(AppDbContext db)
{
    public async Task<ImportResult> ImportExcelAsync(Stream stream, int createdByUserId)
    {
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheet(1);
        var rows = ws.RangeUsed().RowsUsed().Skip(1); // skip header

        var records = rows.Select(row => (
            Code: row.Cell(1).GetString().Trim(),
            Name: row.Cell(2).GetString().Trim(),
            Address: row.Cell(3).GetString().Trim(),
            Email: row.Cell(4).GetString().Trim(),
            Phone: row.Cell(5).GetString().Trim()
        )).ToList();

        return await ImportAsync(records, createdByUserId);
    }

    public async Task<ImportResult> ImportCsvAsync(Stream stream, int createdByUserId)
    {
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        var records = csv.GetRecords<CsvRow>()
            .Select(r => (r.Code, r.Name, r.Address, r.Email, r.PhoneNumber))
            .ToList();
        return await ImportAsync(records, createdByUserId);
    }

    private async Task<ImportResult> ImportAsync(
        IEnumerable<(string Code, string Name, string Address, string Email, string Phone)> records,
        int createdByUserId)
    {
        int imported = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var (code, name, address, email, phone) in records)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                errors.Add($"Skipped row with missing code (name='{name}')");
                skipped++;
                continue;
            }

            if (await db.Customers.AnyAsync(c => c.Code == code))
            {
                skipped++;
                continue;
            }

            db.Customers.Add(new Customer
            {
                Code = code,
                Name = name,
                Address = address,
                Email = email,
                PhoneNumber = phone,
                SalesPersonId = null,
                CreatedByUserId = createdByUserId
            });
            imported++;
        }

        await db.SaveChangesAsync();
        return new ImportResult(imported, skipped, errors);
    }

    public static byte[] GenerateTemplate()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Customers");
        ws.Cell(1, 1).Value = "Code";
        ws.Cell(1, 2).Value = "Name";
        ws.Cell(1, 3).Value = "Address";
        ws.Cell(1, 4).Value = "Email";
        ws.Cell(1, 5).Value = "PhoneNumber";
        ws.Row(1).Style.Font.Bold = true;
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private class CsvRow
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Address { get; set; } = "";
        public string Email { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
    }
}
