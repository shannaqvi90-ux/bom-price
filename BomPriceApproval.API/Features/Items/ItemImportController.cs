using BomPriceApproval.API.Infrastructure.Services;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BomPriceApproval.API.Features.Items;

[ApiController]
[Route("api/items/import")]
[Authorize(Roles = "Admin")]
public class ItemImportController(
    ItemImportService importService,
    PurchaseLedgerService ledgerService) : ControllerBase
{
    private const long MaxUploadBytes = 50L * 1024 * 1024; // 50 MB

    private IActionResult? ValidateUpload(IFormFile file, params string[] allowedExts)
    {
        if (file.Length == 0)
            return Validation.Detail("File is empty").Field("File", "File is empty.").Return();
        if (file.Length > MaxUploadBytes)
            return Validation.Detail($"File exceeds maximum size of {MaxUploadBytes / (1024 * 1024)} MB")
                .Field("File", "File too large.").Return();
        var ext = Path.GetExtension(file.FileName).ToLower();
        if (!allowedExts.Contains(ext))
            return Validation.Detail($"Only {string.Join(", ", allowedExts)} files are supported")
                .Field("File", "Unsupported file type.").Return();
        return null;
    }

    [HttpGet("template")]
    public IActionResult Template()
    {
        var bytes = ItemImportService.GenerateTemplate();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "items-template.xlsx");
    }

    [HttpPost]
    [RequestSizeLimit(MaxUploadBytes)]
    [EnableRateLimiting("imports")]
    public async Task<IActionResult> Import([FromForm] IFormFile file, [FromForm] int branchId)
    {
        if (ValidateUpload(file, ".xlsx", ".csv") is { } error) return error;

        using var stream = file.OpenReadStream();
        var ext = Path.GetExtension(file.FileName).ToLower();
        var result = ext == ".xlsx"
            ? await importService.ImportExcelAsync(stream, branchId)
            : await importService.ImportCsvAsync(stream, branchId);
        return Ok(result);
    }

    [HttpPost("ledger/headers")]
    [RequestSizeLimit(MaxUploadBytes)]
    [EnableRateLimiting("imports")]
    public IActionResult LedgerHeaders([FromForm] IFormFile file)
    {
        if (ValidateUpload(file, ".xlsx") is { } error) return error;

        using var stream = file.OpenReadStream();
        var headers = ledgerService.ExtractHeaders(stream);
        return Ok(new LedgerHeadersResponse(headers));
    }

    [HttpPost("ledger")]
    [RequestSizeLimit(MaxUploadBytes)]
    [EnableRateLimiting("imports")]
    public async Task<IActionResult> LedgerImport(
        [FromForm] IFormFile file,
        [FromForm] string itemCodeColumn,
        [FromForm] string dateColumn,
        [FromForm] string unitPriceColumn,
        [FromForm] int branchId)
    {
        if (ValidateUpload(file, ".xlsx") is { } error) return error;

        using var stream = file.OpenReadStream();
        try
        {
            var result = await ledgerService.ImportAsync(stream, itemCodeColumn, dateColumn, unitPriceColumn, branchId);
            return Ok(new LedgerImportResult(result.Updated, result.Skipped, result.UnmatchedCodes));
        }
        catch (InvalidOperationException ex)
        {
            return Validation.Detail(ex.Message).Field("File", "Import failed.").Return();
        }
    }
}
