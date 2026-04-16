using System.Security.Claims;
using System.Text.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
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
    private int? CurrentBranchId => int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0 ? b : null;

    [HttpGet("{requisitionId}")]
    public async Task<IActionResult> Get(int requisitionId)
    {
        var req = await db.QuotationRequests
            .Include(q => q.Items).ThenInclude(ri => ri.Item)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(b => b!.Cost)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader)
                .ThenInclude(b => b!.Lines).ThenInclude(l => l.Process)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader)
                .ThenInclude(b => b!.Lines).ThenInclude(l => l.RawMaterial)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req is null) return NotFound();
        if (CurrentBranchId.HasValue && req.BranchId != CurrentBranchId) return Forbid();

        var allRawMaterialIds = req.Items
            .Where(ri => ri.BomHeader is not null)
            .SelectMany(ri => ri.BomHeader!.Lines.Select(l => l.RawMaterialItemId))
            .Distinct().ToList();
        var lastCosts = allRawMaterialIds.Count > 0
            ? await db.ItemLastCosts.Where(c => allRawMaterialIds.Contains(c.ItemId)).ToDictionaryAsync(c => c.ItemId)
            : new Dictionary<int, ItemLastCost>();

        var bomHeaderIds = req.Items
            .Where(ri => ri.BomHeader is not null)
            .Select(ri => ri.BomHeader!.Id).ToList();
        var drafts = bomHeaderIds.Count > 0
            ? await db.CostingDrafts.Where(d => bomHeaderIds.Contains(d.BomHeaderId)).ToDictionaryAsync(d => d.BomHeaderId)
            : new Dictionary<int, CostingDraft>();

        var items = req.Items.OrderBy(ri => ri.SortOrder).Select(ri =>
        {
            var bom = ri.BomHeader;
            if (bom is null)
                return new CostingItemResponse(ri.Id, ri.ItemId, ri.Item.Description,
                    ri.ExpectedQty, null, "NotStarted", null, [], null);

            var c = bom.Cost;
            var costStatus = c is not null ? "Submitted" : "NotStarted";

            CostingSummary? costSummary = c is not null
                ? new CostingSummary(c.Id, c.RawMaterialCostTotal, c.LandedCostType.ToString(),
                    c.LandedCostValue, c.FohAmount, c.TotalCostPerKg, c.SubmittedAt)
                : null;

            var bomLines = bom.Lines.Select(l =>
            {
                LastCostInfo? lc = lastCosts.TryGetValue(l.RawMaterialItemId, out var v)
                    ? new LastCostInfo(v.CostPerKg, v.CurrencyCode, v.UpdatedAt)
                    : null;
                return new CostingBomLineResponse(l.Id, l.ProcessId, l.Process.Name,
                    l.RawMaterialItemId, l.RawMaterial.Description,
                    l.QtyPerKg, l.WastagePct, lc);
            }).ToList();

            CostingDraftResponse? draftResp = null;
            if (drafts.TryGetValue(bom.Id, out var draftRow))
            {
                var draftLines = JsonSerializer.Deserialize<List<CostingDraftLineInput>>(draftRow.LinesJson) ?? [];
                draftResp = new CostingDraftResponse(draftLines, draftRow.LandedCostType, draftRow.LandedCostValue, draftRow.FohAmount);
            }

            return new CostingItemResponse(ri.Id, ri.ItemId, ri.Item.Description,
                ri.ExpectedQty, bom.Id, costStatus, costSummary, bomLines, draftResp);
        }).ToList();

        return Ok(new CostingReviewResponse(req.Id, items));
    }

    [HttpPost("{requisitionId}/items/{requisitionItemId}/start")]
    public async Task<IActionResult> StartItem(int requisitionId, int requisitionItemId)
    {
        var req = await db.QuotationRequests.FindAsync(requisitionId);
        if (req is null) return NotFound();
        if (CurrentBranchId.HasValue && req.BranchId != CurrentBranchId) return Forbid();
        if (req.Status != RequisitionStatus.CostingPending && req.Status != RequisitionStatus.CostingInProgress)
            return BadRequest(new { message = "Requisition is not in CostingPending or CostingInProgress status" });

        if (req.Status == RequisitionStatus.CostingPending)
        {
            req.Status = RequisitionStatus.CostingInProgress;
            req.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{requisitionId}/items/{requisitionItemId}/draft")]
    public async Task<IActionResult> SaveDraft(int requisitionId, int requisitionItemId, SaveCostingDraftRequest request)
    {
        var req = await db.QuotationRequests.FindAsync(requisitionId);
        if (req is null) return NotFound();
        if (CurrentBranchId.HasValue && req.BranchId != CurrentBranchId) return Forbid();
        if (req.Status != RequisitionStatus.CostingInProgress)
            return BadRequest(new { message = "Draft can only be saved when status is CostingInProgress" });

        var bom = await db.BomHeaders.FirstOrDefaultAsync(b => b.RequisitionItemId == requisitionItemId);
        if (bom is null) return NotFound();

        var linesJson = JsonSerializer.Serialize(request.Lines);
        var draft = await db.CostingDrafts.FirstOrDefaultAsync(d => d.BomHeaderId == bom.Id);
        if (draft is null)
        {
            draft = new CostingDraft { BomHeaderId = bom.Id };
            db.CostingDrafts.Add(draft);
        }
        draft.LinesJson = linesJson;
        draft.LandedCostType = request.LandedCostType;
        draft.LandedCostValue = request.LandedCostValue;
        draft.FohAmount = request.FohAmount;
        draft.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{requisitionId}/items/{requisitionItemId}/submit")]
    public async Task<IActionResult> SubmitItem(int requisitionId, int requisitionItemId, SubmitCostingRequest request)
    {
        var req = await db.QuotationRequests.FirstOrDefaultAsync(q => q.Id == requisitionId);
        if (req is null) return NotFound();
        if (CurrentBranchId.HasValue && req.BranchId != CurrentBranchId) return Forbid();
        if (req.Status != RequisitionStatus.CostingInProgress)
            return BadRequest(new { message = "Costing can only be submitted when status is CostingInProgress" });

        var bom = await db.BomHeaders
            .Include(b => b.Lines)
            .Include(b => b.Cost)
            .FirstOrDefaultAsync(b => b.RequisitionItemId == requisitionItemId);
        if (bom is null) return BadRequest(new { message = "No BOM found for this item" });

        if (request.RawMaterialCosts.Any(rc => rc.CostPerKg < 0))
            return BadRequest(new { message = "CostPerKg cannot be negative." });

        var submittedBomLineIds = request.RawMaterialCosts.Select(rc => rc.BomLineId).Distinct().ToList();
        var bomLineIds = bom.Lines.Select(l => l.Id).ToList();
        var unknownBomLines = submittedBomLineIds.Except(bomLineIds).ToList();
        if (unknownBomLines.Count > 0)
            return BadRequest(new { message = $"Unknown BOM line(s): {string.Join(", ", unknownBomLines)}" });

        var missingBomLines = bomLineIds.Except(submittedBomLineIds).ToList();
        if (missingBomLines.Count > 0)
            return BadRequest(new { message = $"Missing cost for BOM line(s): {string.Join(", ", missingBomLines)}" });

        var quoteCurrency = (req.CurrencyCode ?? "AED").ToUpperInvariant();

        var usedCurrencies = request.RawMaterialCosts
            .Select(r => (r.CurrencyCode ?? "AED").ToUpperInvariant())
            .Append(quoteCurrency)
            .Distinct().ToList();

        var rates = (await db.ExchangeRates
            .Where(e => e.IsActive && usedCurrencies.Contains(e.CurrencyCode))
            .OrderByDescending(e => e.EffectiveDate).ToListAsync())
            .GroupBy(e => e.CurrencyCode.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First().RateToAed);

        decimal RateToAed(string code)
        {
            if (code == "AED") return 1m;
            if (!rates.TryGetValue(code, out var r))
                throw new InvalidOperationException($"No exchange rate found for {code}. Contact admin.");
            return r;
        }

        decimal quoteRateToAed;
        try { quoteRateToAed = RateToAed(quoteCurrency); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        decimal rawMaterialTotal = 0;
        var newCostLines = new List<BomCostLine>();
        foreach (var rc in request.RawMaterialCosts)
        {
            var line = bom.Lines.First(l => l.Id == rc.BomLineId);

            var currency = (rc.CurrencyCode ?? "AED").ToUpperInvariant();
            decimal entryRate;
            try { entryRate = RateToAed(currency); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

            var costInQuote = rc.CostPerKg * entryRate / quoteRateToAed;
            rawMaterialTotal += costInQuote * line.QtyPerKg * (1 + line.WastagePct / 100);

            newCostLines.Add(new BomCostLine
            {
                BomHeaderId = bom.Id,
                BomLineId = line.Id,
                CostPerKg = rc.CostPerKg,
                CurrencyCode = currency,
                CostPerKgInQuoteCurrency = costInQuote,
                CostPerKgInAed = rc.CostPerKg * entryRate
            });
        }

        decimal landedCost = request.LandedCostType == LandedCostType.Percentage
            ? rawMaterialTotal * request.LandedCostValue / 100
            : request.LandedCostValue;
        decimal totalCost = rawMaterialTotal + landedCost + request.FohAmount;

        if (bom.Cost is not null) db.BomCosts.Remove(bom.Cost);
        var existingLines = await db.BomCostLines.Where(l => l.BomHeaderId == bom.Id).ToListAsync();
        if (existingLines.Count > 0) db.BomCostLines.RemoveRange(existingLines);
        db.BomCostLines.AddRange(newCostLines);

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

        var rawItemIds = newCostLines
            .Select(l => bom.Lines.First(bl => bl.Id == l.BomLineId).RawMaterialItemId)
            .Distinct().ToList();
        var existingLastCosts = await db.ItemLastCosts
            .Where(l => rawItemIds.Contains(l.ItemId)).ToDictionaryAsync(l => l.ItemId);

        foreach (var costLine in newCostLines)
        {
            var bomLine = bom.Lines.First(bl => bl.Id == costLine.BomLineId);
            var itemId = bomLine.RawMaterialItemId;
            if (existingLastCosts.TryGetValue(itemId, out var lc))
            {
                lc.CostPerKg = costLine.CostPerKg;
                lc.CurrencyCode = costLine.CurrencyCode;
                lc.UpdatedAt = DateTime.UtcNow;
                lc.UpdatedByUserId = CurrentUserId;
            }
            else
            {
                var newEntry = new ItemLastCost
                {
                    ItemId = itemId, CostPerKg = costLine.CostPerKg,
                    CurrencyCode = costLine.CurrencyCode,
                    UpdatedAt = DateTime.UtcNow, UpdatedByUserId = CurrentUserId
                };
                db.ItemLastCosts.Add(newEntry);
                existingLastCosts[itemId] = newEntry;
            }
        }

        var draft = await db.CostingDrafts.FirstOrDefaultAsync(d => d.BomHeaderId == bom.Id);
        if (draft is not null) db.CostingDrafts.Remove(draft);

        var allItemIds = await db.RequisitionItems
            .Where(ri => ri.QuotationRequestId == requisitionId)
            .Select(ri => ri.Id).ToListAsync();
        var costCount = await db.BomCosts
            .CountAsync(c => allItemIds.Contains(c.BomHeader.RequisitionItemId));
        var allSubmitted = (costCount + 1) >= allItemIds.Count;

        if (allSubmitted)
        {
            req.Status = RequisitionStatus.MdReview;
        }

        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        if (allSubmitted)
        {
            var mds = await db.Users.Where(u => u.Role == UserRole.ManagingDirector && u.IsActive).ToListAsync();
            foreach (var md in mds)
                await notificationService.SendAsync(md.Id,
                    $"Costing complete, ready for approval: {req.RefNo}", req.Id, "QuotationRequest");
        }

        return NoContent();
    }
}
