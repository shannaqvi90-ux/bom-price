using System.Security.Claims;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Costing;

[ApiController]
[Route("api/costing")]
[Authorize(Roles = "Accountant")]
public class CostingController(AppDbContext db, NotificationService notificationService) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("{requisitionId}")]
    public async Task<IActionResult> Get(int requisitionId)
    {
        var bom = await db.BomHeaders.Include(b => b.Cost).FirstOrDefaultAsync(b => b.QuotationRequestId == requisitionId);
        if (bom?.Cost is null) return NotFound();
        var c = bom.Cost;
        return Ok(new CostingDetailResponse(c.Id, c.RawMaterialCostTotal, c.LandedCostType.ToString(),
            c.LandedCostValue, c.FohAmount, c.TotalCostPerKg, c.SubmittedAt));
    }

    [HttpPost("{requisitionId}/start")]
    public async Task<IActionResult> Start(int requisitionId)
    {
        var req = await db.QuotationRequests.FindAsync(requisitionId);
        if (req is null) return NotFound();
        if (req.Status != RequisitionStatus.CostingPending)
            return BadRequest(new { message = "Requisition is not in CostingPending status" });
        req.Status = RequisitionStatus.CostingInProgress;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{requisitionId}/submit")]
    public async Task<IActionResult> Submit(int requisitionId, SubmitCostingRequest request)
    {
        var req = await db.QuotationRequests.FindAsync(requisitionId);
        if (req is null) return NotFound();
        if (req.Status != RequisitionStatus.CostingInProgress)
            return BadRequest(new { message = "Costing can only be submitted when status is CostingInProgress" });

        var bom = await db.BomHeaders.Include(b => b.Lines).Include(b => b.Cost)
            .FirstOrDefaultAsync(b => b.QuotationRequestId == requisitionId);
        if (bom is null) return BadRequest(new { message = "No BOM found for this requisition" });

        // Calculate raw material cost total
        decimal rawMaterialTotal = 0;
        foreach (var rc in request.RawMaterialCosts)
        {
            var line = bom.Lines.FirstOrDefault(l => l.Id == rc.BomLineId);
            if (line is not null)
                rawMaterialTotal += rc.CostPerKg * line.QtyPerKg * (1 + line.WastagePct / 100);
        }

        // Calculate landed cost
        decimal landedCostAed = request.LandedCostType == LandedCostType.Percentage
            ? rawMaterialTotal * request.LandedCostValue / 100
            : request.LandedCostValue;

        decimal totalCost = rawMaterialTotal + landedCostAed + request.FohAmount;

        if (bom.Cost is not null) db.BomCosts.Remove(bom.Cost);

        var cost = new BomCost
        {
            BomHeaderId = bom.Id,
            RawMaterialCostTotal = rawMaterialTotal,
            LandedCostType = request.LandedCostType,
            LandedCostValue = request.LandedCostValue,
            FohAmount = request.FohAmount,
            TotalCostPerKg = totalCost,
            SubmittedByUserId = CurrentUserId
        };
        db.BomCosts.Add(cost);

        bom.TotalCostPerKg = totalCost;
        req.Status = RequisitionStatus.MdReview;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Notify MD
        var mds = await db.Users.Where(u => u.Role == UserRole.ManagingDirector && u.IsActive).ToListAsync();
        foreach (var md in mds)
            await notificationService.SendAsync(md.Id,
                $"Costing complete, ready for approval: {req.RefNo}", req.Id, "QuotationRequest");

        return NoContent();
    }
}
