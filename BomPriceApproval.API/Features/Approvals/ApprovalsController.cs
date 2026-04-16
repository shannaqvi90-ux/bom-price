using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Approvals;

[ApiController]
[Route("api/approvals")]
[Authorize(Roles = "ManagingDirector")]
public class ApprovalsController(AppDbContext db, NotificationService notificationSvc, EmailService emailSvc, PdfService pdfSvc) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("{requisitionId}")]
    public async Task<IActionResult> GetReview(int requisitionId)
    {
        var req = await db.QuotationRequests
            .Include(q => q.Customer).Include(q => q.Branch)
            .Include(q => q.Items).ThenInclude(ri => ri.Item)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(b => b!.Cost)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req is null) return NotFound();

        if (req.Items.Any(ri => ri.BomHeader?.Cost is null)) return NotFound();

        var items = req.Items.OrderBy(ri => ri.SortOrder).Select(ri =>
        {
            var c = ri.BomHeader!.Cost!;
            var totalCost = ri.BomHeader.TotalCostPerKg;
            var landedCost = totalCost > 0 ? totalCost - c.RawMaterialCostTotal - c.FohAmount : 0;

            return new MdReviewItemDetail(
                ri.Id, ri.Item.Description, ri.ExpectedQty,
                c.RawMaterialCostTotal, landedCost, c.FohAmount, totalCost,
                totalCost > 0 ? c.RawMaterialCostTotal / totalCost * 100 : 0,
                totalCost > 0 ? landedCost / totalCost * 100 : 0,
                totalCost > 0 ? c.FohAmount / totalCost * 100 : 0);
        }).ToList();

        return Ok(new MdReviewDetail(
            req.RefNo, req.Customer.Name,
            req.CurrencyCode, req.ExchangeRateSnapshot, items));
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
            return BadRequest(new { message = "Requisition is not in MdReview status" });

        var approval = new QuotationApproval
        {
            QuotationRequestId = req.Id,
            ApprovedByUserId = CurrentUserId,
            Notes = request.Notes,
            IsApproved = true
        };

        foreach (var input in request.Items)
        {
            var ri = req.Items.FirstOrDefault(i => i.Id == input.RequisitionItemId);
            if (ri?.BomHeader?.Cost is null) continue;

            var totalCost = ri.BomHeader.TotalCostPerKg;
            var profitMargin = (input.SalesPricePerKgAed - totalCost) / input.SalesPricePerKgAed * 100;
            var matPct = ri.BomHeader.Cost.RawMaterialCostTotal / totalCost * 100;
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
        catch
        {
            // Approval committed; notification/email failures are non-fatal
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
            return BadRequest(new { message = "Requisition is not in MdReview status" });

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

        await notificationSvc.SendAsync(req.SalesPersonId,
            $"Quotation rejected: {req.RefNo}. Reason: {request.Notes}", req.Id, "QuotationRequest");

        var accountants = await db.Users
            .Where(u => u.Role == UserRole.Accountant && u.IsActive).ToListAsync();
        foreach (var acct in accountants)
            await notificationSvc.SendAsync(acct.Id,
                $"Quotation rejected by MD: {req.RefNo}. Reason: {request.Notes}", req.Id, "QuotationRequest");

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
            .Include(q => q.Approval).ThenInclude(a => a!.Items)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req?.Approval is null || !req.Approval.IsApproved) return NotFound();

        var pdf = pdfSvc.GenerateQuotation(req, req.Approval);
        return File(pdf, "application/pdf", $"{req.RefNo}-Quotation.pdf");
    }
}
