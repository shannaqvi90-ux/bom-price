using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Domain.Workflow;
using BomPriceApproval.API.Infrastructure.Authorization;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Requisitions;

[ApiController]
[Route("api/requisitions")]
[Authorize]
public class RequisitionsController(
    AppDbContext db,
    NotificationService notificationService,
    ILogger<RequisitionsController> logger) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole => User.FindFirstValue(ClaimTypes.Role)!;
    private int? CurrentBranchId => int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0 ? b : null;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery(Name = "status")] string[]? statuses = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var query = db.QuotationRequests
            .Include(q => q.Items)
            .Include(q => q.Customer)
            .Include(q => q.Branch).Include(q => q.SalesPerson)
            .AsQueryable();

        // Branch scoping (V23a — uses UserBranches for Accountant)
        if (CurrentRole == "Accountant")
        {
            var assignedBranchIds = await db.UserBranches
                .Where(ub => ub.UserId == CurrentUserId)
                .Select(ub => ub.BranchId)
                .ToListAsync();
            query = query.Where(q => assignedBranchIds.Contains(q.BranchId));
        }
        else if (CurrentRole == "BomCreator" && CurrentBranchId.HasValue)
        {
            query = query.Where(q => q.BranchId == CurrentBranchId.Value);
        }
        else if (CurrentRole == "SalesPerson")
        {
            var me = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == CurrentUserId);
            if (me is null) return Forbid();
            var visibleSpIds = SalesAuthorization.VisibleSalesPersonIds(me, db);
            query = query.Where(q => visibleSpIds.Contains(q.SalesPersonId));
        }
        // MD + Admin: no scoping

        if (statuses is { Length: > 0 })
        {
            var parsed = statuses
                .Select(s => Enum.TryParse<RequisitionStatus>(s, ignoreCase: true, out var r) ? r : (RequisitionStatus?)null)
                .Where(r => r.HasValue)
                .Select(r => r!.Value)
                .ToArray();
            if (parsed.Length > 0)
                query = query.Where(q => parsed.Contains(q.Status));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(q =>
                EF.Functions.ILike(q.RefNo, $"%{term}%") ||
                EF.Functions.ILike(q.Customer.Name, $"%{term}%"));
        }

        if (from.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
            query = query.Where(q => q.UpdatedAt >= fromUtc);
        }

        if (to.HasValue)
        {
            // Exclusive upper bound: < to + 1 day (includes the entire to-date)
            var toUtc = DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(q => q.UpdatedAt < toUtc);
        }

        var projected = query.OrderByDescending(q => q.CreatedAt)
            .Select(q => new RequisitionListItem(
                q.Id, q.RefNo, q.Status.ToString(), q.Items.Count,
                q.Customer.Name, q.CurrencyCode,
                q.BranchId, q.Branch.Name,
                q.SalesPersonId, q.SalesPerson.Name, q.CreatedAt));

        if (page.HasValue && pageSize.HasValue)
        {
            var p = Math.Max(page.Value, 1);
            var ps = Math.Clamp(pageSize.Value, 1, 100);
            projected = projected.Skip((p - 1) * ps).Take(ps);
        }

        return Ok(await projected.ToListAsync());
    }

    [HttpGet("count")]
    public async Task<IActionResult> Count([FromQuery] string? status = null)
    {
        var query = db.QuotationRequests.AsQueryable();

        if (CurrentRole == "SalesPerson")
        {
            var me = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == CurrentUserId);
            if (me is null) return Forbid();
            var visibleSpIds = SalesAuthorization.VisibleSalesPersonIds(me, db);
            query = query.Where(q => visibleSpIds.Contains(q.SalesPersonId));
        }
        else
        {
            query = CurrentRole switch
            {
                "BomCreator" => query.Where(q => q.BranchId == CurrentBranchId),
                _ when CurrentBranchId.HasValue => query.Where(q => q.BranchId == CurrentBranchId),
                _ => query
            };
        }

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<RequisitionStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(q => q.Status == parsedStatus);
        }

        return Ok(new { count = await query.CountAsync() });
    }

    // V3 GET shape — nested finishedGoods[].bomLines[] + costs (matches bom-web V3Requisition).
    // BomLine + BomCost reads mirror CostingController.Get's projection so the wastage/purchase
    // mapping stays consistent across the two read endpoints.
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        // AsSplitQuery: 5-level Includes (Items × BomHeader × {Cost, Lines × {Process, RawMaterial}})
        // Cartesian-explode the joined row count at scale (10 FG × 10 lines = 100 rows shipping
        // every parent column on each row). Hottest V3 endpoint — every detail page hits it.
        var q = await db.QuotationRequests
            .AsSplitQuery()
            .Include(r => r.Customer)
            .Include(r => r.Branch).Include(r => r.SalesPerson)
            .Include(r => r.Items).ThenInclude(ri => ri.Item)
            .Include(r => r.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(b => b!.Cost)
            .Include(r => r.Items).ThenInclude(ri => ri.BomHeader)
                .ThenInclude(b => b!.Lines).ThenInclude(l => l.Process)
            .Include(r => r.Items).ThenInclude(ri => ri.BomHeader)
                .ThenInclude(b => b!.Lines).ThenInclude(l => l.RawMaterial)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (q is null) return NotFound();
        if (!CanAccess(q)) return Forbid();

        // BomCostLines key on BomLineId — needed to merge purchaseValuePerKg/currency
        // (BomCostLine) with wastagePercent (BomLine.WastagePct).
        var bomHeaderIds = q.Items
            .Where(ri => ri.BomHeader is not null)
            .Select(ri => ri.BomHeader!.Id).ToList();
        var costLinesByBomLineId = bomHeaderIds.Count > 0
            ? await db.BomCostLines
                .Where(bcl => bomHeaderIds.Contains(bcl.BomHeaderId))
                .ToDictionaryAsync(bcl => bcl.BomLineId)
            : new Dictionary<int, BomCostLine>();

        var finishedGoods = q.Items.OrderBy(ri => ri.SortOrder).Select(ri =>
        {
            var bom = ri.BomHeader;
            var cost = bom?.Cost;

            List<V3BomLineDto>? bomLines = bom is null ? null : bom.Lines
                .OrderBy(l => l.Process.DisplayOrder)
                .ThenBy(l => l.Id)
                .Select(l => new V3BomLineDto(
                    l.Id,
                    l.QtyPerKg,
                    l.Micron,
                    new V3ItemSummary(l.RawMaterial.Id, l.RawMaterial.Code, l.RawMaterial.Description),
                    l.LastModifiedByUserId,
                    l.LastModifiedAt))
                .ToList();

            V3BomCostDto? costs = cost is null ? null : new V3BomCostDto(
                cost.PrintingCostPerKg,
                cost.PrintingCostCurrency,
                cost.FohPerKg,
                cost.TransportPerKg,
                cost.CommissionPerKg,
                bom!.Lines.Select(l => new V3BomCostLineDto(
                    l.Id,
                    l.WastagePct,
                    costLinesByBomLineId.TryGetValue(l.Id, out var bcl)
                        ? (decimal?)bcl.CostPerKg : null,
                    costLinesByBomLineId.TryGetValue(l.Id, out var bcl2)
                        ? bcl2.CurrencyCode : null))
                .ToList());

            return new V3FinishedGoodDto(
                ri.Id,
                ri.ExpectedQty,
                ri.HasPrinting,
                new V3ItemSummary(ri.Item.Id, ri.Item.Code, ri.Item.Description),
                bomLines,
                costs);
        }).ToList();

        return Ok(new V3RequisitionDetail(
            q.Id,
            q.RefNo,
            q.Status.ToString(),
            q.CurrencyCode,
            q.Notes,
            new V3CustomerSummary(q.Customer.Id, q.Customer.Name, q.Customer.Code),
            new V3SalesPersonSummary(q.SalesPerson.Id, q.SalesPerson.Name),
            finishedGoods));
    }

    [HttpPost]
    [Authorize(Roles = "SalesPerson,Admin")]
    public async Task<IActionResult> Create([FromBody] CreateRequisitionV3Request req)
    {
        // Validate customer exists + not deleted
        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.Id == req.CustomerId && !c.IsDeleted);
        if (customer is null) return NotFound(new { error = "Customer not found" });

        // Validate currency
        if (string.IsNullOrWhiteSpace(req.QuotationCurrency))
            return BadRequest(new { error = "QuotationCurrency required" });

        // Validate at least one FG
        if (req.FinishedGoods is null || req.FinishedGoods.Count == 0)
            return BadRequest(new { error = "At least one finished good required" });

        // V3 — each FG must appear at most once (D17 whole-req state per FG)
        var duplicateFg = req.FinishedGoods.GroupBy(fg => fg.ItemId).FirstOrDefault(g => g.Count() > 1);
        if (duplicateFg is not null)
            return BadRequest(new { error = $"Finished good item {duplicateFg.Key} appears more than once in payload" });

        // Validate all referenced items (FG + RM) exist + active
        var allItemIds = req.FinishedGoods
            .SelectMany(fg => new[] { fg.ItemId }.Concat(fg.BomLines.Select(b => b.ItemId)))
            .Distinct()
            .ToList();
        var items = await db.Items
            .Where(i => allItemIds.Contains(i.Id) && i.IsActive)
            .ToDictionaryAsync(i => i.Id);
        foreach (var id in allItemIds)
            if (!items.ContainsKey(id))
                return BadRequest(new { error = $"Item {id} not found or inactive" });

        // Validate all referenced ProcessIds exist
        var allProcessIds = req.FinishedGoods
            .SelectMany(fg => fg.BomLines.Select(b => b.ProcessId))
            .Distinct()
            .ToList();
        if (allProcessIds.Count > 0)
        {
            var validProcessIds = await db.Processes
                .Where(p => allProcessIds.Contains(p.Id) && p.IsActive)
                .Select(p => p.Id)
                .ToListAsync();
            var invalidProcessId = allProcessIds.Except(validProcessIds).FirstOrDefault();
            if (invalidProcessId != 0)
                return BadRequest(new { error = $"Process {invalidProcessId} not found or inactive" });
        }

        // Find Alain branch (V3 scope)
        var alainBranch = await db.Branches
            .FirstOrDefaultAsync(b => b.Name == "Al Ain" && b.IsActive);
        if (alainBranch is null) return BadRequest(new { error = "Al Ain branch not configured" });

        // Sales person id: SP uses self; Admin uses customer's SP or falls back to self
        int salesPersonId = CurrentRole == "SalesPerson"
            ? CurrentUserId
            : (customer.SalesPersonId ?? CurrentUserId);

        await using var tx = await db.Database.BeginTransactionAsync();

        var requisition = new QuotationRequest
        {
            BranchId = alainBranch.Id,
            CustomerId = customer.Id,
            SalesPersonId = salesPersonId,
            Status = RequisitionStatus.Draft,
            CurrencyCode = req.QuotationCurrency,
            Notes = req.Notes,                      // V3 — persist free-text note
            ReferenceNumber = req.ReferenceNumber,  // V3 — persist customer ref
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.QuotationRequests.Add(requisition);
        await db.SaveChangesAsync();

        foreach (var (fg, sortIndex) in req.FinishedGoods.Select((x, i) => (x, i)))
        {
            var reqItem = new RequisitionItem
            {
                QuotationRequestId = requisition.Id,
                ItemId = fg.ItemId,
                ExpectedQty = fg.ExpectedQtyKg,
                HasPrinting = fg.Printing,
                SortOrder = sortIndex + 1
            };
            db.RequisitionItems.Add(reqItem);
            await db.SaveChangesAsync();

            var bomHeader = new BomHeader
            {
                RequisitionItemId = reqItem.Id,
                CreatedByUserId = CurrentUserId,
                CreatedAt = DateTime.UtcNow
            };
            db.BomHeaders.Add(bomHeader);
            await db.SaveChangesAsync();

            foreach (var line in fg.BomLines)
            {
                db.BomLines.Add(new BomLine
                {
                    BomHeaderId = bomHeader.Id,
                    ProcessId = line.ProcessId,
                    RawMaterialItemId = line.ItemId,
                    QtyPerKg = line.QtyPerKg,
                    WastagePct = 0m,
                    Micron = line.Micron
                });
            }
            await db.SaveChangesAsync();
        }

        await tx.CommitAsync();

        return CreatedAtAction(nameof(Get), new { id = requisition.Id },
            new { id = requisition.Id, status = requisition.Status.ToString() });
    }

    [HttpPost("{id}/submit")]
    [Authorize(Roles = "SalesPerson,Admin")]
    public async Task<IActionResult> Submit(int id)
    {
        var req = await db.QuotationRequests
            .Include(r => r.Items)
                .ThenInclude(ri => ri.BomHeader)
                    .ThenInclude(bh => bh!.Lines)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (req is null) return NotFound();

        if (CurrentRole == "SalesPerson" && req.SalesPersonId != CurrentUserId)
            return Forbid();

        if (!RequisitionStateMachine.CanTransition(req.Status, RequisitionStatus.Costing))
            return BadRequest(new { error = $"Cannot submit from status {req.Status}" });

        if (req.Items.Count == 0 || req.Items.Any(ri => ri.BomHeader is null || ri.BomHeader.Lines.Count == 0))
            return BadRequest(new { error = "All finished goods must have a BOM with at least one line" });

        req.Status = RequisitionStatus.Costing;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        try
        {
            var accountantIds = await db.UserBranches
                .Where(ub => ub.BranchId == req.BranchId && ub.User.Role == UserRole.Accountant && ub.User.IsActive)
                .Select(ub => ub.UserId)
                .ToListAsync();

            await notificationService.SendToUsersAsync(
                accountantIds,
                $"{req.RefNo} submitted for costing",
                req.Id,
                "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful submit for {Entity} {Id}",
                "QuotationRequest", req.Id);
        }

        return Ok(new { id = req.Id, status = req.Status.ToString() });
    }

    [HttpPost("{id}/cancel")]
    [Authorize(Roles = "SalesPerson,Admin")]
    public async Task<IActionResult> Cancel(int id, [FromBody] CancelRequisitionRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Trim().Length < 5)
            return BadRequest(new { error = "Reason >= 5 chars required" });

        var req = await db.QuotationRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (req is null) return NotFound();

        if (CurrentRole == "SalesPerson" && req.SalesPersonId != CurrentUserId)
            return Forbid();

        if (!RequisitionStateMachine.CanTransition(req.Status, RequisitionStatus.Cancelled))
            return BadRequest(new { error = $"Cannot cancel from status {req.Status}" });

        req.Status = RequisitionStatus.Cancelled;
        req.CancelledAt = DateTime.UtcNow;
        req.CancelledByUserId = CurrentUserId;
        req.CancelReason = body.Reason.Trim();
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        try
        {
            await notificationService.SendAsync(
                req.SalesPersonId,
                $"{req.RefNo} cancelled: {body.Reason.Trim()}",
                req.Id,
                "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful cancel for {Entity} {Id}",
                "QuotationRequest", req.Id);
        }

        return Ok(new { id = req.Id, status = req.Status.ToString() });
    }

    [HttpPost("{id}/items")]
    [Authorize(Roles = "SalesPerson,Accountant")]
    public async Task<IActionResult> AddItem(int id, AddRequisitionItemRequest req)
    {
        var q = await db.QuotationRequests.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id);
        if (q is null) return NotFound();
        if (q.SalesPersonId != CurrentUserId &&
            CurrentRole != "Admin" &&
            (CurrentRole != "Accountant" || q.BranchId != CurrentBranchId))
            return Forbid();
        if (q.Status != RequisitionStatus.BomPending)
            return Validation
                .Detail("Items can only be added when status is BomPending")
                .Field("Status", "Items can only be added when status is BomPending.")
                .Return();

        if (req.ExpectedQty <= 0)
            return Validation
                .Detail("ExpectedQty must be greater than 0.")
                .Field("ExpectedQty", "Must be greater than 0.")
                .Return();

        if (q.Items.Any(i => i.ItemId == req.ItemId))
            return Validation
                .Detail("Item already added to this requisition.")
                .Field("ItemId", "Item already added.")
                .Return();

        var itemIsValid = await db.Items.AnyAsync(i => i.Id == req.ItemId && i.IsActive);
        if (!itemIsValid)
            return Validation
                .Detail($"Unknown or inactive item: {req.ItemId}")
                .Field("ItemId", "Unknown or inactive.")
                .Return();

        var maxSort = q.Items.Count > 0 ? q.Items.Max(ri => ri.SortOrder) : 0;
        var ri = new RequisitionItem
        {
            QuotationRequestId = id,
            ItemId = req.ItemId,
            ExpectedQty = req.ExpectedQty,
            SortOrder = maxSort + 1
        };
        db.RequisitionItems.Add(ri);
        await db.SaveChangesAsync();
        return Ok(new { ri.Id });
    }

    [HttpDelete("{id}/items/{requisitionItemId}")]
    [Authorize(Roles = "SalesPerson,Accountant")]
    public async Task<IActionResult> RemoveItem(int id, int requisitionItemId)
    {
        var q = await db.QuotationRequests.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id);
        if (q is null) return NotFound();
        if (q.SalesPersonId != CurrentUserId &&
            CurrentRole != "Admin" &&
            (CurrentRole != "Accountant" || q.BranchId != CurrentBranchId))
            return Forbid();
        if (q.Status != RequisitionStatus.BomPending)
            return Validation
                .Detail("Items can only be removed when status is BomPending")
                .Field("Status", "Items can only be removed when status is BomPending.")
                .Return();

        if (q.Items.Count <= 1)
            return Validation
                .Detail("Cannot remove the last item")
                .Field("Items", "Cannot remove the last item.")
                .Return();

        var ri = q.Items.FirstOrDefault(i => i.Id == requisitionItemId);
        if (ri is null) return NotFound();

        db.RequisitionItems.Remove(ri);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // V3 DEPRECATED — kept for legacy V2.3 reqs in BomPending state.
    // V3 state machine has no Resubmit semantics: rejected reqs are terminal,
    // and the V3 cutover SQL (Phase C) cancels all in-flight V2.3 reqs.
    // This endpoint sets Status=BomPending which is no longer a valid V3 transition
    // target — a successful call would leave the req in a state V3 UI cannot render.
    // Returns 410 Gone to prevent accidental use; full removal in Phase B cleanup.
    [HttpPost("{id}/resubmit")]
    [Authorize(Roles = "SalesPerson,Accountant")]
    [Obsolete("V3 has no Resubmit flow; endpoint returns 410 Gone. Removed in Phase B.")]
    public async Task<IActionResult> Resubmit(int id, ResubmitRequisitionRequest req)
    {
        return StatusCode(StatusCodes.Status410Gone, new
        {
            error = "Resubmit is not supported in V3. Rejected requisitions are terminal — create a new requisition."
        });
#pragma warning disable CS0162 // Unreachable code — body kept for legacy reference until Phase B removal.
        var q = await db.QuotationRequests
            .Include(r => r.Items)
            .Include(r => r.Approvals)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (q is null) return NotFound();
        if (q.SalesPersonId != CurrentUserId &&
            CurrentRole != "Admin" &&
            (CurrentRole != "Accountant" || q.BranchId != CurrentBranchId))
            return Forbid();

        if (q.Status != RequisitionStatus.Rejected)
            return Validation
                .Detail("Requisition is not in Rejected status.")
                .Field("Status", "Only rejected requisitions can be resubmitted.")
                .Return();

        if (req.Items.Count == 0)
            return Validation
                .Detail("At least one item is required.")
                .Field("Items", "At least one item is required.")
                .Return();

        if (req.Items.Any(i => i.ExpectedQty <= 0))
        {
            var builder = Validation.Detail("ExpectedQty must be greater than 0.");
            for (int i = 0; i < req.Items.Count; i++)
                if (req.Items[i].ExpectedQty <= 0)
                    builder.Field($"Items[{i}].ExpectedQty", "Must be greater than 0.");
            return builder.Return();
        }

        var distinctItemIds = req.Items.Select(i => i.ItemId).Distinct().ToList();
        if (distinctItemIds.Count != req.Items.Count)
            return Validation
                .Detail("Duplicate items in requisition are not allowed.")
                .Field("Items", "Duplicate items are not allowed.")
                .Return();

        var activeItemIds = await db.Items
            .Where(i => distinctItemIds.Contains(i.Id) && i.IsActive)
            .Select(i => i.Id)
            .ToListAsync();
        var missingItems = distinctItemIds.Except(activeItemIds).ToList();
        if (missingItems.Count > 0)
        {
            var builder = Validation.Detail($"Unknown or inactive items: {string.Join(", ", missingItems)}");
            for (int i = 0; i < req.Items.Count; i++)
                if (missingItems.Contains(req.Items[i].ItemId))
                    builder.Field($"Items[{i}].ItemId", "Unknown or inactive.");
            return builder.Return();
        }

        decimal? rateSnapshot = null;
        if (q.CurrencyCode != "AED")
        {
            var rate = await db.ExchangeRates
                .Where(e => e.CurrencyCode == q.CurrencyCode && e.IsActive)
                .OrderByDescending(e => e.EffectiveDate).FirstOrDefaultAsync();
            if (rate is null)
                return Validation
                    .Detail($"No active exchange rate for {q.CurrencyCode}")
                    .Field("CurrencyCode", "No active exchange rate.")
                    .Return();
            rateSnapshot = rate.RateToAed;
        }

        await using var tx = await db.Database.BeginTransactionAsync();

        var currentApproval = q.Approvals.FirstOrDefault(a => !a.IsSuperseded);
        if (currentApproval is not null)
        {
            currentApproval.IsSuperseded = true;
            currentApproval.SupersededAt = DateTime.UtcNow;
        }

        db.RequisitionItems.RemoveRange(q.Items);

        foreach (var (input, i) in req.Items.Select((x, idx) => (x, idx)))
        {
            db.RequisitionItems.Add(new RequisitionItem
            {
                QuotationRequestId = q.Id,
                ItemId = input.ItemId,
                ExpectedQty = input.ExpectedQty,
                SortOrder = i + 1
            });
        }

        q.ExchangeRateSnapshot = rateSnapshot;
        q.Status = RequisitionStatus.BomPending;
        q.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        try
        {
            var resubmitBomCandidates = await db.Users
                .Where(u => u.Role == UserRole.BomCreator && u.IsActive)
                .ToListAsync();
            var resubmitBomCreators = resubmitBomCandidates
                .Where(u => BranchAuthorization.UserAuthorizedForBranch(u, q.BranchId, db))
                .ToList();

            await notificationService.SendToUsersAsync(
                resubmitBomCreators.Select(u => u.Id),
                $"Resubmitted BOM request: {q.RefNo}",
                q.Id,
                "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful commit for {Entity} {Id}",
                "QuotationRequest", q.Id);
        }

        return Ok(new { q.Id, q.RefNo, Status = q.Status.ToString() });
#pragma warning restore CS0162
    }

    [HttpPatch("{id}/customer")]
    [Authorize(Roles = "Accountant,Admin")]
    public async Task<IActionResult> ChangeCustomer(int id, ChangeCustomerRequest req)
    {
        var q = await db.QuotationRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (q is null) return NotFound();

        // Accountant is branch-scoped (matches AddItem/RemoveItem/Resubmit guards).
        // Admin (null branch) bypasses.
        if (CurrentRole == "Accountant" && q.BranchId != CurrentBranchId)
            return Forbid();

        if (q.Status != RequisitionStatus.CostingPending &&
            q.Status != RequisitionStatus.CostingInProgress)
        {
            return Validation
                .Detail("Customer can only be changed during the costing stage.")
                .Field("Status", "Not in a costing state.")
                .Return();
        }

        if (req.CustomerId == q.CustomerId)
        {
            return Validation
                .Detail("New customer is the same as the current customer.")
                .Field("CustomerId", "No change.")
                .Return();
        }

        var newCustomerExists = await db.Customers.AnyAsync(c => c.Id == req.CustomerId);
        if (!newCustomerExists) return NotFound();

        var oldCustomerId = q.CustomerId;

        await using var tx = await db.Database.BeginTransactionAsync();

        q.CustomerId = req.CustomerId;
        q.UpdatedAt = DateTime.UtcNow;

        db.CustomerChangeHistories.Add(new CustomerChangeHistory
        {
            RequisitionId = q.Id,
            OldCustomerId = oldCustomerId,
            NewCustomerId = req.CustomerId,
            ChangedByUserId = CurrentUserId,
            Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim()
        });

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        // Fire-and-forget notification (same try/catch pattern as Create / Resubmit)
        try
        {
            var oldCust = await db.Customers.FindAsync(oldCustomerId);
            var newCust = await db.Customers.FindAsync(req.CustomerId);
            var actor = await db.Users.FindAsync(CurrentUserId);
            var message = $"Customer on {q.RefNo} changed from {oldCust?.Name} to {newCust?.Name} by {actor?.Name}";

            await notificationService.SendAsync(q.SalesPersonId, message, q.Id, "QuotationRequest");

            var mdIds = await db.Users
                .Where(u => u.Role == UserRole.ManagingDirector && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync();
            await notificationService.SendToUsersAsync(mdIds, message, q.Id, "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful customer change for {Entity} {Id}",
                "QuotationRequest", q.Id);
        }

        return NoContent();
    }

    [HttpPatch("{id}/branch")]
    [Authorize(Roles = "Accountant,Admin")]
    public async Task<IActionResult> ChangeBranch(int id, ChangeBranchRequest req)
    {
        var q = await db.QuotationRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (q is null) return NotFound();

        // Status guard — only allowed up to CostingPending
        var allowed = new[] { RequisitionStatus.BomPending, RequisitionStatus.BomInProgress, RequisitionStatus.CostingPending };
        if (!allowed.Contains(q.Status))
            return Validation
                .Detail($"Branch change not allowed for status {q.Status}.")
                .Field("Status", "Invalid status for branch change.")
                .Return();

        // Branch authorization for the actor — Accountant must be assigned to the CURRENT (old) branch
        if (CurrentRole == "Accountant")
        {
            var actorAuthorized = await db.UserBranches.AnyAsync(ub => ub.UserId == CurrentUserId && ub.BranchId == q.BranchId);
            if (!actorAuthorized) return Forbid();
        }

        // New branch must exist + be active
        var newBranch = await db.Branches.FindAsync(req.BranchId);
        if (newBranch is null || !newBranch.IsActive) return NotFound();

        // Same-branch rejection
        if (req.BranchId == q.BranchId)
            return Validation
                .Detail("New branch is the same as current.")
                .Field("BranchId", "Pick a different branch.")
                .Return();

        // Strict block: any req item must already belong to the new branch
        var reqItemIds = await db.RequisitionItems
            .Where(ri => ri.QuotationRequestId == id)
            .Select(ri => ri.ItemId)
            .ToListAsync();
        var dbItems = await db.Items.Where(i => reqItemIds.Contains(i.Id)).ToListAsync();
        var mismatched = dbItems.Where(i => i.BranchId != req.BranchId).ToList();
        if (mismatched.Any())
            return Validation
                .Detail($"{mismatched.Count} item(s) do not belong to branch {req.BranchId}.")
                .Field("Items", $"Mismatched items in branch {req.BranchId}: {string.Join(", ", mismatched.Select(m => m.Code))}")
                .Return();

        // Mutate + write history
        var oldBranchId = q.BranchId;
        q.BranchId = req.BranchId;
        q.UpdatedAt = DateTime.UtcNow;
        db.BranchChangeHistories.Add(new BranchChangeHistory
        {
            RequisitionId = id,
            OldBranchId = oldBranchId,
            NewBranchId = req.BranchId,
            ChangedByUserId = CurrentUserId,
            Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim()
        });
        await db.SaveChangesAsync();

        // Notify SP + old/new branch's BomCreator + Accountant + all MDs
        try
        {
            var oldBranch = await db.Branches.FindAsync(oldBranchId);
            var actor = await db.Users.FindAsync(CurrentUserId);
            var msg = $"Branch on {q.RefNo} changed from {oldBranch?.Name} to {newBranch.Name} by {actor?.Name}";

            await notificationService.SendAsync(q.SalesPersonId, msg, q.Id, "QuotationRequest");

            var mdIds = await db.Users
                .Where(u => u.Role == UserRole.ManagingDirector && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync();
            await notificationService.SendToUsersAsync(mdIds, msg, q.Id, "QuotationRequest");

            var allUsers = await db.Users
                .Where(u => u.IsActive && (u.Role == UserRole.BomCreator || u.Role == UserRole.Accountant))
                .ToListAsync();
            var authorizedIds = allUsers
                .Where(u => BranchAuthorization.UserAuthorizedForBranch(u, oldBranchId, db)
                            || BranchAuthorization.UserAuthorizedForBranch(u, req.BranchId, db))
                .Select(u => u.Id);
            await notificationService.SendToUsersAsync(authorizedIds, msg, q.Id, "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Notification dispatch failed after successful branch change for {Entity} {Id}", "QuotationRequest", q.Id);
        }

        return NoContent();
    }

    [HttpGet("{id}/customer-history")]
    public async Task<IActionResult> GetCustomerHistory(int id)
    {
        var q = await db.QuotationRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (q is null) return NotFound();
        if (!CanAccess(q)) return Forbid();

        var entries = await db.CustomerChangeHistories
            .Where(h => h.RequisitionId == id)
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => new CustomerChangeHistoryResponse(
                h.Id,
                h.OldCustomerId, h.OldCustomer.Name,
                h.NewCustomerId, h.NewCustomer.Name,
                h.ChangedByUserId, h.ChangedBy.Name,
                h.ChangedAt, h.Reason))
            .ToListAsync();

        return Ok(entries);
    }

    [HttpGet("{id}/branch-history")]
    public async Task<IActionResult> GetBranchHistory(int id)
    {
        var q = await db.QuotationRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (q is null) return NotFound();
        if (!CanAccess(q)) return Forbid();

        var entries = await db.BranchChangeHistories
            .Where(h => h.RequisitionId == id)
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => new BranchChangeHistoryResponse(
                h.Id,
                h.OldBranchId, h.OldBranch.Name,
                h.NewBranchId, h.NewBranch.Name,
                h.ChangedByUserId, h.ChangedBy.Name,
                h.ChangedAt, h.Reason))
            .ToListAsync();

        return Ok(entries);
    }

    private bool CanAccess(QuotationRequest q)
    {
        if (CurrentRole == "SalesPerson")
        {
            var currentUser = db.Users.FirstOrDefault(u => u.Id == CurrentUserId);
            if (currentUser is null) return false;
            var visibleIds = SalesAuthorization.VisibleSalesPersonIds(currentUser, db);
            return visibleIds.Contains(q.SalesPersonId);
        }

        return CurrentRole switch
        {
            "BomCreator" => q.BranchId == CurrentBranchId,
            "Accountant" => true,
            "ManagingDirector" => true,
            "Admin" => true,
            _ => false
        };
    }
}
