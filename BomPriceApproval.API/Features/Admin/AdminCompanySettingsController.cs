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

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
}
