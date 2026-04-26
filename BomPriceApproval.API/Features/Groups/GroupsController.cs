using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Groups;

[ApiController]
[Route("api/groups")]
[Authorize]
public class GroupsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.SalesGroups
            .OrderBy(g => g.Id)
            .Select(g => new GroupAdminResponse(g.Id, g.Name, g.IsActive))
            .ToListAsync());

    [HttpPost]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<IActionResult> Create(CreateGroupRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Validation.Detail("Group name is required.").Field("Name", "Required.").Return();

        var entity = new SalesGroup { Name = req.Name.Trim(), IsActive = true };
        db.SalesGroups.Add(entity);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new GroupAdminResponse(entity.Id, entity.Name, entity.IsActive));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<IActionResult> Update(int id, UpdateGroupRequest req)
    {
        var g = await db.SalesGroups.FindAsync(id);
        if (g is null) return NotFound();
        g.Name = req.Name.Trim();
        g.IsActive = req.IsActive;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<IActionResult> Delete(int id)
    {
        var g = await db.SalesGroups.FindAsync(id);
        if (g is null) return NotFound();

        // Block soft-delete if any user references this group (regardless of IsActive)
        var inUse = await db.Users.AnyAsync(u => u.GroupId == id);
        if (inUse)
            return Conflict(new { message = $"Group {g.Name} has assigned users and cannot be deleted." });

        g.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
