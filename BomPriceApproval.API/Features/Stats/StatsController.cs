using System.Security.Claims;
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
    private string? CurrentRole => User.FindFirstValue(ClaimTypes.Role);

    /// <summary>
    /// V3 unified dashboard endpoint. Branches by role:
    ///   - ManagingDirector → 4 MD KPI counts (toPrice, toSign, inFlight, signedToday).
    ///   - other roles → 403.
    /// Accountant dashboard remains on its own endpoint for back-compat with shipped mobile (D-2).
    /// </summary>
    [HttpGet("v3-dashboard")]
    [Authorize(Roles = "ManagingDirector,Admin")]
    public async Task<IActionResult> V3Dashboard()
    {
        if (CurrentRole == "ManagingDirector" || CurrentRole == "Admin")
        {
            var todayUtc = DateTime.UtcNow.Date;
            var tomorrowUtc = todayUtc.AddDays(1);

            var toPrice = await db.QuotationRequests
                .CountAsync(r => r.Status == RequisitionStatus.MdPricing);
            var toSign = await db.QuotationRequests
                .CountAsync(r => r.Status == RequisitionStatus.MdFinalSign);
            var inFlight = await db.QuotationRequests
                .CountAsync(r => r.Status == RequisitionStatus.CustomerConfirm
                              || r.Status == RequisitionStatus.Costing);
            var signedToday = await db.QuotationRequests
                .CountAsync(r => r.Status == RequisitionStatus.Signed
                              && r.UpdatedAt >= todayUtc
                              && r.UpdatedAt < tomorrowUtc);

            return Ok(new
            {
                toPrice,
                toSign,
                inFlight,
                signedToday,
            });
        }

        return Forbid();
    }

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
        // Costing (Status in {MdPricing, CustomerConfirm, MdFinalSign, Signed}) with UpdatedAt
        // within the current month. Rejected is intentionally excluded — accountant dashboard
        // shows successful MD-bound flows; rejections are surfaced via notifications instead.
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
