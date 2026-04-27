using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Admin;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminRequisitionsController(
    AppDbContext db,
    AdminAuditLogger audit,
    NotificationService notify,
    EmailService emailSvc,
    PdfService pdfSvc,
    ILogger<AdminRequisitionsController> logger) : ControllerBase
{
    private const decimal PercentSumTolerance = 0.01m;

    [HttpPost("requisitions/{id}/override-prices")]
    public async Task<IActionResult> OverridePrices(int id, [FromBody] OverridePricesRequest? body)
    {
        if (body is null)
            return BadRequest(new { error = "Request body is required" });
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
            return BadRequest(new { error = "Reason is required (min 5 chars)" });
        if (body.Items is null || body.Items.Count == 0)
            return BadRequest(new { error = "Items are required" });

        var req = await db.QuotationRequests
            .Include(q => q.Customer)
            .Include(q => q.Branch)
            .Include(q => q.SalesPerson)
            .Include(q => q.Items).ThenInclude(ri => ri.Item)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(b => b!.Cost)
            .Include(q => q.Approvals).ThenInclude(a => a.Items)
            .FirstOrDefaultAsync(q => q.Id == id);
        if (req is null) return NotFound();

        if (req.Status != RequisitionStatus.Approved)
            return BadRequest(new { error = $"Cannot override prices on a {req.Status} requisition" });

        var currentApproval = req.Approvals.FirstOrDefault(a => !a.IsSuperseded && a.IsApproved);
        if (currentApproval is null)
            return BadRequest(new { error = "No active approval to override" });

        // D5: item set frozen — input must contain exactly the existing RequisitionItem set
        var requisitionItemIds = req.Items.Select(i => i.Id).ToHashSet();
        var inputIds = body.Items.Select(i => i.RequisitionItemId).ToList();
        if (inputIds.Distinct().Count() != inputIds.Count)
            return BadRequest(new { error = "Duplicate items in override request" });

        var inputSet = inputIds.ToHashSet();
        if (!inputSet.SetEquals(requisitionItemIds))
            return BadRequest(new
            {
                error = "Items must exactly match the existing requisition items (no add/remove allowed in override)"
            });

        // D4 validation: prices >= 0; percent sum ~ 100; non-AED requires foreign price
        foreach (var item in body.Items)
        {
            if (item.SalesPricePerKgAed < 0
                || (item.SalesPricePerKgForeign.HasValue && item.SalesPricePerKgForeign.Value < 0)
                || item.ProfitMarginPct < 0
                || item.MaterialCostPct < 0
                || item.OtherCostPct < 0)
                return BadRequest(new { error = $"Negative values not allowed (item {item.RequisitionItemId})" });

            var sum = item.ProfitMarginPct + item.MaterialCostPct + item.OtherCostPct;
            if (Math.Abs(sum - 100m) > PercentSumTolerance)
                return BadRequest(new
                {
                    error = $"Percent fields must sum to 100 (item {item.RequisitionItemId} sums to {sum})"
                });

            if (req.CurrencyCode != "AED" && !item.SalesPricePerKgForeign.HasValue)
                return BadRequest(new
                {
                    error = $"SalesPricePerKgForeign required for non-AED currency (item {item.RequisitionItemId})"
                });
        }

        // D7: re-snap exchange rate. Original currency lookup; null when AED.
        decimal? newRateSnapshot = null;
        if (req.CurrencyCode != "AED")
        {
            var rate = await db.ExchangeRates
                .Where(e => e.CurrencyCode == req.CurrencyCode && e.IsActive)
                .OrderByDescending(e => e.EffectiveDate)
                .FirstOrDefaultAsync();
            if (rate is null)
                return BadRequest(new { error = $"No active exchange rate for {req.CurrencyCode}" });
            newRateSnapshot = rate.RateToAed;
        }

        // Capture BeforeJson (current approval + its items) — D-N for audit
        var supersededApprovalId = currentApproval.Id;
        var before = new
        {
            currentApproval.Id,
            currentApproval.QuotationRequestId,
            currentApproval.ApprovedByUserId,
            currentApproval.ApprovedAt,
            currentApproval.IsApproved,
            currentApproval.RateSnapshot,
            Items = currentApproval.Items.Select(ai => new
            {
                ai.Id,
                ai.RequisitionItemId,
                ai.SalesPricePerKgAed,
                ai.SalesPricePerKgForeign,
                ai.ProfitMarginPct,
                ai.MaterialCostPct,
                ai.OtherCostPct
            }).ToList()
        };

        var originalMdUserId = currentApproval.ApprovedByUserId;

        // D10: same transaction — supersede old, create new approval
        currentApproval.IsSuperseded = true;
        currentApproval.SupersededAt = DateTime.UtcNow;

        var newApproval = new QuotationApproval
        {
            QuotationRequestId = req.Id,
            ApprovedByUserId = CurrentUserId,
            ApprovedAt = DateTime.UtcNow,
            Notes = $"[Override] {body.Reason}",
            IsApproved = true,
            IsSuperseded = false,
            RateSnapshot = newRateSnapshot,
        };

        foreach (var input in body.Items)
        {
            newApproval.Items.Add(new ApprovalItem
            {
                RequisitionItemId = input.RequisitionItemId,
                SalesPricePerKgAed = input.SalesPricePerKgAed,
                SalesPricePerKgForeign = input.SalesPricePerKgForeign,
                ProfitMarginPct = input.ProfitMarginPct,
                MaterialCostPct = input.MaterialCostPct,
                OtherCostPct = input.OtherCostPct,
            });
        }
        db.QuotationApprovals.Add(newApproval);
        req.UpdatedAt = DateTime.UtcNow; // Status stays Approved (D10)

        // After-snapshot uses transient values — Id is 0 until SaveChanges
        var afterSnapshotItems = newApproval.Items.Select(ai => new
        {
            ai.RequisitionItemId,
            ai.SalesPricePerKgAed,
            ai.SalesPricePerKgForeign,
            ai.ProfitMarginPct,
            ai.MaterialCostPct,
            ai.OtherCostPct
        }).ToList();
        var after = new
        {
            newApproval.QuotationRequestId,
            newApproval.ApprovedByUserId,
            newApproval.ApprovedAt,
            newApproval.IsApproved,
            newApproval.RateSnapshot,
            SupersededApprovalId = supersededApprovalId,
            Items = afterSnapshotItems
        };

        audit.Log(
            CurrentUserId,
            AdminActionType.OverridePrices,
            "Requisition",
            id,
            body.Reason,
            before,
            after);

        await db.SaveChangesAsync();

        // Best-effort PDF generation + email to SP (D6 broad: never customer).
        // Failure here doesn't roll back the approval supersession.
        int? emailSentToSpUserId = null;
        try
        {
            await db.Entry(newApproval).Collection(a => a.Items).LoadAsync();
            var pdf = pdfSvc.GenerateQuotation(req, newApproval);

            if (!string.IsNullOrWhiteSpace(req.SalesPerson.Email))
            {
                var customerContact = string.Join(" / ",
                    new[] { req.Customer.Email, req.Customer.PhoneNumber }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));
                if (string.IsNullOrEmpty(customerContact)) customerContact = "(no contact on file)";

                await emailSvc.SendAsync(
                    req.SalesPerson.Email,
                    req.SalesPerson.Name,
                    $"Quotation re-issued (override) – {req.RefNo}",
                    $"<p>Dear {req.SalesPerson.Name},</p>" +
                    $"<p>Quotation <strong>{req.RefNo}</strong> has been re-issued by Admin with updated prices. " +
                    $"Please forward the attached PDF to the customer.</p>" +
                    $"<p><strong>Customer:</strong> {req.Customer.Name}<br/>" +
                    $"<strong>Customer contact:</strong> {customerContact}</p>" +
                    $"<p>Override reason: {body.Reason}</p>" +
                    $"<p>Regards,<br/>Fujairah Plastic Factory</p>",
                    pdf,
                    $"{req.RefNo}-Quotation-Override.pdf");
                emailSentToSpUserId = req.SalesPersonId;
            }
            else
            {
                logger.LogWarning(
                    "[Override] SP {SalesPersonId} has no email on record; skipping email dispatch for {RefNo}",
                    req.SalesPersonId, req.RefNo);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Override-prices PDF/email dispatch failed after successful commit for {RefNo}",
                req.RefNo);
        }

        // Notify SP + original-MD + Accountants in branch (D8)
        var recipients = new HashSet<int> { req.SalesPersonId, originalMdUserId };
        var accountantIds = await db.Users
            .Where(u => u.Role == UserRole.Accountant && u.IsActive)
            .Where(u => db.UserBranches.Any(ub => ub.UserId == u.Id && ub.BranchId == req.BranchId))
            .Select(u => u.Id)
            .ToListAsync();
        foreach (var aid in accountantIds) recipients.Add(aid);
        recipients.Remove(CurrentUserId); // don't notify self

        if (recipients.Count > 0)
        {
            await notify.SendToUsersAsync(
                recipients,
                $"Prices overridden by Admin on {req.RefNo}",
                referenceId: id,
                referenceType: nameof(NotificationType.PricesOverridden));
        }

        return Ok(new OverridePricesResponse(
            NewApprovalId: newApproval.Id,
            SupersededApprovalId: supersededApprovalId,
            EmailSentToSpUserId: emailSentToSpUserId));
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
}
