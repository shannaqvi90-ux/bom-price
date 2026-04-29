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

        // V3 status mapping (V2.3 sub-states collapsed):
        //   CostingPending + CostingInProgress -> Costing  (single accountant-active state)
        //   MdReview                           -> MdPricing (V3 name for initial-margin review)
        // V3 has no V2.3-equivalent of "in-progress" sub-state, so the InProgress counter
        // is reported as 0 to preserve the response contract for the (Phase B-rewritten) UI.

        var pendingCosting = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.Costing);

        const int inProgress = 0; // V3 collapsed sub-state; field kept for FE-compat.

        var submittedThisMonth = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.MdPricing && q.UpdatedAt >= startOfMonth);

        var awaitingMd = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.MdPricing);

        return Ok(new AccountantDashboardStats(
            PendingCosting: pendingCosting,
            InProgress: inProgress,
            SubmittedThisMonth: submittedThisMonth,
            AwaitingMd: awaitingMd));
    }
}
