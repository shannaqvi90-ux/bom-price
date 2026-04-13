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
            .Include(q => q.Item).Include(q => q.Customer).Include(q => q.Branch)
            .Include(q => q.BomHeader).ThenInclude(b => b!.Cost)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req?.BomHeader?.Cost is null) return NotFound();
        var c = req.BomHeader.Cost;

        return Ok(new MdReviewDetail(
            req.RefNo, req.Item.Description, req.Customer.Name,
            req.ExpectedQty, req.CurrencyCode, req.ExchangeRateSnapshot,
            c.RawMaterialCostTotal,
            req.BomHeader.TotalCostPerKg > 0 ? req.BomHeader.TotalCostPerKg - c.RawMaterialCostTotal - c.FohAmount : 0,
            c.FohAmount, req.BomHeader.TotalCostPerKg,
            c.RawMaterialCostTotal / req.BomHeader.TotalCostPerKg * 100,
            (req.BomHeader.TotalCostPerKg - c.RawMaterialCostTotal - c.FohAmount) / req.BomHeader.TotalCostPerKg * 100,
            c.FohAmount / req.BomHeader.TotalCostPerKg * 100));
    }

    [HttpPost("{requisitionId}/approve")]
    public async Task<IActionResult> Approve(int requisitionId, ApproveRequest request)
    {
        var req = await db.QuotationRequests
            .Include(q => q.Item).Include(q => q.Customer)
            .Include(q => q.Branch).Include(q => q.SalesPerson)
            .Include(q => q.BomHeader).ThenInclude(b => b!.Cost)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req is null || req.BomHeader?.Cost is null) return NotFound();
        if (req.Status != RequisitionStatus.MdReview)
            return BadRequest(new { message = "Requisition is not in MdReview status" });

        var totalCost = req.BomHeader.TotalCostPerKg;
        var profitMargin = (request.SalesPricePerKgAed - totalCost) / request.SalesPricePerKgAed * 100;
        var matPct = req.BomHeader.Cost.RawMaterialCostTotal / totalCost * 100;
        var otherPct = 100 - matPct;

        decimal? foreignPrice = null;
        if (req.CurrencyCode != "AED" && req.ExchangeRateSnapshot.HasValue)
            foreignPrice = request.SalesPricePerKgAed / req.ExchangeRateSnapshot.Value;

        var approval = new QuotationApproval
        {
            QuotationRequestId = req.Id,
            SalesPricePerKgAed = request.SalesPricePerKgAed,
            SalesPricePerKgForeign = foreignPrice,
            ProfitMarginPct = profitMargin,
            MaterialCostPct = matPct,
            OtherCostPct = otherPct,
            ApprovedByUserId = CurrentUserId,
            Notes = request.Notes,
            IsApproved = true
        };
        db.QuotationApprovals.Add(approval);
        req.Status = RequisitionStatus.Approved;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Reload for PDF
        await db.Entry(approval).Reference(a => a.QuotationRequest).LoadAsync();

        // Generate PDF
        var pdf = pdfSvc.GenerateQuotation(req, approval);

        // Notify + email sales person
        await notificationSvc.SendAsync(req.SalesPersonId,
            $"Quotation approved! Download ready: {req.RefNo}", req.Id, "QuotationRequest");

        await emailSvc.SendAsync(req.SalesPerson.Email, req.SalesPerson.Name,
            $"Quotation Approved – {req.RefNo}",
            $"<p>Dear {req.SalesPerson.Name},</p><p>Your quotation <strong>{req.RefNo}</strong> has been approved. Please find the quotation PDF attached.</p><p>Regards,<br/>Fujairah Plastic Factory</p>",
            pdf, $"{req.RefNo}-Quotation.pdf");

        return Ok(new { message = "Approved", req.RefNo });
    }

    [HttpPost("{requisitionId}/reject")]
    public async Task<IActionResult> Reject(int requisitionId, RejectRequest request)
    {
        var req = await db.QuotationRequests
            .Include(q => q.SalesPerson)
            .Include(q => q.BomHeader).ThenInclude(b => b!.Cost)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req is null) return NotFound();
        if (req.Status != RequisitionStatus.MdReview)
            return BadRequest(new { message = "Requisition is not in MdReview status" });

        var approval = new QuotationApproval
        {
            QuotationRequestId = req.Id,
            SalesPricePerKgAed = 0,
            ProfitMarginPct = 0,
            MaterialCostPct = 0,
            OtherCostPct = 0,
            ApprovedByUserId = CurrentUserId,
            Notes = request.Notes,
            IsApproved = false
        };
        db.QuotationApprovals.Add(approval);
        req.Status = RequisitionStatus.Rejected;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Notify sales person and accountants
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
            .Include(q => q.Item).Include(q => q.Customer)
            .Include(q => q.Branch).Include(q => q.SalesPerson)
            .Include(q => q.BomHeader).ThenInclude(b => b!.Cost)
            .Include(q => q.Approval)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req?.Approval is null || !req.Approval.IsApproved) return NotFound();

        var pdf = pdfSvc.GenerateQuotation(req, req.Approval);
        return File(pdf, "application/pdf", $"{req.RefNo}-Quotation.pdf");
    }
}
