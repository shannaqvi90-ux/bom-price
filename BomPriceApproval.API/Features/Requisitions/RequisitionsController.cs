using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Requisitions;

[ApiController]
[Route("api/requisitions")]
[Authorize]
public class RequisitionsController(AppDbContext db) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole => User.FindFirstValue(ClaimTypes.Role)!;
    private int? CurrentBranchId => int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0 ? b : null;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var query = db.QuotationRequests
            .Include(q => q.Item).Include(q => q.Customer)
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
                q.Id, q.RefNo, q.Status.ToString(), q.Item.Description,
                q.Customer.Name, q.ExpectedQty, q.CurrencyCode,
                q.Branch.Name, q.SalesPerson.Name, q.CreatedAt))
            .ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var q = await db.QuotationRequests
            .Include(r => r.Item).Include(r => r.Customer)
            .Include(r => r.Branch).Include(r => r.SalesPerson)
            .Include(r => r.BomHeader).ThenInclude(b => b != null ? b.Cost : null)
            .Include(r => r.Approval)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (q is null) return NotFound();
        if (!CanAccess(q)) return Forbid();

        return Ok(new RequisitionDetail(
            q.Id, q.RefNo, q.Status.ToString(),
            q.ItemId, q.Item.Description,
            q.CustomerId, q.Customer.Name, q.Customer.Email, q.Customer.PhoneNumber, q.Customer.Address,
            q.ExpectedQty, q.CurrencyCode, q.ExchangeRateSnapshot,
            q.BranchId, q.Branch.Name, q.SalesPersonId, q.SalesPerson.Name,
            q.CreatedAt, q.UpdatedAt,
            q.BomHeader is null ? null : new BomSummary(q.BomHeader.Id, q.BomHeader.TotalCostPerKg, q.BomHeader.Cost is not null),
            q.Approval is null ? null : new ApprovalSummary(q.Approval.SalesPricePerKgAed, q.Approval.SalesPricePerKgForeign, q.Approval.ProfitMarginPct, q.Approval.IsApproved)));
    }

    [HttpPost]
    [Authorize(Roles = "SalesPerson")]
    public async Task<IActionResult> Create(CreateRequisitionRequest req)
    {
        if (CurrentBranchId is null)
            return BadRequest(new { message = "A branch-assigned sales person is required to create requisitions." });

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
            ItemId = req.ItemId,
            ExpectedQty = req.ExpectedQty,
            CurrencyCode = req.CurrencyCode,
            ExchangeRateSnapshot = rateSnapshot,
            Status = RequisitionStatus.BomPending
        };

        db.QuotationRequests.Add(requisition);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = requisition.Id }, new { requisition.Id, requisition.RefNo });
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
