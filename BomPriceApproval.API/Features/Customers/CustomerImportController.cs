using System.Security.Claims;
using BomPriceApproval.API.Infrastructure.Services;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BomPriceApproval.API.Features.Customers;

[ApiController]
[Route("api/customers/import")]
[Authorize(Roles = "Admin")]
public class CustomerImportController(CustomerImportService importService) : ControllerBase
{
    [HttpGet("template")]
    public IActionResult Template()
    {
        var bytes = CustomerImportService.GenerateTemplate();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "customers-template.xlsx");
    }

    [HttpPost]
    public async Task<IActionResult> Import([FromForm] IFormFile file)
    {
        if (file.Length == 0)
            return Validation.Detail("File is empty").Field("File", "File is empty.").Return();

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext is not (".xlsx" or ".csv"))
            return Validation.Detail("Only .xlsx and .csv files are supported").Field("File", "Only .xlsx and .csv files are supported.").Return();

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        using var stream = file.OpenReadStream();
        var result = ext == ".xlsx"
            ? await importService.ImportExcelAsync(stream, userId)
            : await importService.ImportCsvAsync(stream, userId);

        return Ok(result);
    }
}
