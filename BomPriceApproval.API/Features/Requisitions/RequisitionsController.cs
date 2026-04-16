using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Requisitions;

[ApiController]
[Route("api/requisitions")]
[Authorize]
public class RequisitionsController(AppDbContext db, NotificationService notificationService) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole => User.FindFirstValue(ClaimTypes.Role)!;
    private int? CurrentBranchId => int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0 ? b : null;

    [HttpGet]
    public async Task<IActionResult> GetAll()
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

        return Ok(await query.OrderByDescending(q => q.CreatedAt)
            .Select(q => new RequisitionListItem(
                q.Id, q.RefNo, q.Status.ToString(), q.Items.Count,
                q.Customer.Name, q.CurrencyCode,
                q.Branch.Name, q.SalesPerson.Name, q.CreatedAt))
            .ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var q = await db.QuotationRequests
            .Include(r => r.Items).ThenInclude(ri => ri.Item)
            .Include(r => r.Customer)
            .Include(r => r.Branch).Include(r => r.SalesPerson)
            .Include(r => r.Approval)
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
            q.Approval is null ? null : new ApprovalSummary(q.Approval.IsApproved)));
    }

    [HttpPost]
    [Authorize(Roles = "SalesPerson")]
    public async Task<IActionResult> Create(CreateRequisitionRequest req)
    {
        if (CurrentBranchId is null)
            return BadRequest(new { message = "A branch-assigned sales person is required to create requisitions." });

        if (req.Items.Count == 0)
            return BadRequest(new { message = "At least one item is required." });

        decimal? rateSnapshot = null;
        if (req.CurrencyCode != "AED")
        {
            var rate = await db.ExchangeRates
                .Where(e => e.CurrencyCode == req.CurrencyCode && e.IsActive)
                .OrderByDescending(e => e.EffectiveDate).FirstOrDefaultAsync();
            if (rate is null) return BadRequest(new { message = $"No active exchange rate for {req.CurrencyCode}" });
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

        var bomCreators = await db.Users
            .Where(u => u.Role == UserRole.BomCreator && (u.BranchId == requisition.BranchId || u.BranchId == null) && u.IsActive)
            .ToListAsync();

        foreach (var creator in bomCreators)
            await notificationService.SendAsync(creator.Id,
                $"New BOM request: {requisition.RefNo}", requisition.Id, "QuotationRequest");

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
            return BadRequest(new { message = "Items can only be added when status is BomPending" });

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
            return BadRequest(new { message = "Items can only be removed when status is BomPending" });

        if (q.Items.Count <= 1)
            return BadRequest(new { message = "Cannot remove the last item" });

        var ri = q.Items.FirstOrDefault(i => i.Id == requisitionItemId);
        if (ri is null) return NotFound();

        db.RequisitionItems.Remove(ri);
        await db.SaveChangesAsync();
        return NoContent();
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
