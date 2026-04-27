using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Authorization;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Bom;

[ApiController]
[Route("api/bom")]
[Authorize]
public class BomController(
    AppDbContext db,
    NotificationService notificationService,
    ILogger<BomController> logger) : ControllerBase
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
            return Validation.Detail("Requisition is not in BomPending or BomInProgress status")
                .Field("Status", "Must be BomPending or BomInProgress.")
                .Return();

        var ri = await db.RequisitionItems.Include(r => r.BomHeader)
            .FirstOrDefaultAsync(r => r.Id == requisitionItemId && r.QuotationRequestId == requisitionId);
        if (ri is null) return NotFound();
        if (ri.BomHeader is not null)
            return Validation.Detail("BOM already started for this item")
                .Field("RequisitionItemId", "BOM already started for this item.")
                .Return();

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
            return Validation.Detail("BOM can only be saved when status is BomInProgress")
                .Field("Status", "Must be BomInProgress.")
                .Return();

        var bom = await db.BomHeaders.Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.RequisitionItemId == requisitionItemId);
        if (bom is null) return NotFound();

        if (request.Lines.Any(l => l.QtyPerKg <= 0))
        {
            var builder = Validation.Detail("QtyPerKg must be greater than 0.");
            for (int i = 0; i < request.Lines.Count; i++)
                if (request.Lines[i].QtyPerKg <= 0)
                    builder.Field($"Lines[{i}].QtyPerKg", "Must be greater than 0.");
            return builder.Return();
        }

        if (request.Lines.Any(l => l.WastagePct < 0))
        {
            var builder = Validation.Detail("WastagePct cannot be negative.");
            for (int i = 0; i < request.Lines.Count; i++)
                if (request.Lines[i].WastagePct < 0)
                    builder.Field($"Lines[{i}].WastagePct", "Cannot be negative.");
            return builder.Return();
        }

        var processIds = request.Lines.Select(l => l.ProcessId).Distinct().ToList();
        var validProcessIds = await db.Processes
            .Where(p => processIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync();
        var invalidProcessIds = processIds.Except(validProcessIds).ToHashSet();
        if (invalidProcessIds.Count > 0)
        {
            var builder = Validation.Detail("One or more ProcessIds are invalid.");
            for (int i = 0; i < request.Lines.Count; i++)
                if (invalidProcessIds.Contains(request.Lines[i].ProcessId))
                    builder.Field($"Lines[{i}].ProcessId", "Invalid ProcessId.");
            return builder.Return();
        }

        var rawMatIds = request.Lines.Select(l => l.RawMaterialItemId).Distinct().ToList();
        var validRawMatIds = await db.Items
            .Where(i => rawMatIds.Contains(i.Id) && i.IsActive)
            .Select(i => i.Id)
            .ToListAsync();
        var invalidRawMatIds = rawMatIds.Except(validRawMatIds).ToHashSet();
        if (invalidRawMatIds.Count > 0)
        {
            var builder = Validation.Detail("One or more RawMaterialItemIds are invalid or inactive.");
            for (int i = 0; i < request.Lines.Count; i++)
                if (invalidRawMatIds.Contains(request.Lines[i].RawMaterialItemId))
                    builder.Field($"Lines[{i}].RawMaterialItemId", "Invalid or inactive.");
            return builder.Return();
        }

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
            return Validation.Detail("BOM can only be submitted when status is BomInProgress")
                .Field("Status", "Must be BomInProgress.")
                .Return();

        var itemList = req.Items.ToList();
        for (int i = 0; i < itemList.Count; i++)
        {
            var ri = itemList[i];
            if (ri.BomHeader is null || ri.BomHeader.Lines.Count == 0)
                return Validation.Detail($"Item '{ri.ItemId}' has no BOM lines. All items must have at least one line.")
                    .Field($"Items[{i}].BomLines", "Must have at least one BOM line.")
                    .Return();
        }

        foreach (var ri in req.Items)
        {
            ri.BomHeader!.SubmittedAt ??= DateTime.UtcNow;
        }

        req.Status = RequisitionStatus.CostingPending;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        try
        {
            var accountantCandidates = await db.Users
                .Where(u => u.Role == UserRole.Accountant && u.IsActive)
                .ToListAsync();
            var accountants = accountantCandidates
                .Where(u => BranchAuthorization.UserAuthorizedForBranch(u, req.BranchId, db))
                .ToList();
            await notificationService.SendToUsersAsync(
                accountants.Select(u => u.Id),
                $"BOM ready for costing: {req.RefNo}",
                req.Id,
                "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful commit for {Entity} {Id}",
                "QuotationRequest", req.Id);
        }

        return NoContent();
    }
}
