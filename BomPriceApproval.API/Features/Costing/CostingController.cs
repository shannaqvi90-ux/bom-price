using System.Security.Claims;
using System.Text.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Costing;

[ApiController]
[Route("api/costing")]
[Authorize(Roles = "Accountant")]
public class CostingController(
    AppDbContext db,
    NotificationService notificationService,
    ILogger<CostingController> logger) : ControllerBase
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
            var costStatus = c is not null ? "Submitted"
                           : ri.CostingStartedAt is not null ? "InProgress"
                           : "NotStarted";

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
            return Validation
                .Detail("Requisition is not in CostingPending or CostingInProgress status")
                .Field("Status", "Must be CostingPending or CostingInProgress.")
                .Return();

        var ri = await db.RequisitionItems.FirstOrDefaultAsync(
            x => x.QuotationRequestId == requisitionId && x.Id == requisitionItemId);
        if (ri is null) return NotFound();

        if (ri.CostingStartedAt is null)
            ri.CostingStartedAt = DateTime.UtcNow;

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
            return Validation
                .Detail("Draft can only be saved when status is CostingInProgress")
                .Field("Status", "Must be CostingInProgress.")
                .Return();

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
        await using var tx = await db.Database.BeginTransactionAsync();

        // Lock the requisition row to serialize concurrent submits for the same requisition.
        var req = await db.QuotationRequests
            .FromSqlInterpolated($"SELECT * FROM \"QuotationRequests\" WHERE \"Id\" = {requisitionId} FOR UPDATE")
            .FirstOrDefaultAsync();
        if (req is null) return NotFound();
        if (CurrentBranchId.HasValue && req.BranchId != CurrentBranchId) return Forbid();
        if (req.Status != RequisitionStatus.CostingInProgress)
            return Validation
                .Detail("Costing can only be submitted when status is CostingInProgress")
                .Field("Status", "Must be CostingInProgress.")
                .Return();

        var bom = await db.BomHeaders
            .Include(b => b.Lines)
            .Include(b => b.Cost)
            .FirstOrDefaultAsync(b => b.RequisitionItemId == requisitionItemId);
        if (bom is null)
            return Validation
                .Detail("No BOM found for this item")
                .Field("BomHeaderId", "No BOM found for this item.")
                .Return();

        if (request.RawMaterialCosts.Any(rc => rc.CostPerKg < 0))
        {
            var builder = Validation.Detail("CostPerKg cannot be negative.");
            for (int i = 0; i < request.RawMaterialCosts.Count; i++)
                if (request.RawMaterialCosts[i].CostPerKg < 0)
                    builder.Field($"RawMaterialCosts[{i}].CostPerKg", "Cannot be negative.");
            return builder.Return();
        }

        var submittedBomLineIds = request.RawMaterialCosts.Select(rc => rc.BomLineId).Distinct().ToList();
        var bomLineIds = bom.Lines.Select(l => l.Id).ToList();
        var unknownBomLines = submittedBomLineIds.Except(bomLineIds).ToHashSet();
        if (unknownBomLines.Count > 0)
        {
            var builder = Validation.Detail($"Unknown BOM line(s): {string.Join(", ", unknownBomLines)}");
            for (int i = 0; i < request.RawMaterialCosts.Count; i++)
                if (unknownBomLines.Contains(request.RawMaterialCosts[i].BomLineId))
                    builder.Field($"RawMaterialCosts[{i}].BomLineId", "Unknown BOM line.");
            return builder.Return();
        }

        var missingBomLines = bomLineIds.Except(submittedBomLineIds).ToList();
        if (missingBomLines.Count > 0)
            return Validation
                .Detail($"Missing cost for BOM line(s): {string.Join(", ", missingBomLines)}")
                .Field("RawMaterialCosts", "Missing cost for one or more BOM lines.")
                .Return();

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
        catch (InvalidOperationException ex)
        {
            return Validation.Detail(ex.Message).Field("CurrencyCode", ex.Message).Return();
        }

        decimal rawMaterialTotal = 0;
        var newCostLines = new List<BomCostLine>();
        foreach (var rc in request.RawMaterialCosts)
        {
            var line = bom.Lines.First(l => l.Id == rc.BomLineId);

            var currency = (rc.CurrencyCode ?? "AED").ToUpperInvariant();
            decimal entryRate;
            try { entryRate = RateToAed(currency); }
            catch (InvalidOperationException ex)
            {
                return Validation.Detail(ex.Message).Field("CurrencyCode", ex.Message).Return();
            }

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

        // Upsert ItemLastCost via Postgres INSERT … ON CONFLICT to avoid a TOCTOU
        // race on the unique index when concurrent submits share a raw material.
        var upsertsByItem = newCostLines
            .Select(cl => new
            {
                ItemId = bom.Lines.First(bl => bl.Id == cl.BomLineId).RawMaterialItemId,
                cl.CostPerKg,
                cl.CurrencyCode
            })
            .GroupBy(x => x.ItemId)
            .Select(g => g.Last())
            .OrderBy(u => u.ItemId)
            .ToList();

        var now = DateTime.UtcNow;
        foreach (var u in upsertsByItem)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""ItemLastCosts"" (""ItemId"", ""CostPerKg"", ""CurrencyCode"", ""UpdatedAt"", ""UpdatedByUserId"")
                VALUES ({u.ItemId}, {u.CostPerKg}, {u.CurrencyCode}, {now}, {CurrentUserId})
                ON CONFLICT (""ItemId"") DO UPDATE SET
                    ""CostPerKg"" = EXCLUDED.""CostPerKg"",
                    ""CurrencyCode"" = EXCLUDED.""CurrencyCode"",
                    ""UpdatedAt"" = EXCLUDED.""UpdatedAt"",
                    ""UpdatedByUserId"" = EXCLUDED.""UpdatedByUserId""");
        }

        var draft = await db.CostingDrafts.FirstOrDefaultAsync(d => d.BomHeaderId == bom.Id);
        if (draft is not null) db.CostingDrafts.Remove(draft);

        req.UpdatedAt = DateTime.UtcNow;

        // Persist the new cost rows inside the transaction.
        await db.SaveChangesAsync();

        // Re-query the count after saving so concurrent submits each see the
        // committed state of the other (FOR UPDATE at the top serialises them).
        var allItemIds = await db.RequisitionItems
            .Where(ri => ri.QuotationRequestId == requisitionId)
            .Select(ri => ri.Id).ToListAsync();
        var costCount = await db.BomCosts
            .CountAsync(c => allItemIds.Contains(c.BomHeader.RequisitionItemId));
        var allSubmitted = costCount >= allItemIds.Count;

        if (allSubmitted)
        {
            req.Status = RequisitionStatus.MdReview;
            req.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await tx.CommitAsync();

        // Send notifications outside the transaction so a delivery failure
        // cannot roll back the status promotion. Swallow and log dispatch
        // failures — the state change is already committed.
        if (allSubmitted)
        {
            try
            {
                var mdIds = await db.Users
                    .Where(u => u.Role == UserRole.ManagingDirector && u.IsActive)
                    .Select(u => u.Id)
                    .ToListAsync();
                await notificationService.SendToUsersAsync(
                    mdIds,
                    $"Costing complete, ready for approval: {req.RefNo}",
                    req.Id,
                    "QuotationRequest");
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Notification dispatch failed after successful commit for {Entity} {Id}",
                    "QuotationRequest", req.Id);
            }
        }

        return NoContent();
    }
}
