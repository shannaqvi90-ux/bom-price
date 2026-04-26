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

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var q = await db.QuotationRequests
            .Include(r => r.Items).ThenInclude(ri => ri.Item)
            .Include(r => r.Customer)
            .Include(r => r.Branch).Include(r => r.SalesPerson)
            .Include(r => r.Approvals.Where(a => !a.IsSuperseded)).ThenInclude(a => a.Items)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (q is null) return NotFound();
        if (!CanAccess(q)) return Forbid();

        return Ok(new RequisitionDetail(
            q.Id, q.RefNo, q.Status.ToString(),
            q.CustomerId, q.Customer.Name, q.Customer.Email, q.Customer.PhoneNumber, q.Customer.Address,
            q.CurrencyCode, q.ExchangeRateSnapshot,
            q.BranchId, q.Branch.Name, q.SalesPersonId, q.SalesPerson.Name,
            q.CreatedAt, q.UpdatedAt,
            q.Items.OrderBy(ri => ri.SortOrder).Select(ri => new RequisitionItemDto(
                ri.Id, ri.ItemId, ri.Item.Description, ri.ExpectedQty, ri.SortOrder)).ToList(),
            q.Approvals
                .Where(a => !a.IsSuperseded)
                .Select(a => new ApprovalSummary(
                    a.IsApproved, a.Notes, a.ApprovedAt,
                    a.Items.Select(ai => new ApprovalItemPrice(
                        ai.RequisitionItemId,
                        ai.SalesPricePerKgAed,
                        ai.SalesPricePerKgForeign)).ToList()))
                .FirstOrDefault()));
    }

    [HttpPost]
    [Authorize(Roles = "SalesPerson,Accountant")]
    public async Task<IActionResult> Create(CreateRequisitionRequest req)
    {
        // V23a: SP picks branch per-req. Accept payload BranchId.
        // Transition fallback (1 release): if SP omits BranchId, fall back to User.BranchId (logged).
        int branchId;
        if (req.BranchId.HasValue)
        {
            branchId = req.BranchId.Value;
        }
        else if (CurrentBranchId.HasValue)
        {
            logger.LogWarning("V23a transition: requisition created without payload BranchId by user {UserId}; falling back to User.BranchId={BranchId}",
                CurrentUserId, CurrentBranchId.Value);
            branchId = CurrentBranchId.Value;
        }
        else
        {
            return Validation
                .Detail("BranchId is required.")
                .Field("BranchId", "Branch must be specified.")
                .Return();
        }

        // Validate branch exists and is active
        var branch = await db.Branches.FindAsync(branchId);
        if (branch is null || !branch.IsActive)
            return Validation.Detail("Branch not found or inactive.").Field("BranchId", "Invalid branch.").Return();

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

        // Validate every item belongs to the chosen branch
        var dbItems = await db.Items.Where(i => distinctItemIds.Contains(i.Id)).ToListAsync();
        var mismatched = dbItems.Where(i => i.BranchId != branchId).ToList();
        if (mismatched.Count > 0)
            return Validation
                .Detail($"{mismatched.Count} item(s) do not belong to the selected branch.")
                .Field("Items", $"Items not in branch {branchId}: {string.Join(", ", mismatched.Select(m => m.Code))}")
                .Return();

        bool customerExists;
        if (CurrentRole == "SalesPerson")
        {
            var spUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == CurrentUserId);
            if (spUser is null) return Forbid();
            var visibleSpIds = SalesAuthorization.VisibleSalesPersonIds(spUser, db);
            customerExists = await db.Customers.AnyAsync(c =>
                c.Id == req.CustomerId &&
                c.SalesPersonId.HasValue && visibleSpIds.Contains(c.SalesPersonId.Value));
        }
        else
        {
            customerExists = await db.Customers.AnyAsync(c =>
                c.Id == req.CustomerId &&
                (!CurrentBranchId.HasValue || CurrentRole == "Accountant" || c.SalesPersonId == CurrentUserId));
        }
        if (!customerExists)
            return Validation
                .Detail("Customer not found.")
                .Field("CustomerId", "Unknown customer.")
                .Return();

        decimal? rateSnapshot = null;
        if (req.CurrencyCode != "AED")
        {
            var rate = await db.ExchangeRates
                .Where(e => e.CurrencyCode == req.CurrencyCode && e.IsActive)
                .OrderByDescending(e => e.EffectiveDate).FirstOrDefaultAsync();
            if (rate is null)
                return Validation
                    .Detail($"No active exchange rate for {req.CurrencyCode}")
                    .Field("CurrencyCode", "No active exchange rate.")
                    .Return();
            rateSnapshot = rate.RateToAed;
        }

        var requisition = new QuotationRequest
        {
            BranchId = branchId,
            SalesPersonId = CurrentUserId,
            CustomerId = req.CustomerId,
            CurrencyCode = req.CurrencyCode,
            ExchangeRateSnapshot = rateSnapshot,
            Status = RequisitionStatus.BomPending,
            Items = req.Items.Select((item, i) => new RequisitionItem
            {
                ItemId = item.ItemId,
                ExpectedQty = item.ExpectedQty,
                SortOrder = i + 1
            }).ToList()
        };

        db.QuotationRequests.Add(requisition);
        await db.SaveChangesAsync();

        try
        {
            var bomCreatorCandidates = await db.Users
                .Where(u => u.Role == UserRole.BomCreator && u.IsActive)
                .ToListAsync();
            var bomCreators = bomCreatorCandidates
                .Where(u => BranchAuthorization.UserAuthorizedForBranch(u, requisition.BranchId, db))
                .ToList();

            foreach (var creator in bomCreators)
                await notificationService.SendAsync(creator.Id,
                    $"New BOM request: {requisition.RefNo}", requisition.Id, "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful commit for {Entity} {Id}",
                "QuotationRequest", requisition.Id);
        }

        return CreatedAtAction(nameof(Get), new { id = requisition.Id }, new { requisition.Id, requisition.RefNo });
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

    [HttpPost("{id}/resubmit")]
    [Authorize(Roles = "SalesPerson,Accountant")]
    public async Task<IActionResult> Resubmit(int id, ResubmitRequisitionRequest req)
    {
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

            foreach (var creator in resubmitBomCreators)
                await notificationService.SendAsync(creator.Id,
                    $"Resubmitted BOM request: {q.RefNo}", q.Id, "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful commit for {Entity} {Id}",
                "QuotationRequest", q.Id);
        }

        return Ok(new { q.Id, q.RefNo, Status = q.Status.ToString() });
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

            var mds = await db.Users.Where(u => u.Role == UserRole.ManagingDirector && u.IsActive).ToListAsync();
            foreach (var md in mds)
                await notificationService.SendAsync(md.Id, message, q.Id, "QuotationRequest");
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

            var mds = await db.Users.Where(u => u.Role == UserRole.ManagingDirector && u.IsActive).ToListAsync();
            foreach (var md in mds) await notificationService.SendAsync(md.Id, msg, q.Id, "QuotationRequest");

            var allUsers = await db.Users
                .Where(u => u.IsActive && (u.Role == UserRole.BomCreator || u.Role == UserRole.Accountant))
                .ToListAsync();
            foreach (var u in allUsers)
            {
                if (BranchAuthorization.UserAuthorizedForBranch(u, oldBranchId, db) ||
                    BranchAuthorization.UserAuthorizedForBranch(u, req.BranchId, db))
                {
                    await notificationService.SendAsync(u.Id, msg, q.Id, "QuotationRequest");
                }
            }
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
