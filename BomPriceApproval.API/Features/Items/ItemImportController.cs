using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BomPriceApproval.API.Features.Items;

[ApiController]
[Route("api/items/import")]
[Authorize(Roles = "Admin")]
public class ItemImportController(
    ItemImportService importService,
    PurchaseLedgerService ledgerService) : ControllerBase
{
    [HttpGet("template")]
    public IActionResult Template()
    {
        var bytes = ItemImportService.GenerateTemplate();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "items-template.xlsx");
    }

    [HttpPost]
    public async Task<IActionResult> Import([FromForm] IFormFile file, [FromForm] int branchId)
    {
        if (file.Length == 0) return BadRequest(new { message = "File is empty" });
        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext is not (".xlsx" or ".csv"))
            return BadRequest(new { message = "Only .xlsx and .csv files are supported" });

        using var stream = file.OpenReadStream();
        var result = ext == ".xlsx"
            ? await importService.ImportExcelAsync(stream, branchId)
            : await importService.ImportCsvAsync(stream, branchId);
        return Ok(result);
    }

    [HttpPost("ledger/headers")]
    public IActionResult LedgerHeaders([FromForm] IFormFile file)
    {
        if (file.Length == 0) return BadRequest(new { message = "File is empty" });
        if (Path.GetExtension(file.FileName).ToLower() != ".xlsx")
            return BadRequest(new { message = "Only .xlsx files are supported for ledger import" });

        using var stream = file.OpenReadStream();
        var headers = ledgerService.ExtractHeaders(stream);
        return Ok(new LedgerHeadersResponse(headers));
    }

    [HttpPost("ledger")]
    public async Task<IActionResult> LedgerImport(
        [FromForm] IFormFile file,
        [FromForm] string itemCodeColumn,
        [FromForm] string dateColumn,
        [FromForm] string unitPriceColumn,
        [FromForm] int branchId)
    {
        if (file.Length == 0) return BadRequest(new { message = "File is empty" });
        if (Path.GetExtension(file.FileName).ToLower() != ".xlsx")
            return BadRequest(new { message = "Only .xlsx files are supported for ledger import" });

        using var stream = file.OpenReadStream();
        try
        {
            var result = await ledgerService.ImportAsync(stream, itemCodeColumn, dateColumn, unitPriceColumn, branchId);
            return Ok(new LedgerImportResult(result.Updated, result.Skipped, result.UnmatchedCodes));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
