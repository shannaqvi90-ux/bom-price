using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Domain.Workflow;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using BomPriceApproval.API.Infrastructure.Validation;
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
            return Validation.Detail("Request body is required").Return();
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Trim().Length < 5)
            return Validation.Detail("Reason is required (min 5 chars)")
                .Field("Reason", "Reason is required (min 5 chars).").Return();
        if (body.ConfirmationToken != "OVERRIDE")
            return Validation.Detail("ConfirmationToken must be \"OVERRIDE\"")
                .Field("ConfirmationToken", "Must equal \"OVERRIDE\" exactly to break the lock.").Return();
        if (body.Items is null || body.Items.Count == 0)
            return Validation.Detail("Items are required")
                .Field("Items", "At least one item is required.").Return();

        var req = await db.QuotationRequests
            .Include(q => q.Customer)
            .Include(q => q.Branch)
            .Include(q => q.SalesPerson)
            .Include(q => q.Items).ThenInclude(ri => ri.Item)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(b => b!.Cost)
            .Include(q => q.Approvals).ThenInclude(a => a.Items)
            .FirstOrDefaultAsync(q => q.Id == id);
        if (req is null) return NotFound();

        // V3 extends override to Signed status (in addition to legacy V2.3 Approved).
        if (req.Status != RequisitionStatus.Approved && req.Status != RequisitionStatus.Signed)
            return Validation.Detail($"Cannot override prices on a {req.Status} requisition")
                .Field("Status", $"Status must be Approved or Signed (current: {req.Status}).").Return();

        var currentApproval = req.Approvals.FirstOrDefault(a => !a.IsSuperseded && a.IsApproved);
        if (currentApproval is null)
            return Validation.Detail("No active approval to override").Return();

        // D5: item set frozen — input must contain exactly the existing RequisitionItem set
        var requisitionItemIds = req.Items.Select(i => i.Id).ToHashSet();
        var inputIds = body.Items.Select(i => i.RequisitionItemId).ToList();
        if (inputIds.Distinct().Count() != inputIds.Count)
            return Validation.Detail("Duplicate items in override request")
                .Field("Items", "Duplicate items not allowed.").Return();

        var inputSet = inputIds.ToHashSet();
        if (!inputSet.SetEquals(requisitionItemIds))
            return Validation.Detail("Items must exactly match the existing requisition items (no add/remove allowed in override)")
                .Field("Items", "Item set must match exactly — no add or remove during override.").Return();

        // D4 validation: prices >= 0; percent sum ~ 100; non-AED requires foreign price
        for (int i = 0; i < body.Items.Count; i++)
        {
            var item = body.Items[i];
            if (item.SalesPricePerKgAed < 0
                || (item.SalesPricePerKgForeign.HasValue && item.SalesPricePerKgForeign.Value < 0)
                || item.ProfitMarginPct < 0
                || item.MaterialCostPct < 0
                || item.OtherCostPct < 0)
                return Validation.Detail($"Negative values not allowed (item {item.RequisitionItemId})")
                    .Field($"Items[{i}]", "Negative values not allowed.").Return();

            var sum = item.ProfitMarginPct + item.MaterialCostPct + item.OtherCostPct;
            if (Math.Abs(sum - 100m) > PercentSumTolerance)
                return Validation.Detail($"Percent fields must sum to 100 (item {item.RequisitionItemId} sums to {sum})")
                    .Field($"Items[{i}].PercentSum", "Percent fields must sum to 100.").Return();

            if (req.CurrencyCode != "AED" && !item.SalesPricePerKgForeign.HasValue)
                return Validation.Detail($"SalesPricePerKgForeign required for non-AED currency (item {item.RequisitionItemId})")
                    .Field($"Items[{i}].SalesPricePerKgForeign", "Required for non-AED currency.").Return();
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
                return Validation.Detail($"No active exchange rate for {req.CurrencyCode}")
                    .Field("CurrencyCode", "No active exchange rate.").Return();
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

        var notesBody = string.IsNullOrWhiteSpace(body.Notes) ? body.Reason : body.Notes;
        var newApproval = new QuotationApproval
        {
            QuotationRequestId = req.Id,
            ApprovedByUserId = CurrentUserId,
            ApprovedAt = DateTime.UtcNow,
            Notes = $"[Override] {notesBody}",
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
        req.UpdatedAt = DateTime.UtcNow; // Status unchanged — Approved (V2.3) or Signed (V3) (D10)

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
            // Override creates a new InitialPricing approval that hasn't been
            // final-signed yet — no signer.
            var pdf = await pdfSvc.GenerateQuotationAsync(req, newApproval, signer: null);

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
            return Validation.Detail("Request body is required").Return();

        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
            return Validation.Detail("Reason is required (min 5 chars)")
                .Field("Reason", "Reason is required (min 5 chars).").Return();

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
            return Validation.Detail("Request body is required").Return();
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
            return Validation.Detail("Reason is required (min 5 chars)")
                .Field("Reason", "Reason is required (min 5 chars).").Return();

        var req = await db.QuotationRequests.FindAsync(id);
        if (req is null) return NotFound();

        var allowedTargets = RequisitionStateMachine.AdminRollbackTargets(req.Status);
        if (!allowedTargets.Contains(body.TargetStatus))
            return Validation.Detail($"Cannot rollback {req.Status} → {body.TargetStatus}")
                .Field("TargetStatus", $"Rollback from {req.Status} to {body.TargetStatus} is not allowed.").Return();

        var before = new { req.Id, req.Status };
        req.Status = body.TargetStatus;
        req.UpdatedAt = DateTime.UtcNow;
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
            return Validation.Detail("Request body is required").Return();
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
            return Validation.Detail("Reason is required (min 5 chars)")
                .Field("Reason", "Reason is required (min 5 chars).").Return();

        var req = await db.QuotationRequests.FindAsync(id);
        if (req is null) return NotFound();

        var newSp = await db.Users.FindAsync(body.NewSalesPersonId);
        if (newSp is null || newSp.Role != UserRole.SalesPerson || !newSp.IsActive)
            return Validation.Detail("Target user must be an active SalesPerson")
                .Field("NewSalesPersonId", "Target user must be an active SalesPerson.").Return();

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

    [HttpPost("requisitions/{id}/rollback-to-costing")]
    public async Task<IActionResult> RollbackToCosting(int id, [FromBody] RollbackToCostingRequest? body)
    {
        if (body is null)
            return Validation.Detail("Request body is required").Return();
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Trim().Length < 5)
            return Validation.Detail("Reason is required (min 5 chars)")
                .Field("Reason", "Reason is required (min 5 chars).").Return();

        var req = await db.QuotationRequests.FindAsync(id);
        if (req is null) return NotFound();

        // V3: only MdPricing can be rolled back to Costing via this dedicated UX action.
        // For deeper rollbacks (CustomerConfirm/MdFinalSign), admin uses the generic
        // rollback-status endpoint with an explicit target.
        if (req.Status != RequisitionStatus.MdPricing)
            return Validation.Detail($"Cannot rollback to Costing from status {req.Status}")
                .Field("Status", $"Only MdPricing can be rolled back to Costing (current: {req.Status}).").Return();

        var before = new { req.Id, req.Status };
        req.Status = RequisitionStatus.Costing;
        req.MdPricingNotifiedAfterEdit = false;  // leaving MdPricing
        req.UpdatedAt = DateTime.UtcNow;
        var after = new { req.Id, req.Status };

        audit.Log(CurrentUserId, AdminActionType.RollbackToCosting, "Requisition", id, body.Reason, before, after);
        await db.SaveChangesAsync();

        var recipientIds = await db.Users
            .Where(u => (u.BranchId == req.BranchId && u.Role == UserRole.Accountant) || u.Role == UserRole.ManagingDirector)
            .Select(u => u.Id).ToListAsync();

        await notify.SendToUsersAsync(
            recipientIds,
            $"Requisition {req.RefNo} rolled back to Costing by Admin",
            referenceId: id,
            referenceType: nameof(NotificationType.CostingUnlocked));

        return Ok(new { req.Id, req.Status });
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
}
