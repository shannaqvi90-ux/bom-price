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
    private int? CurrentBranchId => int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0 ? b : null;

    [HttpGet("{requisitionId}")]
    public async Task<IActionResult> Get(int requisitionId)
    {
        var req = await db.QuotationRequests
            .Include(q => q.Items).ThenInclude(ri => ri.Item)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader)
                .ThenInclude(b => b!.Lines).ThenInclude(l => l.Process)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader)
                .ThenInclude(b => b!.Lines).ThenInclude(l => l.RawMaterial)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req is null) return NotFound();
        if (CurrentBranchId.HasValue && req.BranchId != CurrentBranchId) return Forbid();

        var bomHeaderIds = req.Items
            .Where(ri => ri.BomHeader is not null)
            .Select(ri => ri.BomHeader!.Id).ToList();
        var costLines = bomHeaderIds.Count > 0
            ? await db.BomCostLines.Where(c => bomHeaderIds.Contains(c.BomHeaderId)).ToListAsync()
            : [];
        var costLinesByBom = costLines.ToLookup(c => c.BomHeaderId);

        var items = req.Items.OrderBy(ri => ri.SortOrder).Select(ri =>
        {
            var bom = ri.BomHeader;
            var bomStatus = bom is null ? "NotStarted"
                : bom.SubmittedAt.HasValue ? "Submitted" : "InProgress";

            var lines = bom?.Lines.Select(l =>
            {
                var cl = costLinesByBom[bom.Id].FirstOrDefault(c => c.BomLineId == l.Id);
                decimal? contribution = cl is not null
                    ? cl.CostPerKgInAed * l.QtyPerKg * (1 + l.WastagePct / 100) : null;
                return new BomLineResponse(l.Id, l.ProcessId, l.Process.Name,
                    l.RawMaterialItemId, l.RawMaterial.Description,
                    l.QtyPerKg, l.WastagePct,
                    cl?.CostPerKg, cl?.CurrencyCode, cl?.CostPerKgInAed, contribution);
            }).ToList() ?? [];

            return new BomItemResponse(ri.Id, ri.ItemId, ri.Item.Description,
                ri.ExpectedQty, ri.SortOrder, bom?.Id, bomStatus,
                lines, bom?.TotalCostPerKg ?? 0, bom?.SubmittedAt);
        }).ToList();

        return Ok(new BomReviewResponse(req.Id, req.RefNo, req.Status.ToString(), items));
    }

    [HttpPost("{requisitionId}/items/{requisitionItemId}/start")]
    [Authorize(Roles = "BomCreator")]
    public async Task<IActionResult> StartItem(int requisitionId, int requisitionItemId)
    {
        var req = await db.QuotationRequests.FindAsync(requisitionId);
        if (req is null) return NotFound();
        if (CurrentBranchId.HasValue && req.BranchId != CurrentBranchId) return Forbid();
        if (req.Status != RequisitionStatus.BomPending && req.Status != RequisitionStatus.BomInProgress)
            return BadRequest(new { message = "Requisition is not in BomPending or BomInProgress status" });

        var ri = await db.RequisitionItems.Include(r => r.BomHeader)
            .FirstOrDefaultAsync(r => r.Id == requisitionItemId && r.QuotationRequestId == requisitionId);
        if (ri is null) return NotFound();
        if (ri.BomHeader is not null)
            return BadRequest(new { message = "BOM already started for this item" });

        if (req.Status == RequisitionStatus.BomPending)
        {
            req.Status = RequisitionStatus.BomInProgress;
            req.UpdatedAt = DateTime.UtcNow;
        }

        var bom = new BomHeader { RequisitionItemId = requisitionItemId, CreatedByUserId = CurrentUserId };
        db.BomHeaders.Add(bom);
        await db.SaveChangesAsync();
        return Ok(new { bom.Id });
    }

    [HttpPut("{requisitionId}/items/{requisitionItemId}/lines")]
    [Authorize(Roles = "BomCreator")]
    public async Task<IActionResult> SaveLines(int requisitionId, int requisitionItemId, SaveBomLinesRequest request)
    {
        var req = await db.QuotationRequests.FindAsync(requisitionId);
        if (req is null) return NotFound();
        if (CurrentBranchId.HasValue && req.BranchId != CurrentBranchId) return Forbid();
        if (req.Status != RequisitionStatus.BomInProgress)
            return BadRequest(new { message = "BOM can only be saved when status is BomInProgress" });

        var bom = await db.BomHeaders.Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.RequisitionItemId == requisitionItemId);
        if (bom is null) return NotFound();
        if (bom.CreatedByUserId != CurrentUserId) return Forbid();

        if (request.Lines.Any(l => l.QtyPerKg <= 0))
            return BadRequest(new { message = "QtyPerKg must be greater than 0." });

        if (request.Lines.Any(l => l.WastagePct < 0))
            return BadRequest(new { message = "WastagePct cannot be negative." });

        var processIds = request.Lines.Select(l => l.ProcessId).Distinct().ToList();
        var validProcessCount = await db.Processes.CountAsync(p => processIds.Contains(p.Id));
        if (validProcessCount != processIds.Count)
            return BadRequest(new { message = "One or more ProcessIds are invalid." });

        var rawMatIds = request.Lines.Select(l => l.RawMaterialItemId).Distinct().ToList();
        var validRawMatCount = await db.Items.CountAsync(i => rawMatIds.Contains(i.Id) && i.IsActive);
        if (validRawMatCount != rawMatIds.Count)
            return BadRequest(new { message = "One or more RawMaterialItemIds are invalid or inactive." });

        db.BomLines.RemoveRange(bom.Lines);
        bom.Lines = request.Lines.Select(l => new BomLine
        {
            ProcessId = l.ProcessId,
            RawMaterialItemId = l.RawMaterialItemId,
            QtyPerKg = l.QtyPerKg,
            WastagePct = l.WastagePct
        }).ToList();

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{requisitionId}/submit")]
    [Authorize(Roles = "BomCreator")]
    public async Task<IActionResult> Submit(int requisitionId)
    {
        var req = await db.QuotationRequests.Include(q => q.Branch)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(b => b!.Lines)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);
        if (req is null) return NotFound();
        if (CurrentBranchId.HasValue && req.BranchId != CurrentBranchId) return Forbid();
        if (req.Status != RequisitionStatus.BomInProgress)
            return BadRequest(new { message = "BOM can only be submitted when status is BomInProgress" });

        foreach (var ri in req.Items)
        {
            if (ri.BomHeader is null || ri.BomHeader.Lines.Count == 0)
                return BadRequest(new { message = $"Item '{ri.ItemId}' has no BOM lines. All items must have at least one line." });
        }

        foreach (var ri in req.Items)
        {
            ri.BomHeader!.SubmittedAt ??= DateTime.UtcNow;
        }

        req.Status = RequisitionStatus.CostingPending;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var accountants = await db.Users
            .Where(u => u.Role == UserRole.Accountant && (u.BranchId == req.BranchId || u.BranchId == null) && u.IsActive)
            .ToListAsync();
        foreach (var accountant in accountants)
            await notificationService.SendAsync(accountant.Id,
                $"BOM ready for costing: {req.RefNo}", req.Id, "QuotationRequest");

        return NoContent();
    }
}
