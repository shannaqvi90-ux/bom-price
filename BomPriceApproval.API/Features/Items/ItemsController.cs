using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Items;

[ApiController]
[Route("api/items")]
[Authorize]
public class ItemsController(AppDbContext db, ICodeGeneratorService codeGen) : ControllerBase
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
        // Resolve target branch: payload BranchId (admin path) OR caller's
        // branch from JWT (SP path). Admin has CurrentBranchId == null and
        // must specify a branch in the payload.
        int branchId;
        if (req.BranchId.HasValue)
        {
            branchId = req.BranchId.Value;
        }
        else if (CurrentBranchId.HasValue)
        {
            branchId = CurrentBranchId.Value;
        }
        else
        {
            return Validation
                .Detail("BranchId is required.")
                .Field("BranchId", "Select a branch.")
                .Return();
        }

        // Validate branch exists and is active
        var branch = await db.Branches.FindAsync(branchId);
        if (branch is null || !branch.IsActive)
            return Validation
                .Detail("Branch not found or inactive.")
                .Field("BranchId", "Invalid branch.")
                .Return();

        var item = new Item
        {
            Code = await codeGen.NextItemCodeAsync(req.Type),
            Description = req.Description,
            Type = req.Type,
            BranchId = branchId,
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
