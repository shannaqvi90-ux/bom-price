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
        var bom = await db.BomHeaders
            .Include(b => b.QuotationRequest)
            .Include(b => b.Cost)
            .Include(b => b.Lines).ThenInclude(l => l.Process)
            .Include(b => b.Lines).ThenInclude(l => l.RawMaterial)
            .FirstOrDefaultAsync(b => b.QuotationRequestId == requisitionId);

        if (bom is null) return NotFound();
        if (CurrentBranchId.HasValue && bom.QuotationRequest.BranchId != CurrentBranchId)
            return Forbid();

        var rawMaterialItemIds = bom.Lines.Select(l => l.RawMaterialItemId).ToList();
        var lastCosts = await db.ItemLastCosts
            .Where(c => rawMaterialItemIds.Contains(c.ItemId))
            .ToDictionaryAsync(c => c.ItemId);

        var bomLineResponses = bom.Lines.Select(l =>
        {
            LastCostInfo? lastCost = lastCosts.TryGetValue(l.RawMaterialItemId, out var lc)
                ? new LastCostInfo(lc.CostPerKg, lc.CurrencyCode, lc.UpdatedAt)
                : null;
            return new CostingBomLineResponse(
                l.Id, l.ProcessId, l.Process.Name,
                l.RawMaterialItemId, l.RawMaterial.Description,
                l.QtyPerKg, l.WastagePct, lastCost);
        }).ToList();

        var draftRow = await db.CostingDrafts.FirstOrDefaultAsync(d => d.BomHeaderId == bom.Id);
        CostingDraftResponse? draft = null;
        if (draftRow is not null)
        {
            var lines = JsonSerializer.Deserialize<List<CostingDraftLineInput>>(draftRow.LinesJson)
                ?? new List<CostingDraftLineInput>();
            draft = new CostingDraftResponse(lines, draftRow.LandedCostType, draftRow.LandedCostValue, draftRow.FohAmount);
        }

        var c = bom.Cost;
        return Ok(new CostingDetailResponse(
            c?.Id ?? 0,
            c?.RawMaterialCostTotal ?? 0m,
            (c?.LandedCostType ?? LandedCostType.Percentage).ToString(),
            c?.LandedCostValue ?? 0m,
            c?.FohAmount ?? 0m,
            c?.TotalCostPerKg ?? 0m,
            c?.SubmittedAt,
            bomLineResponses,
            draft));
    }

    [HttpPost("{requisitionId}/start")]
    public async Task<IActionResult> Start(int requisitionId)
    {
        var req = await db.QuotationRequests.FindAsync(requisitionId);
        if (req is null) return NotFound();
        if (CurrentBranchId.HasValue && req.BranchId != CurrentBranchId)
            return Forbid();
        if (req.Status != RequisitionStatus.CostingPending)
            return BadRequest(new { message = "Requisition is not in CostingPending status" });

        req.Status = RequisitionStatus.CostingInProgress;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{requisitionId}/draft")]
    public async Task<IActionResult> SaveDraft(int requisitionId, SaveCostingDraftRequest request)
    {
        var req = await db.QuotationRequests.FindAsync(requisitionId);
        if (req is null) return NotFound();
        if (CurrentBranchId.HasValue && req.BranchId != CurrentBranchId)
            return Forbid();
        if (req.Status != RequisitionStatus.CostingInProgress)
            return BadRequest(new { message = "Draft can only be saved when status is CostingInProgress" });

        var bom = await db.BomHeaders.FirstOrDefaultAsync(b => b.QuotationRequestId == requisitionId);
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

    [HttpPost("{requisitionId}/submit")]
    public async Task<IActionResult> Submit(int requisitionId, SubmitCostingRequest request)
    {
        var req = await db.QuotationRequests.FirstOrDefaultAsync(q => q.Id == requisitionId);
        if (req is null) return NotFound();
        if (CurrentBranchId.HasValue && req.BranchId != CurrentBranchId)
            return Forbid();
        if (req.Status != RequisitionStatus.CostingInProgress)
            return BadRequest(new { message = "Costing can only be submitted when status is CostingInProgress" });

        var bom = await db.BomHeaders
            .Include(b => b.Lines)
            .Include(b => b.Cost)
            .FirstOrDefaultAsync(b => b.QuotationRequestId == requisitionId);
        if (bom is null) return BadRequest(new { message = "No BOM found for this requisition" });

        var quoteCurrency = (req.CurrencyCode ?? "AED").ToUpperInvariant();

        // Build currency → rate-to-AED map (1.0 for AED)
        var usedCurrencies = request.RawMaterialCosts
            .Select(r => (r.CurrencyCode ?? "AED").ToUpperInvariant())
            .Append(quoteCurrency)
            .Distinct()
            .ToList();

        var rates = (await db.ExchangeRates
            .Where(e => e.IsActive && usedCurrencies.Contains(e.CurrencyCode))
            .OrderByDescending(e => e.EffectiveDate)
            .ToListAsync())
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

        // Calculate raw material cost total in quote currency + prepare BomCostLine rows
        decimal rawMaterialTotal = 0;
        var newCostLines = new List<BomCostLine>();
        foreach (var rc in request.RawMaterialCosts)
        {
            var line = bom.Lines.FirstOrDefault(l => l.Id == rc.BomLineId);
            if (line is null) continue;

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

        // Calculate landed cost (already in quote currency)
        decimal landedCost = request.LandedCostType == LandedCostType.Percentage
            ? rawMaterialTotal * request.LandedCostValue / 100
            : request.LandedCostValue;

        decimal totalCost = rawMaterialTotal + landedCost + request.FohAmount;

        // Replace prior BomCost aggregate and BomCostLine rows for this BomHeader
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

        // Upsert ItemLastCost for each raw material (stores original entry cost + currency)
        var rawItemIds = newCostLines
            .Select(l => bom.Lines.First(bl => bl.Id == l.BomLineId).RawMaterialItemId)
            .Distinct()
            .ToList();
        var existingLastCosts = await db.ItemLastCosts
            .Where(l => rawItemIds.Contains(l.ItemId))
            .ToDictionaryAsync(l => l.ItemId);

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
                    ItemId = itemId,
                    CostPerKg = costLine.CostPerKg,
                    CurrencyCode = costLine.CurrencyCode,
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedByUserId = CurrentUserId
                };
                db.ItemLastCosts.Add(newEntry);
                existingLastCosts[itemId] = newEntry;
            }
        }

        // Delete draft
        var draft = await db.CostingDrafts.FirstOrDefaultAsync(d => d.BomHeaderId == bom.Id);
        if (draft is not null) db.CostingDrafts.Remove(draft);

        req.Status = RequisitionStatus.MdReview;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Notify active MDs
        var mds = await db.Users.Where(u => u.Role == UserRole.ManagingDirector && u.IsActive).ToListAsync();
        foreach (var md in mds)
            await notificationService.SendAsync(md.Id,
                $"Costing complete, ready for approval: {req.RefNo}", req.Id, "QuotationRequest");

        return NoContent();
    }
}
