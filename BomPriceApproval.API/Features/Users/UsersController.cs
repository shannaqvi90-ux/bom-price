using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Users;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(AppDbContext db, ILogger<UsersController> logger) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.Users.Include(u => u.Branch)
            .Select(u => new UserResponse(u.Id, u.Name, u.Email, u.Role.ToString(), u.BranchId, u.Branch == null ? null : u.Branch.Name, u.IsActive))
            .ToListAsync());

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(CreateUserRequest req)
    {
        if (PasswordValidator.Validate(req.Password) is { } pwdError)
            return Validation.Detail(pwdError).Field("Password", pwdError).Return();

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

        logger.LogInformation("[Audit] User created {TargetUserId} {Email} Role={Role} BranchId={BranchId}",
            user.Id, user.Email, user.Role, user.BranchId);

        return CreatedAtAction(nameof(GetAll), new UserResponse(user.Id, user.Name, user.Email, user.Role.ToString(), user.BranchId, null, user.IsActive));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
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

        if (user.Role != oldRole || user.BranchId != oldBranchId)
        {
            logger.LogWarning("[Audit] User role/branch changed {TargetUserId} {Email} OldRole={OldRole} NewRole={NewRole} OldBranchId={OldBranchId} NewBranchId={NewBranchId}",
                user.Id, user.Email, oldRole, user.Role, oldBranchId, user.BranchId);
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();
        user.IsActive = false;
        await db.SaveChangesAsync();

        logger.LogWarning("[Audit] User deactivated {TargetUserId} {Email}",
            user.Id, user.Email);

        return NoContent();
    }

    // Admin-only: revoke all active refresh tokens for a user. Effectively kicks
    // the user out of every device/session — on next /refresh they get a 401 and
    // must log in again. Access tokens issued before revocation stay valid until
    // their 15-min expiry; for a full immediate kick, the client must also log out.
    [HttpPost("{id}/revoke-sessions")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RevokeSessions(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();

        var tokens = await db.RefreshTokens
            .Where(rt => rt.UserId == id && !rt.IsRevoked)
            .ToListAsync();
        foreach (var rt in tokens)
            rt.IsRevoked = true;
        await db.SaveChangesAsync();

        logger.LogWarning("[Audit] Sessions revoked {TargetUserId} {Email} RevokedTokenCount={Count}",
            user.Id, user.Email, tokens.Count);

        return Ok(new { revokedTokens = tokens.Count });
    }

    [HttpGet("{id}/branches")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetBranches(int id)
    {
        var ids = await db.UserBranches
            .Where(ub => ub.UserId == id)
            .Select(ub => ub.BranchId)
            .OrderBy(x => x)
            .ToListAsync();
        return Ok(ids);
    }

    [HttpPut("{id}/branches")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetBranches(int id, SetUserBranchesRequest req)
    {
        var u = await db.Users.FindAsync(id);
        if (u is null) return NotFound();
        if (u.Role != UserRole.Accountant)
            return Validation.Detail("Branches can only be set on Accountants.")
                .Field("Role", "Must be Accountant.")
                .Return();

        // Validate all branch IDs exist and are active
        var distinct = req.BranchIds.Distinct().ToList();
        var validIds = await db.Branches
            .Where(b => distinct.Contains(b.Id) && b.IsActive)
            .Select(b => b.Id)
            .ToListAsync();
        var invalid = distinct.Except(validIds).ToList();
        if (invalid.Any())
            return Validation.Detail($"Invalid branch ids: {string.Join(",", invalid)}")
                .Field("BranchIds", "Some branches not found or inactive.")
                .Return();

        // Replace semantics — remove existing, add new
        var existing = db.UserBranches.Where(ub => ub.UserId == id);
        db.UserBranches.RemoveRange(existing);
        foreach (var bid in distinct)
            db.UserBranches.Add(new UserBranch { UserId = id, BranchId = bid });
        await db.SaveChangesAsync();

        logger.LogInformation("[Audit] UserBranches updated {TargetUserId} BranchIds={BranchIds}",
            id, string.Join(",", distinct));

        return NoContent();
    }

    [HttpGet("{id}/group")]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<IActionResult> GetGroup(int id)
    {
        var u = await db.Users
            .Where(x => x.Id == id)
            .Select(x => new { x.GroupId, GroupName = x.Group != null ? x.Group.Name : null })
            .FirstOrDefaultAsync();
        if (u is null) return NotFound();
        return Ok(new UserGroupResponse(u.GroupId, u.GroupName));
    }

    [HttpPut("{id}/group")]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<IActionResult> SetGroup(int id, SetUserGroupRequest req)
    {
        var u = await db.Users.FindAsync(id);
        if (u is null) return NotFound();
        if (u.Role != UserRole.SalesPerson)
            return Validation.Detail("Groups can only be set on SalesPersons.")
                .Field("Role", "Must be SalesPerson.")
                .Return();

        if (req.GroupId.HasValue)
        {
            var grp = await db.SalesGroups.FindAsync(req.GroupId.Value);
            if (grp is null || !grp.IsActive)
                return Validation.Detail("Group not found or inactive.")
                    .Field("GroupId", "Invalid group.")
                    .Return();
        }

        u.GroupId = req.GroupId;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
