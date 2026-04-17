using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Items;

[ApiController]
[Route("api/items")]
[Authorize]
public class ItemsController(AppDbContext db) : ControllerBase
{
    private int? CurrentBranchId => int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0 ? b : null;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? type = null, [FromQuery] bool includeInactive = false)
    {
        var query = db.Items.AsQueryable();
        if (CurrentBranchId.HasValue) query = query.Where(i => i.BranchId == CurrentBranchId);
        if (type is not null && Enum.TryParse<ItemType>(type, out var t))
            query = query.Where(i => i.Type == t);
        if (!includeInactive)
            query = query.Where(i => i.IsActive);
        return Ok(await query
            .Select(i => new ItemResponse(i.Id, i.Code, i.Description, i.Type.ToString(), i.BranchId, i.IsActive, i.LastPurchasePrice))
            .ToListAsync());
    }

    [HttpGet("check-similar")]
    public async Task<IActionResult> CheckSimilar([FromQuery] string description)
    {
        var branchId = CurrentBranchId;
        var similar = await db.Items
            .Where(i => (branchId == null || i.BranchId == branchId) &&
                        EF.Functions.ILike(i.Description, $"%{description}%"))
            .Select(i => new SimilarItemResult(i.Id, i.Code, i.Description))
            .Take(5).ToListAsync();
        return Ok(similar);
    }

    [HttpPost]
    [Authorize(Roles = "SalesPerson,Admin")]
    public async Task<IActionResult> Create(CreateItemRequest req)
    {
        if (CurrentBranchId is null)
            return Validation
                .Detail("A branch-assigned user is required to create items.")
                .Field("BranchId", "A branch-assigned user is required.")
                .Return();

        var item = new Item
        {
            Code = req.Code, Description = req.Description, Type = req.Type,
            BranchId = CurrentBranchId.Value,
            LastPurchasePrice = req.LastPurchasePrice
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll),
            new ItemResponse(item.Id, item.Code, item.Description, item.Type.ToString(), item.BranchId, item.IsActive, item.LastPurchasePrice));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<IActionResult> Update(int id, UpdateItemRequest req)
    {
        var item = await db.Items.FindAsync(id);
        if (item is null) return NotFound();
        if (CurrentBranchId.HasValue && item.BranchId != CurrentBranchId)
            return Forbid();

        var duplicate = await db.Items.AnyAsync(i => i.Code == req.Code && i.BranchId == item.BranchId && i.Id != id);
        if (duplicate) return Conflict(new { message = "An item with this code already exists in the branch." });

        item.Code = req.Code;
        item.Description = req.Description;
        item.Type = req.Type;
        item.LastPurchasePrice = req.LastPurchasePrice;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id}/status")]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateItemStatusRequest req)
    {
        var item = await db.Items.FindAsync(id);
        if (item is null) return NotFound();
        if (CurrentBranchId.HasValue && item.BranchId != CurrentBranchId)
            return Forbid();

        item.IsActive = req.IsActive;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
