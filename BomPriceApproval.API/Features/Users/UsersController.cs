using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Validation;
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
        var email = req.Email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { message = "Email already exists" });

        var user = new User
        {
            Name = req.Name, Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = req.Role, BranchId = req.BranchId
        };
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (
            ex.InnerException?.Message.Contains("IX_Users_Email", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Validation
                .Detail("Email already registered.")
                .Field("Email", "Already registered.")
                .Return();
        }
        return CreatedAtAction(nameof(GetAll), new UserResponse(user.Id, user.Name, user.Email, user.Role.ToString(), user.BranchId, null, user.IsActive));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateUserRequest req)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();

        var oldRole = user.Role;
        var oldBranchId = user.BranchId;

        user.Name = req.Name;
        user.Email = req.Email.Trim().ToLowerInvariant();
        user.Role = req.Role; user.BranchId = req.BranchId; user.IsActive = req.IsActive;

        if (user.Role != oldRole || user.BranchId != oldBranchId)
        {
            var activeTokens = await db.RefreshTokens
                .Where(rt => rt.UserId == user.Id && !rt.IsRevoked)
                .ToListAsync();
            foreach (var rt in activeTokens)
                rt.IsRevoked = true;
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (
            ex.InnerException?.Message.Contains("IX_Users_Email", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Validation
                .Detail("Email already registered.")
                .Field("Email", "Already registered.")
                .Return();
        }
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
