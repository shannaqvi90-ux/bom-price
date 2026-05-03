using System.Security.Claims;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Admin;

[ApiController]
[Route("api/admin/company-settings")]
[Authorize(Roles = "Admin")]
public class AdminCompanySettingsController(AppDbContext db, AdminAuditLogger audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CompanySettingsDto>> Get()
    {
        var s = await db.CompanySettings
            .AsNoTracking()
            .Include(x => x.UpdatedByUser)
            .FirstOrDefaultAsync(x => x.Id == 1);
        if (s is null) return NotFound();

        return Ok(new CompanySettingsDto(
            s.CompanyName, s.Address, s.Telephone, s.Trn,
            s.Email, s.Website, s.QuotationValidityDays,
            s.TermsAndConditions, s.UpdatedAt,
            s.UpdatedByUser?.Name));
    }

    [HttpPut]
    public async Task<ActionResult<CompanySettingsDto>> Put([FromBody] UpdateCompanySettingsRequest? body)
    {
        if (body is null)
            return Validation.Detail("Request body is required").Return();

        // Validation
        var v = Validation.Detail("Company settings update is invalid");
        var hasError = false;

        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Trim().Length < 5)
        {
            v.Field("Reason", "Reason is required (min 5 chars).");
            hasError = true;
        }
        if (string.IsNullOrWhiteSpace(body.CompanyName))
        {
            v.Field("CompanyName", "Company name is required.");
            hasError = true;
        }
        if (body.QuotationValidityDays < 1 || body.QuotationValidityDays > 365)
        {
            v.Field("QuotationValidityDays", "Validity must be between 1 and 365 days.");
            hasError = true;
        }
        if (!string.IsNullOrWhiteSpace(body.Email) && !body.Email.Contains('@'))
        {
            v.Field("Email", "Email must contain '@'.");
            hasError = true;
        }
        if (hasError) return v.Return();

        var s = await db.CompanySettings.FirstOrDefaultAsync(x => x.Id == 1);
        if (s is null) return NotFound();

        var before = new
        {
            s.CompanyName, s.Address, s.Telephone, s.Trn, s.Email, s.Website,
            s.QuotationValidityDays, s.TermsAndConditions
        };

        // Trim string fields; normalize T&C line endings to \n
        s.CompanyName = body.CompanyName.Trim();
        s.Address = (body.Address ?? "").Trim();
        s.Telephone = (body.Telephone ?? "").Trim();
        s.Trn = (body.Trn ?? "").Trim();
        s.Email = (body.Email ?? "").Trim();
        s.Website = (body.Website ?? "").Trim();
        s.QuotationValidityDays = body.QuotationValidityDays;
        s.TermsAndConditions = (body.TermsAndConditions ?? "")
            .Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        s.UpdatedAt = DateTime.UtcNow;
        s.UpdatedByUserId = CurrentUserId;

        var after = new
        {
            s.CompanyName, s.Address, s.Telephone, s.Trn, s.Email, s.Website,
            s.QuotationValidityDays, s.TermsAndConditions
        };

        audit.Log(CurrentUserId, AdminActionType.UpdateCompanySettings,
            "CompanySettings", 1, body.Reason.Trim(), before, after);
        await db.SaveChangesAsync();

        // Re-load with the updated user nav so the response carries the name.
        var updatedBy = await db.Users.AsNoTracking()
            .Where(u => u.Id == CurrentUserId)
            .Select(u => u.Name)
            .FirstOrDefaultAsync();

        return Ok(new CompanySettingsDto(
            s.CompanyName, s.Address, s.Telephone, s.Trn,
            s.Email, s.Website, s.QuotationValidityDays,
            s.TermsAndConditions, s.UpdatedAt, updatedBy));
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
}
