using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Stats;

[ApiController]
[Route("api/stats")]
[Authorize]
public class StatsController(AppDbContext db) : ControllerBase
{
    [HttpGet("accountant-dashboard")]
    [Authorize(Roles = "Accountant,Admin")]
    public async Task<ActionResult<AccountantDashboardV3Dto>> AccountantDashboard()
    {
        // Counts are global (cross-branch) by design — accountants triage across all branches.
        var startOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var costing = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.Costing);

        var awaitingMd = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.MdPricing
                          || q.Status == RequisitionStatus.MdFinalSign);

        var awaitingCustomer = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.CustomerConfirm);

        // Proxy for "costing submitted this calendar month": reqs that have passed through
        // Costing (Status >= MdPricing) with UpdatedAt within the current month.
        var submittedThisMonth = await db.QuotationRequests
            .CountAsync(q =>
                (q.Status == RequisitionStatus.MdPricing
                 || q.Status == RequisitionStatus.CustomerConfirm
                 || q.Status == RequisitionStatus.MdFinalSign
                 || q.Status == RequisitionStatus.Signed)
                && q.UpdatedAt >= startOfMonth);

        return Ok(new AccountantDashboardV3Dto(costing, awaitingMd, awaitingCustomer, submittedThisMonth));
    }
}
