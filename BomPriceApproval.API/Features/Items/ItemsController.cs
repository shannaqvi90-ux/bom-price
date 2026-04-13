using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
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
    public async Task<IActionResult> GetAll([FromQuery] string? type = null)
    {
        var query = db.Items.AsQueryable();
        if (CurrentBranchId.HasValue) query = query.Where(i => i.BranchId == CurrentBranchId);
        if (type is not null && Enum.TryParse<ItemType>(type, out var t))
            query = query.Where(i => i.Type == t);
        return Ok(await query.Where(i => i.IsActive)
            .Select(i => new ItemResponse(i.Id, i.Code, i.Description, i.Type.ToString(), i.BranchId, i.IsActive))
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
            return BadRequest(new { message = "A branch-assigned user is required to create items." });

        var item = new Item
        {
            Code = req.Code, Description = req.Description, Type = req.Type,
            BranchId = CurrentBranchId.Value
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll),
            new ItemResponse(item.Id, item.Code, item.Description, item.Type.ToString(), item.BranchId, item.IsActive));
    }
}
