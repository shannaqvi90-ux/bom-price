using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Processes;

[ApiController]
[Route("api/processes")]
public class ProcessesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.Processes.OrderBy(p => p.DisplayOrder)
            .Select(p => new ProcessResponse(p.Id, p.Name, p.DisplayOrder, p.IsActive))
            .ToListAsync());

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(CreateProcessRequest req)
    {
        var process = new Process { Name = req.Name, DisplayOrder = req.DisplayOrder };
        db.Processes.Add(process);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new ProcessResponse(process.Id, process.Name, process.DisplayOrder, process.IsActive));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, UpdateProcessRequest req)
    {
        var p = await db.Processes.FindAsync(id);
        if (p is null) return NotFound();
        p.Name = req.Name; p.DisplayOrder = req.DisplayOrder; p.IsActive = req.IsActive;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await db.Processes.FindAsync(id);
        if (p is null) return NotFound();
        p.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
