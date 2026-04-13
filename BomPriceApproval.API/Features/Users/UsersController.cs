using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Users;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public class UsersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.Users.Include(u => u.Branch)
            .Select(u => new UserResponse(u.Id, u.Name, u.Email, u.Role.ToString(), u.BranchId, u.Branch == null ? null : u.Branch.Name, u.IsActive))
            .ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest req)
    {
        if (await db.Users.AnyAsync(u => u.Email == req.Email))
            return Conflict(new { message = "Email already exists" });

        var user = new User
        {
            Name = req.Name, Email = req.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = req.Role, BranchId = req.BranchId
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new UserResponse(user.Id, user.Name, user.Email, user.Role.ToString(), user.BranchId, null, user.IsActive));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateUserRequest req)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();
        user.Name = req.Name; user.Email = req.Email;
        user.Role = req.Role; user.BranchId = req.BranchId; user.IsActive = req.IsActive;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();
        user.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
