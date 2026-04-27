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

    [HttpGet("requisitions/{id}/current-approval")]
    public async Task<IActionResult> GetCurrentApproval(int id)
    {
        var req = await db.QuotationRequests
            .Include(q => q.Items).ThenInclude(ri => ri.Item)
            .Include(q => q.Approvals).ThenInclude(a => a.Items)
            .FirstOrDefaultAsync(q => q.Id == id);
        if (req is null) return NotFound();

        var currentApproval = req.Approvals.FirstOrDefault(a => !a.IsSuperseded && a.IsApproved);
        if (currentApproval is null)
            return NotFound(new { error = "No active approval for this requisition" });

        var itemLookup = req.Items.ToDictionary(ri => ri.Id);

        var items = currentApproval.Items
            .Select(ai =>
            {
                itemLookup.TryGetValue(ai.RequisitionItemId, out var ri);
                return new CurrentApprovalItemDto(
                    RequisitionItemId: ai.RequisitionItemId,
                    ItemDescription: ri?.Item.Description ?? string.Empty,
                    ExpectedQty: ri?.ExpectedQty ?? 0m,
                    SalesPricePerKgAed: ai.SalesPricePerKgAed,
                    SalesPricePerKgForeign: ai.SalesPricePerKgForeign,
                    ProfitMarginPct: ai.ProfitMarginPct,
                    MaterialCostPct: ai.MaterialCostPct,
                    OtherCostPct: ai.OtherCostPct);
            })
            .ToList();

        return Ok(new CurrentApprovalResponse(
            Id: currentApproval.Id,
            QuotationRequestId: req.Id,
            RefNo: req.RefNo,
            CurrencyCode: req.CurrencyCode,
            RateSnapshot: currentApproval.RateSnapshot,
            ApprovedAt: currentApproval.ApprovedAt,
            ApprovedByUserId: currentApproval.ApprovedByUserId,
            Notes: currentApproval.Notes,
            Items: items));
    }

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

    [HttpDelete("requisitions/{id}")]
    public async Task<IActionResult> DeleteRequisition(int id, [FromBody] DeleteRequisitionRequest? body)
    {
        if (body is null)
            return BadRequest(new { error = "Request body is required" });

        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
            return BadRequest(new { error = "Reason is required (min 5 chars)" });

        var req = await db.QuotationRequests
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (req is null) return NotFound();

        var snapshot = new
        {
            req.Id,
            req.RefNo,
            req.Status,
            req.SalesPersonId,
            req.BranchId,
            req.CustomerId,
            ItemCount = req.Items.Count,
            BomHeaderCount = await db.BomHeaders.CountAsync(b => b.RequisitionItem.QuotationRequestId == id),
            ApprovalCount = await db.QuotationApprovals.CountAsync(a => a.QuotationRequestId == id)
        };

        var spId = req.SalesPersonId;
        var branchId = req.BranchId;
        var refNo = req.RefNo;

        db.QuotationRequests.Remove(req);
        audit.Log(CurrentUserId, AdminActionType.DeleteRequisition, "Requisition", id, body.Reason, snapshot, after: (object?)null);
        await db.SaveChangesAsync();

        var recipientIds = await db.Users
            .Where(u => (u.Id == spId)
                || (u.BranchId == branchId && (u.Role == UserRole.BomCreator || u.Role == UserRole.Accountant))
                || u.Role == UserRole.ManagingDirector)
            .Select(u => u.Id)
            .ToListAsync();

        await notify.SendToUsersAsync(
            recipientIds,
            $"Requisition {refNo} was deleted by Admin",
            referenceId: id,
            referenceType: nameof(NotificationType.RequisitionDeleted));

        return NoContent();
    }

    [HttpPost("requisitions/{id}/rollback-status")]
    public async Task<IActionResult> RollbackStatus(int id, [FromBody] RollbackStatusRequest? body)
    {
        if (body is null)
            return BadRequest(new { error = "Request body is required" });
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
            return BadRequest(new { error = "Reason is required (min 5 chars)" });

        var req = await db.QuotationRequests.FindAsync(id);
        if (req is null) return NotFound();

        if (!Infrastructure.Authorization.AdminOverrideAuthorization.CanRollback(req.Status, body.TargetStatus))
            return BadRequest(new { error = $"Cannot rollback {req.Status} → {body.TargetStatus}" });

        var before = new { req.Id, req.Status };
        req.Status = body.TargetStatus;
        var after = new { req.Id, req.Status };

        audit.Log(CurrentUserId, AdminActionType.RollbackStatus, "Requisition", id, body.Reason, before, after);
        await db.SaveChangesAsync();

        var recipientIds = await db.Users
            .Where(u => u.Id == req.SalesPersonId
                || (u.BranchId == req.BranchId && (u.Role == UserRole.BomCreator || u.Role == UserRole.Accountant))
                || u.Role == UserRole.ManagingDirector)
            .Select(u => u.Id).ToListAsync();

        await notify.SendToUsersAsync(
            recipientIds,
            $"Requisition {req.RefNo} status rolled back to {req.Status} by Admin",
            referenceId: id,
            referenceType: nameof(NotificationType.StatusRolledBack));

        return Ok(new { req.Id, req.Status });
    }

    [HttpPost("requisitions/{id}/reassign-sp")]
    public async Task<IActionResult> ReassignSp(int id, [FromBody] ReassignSpRequest? body)
    {
        if (body is null)
            return BadRequest(new { error = "Request body is required" });
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
            return BadRequest(new { error = "Reason is required (min 5 chars)" });

        var req = await db.QuotationRequests.FindAsync(id);
        if (req is null) return NotFound();

        var newSp = await db.Users.FindAsync(body.NewSalesPersonId);
        if (newSp is null || newSp.Role != UserRole.SalesPerson || !newSp.IsActive)
            return BadRequest(new { error = "Target user must be an active SalesPerson" });

        var oldSpId = req.SalesPersonId;
        var before = new { req.Id, OldSalesPersonId = oldSpId };
        req.SalesPersonId = newSp.Id;
        var after = new { req.Id, NewSalesPersonId = newSp.Id };

        audit.Log(CurrentUserId, AdminActionType.ReassignSp, "Requisition", id, body.Reason, before, after);
        await db.SaveChangesAsync();

        var recipientIds = await db.Users
            .Where(u => u.Id == oldSpId || u.Id == newSp.Id || u.Role == UserRole.ManagingDirector)
            .Select(u => u.Id).ToListAsync();

        await notify.SendToUsersAsync(
            recipientIds,
            $"Requisition {req.RefNo} reassigned by Admin",
            referenceId: id,
            referenceType: nameof(NotificationType.SalesPersonReassigned));

        return Ok(new { req.Id, req.SalesPersonId });
    }

    [HttpPost("requisitions/{id}/unlock-bom")]
    public async Task<IActionResult> UnlockBom(int id, [FromBody] UnlockBomRequest? body)
    {
        if (body is null)
            return BadRequest(new { error = "Request body is required" });
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
            return BadRequest(new { error = "Reason is required (min 5 chars)" });

        var req = await db.QuotationRequests.FindAsync(id);
        if (req is null) return NotFound();

        if (!Infrastructure.Authorization.AdminOverrideAuthorization.CanUnlockBom(req.Status))
            return BadRequest(new { error = $"Cannot unlock BOM from status {req.Status}" });

        var before = new { req.Id, req.Status };
        req.Status = RequisitionStatus.BomInProgress;
        var after = new { req.Id, req.Status };

        audit.Log(CurrentUserId, AdminActionType.UnlockBom, "Requisition", id, body.Reason, before, after);
        await db.SaveChangesAsync();

        var recipientIds = await db.Users
            .Where(u => u.BranchId == req.BranchId && (u.Role == UserRole.BomCreator || u.Role == UserRole.Accountant))
            .Select(u => u.Id).ToListAsync();

        await notify.SendToUsersAsync(
            recipientIds,
            $"BOM for requisition {req.RefNo} has been unlocked by Admin",
            referenceId: id,
            referenceType: nameof(NotificationType.BomUnlocked));

        return Ok(new { req.Id, req.Status });
    }

    [HttpPost("requisitions/{id}/unlock-costing")]
    public async Task<IActionResult> UnlockCosting(int id, [FromBody] UnlockCostingRequest? body)
    {
        if (body is null)
            return BadRequest(new { error = "Request body is required" });
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
            return BadRequest(new { error = "Reason is required (min 5 chars)" });

        var req = await db.QuotationRequests.FindAsync(id);
        if (req is null) return NotFound();

        if (!Infrastructure.Authorization.AdminOverrideAuthorization.CanUnlockCosting(req.Status))
            return BadRequest(new { error = $"Cannot unlock costing from status {req.Status}" });

        var before = new { req.Id, req.Status };
        req.Status = RequisitionStatus.CostingInProgress;
        var after = new { req.Id, req.Status };

        audit.Log(CurrentUserId, AdminActionType.UnlockCosting, "Requisition", id, body.Reason, before, after);
        await db.SaveChangesAsync();

        var recipientIds = await db.Users
            .Where(u => (u.BranchId == req.BranchId && u.Role == UserRole.Accountant) || u.Role == UserRole.ManagingDirector)
            .Select(u => u.Id).ToListAsync();

        await notify.SendToUsersAsync(
            recipientIds,
            $"Costing for requisition {req.RefNo} has been unlocked by Admin",
            referenceId: id,
            referenceType: nameof(NotificationType.CostingUnlocked));

        return Ok(new { req.Id, req.Status });
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
}
