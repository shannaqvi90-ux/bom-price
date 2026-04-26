using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Branches;

[ApiController]
[Route("api/branches")]
[Authorize]
public class BranchesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.Branches
            .OrderBy(b => b.Id)
            .Select(b => new BranchAdminResponse(b.Id, b.Name, b.IsActive))
            .ToListAsync());

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(CreateBranchRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Validation.Detail("Branch name is required.").Field("Name", "Required.").Return();

        var entity = new Branch { Name = req.Name.Trim(), IsActive = true };
        db.Branches.Add(entity);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new BranchAdminResponse(entity.Id, entity.Name, entity.IsActive));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, UpdateBranchRequest req)
    {
        var b = await db.Branches.FindAsync(id);
        if (b is null) return NotFound();
        b.Name = req.Name.Trim();
        b.IsActive = req.IsActive;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var b = await db.Branches.FindAsync(id);
        if (b is null) return NotFound();

        // Block soft-delete if branch is in active use
        var inUse = await db.Users.AnyAsync(u => u.BranchId == id)
                 || await db.QuotationRequests.AnyAsync(q => q.BranchId == id)
                 || await db.Items.AnyAsync(i => i.BranchId == id)
                 || await db.UserBranches.AnyAsync(ub => ub.BranchId == id);
        if (inUse)
            return Conflict(new { message = $"Branch {b.Name} is in use and cannot be deleted." });

        b.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
