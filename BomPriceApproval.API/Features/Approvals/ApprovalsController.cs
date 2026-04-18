using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Approvals;

[ApiController]
[Route("api/approvals")]
[Authorize(Roles = "ManagingDirector")]
public class ApprovalsController(
    AppDbContext db,
    NotificationService notificationSvc,
    EmailService emailSvc,
    PdfService pdfSvc,
    ILogger<ApprovalsController> logger) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole => User.FindFirstValue(ClaimTypes.Role)!;

    [HttpGet("{requisitionId}")]
    public async Task<IActionResult> GetReview(int requisitionId)
    {
        var req = await db.QuotationRequests
            .Include(q => q.Customer).Include(q => q.Branch)
            .Include(q => q.Items).ThenInclude(ri => ri.Item)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(b => b!.Cost)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req is null) return NotFound();

        var readyForReview = req.Items.All(ri => ri.BomHeader?.Cost is not null);

        var items = req.Items.OrderBy(ri => ri.SortOrder).Select(ri =>
        {
            var c = ri.BomHeader?.Cost;
            if (c is null)
                return new MdReviewItemDetail(ri.Id, ri.Item.Description, ri.ExpectedQty,
                    "NotStarted", null);

            var totalCost = ri.BomHeader!.TotalCostPerKg;
            var landedCost = totalCost > 0 ? totalCost - c.RawMaterialCostTotal - c.FohAmount : 0;

            var cost = new MdReviewItemCost(
                c.RawMaterialCostTotal, landedCost, c.FohAmount, totalCost,
                totalCost > 0 ? c.RawMaterialCostTotal / totalCost * 100 : 0,
                totalCost > 0 ? landedCost / totalCost * 100 : 0,
                totalCost > 0 ? c.FohAmount / totalCost * 100 : 0);

            return new MdReviewItemDetail(ri.Id, ri.Item.Description, ri.ExpectedQty,
                "Submitted", cost);
        }).ToList();

        return Ok(new MdReviewDetail(
            req.RefNo, req.Customer.Name,
            req.CurrencyCode, req.ExchangeRateSnapshot, readyForReview, items));
    }

    [HttpPost("{requisitionId}/approve")]
    public async Task<IActionResult> Approve(int requisitionId, ApproveRequest request)
    {
        var req = await db.QuotationRequests
            .Include(q => q.Customer).Include(q => q.Branch)
            .Include(q => q.SalesPerson)
            .Include(q => q.Items).ThenInclude(ri => ri.Item)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(b => b!.Cost)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req is null) return NotFound();
        if (req.Status != RequisitionStatus.MdReview)
            return Validation
                .Detail("Requisition is not in MdReview status")
                .Field("Status", "Requisition is not in MdReview status.")
                .Return();

        if (request.Items is null || request.Items.Count == 0)
            return Validation
                .Detail("No items provided for approval.")
                .Field("Items", "No items provided for approval.")
                .Return();

        if (request.Items.Any(i => i.SalesPricePerKgAed <= 0))
        {
            var builder = Validation.Detail("SalesPrice must be greater than 0.");
            for (int i = 0; i < request.Items.Count; i++)
                if (request.Items[i].SalesPricePerKgAed <= 0)
                    builder.Field($"Items[{i}].SalesPricePerKgAed", "Must be greater than 0.");
            return builder.Return();
        }

        var inputIds = request.Items.Select(i => i.RequisitionItemId).ToList();
        if (inputIds.Count != inputIds.Distinct().Count())
            return Validation
                .Detail("Duplicate items in approval request.")
                .Field("Items", "Duplicate items in request.")
                .Return();

        var requisitionItemIds = req.Items.Select(i => i.Id).ToList();
        var missingInputs = requisitionItemIds.Except(inputIds).ToList();
        if (missingInputs.Count > 0)
            return Validation
                .Detail($"Missing price for item(s): {string.Join(", ", missingInputs)}")
                .Field("Items", "Missing price for one or more items.")
                .Return();

        var orphanInputSet = inputIds.Except(requisitionItemIds).ToHashSet();
        if (orphanInputSet.Count > 0)
        {
            var builder = Validation.Detail($"Unknown item(s) in request: {string.Join(", ", orphanInputSet)}");
            for (int i = 0; i < request.Items.Count; i++)
                if (orphanInputSet.Contains(request.Items[i].RequisitionItemId))
                    builder.Field($"Items[{i}].RequisitionItemId", "Unknown item.");
            return builder.Return();
        }

        if (req.Items.Any(i => i.BomHeader?.Cost is null))
            return Validation
                .Detail("All items must have a costed BOM before approval.")
                .Field("Items", "One or more items have no costed BOM.")
                .Return();

        var approval = new QuotationApproval
        {
            QuotationRequestId = req.Id,
            ApprovedByUserId = CurrentUserId,
            Notes = request.Notes,
            IsApproved = true
        };

        foreach (var input in request.Items)
        {
            var ri = req.Items.First(i => i.Id == input.RequisitionItemId);

            var totalCost = ri.BomHeader!.TotalCostPerKg;
            var profitMargin = (input.SalesPricePerKgAed - totalCost) / input.SalesPricePerKgAed * 100;
            var matPct = totalCost == 0 ? 0 : ri.BomHeader.Cost!.RawMaterialCostTotal / totalCost * 100;
            var otherPct = 100 - matPct;

            decimal? foreignPrice = null;
            if (req.CurrencyCode != "AED" && req.ExchangeRateSnapshot.HasValue)
                foreignPrice = input.SalesPricePerKgAed / req.ExchangeRateSnapshot.Value;

            approval.Items.Add(new ApprovalItem
            {
                RequisitionItemId = ri.Id,
                SalesPricePerKgAed = input.SalesPricePerKgAed,
                SalesPricePerKgForeign = foreignPrice,
                ProfitMarginPct = profitMargin,
                MaterialCostPct = matPct,
                OtherCostPct = otherPct
            });
        }

        db.QuotationApprovals.Add(approval);
        req.Status = RequisitionStatus.Approved;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        try
        {
            await db.Entry(approval).Collection(a => a.Items).LoadAsync();
            var pdf = pdfSvc.GenerateQuotation(req, approval);

            await notificationSvc.SendAsync(req.SalesPersonId,
                $"Quotation approved! Download ready: {req.RefNo}", req.Id, "QuotationRequest");

            await emailSvc.SendAsync(req.SalesPerson.Email, req.SalesPerson.Name,
                $"Quotation Approved – {req.RefNo}",
                $"<p>Dear {req.SalesPerson.Name},</p><p>Your quotation <strong>{req.RefNo}</strong> has been approved. Please find the quotation PDF attached.</p><p>Regards,<br/>Fujairah Plastic Factory</p>",
                pdf, $"{req.RefNo}-Quotation.pdf");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful commit for {Entity} {Id}",
                "QuotationRequest", req.Id);
        }

        return Ok(new { message = "Approved", req.RefNo });
    }

    [HttpPost("{requisitionId}/reject")]
    public async Task<IActionResult> Reject(int requisitionId, RejectRequest request)
    {
        var req = await db.QuotationRequests
            .Include(q => q.SalesPerson)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req is null) return NotFound();
        if (req.Status != RequisitionStatus.MdReview)
            return Validation
                .Detail("Requisition is not in MdReview status")
                .Field("Status", "Requisition is not in MdReview status.")
                .Return();

        var approval = new QuotationApproval
        {
            QuotationRequestId = req.Id,
            ApprovedByUserId = CurrentUserId,
            Notes = request.Notes,
            IsApproved = false
        };
        db.QuotationApprovals.Add(approval);
        req.Status = RequisitionStatus.Rejected;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        try
        {
            await notificationSvc.SendAsync(req.SalesPersonId,
                $"Quotation rejected: {req.RefNo}. Reason: {request.Notes}", req.Id, "QuotationRequest");

            var accountants = await db.Users
                .Where(u => u.Role == UserRole.Accountant && u.IsActive).ToListAsync();
            foreach (var acct in accountants)
                await notificationSvc.SendAsync(acct.Id,
                    $"Quotation rejected by MD: {req.RefNo}. Reason: {request.Notes}", req.Id, "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful commit for {Entity} {Id}",
                "QuotationRequest", req.Id);
        }

        return Ok(new { message = "Rejected" });
    }

    [HttpGet("{requisitionId}/pdf")]
    [Authorize(Roles = "ManagingDirector,SalesPerson,Accountant,Admin")]
    public async Task<IActionResult> DownloadPdf(int requisitionId)
    {
        var req = await db.QuotationRequests
            .Include(q => q.Items).ThenInclude(ri => ri.Item)
            .Include(q => q.Customer).Include(q => q.Branch)
            .Include(q => q.SalesPerson)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(b => b!.Cost)
            .Include(q => q.Approvals).ThenInclude(a => a.Items)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req is null) return NotFound();

        // SalesPerson can only download PDFs for their own requisitions.
        // Accountant, MD, Admin have null BranchId and see all branches.
        if (CurrentRole == "SalesPerson" && req.SalesPersonId != CurrentUserId)
            return Forbid();

        var currentApproval = req.Approvals.FirstOrDefault(a => !a.IsSuperseded);
        if (currentApproval is null || !currentApproval.IsApproved) return NotFound();

        var pdf = pdfSvc.GenerateQuotation(req, currentApproval);
        return File(pdf, "application/pdf", $"{req.RefNo}-Quotation.pdf");
    }
}
