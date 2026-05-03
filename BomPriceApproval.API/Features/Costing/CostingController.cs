using System.Security.Claims;
using System.Text.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Domain.Workflow;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Costing;

[ApiController]
[Route("api/costing")]
[Authorize(Roles = "Accountant,Admin")]
public class CostingController(
    AppDbContext db,
    NotificationService notificationService,
    ILogger<CostingController> logger) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// V2.3-A authorization for Accountant + Admin. Admin is cross-branch (always allowed).
    /// Accountant is scoped via the M:N UserBranches table — NOT the JWT branchId claim,
    /// since Accountants can be assigned to multiple branches and the User.BranchId column
    /// is documented as "ignored" for that role. The previous CurrentBranchId-based check
    /// blocked accountants from any req in a branch other than their User.BranchId hint.
    /// </summary>
    private async Task<bool> AccountantAuthorizedForBranchAsync(int branchId)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (role == "Admin") return true;
        if (role != "Accountant") return false;
        return await db.UserBranches.AnyAsync(ub => ub.UserId == CurrentUserId && ub.BranchId == branchId);
    }

    // V3 — full BOM tree + cost data per FG. New-shape JSON consumed by V3 web UI (Phase B).
    // wastagePercent + purchaseValuePerKg/Currency on cost lines are still derived from
    // BomLine + BomCostLine (V2.3 entities). The V3 BomCost reshape (Task 25.5) added
    // PrintingCost*, FohPerKg, TransportPerKg, CommissionPerKg as real columns.
    [HttpGet("{requisitionId}")]
    public async Task<IActionResult> Get(int requisitionId)
    {
        var req = await db.QuotationRequests
            .Include(q => q.Customer)
            .Include(q => q.SalesPerson)
            .Include(q => q.Items).ThenInclude(ri => ri.Item)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(b => b!.Cost)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader)
                .ThenInclude(b => b!.Lines).ThenInclude(l => l.Process)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader)
                .ThenInclude(b => b!.Lines).ThenInclude(l => l.RawMaterial)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req is null) return NotFound();
        if (!await AccountantAuthorizedForBranchAsync(req.BranchId)) return Forbid();

        var bomHeaderIds = req.Items
            .Where(ri => ri.BomHeader is not null)
            .Select(ri => ri.BomHeader!.Id).ToList();

        // Load V2.3 BomCostLine rows keyed by BomLineId so we can join wastagePercent
        // (which lives on BomLine) with purchaseValuePerKg/purchaseCurrency (on BomCostLine).
        var costLinesByBomLineId = bomHeaderIds.Count > 0
            ? await db.BomCostLines
                .Where(bcl => bomHeaderIds.Contains(bcl.BomHeaderId))
                .ToDictionaryAsync(bcl => bcl.BomLineId)
            : new Dictionary<int, BomCostLine>();

        var finishedGoods = req.Items.OrderBy(ri => ri.SortOrder).Select(ri =>
        {
            var bom = ri.BomHeader;
            var cost = bom?.Cost;

            object? bomLines = bom is null ? null : bom.Lines
                .OrderBy(l => l.Process.DisplayOrder)
                .ThenBy(l => l.Id)
                .Select(l => new
                {
                    l.Id,
                    l.QtyPerKg,
                    l.Micron,
                    Item = new { l.RawMaterial.Id, l.RawMaterial.Code, l.RawMaterial.Description },
                    l.LastModifiedByUserId,
                    l.LastModifiedAt
                });

            object? costs = cost is null ? null : new
            {
                cost.PrintingCostPerKg,
                cost.PrintingCostCurrency,
                cost.FohPerKg,
                cost.TransportPerKg,
                cost.CommissionPerKg,
                Lines = bom!.Lines.Select(l => new
                {
                    BomLineId = l.Id,
                    WastagePercent = l.WastagePct,
                    PurchaseValuePerKg = costLinesByBomLineId.TryGetValue(l.Id, out var bcl)
                        ? (decimal?)bcl.CostPerKg : null,
                    PurchaseCurrency = costLinesByBomLineId.TryGetValue(l.Id, out var bcl2)
                        ? bcl2.CurrencyCode : null
                })
            };

            return new
            {
                ri.Id,
                ri.ExpectedQty,
                ri.HasPrinting,
                Item = new { ri.Item.Id, ri.Item.Code, ri.Item.Description },
                BomLines = bomLines,
                Costs = costs
            };
        }).ToList();

        return Ok(new
        {
            req.Id,
            req.RefNo,
            Status = req.Status.ToString(),
            req.CurrencyCode,
            req.Notes,
            Customer = new { req.Customer.Id, req.Customer.Name, req.Customer.Code },
            SalesPerson = new { req.SalesPerson.Id, req.SalesPerson.Name },
            FinishedGoods = finishedGoods
        });
    }

    // V3 — editable BOM endpoint. Accountant can adjust QtyPerKg / Micron / ItemId
    // (mapped to RawMaterialItemId on entity) on existing BOM lines while in Costing.
    // New-line creation is rejected with 400 in Phase A — V3 cost-entity reshape needs to
    // land first to specify ProcessId / WastagePct defaults for fresh lines.
    [HttpPut("{requisitionId}/bom")]
    public async Task<IActionResult> UpdateBom(int requisitionId, [FromBody] UpdateBomRequest body)
    {
        var req = await db.QuotationRequests
            .Include(r => r.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(bh => bh!.Lines)
            .FirstOrDefaultAsync(r => r.Id == requisitionId);
        if (req is null) return NotFound();
        if (!await AccountantAuthorizedForBranchAsync(req.BranchId)) return Forbid();

        if (req.Status != RequisitionStatus.Costing)
            return BadRequest(new { error = $"BOM editable only in Costing status (current: {req.Status})" });

        var fg = req.Items.FirstOrDefault(ri => ri.Id == body.FinishedGoodId);
        if (fg is null)
            return BadRequest(new { error = "FG not found in this requisition" });

        var bom = fg.BomHeader;
        if (bom is null)
            return BadRequest(new { error = "FG has no BOM yet" });

        // Validate target ItemIds for update operations: must exist, be active, RawMaterial type,
        // and belong to the requisition's branch. New-line creation is gated below (Phase A gap),
        // so only updates need validation here.
        var updateItemIds = body.Lines
            .Where(l => l.Id is not null && !l.Delete)
            .Select(l => l.ItemId)
            .Distinct()
            .ToList();
        if (updateItemIds.Count > 0)
        {
            var validIds = await db.Items
                .Where(i => updateItemIds.Contains(i.Id)
                         && i.IsActive
                         && i.Type == ItemType.RawMaterial
                         && i.BranchId == req.BranchId)
                .Select(i => i.Id)
                .ToListAsync();
            var invalidIds = updateItemIds.Except(validIds).ToHashSet();
            if (invalidIds.Count > 0)
            {
                var builder = Validation.Detail("One or more BOM line ItemIds are invalid (must be active raw materials in this requisition's branch).");
                for (int i = 0; i < body.Lines.Count; i++)
                {
                    var l = body.Lines[i];
                    if (l.Id is not null && !l.Delete && invalidIds.Contains(l.ItemId))
                        builder.Field($"Lines[{i}].ItemId", "Invalid, inactive, wrong type, or wrong branch.");
                }
                return builder.Return();
            }
        }

        var now = DateTime.UtcNow;
        var mutated = false;

        foreach (var line in body.Lines)
        {
            if (line.Id is null && !line.Delete)
            {
                // Phase A gap — see DTO comment + Task 23/24 deferral notes.
                return BadRequest(new
                {
                    error = "Creating new BOM lines via this endpoint is not yet supported (Phase A gap)"
                });
            }

            var existing = bom.Lines.FirstOrDefault(bl => bl.Id == line.Id);
            if (existing is null) continue;

            if (line.Delete)
            {
                db.BomLines.Remove(existing);
                mutated = true;
                continue;
            }

            if (existing.QtyPerKg != line.QtyPerKg
                || existing.Micron != line.Micron
                || existing.RawMaterialItemId != line.ItemId)
            {
                existing.QtyPerKg = line.QtyPerKg;
                existing.Micron = line.Micron;
                existing.RawMaterialItemId = line.ItemId;
                existing.LastModifiedByUserId = CurrentUserId;
                existing.LastModifiedAt = now;
                mutated = true;
            }
        }

        if (mutated)
        {
            req.UpdatedAt = now;
            await db.SaveChangesAsync();
        }

        return Ok(new { ok = true, finishedGoodId = body.FinishedGoodId });
    }

    // V3 — bulk cost-data upsert. Replaces V2.3 per-FG Start/SaveDraft/SubmitItem cycle
    // (Decision #17 — whole-req single state, accountant costs all FGs together).
    //
    // Idempotent: a second call replaces all BomCost + BomCostLine rows for the requisition.
    // Status guard: Costing only. Caller must provide a CostInput per FG of the requisition;
    // partial submissions are rejected (frontend pads in default zeros for FGs the user
    // hasn't touched yet — accountant sees them as "needs cost" until filled).
    [HttpPut("{requisitionId}/cost-data")]
    public async Task<IActionResult> SaveV3CostData(int requisitionId, [FromBody] SaveV3CostDataRequest body)
    {
        await using var tx = await db.Database.BeginTransactionAsync();

        // Lock the requisition row to serialize concurrent saves for the same req.
        var req = await db.QuotationRequests
            .FromSqlInterpolated($"SELECT * FROM \"QuotationRequests\" WHERE \"Id\" = {requisitionId} FOR UPDATE")
            .FirstOrDefaultAsync();
        if (req is null) return NotFound();
        if (!await AccountantAuthorizedForBranchAsync(req.BranchId)) return Forbid();
        if (req.Status != RequisitionStatus.Costing)
            return Validation
                .Detail($"Cost data can only be saved when status is Costing (current: {req.Status})")
                .Field("Status", "Must be Costing.")
                .Return();

        // Load all FGs with their BOMs + lines so we can validate + upsert.
        var fgs = await db.RequisitionItems
            .Include(ri => ri.BomHeader).ThenInclude(b => b!.Lines)
            .Include(ri => ri.BomHeader).ThenInclude(b => b!.Cost)
            .Where(ri => ri.QuotationRequestId == requisitionId)
            .ToListAsync();

        // Validate every FG of the req is in the payload exactly once.
        var fgIds = fgs.Select(f => f.Id).ToHashSet();
        var payloadIds = body.FinishedGoods.Select(p => p.RequisitionItemId).ToList();
        var dupes = payloadIds.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0)
            return Validation.Detail($"Duplicate FG entries: {string.Join(", ", dupes)}")
                .Field("FinishedGoods", "Each FG may appear only once.").Return();
        var unknown = payloadIds.Where(id => !fgIds.Contains(id)).ToList();
        if (unknown.Count > 0)
            return Validation.Detail($"Unknown FG(s): {string.Join(", ", unknown)}")
                .Field("FinishedGoods", "Unknown FG id.").Return();
        var missing = fgIds.Where(id => !payloadIds.Contains(id)).ToList();
        if (missing.Count > 0)
            return Validation.Detail($"Missing FG(s): {string.Join(", ", missing)}")
                .Field("FinishedGoods", "All FGs must be included.").Return();

        // Validate per-FG: every BOM line costed exactly once, no negatives, printing/currency rules.
        var validationBuilder = Validation.Detail("V3 cost data validation failed");
        bool hasErrors = false;
        for (int i = 0; i < body.FinishedGoods.Count; i++)
        {
            var fgInput = body.FinishedGoods[i];
            var fg = fgs.First(f => f.Id == fgInput.RequisitionItemId);
            if (fg.BomHeader is null)
            {
                validationBuilder.Field($"FinishedGoods[{i}].RequisitionItemId", "FG has no BOM header.");
                hasErrors = true;
                continue;
            }

            var bomLineIds = fg.BomHeader.Lines.Select(l => l.Id).ToHashSet();
            var inputLineIds = fgInput.RawMaterialCosts.Select(rc => rc.BomLineId).ToList();
            var dupLines = inputLineIds.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupLines.Count > 0)
            {
                validationBuilder.Field($"FinishedGoods[{i}].RawMaterialCosts",
                    $"Duplicate BOM line entries: {string.Join(", ", dupLines)}");
                hasErrors = true;
            }
            var unknownLines = inputLineIds.Where(id => !bomLineIds.Contains(id)).ToList();
            if (unknownLines.Count > 0)
            {
                validationBuilder.Field($"FinishedGoods[{i}].RawMaterialCosts",
                    $"Unknown BOM line(s): {string.Join(", ", unknownLines)}");
                hasErrors = true;
            }
            var missingLines = bomLineIds.Where(id => !inputLineIds.Contains(id)).ToList();
            if (missingLines.Count > 0)
            {
                validationBuilder.Field($"FinishedGoods[{i}].RawMaterialCosts",
                    $"Missing cost for BOM line(s): {string.Join(", ", missingLines)}");
                hasErrors = true;
            }

            for (int j = 0; j < fgInput.RawMaterialCosts.Count; j++)
            {
                if (fgInput.RawMaterialCosts[j].CostPerKg < 0)
                {
                    validationBuilder.Field($"FinishedGoods[{i}].RawMaterialCosts[{j}].CostPerKg", "Cannot be negative.");
                    hasErrors = true;
                }
            }

            if (fg.HasPrinting)
            {
                if (fgInput.PrintingCostPerKg is null || fgInput.PrintingCostPerKg < 0)
                {
                    validationBuilder.Field($"FinishedGoods[{i}].PrintingCostPerKg",
                        "FG has printing — cost is required and must be >= 0.");
                    hasErrors = true;
                }
                if (string.IsNullOrWhiteSpace(fgInput.PrintingCostCurrency))
                {
                    validationBuilder.Field($"FinishedGoods[{i}].PrintingCostCurrency",
                        "FG has printing — currency is required.");
                    hasErrors = true;
                }
            }
            else
            {
                if (fgInput.PrintingCostPerKg is not null && fgInput.PrintingCostPerKg != 0)
                {
                    validationBuilder.Field($"FinishedGoods[{i}].PrintingCostPerKg",
                        "FG does not have printing — cost must be omitted or zero.");
                    hasErrors = true;
                }
            }

            if (fgInput.FohPerKg < 0 || fgInput.TransportPerKg < 0 || fgInput.CommissionPerKg < 0)
            {
                if (fgInput.FohPerKg < 0)
                    validationBuilder.Field($"FinishedGoods[{i}].FohPerKg", "Cannot be negative.");
                if (fgInput.TransportPerKg < 0)
                    validationBuilder.Field($"FinishedGoods[{i}].TransportPerKg", "Cannot be negative.");
                if (fgInput.CommissionPerKg < 0)
                    validationBuilder.Field($"FinishedGoods[{i}].CommissionPerKg", "Cannot be negative.");
                hasErrors = true;
            }
        }
        if (hasErrors) return validationBuilder.Return();

        // Currency conversion: load active rates for every currency seen in the payload.
        var quoteCurrency = (req.CurrencyCode ?? "AED").ToUpperInvariant();
        var allCurrencies = body.FinishedGoods
            .SelectMany(f => f.RawMaterialCosts.Select(r => (r.CurrencyCode ?? "AED").ToUpperInvariant()))
            .Concat(body.FinishedGoods
                .Where(f => !string.IsNullOrWhiteSpace(f.PrintingCostCurrency))
                .Select(f => f.PrintingCostCurrency!.ToUpperInvariant()))
            .Append(quoteCurrency)
            .Distinct().ToList();
        var rates = (await db.ExchangeRates
            .Where(e => e.IsActive && allCurrencies.Contains(e.CurrencyCode))
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

        // Per-FG upsert: replace BomCost + BomCostLine rows.
        var now = DateTime.UtcNow;
        var allUpserts = new List<(int ItemId, decimal CostPerKg, string CurrencyCode)>();
        foreach (var fgInput in body.FinishedGoods)
        {
            var fg = fgs.First(f => f.Id == fgInput.RequisitionItemId);
            var bom = fg.BomHeader!;

            // Compute cost lines + raw material total in quote currency.
            decimal rawMaterialTotal = 0;
            var newCostLines = new List<BomCostLine>();
            foreach (var rc in fgInput.RawMaterialCosts)
            {
                var line = bom.Lines.First(l => l.Id == rc.BomLineId);
                var currency = (rc.CurrencyCode ?? "AED").ToUpperInvariant();
                decimal entryRate;
                try { entryRate = RateToAed(currency); }
                catch (InvalidOperationException ex)
                {
                    return Validation.Detail(ex.Message).Field("CurrencyCode", ex.Message).Return();
                }
                // Persist accountant's wastage edit on the BomLine so it flows into
                // the cost computation here AND into the read DTO for the MD page.
                if (rc.WastagePercent < 0)
                    return Validation.Detail("Wastage % cannot be negative.")
                        .Field("WastagePercent", "Must be >= 0.").Return();
                line.WastagePct = rc.WastagePercent;

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
                allUpserts.Add((line.RawMaterialItemId, rc.CostPerKg, currency));
            }

            // V3 totals: per-KG breakdown sums into TotalCostPerKg in quote currency.
            decimal totalCost = rawMaterialTotal + fgInput.FohPerKg + fgInput.TransportPerKg + fgInput.CommissionPerKg;
            if (fgInput.PrintingCostPerKg is decimal printing && printing > 0)
            {
                var printCcy = (fgInput.PrintingCostCurrency ?? "AED").ToUpperInvariant();
                decimal printRate;
                try { printRate = RateToAed(printCcy); }
                catch (InvalidOperationException ex)
                {
                    return Validation.Detail(ex.Message).Field("PrintingCostCurrency", ex.Message).Return();
                }
                totalCost += printing * printRate / quoteRateToAed;
            }

            // Replace BomCost + BomCostLine rows for this FG (idempotent upsert).
            if (bom.Cost is not null) db.BomCosts.Remove(bom.Cost);
            var existingLines = await db.BomCostLines.Where(l => l.BomHeaderId == bom.Id).ToListAsync();
            if (existingLines.Count > 0) db.BomCostLines.RemoveRange(existingLines);
            db.BomCostLines.AddRange(newCostLines);

            db.BomCosts.Add(new BomCost
            {
                BomHeaderId = bom.Id,
                RawMaterialCostTotal = rawMaterialTotal,
                LandedCostType = LandedCostType.FixedValue,
                LandedCostValue = 0,
                FohAmount = fgInput.FohPerKg,
                TotalCostPerKg = totalCost,
                SubmittedByUserId = CurrentUserId,
                PrintingCostPerKg = fg.HasPrinting ? fgInput.PrintingCostPerKg : null,
                PrintingCostCurrency = fg.HasPrinting && fgInput.PrintingCostPerKg is not null && fgInput.PrintingCostPerKg > 0
                    ? (fgInput.PrintingCostCurrency ?? "AED").ToUpperInvariant()
                    : null,
                FohPerKg = fgInput.FohPerKg,
                TransportPerKg = fgInput.TransportPerKg,
                CommissionPerKg = fgInput.CommissionPerKg,
            });
            bom.TotalCostPerKg = totalCost;

            // Drop any V2.3 draft for this BOM (clean cut-over to V3 path).
            var draft = await db.CostingDrafts.FirstOrDefaultAsync(d => d.BomHeaderId == bom.Id);
            if (draft is not null) db.CostingDrafts.Remove(draft);
        }

        // Upsert ItemLastCost for every distinct RM (last write wins, ordered to avoid deadlock).
        var grouped = allUpserts.GroupBy(u => u.ItemId).Select(g => g.Last()).OrderBy(u => u.ItemId).ToList();
        foreach (var u in grouped)
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

        req.UpdatedAt = now;
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(new { id = req.Id, status = req.Status.ToString(), fgCount = body.FinishedGoods.Count });
    }

    // V3 — single overall Submit (Costing -> MdPricing). Replaces V2.3 per-item SubmitItem
    // promotion to MdReview. CostFxSnapshot capture is deferred to ApprovalsController.SetMargin
    // (Phase A Task 28) — single-approval-row creation point.
    [HttpPost("{requisitionId}/submit")]
    public async Task<IActionResult> Submit(int requisitionId)
    {
        var req = await db.QuotationRequests
            .Include(r => r.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(bh => bh!.Cost)
            .FirstOrDefaultAsync(r => r.Id == requisitionId);
        if (req is null) return NotFound();
        if (!await AccountantAuthorizedForBranchAsync(req.BranchId)) return Forbid();

        if (!RequisitionStateMachine.CanTransition(req.Status, RequisitionStatus.MdPricing))
            return BadRequest(new { error = $"Cannot submit costing from {req.Status}" });

        if (req.Items.Count == 0
            || req.Items.Any(ri => ri.BomHeader is null || ri.BomHeader.Cost is null))
        {
            return BadRequest(new { error = "All FGs must have cost data before submit" });
        }

        req.Status = RequisitionStatus.MdPricing;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        try
        {
            var mdIds = await db.Users
                .Where(u => u.Role == UserRole.ManagingDirector && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync();
            await notificationService.SendToUsersAsync(
                mdIds,
                $"{req.RefNo} awaiting your margin",
                req.Id,
                "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful costing submit for {Entity} {Id}",
                "QuotationRequest", req.Id);
        }

        return Ok(new { id = req.Id, status = req.Status.ToString() });
    }

    [HttpPost("{requisitionId}/items/{requisitionItemId}/start")]
    public async Task<IActionResult> StartItem(int requisitionId, int requisitionItemId)
    {
        var req = await db.QuotationRequests.FindAsync(requisitionId);
        if (req is null) return NotFound();
        if (!await AccountantAuthorizedForBranchAsync(req.BranchId)) return Forbid();
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
        if (!await AccountantAuthorizedForBranchAsync(req.BranchId)) return Forbid();
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
        if (!await AccountantAuthorizedForBranchAsync(req.BranchId)) return Forbid();
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
