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
        // Spec §4.1.1: Accountant dashboard counts are global (cross-branch) by design.
        // Admin is also branch-less. Branch filter intentionally omitted.

        // Spec §10 #1: count by UTC month; ~5h skew vs PKT is accepted imprecision.
        var startOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var pendingCosting = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.CostingPending);

        var inProgress = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.CostingInProgress);

        var submittedThisMonth = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.MdReview && q.UpdatedAt >= startOfMonth);

        var awaitingMd = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.MdReview);

        return Ok(new AccountantDashboardStats(
            PendingCosting: pendingCosting,
            InProgress: inProgress,
            SubmittedThisMonth: submittedThisMonth,
            AwaitingMd: awaitingMd));
    }
}
