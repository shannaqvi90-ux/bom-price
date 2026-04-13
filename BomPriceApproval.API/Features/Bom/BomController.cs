using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Bom;

[ApiController]
[Route("api/bom")]
[Authorize]
public class BomController(AppDbContext db, NotificationService notificationService) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("{requisitionId}")]
    public async Task<IActionResult> Get(int requisitionId)
    {
        var bom = await db.BomHeaders
            .Include(b => b.Lines).ThenInclude(l => l.Process)
            .Include(b => b.Lines).ThenInclude(l => l.RawMaterial)
            .Include(b => b.QuotationRequest)
            .Include(b => b.Item)
            .FirstOrDefaultAsync(b => b.QuotationRequestId == requisitionId);

        if (bom is null) return NotFound();

        return Ok(new BomDetailResponse(
            bom.Id, bom.QuotationRequestId, bom.QuotationRequest.RefNo,
            bom.Item.Description,
            bom.Lines.Select(l => new BomLineResponse(
                l.Id, l.ProcessId, l.Process.Name, l.RawMaterialItemId,
                l.RawMaterial.Description, l.QtyPerKg, l.WastagePct)).ToList(),
            bom.TotalCostPerKg, bom.SubmittedAt));
    }

    [HttpPost("{requisitionId}/start")]
    [Authorize(Roles = "BomCreator")]
    public async Task<IActionResult> Start(int requisitionId)
    {
        var req = await db.QuotationRequests.FindAsync(requisitionId);
        if (req is null) return NotFound();
        if (req.Status != RequisitionStatus.BomPending)
            return BadRequest(new { message = "Requisition is not in BomPending status" });

        req.Status = RequisitionStatus.BomInProgress;
        req.UpdatedAt = DateTime.UtcNow;

        var bom = new BomHeader { QuotationRequestId = requisitionId, ItemId = req.ItemId, CreatedByUserId = CurrentUserId };
        db.BomHeaders.Add(bom);
        await db.SaveChangesAsync();
        return Ok(new { bom.Id });
    }

    [HttpPost("{requisitionId}/submit")]
    [Authorize(Roles = "BomCreator")]
    public async Task<IActionResult> Submit(int requisitionId, SubmitBomRequest request)
    {
        var req = await db.QuotationRequests.Include(q => q.Branch).FirstOrDefaultAsync(q => q.Id == requisitionId);
        if (req is null) return NotFound();
        if (req.Status != RequisitionStatus.BomInProgress)
            return BadRequest(new { message = "BOM can only be submitted when status is BomInProgress" });

        var bom = await db.BomHeaders.Include(b => b.Lines).FirstOrDefaultAsync(b => b.QuotationRequestId == requisitionId);
        if (bom is null) return BadRequest(new { message = "BOM not started. Call /start first." });

        // Replace lines
        db.BomLines.RemoveRange(bom.Lines);
        bom.Lines = request.Lines.Select(l => new BomLine
        {
            ProcessId = l.ProcessId, RawMaterialItemId = l.RawMaterialItemId,
            QtyPerKg = l.QtyPerKg, WastagePct = l.WastagePct
        }).ToList();

        bom.SubmittedAt = DateTime.UtcNow;
        req.Status = RequisitionStatus.CostingPending;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Notify accountants in same branch
        var accountants = await db.Users
            .Where(u => u.Role == UserRole.Accountant && (u.BranchId == req.BranchId || u.BranchId == null) && u.IsActive)
            .ToListAsync();

        foreach (var accountant in accountants)
            await notificationService.SendAsync(accountant.Id,
                $"BOM ready for costing: {req.RefNo}", req.Id, "QuotationRequest");

        return NoContent();
    }
}
