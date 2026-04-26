using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BomPriceApproval.API.Features.Items;

[ApiController]
[Route("api/items")]
[Authorize]
public class ItemsController(AppDbContext db) : ControllerBase
{
    private int? CurrentBranchId => int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0 ? b : null;
    private string? CurrentRole => User.FindFirstValue(ClaimTypes.Role);

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? branchId = null,
        [FromQuery] string? type = null,
        [FromQuery] bool includeInactive = false)
    {
        var query = db.Items.AsQueryable();

        // V23a: SP role server-enforces FinishedGood-only (defense-in-depth — UI also filters)
        if (CurrentRole == "SalesPerson")
        {
            query = query.Where(i => i.Type == ItemType.FinishedGood);
        }
        else if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<ItemType>(type, ignoreCase: true, out var parsed))
        {
            query = query.Where(i => i.Type == parsed);
        }

        // Branch scoping: an explicit ?branchId= param takes precedence (SP cross-branch picker).
        // Without it, JWT-bound users fall back to their own branch.
        if (branchId.HasValue)
            query = query.Where(i => i.BranchId == branchId.Value);
        else if (CurrentBranchId.HasValue)
            query = query.Where(i => i.BranchId == CurrentBranchId);

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

        var duplicateExists = await db.Items.AnyAsync(i =>
            i.Code == req.Code &&
            i.BranchId == CurrentBranchId.Value);
        if (duplicateExists)
            return Validation
                .Detail("An item with this code already exists in the branch.")
                .Field("Code", "Already exists.")
                .Return();

        var item = new Item
        {
            Code = req.Code, Description = req.Description, Type = req.Type,
            BranchId = CurrentBranchId.Value,
            LastPurchasePrice = req.LastPurchasePrice
        };
        db.Items.Add(item);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            return Validation
                .Detail("An item with this code already exists in the branch.")
                .Field("Code", "Already exists.")
                .Return();
        }
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
