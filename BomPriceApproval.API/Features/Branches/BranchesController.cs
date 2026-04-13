using BomPriceApproval.API.Infrastructure.Data;
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
        Ok(await db.Branches.Select(b => new { b.Id, b.Name }).ToListAsync());
}
