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
    public async Task<IActionResult> AccountantDashboard()
    {
        // Accountant has null BranchId per CLAUDE.md (sees all branches), so no branch filter.
        var startOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var pendingCosting = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.CostingPending);

        var inProgress = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.CostingInProgress);

        var submittedThisMonth = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.MdReview && q.UpdatedAt >= startOfMonth);

        var awaitingMd = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.MdReview);

        return Ok(new AccountantDashboardStats(pendingCosting, inProgress, submittedThisMonth, awaitingMd));
    }
}
