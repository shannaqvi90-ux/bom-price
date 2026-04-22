using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
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
        [FromQuery] string? status = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var query = db.QuotationRequests
            .Include(q => q.Items)
            .Include(q => q.Customer)
            .Include(q => q.Branch).Include(q => q.SalesPerson)
            .AsQueryable();

        query = CurrentRole switch
        {
            "SalesPerson" => query.Where(q => q.SalesPersonId == CurrentUserId),
            "BomCreator" => query.Where(q => q.BranchId == CurrentBranchId),
            _ when CurrentBranchId.HasValue => query.Where(q => q.BranchId == CurrentBranchId),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<RequisitionStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(q => q.Status == parsedStatus);
        }

        var projected = query.OrderByDescending(q => q.CreatedAt)
            .Select(q => new RequisitionListItem(
                q.Id, q.RefNo, q.Status.ToString(), q.Items.Count,
                q.Customer.Name, q.CurrencyCode,
                q.Branch.Name, q.SalesPerson.Name, q.CreatedAt));

        if (page.HasValue && pageSize.HasValue)
        {
            var p = Math.Max(page.Value, 1);
            var ps = Math.Clamp(pageSize.Value, 1, 100);
            projected = projected.Skip((p - 1) * ps).Take(ps);
        }

        return Ok(await projected.ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var q = await db.QuotationRequests
            .Include(r => r.Items).ThenInclude(ri => ri.Item)
            .Include(r => r.Customer)
            .Include(r => r.Branch).Include(r => r.SalesPerson)
            .Include(r => r.Approvals)
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
                .Select(a => new ApprovalSummary(a.IsApproved, a.Notes, a.ApprovedAt))
                .FirstOrDefault()));
    }

    [HttpPost]
    [Authorize(Roles = "SalesPerson")]
    public async Task<IActionResult> Create(CreateRequisitionRequest req)
    {
        if (CurrentBranchId is null)
            return Validation
                .Detail("A branch-assigned sales person is required to create requisitions.")
                .Field("BranchId", "A branch-assigned sales person is required.")
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

        var customerExists = await db.Customers.AnyAsync(c =>
            c.Id == req.CustomerId &&
            (!CurrentBranchId.HasValue || c.SalesPersonId == CurrentUserId));
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
            BranchId = CurrentBranchId.Value,
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
            var bomCreators = await db.Users
                .Where(u => u.Role == UserRole.BomCreator && (u.BranchId == requisition.BranchId || u.BranchId == null) && u.IsActive)
                .ToListAsync();

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
    [Authorize(Roles = "SalesPerson")]
    public async Task<IActionResult> AddItem(int id, AddRequisitionItemRequest req)
    {
        var q = await db.QuotationRequests.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id);
        if (q is null) return NotFound();
        if (q.SalesPersonId != CurrentUserId) return Forbid();
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
    [Authorize(Roles = "SalesPerson")]
    public async Task<IActionResult> RemoveItem(int id, int requisitionItemId)
    {
        var q = await db.QuotationRequests.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id);
        if (q is null) return NotFound();
        if (q.SalesPersonId != CurrentUserId) return Forbid();
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
    [Authorize(Roles = "SalesPerson")]
    public async Task<IActionResult> Resubmit(int id, ResubmitRequisitionRequest req)
    {
        var q = await db.QuotationRequests
            .Include(r => r.Items)
            .Include(r => r.Approvals)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (q is null) return NotFound();
        if (q.SalesPersonId != CurrentUserId) return Forbid();

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
            var bomCreators = await db.Users
                .Where(u => u.Role == UserRole.BomCreator && (u.BranchId == q.BranchId || u.BranchId == null) && u.IsActive)
                .ToListAsync();

            foreach (var creator in bomCreators)
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

    private bool CanAccess(QuotationRequest q) => CurrentRole switch
    {
        "SalesPerson" => q.SalesPersonId == CurrentUserId,
        "BomCreator" => q.BranchId == CurrentBranchId,
        "Accountant" => true,
        "ManagingDirector" => true,
        "Admin" => true,
        _ => false
    };
}
