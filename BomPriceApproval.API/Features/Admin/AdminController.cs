using System.Security.Claims;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Admin;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController(AppDbContext db) : ControllerBase
{
    [HttpGet("audit-log")]
    public async Task<IActionResult> GetAuditLog(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] AdminActionType? actionType = null,
        [FromQuery] int? adminUserId = null,
        [FromQuery] string? entityType = null,
        [FromQuery] int? entityId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var q = db.AdminAuditLogs.Include(x => x.AdminUser).AsQueryable();
        if (actionType.HasValue) q = q.Where(x => x.ActionType == actionType.Value);
        if (adminUserId.HasValue) q = q.Where(x => x.AdminUserId == adminUserId.Value);
        if (!string.IsNullOrEmpty(entityType)) q = q.Where(x => x.EntityType == entityType);
        if (entityId.HasValue) q = q.Where(x => x.EntityId == entityId.Value);
        if (from.HasValue) q = q.Where(x => x.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(x => x.CreatedAt <= to.Value);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AuditLogItemDto(
                x.Id,
                x.AdminUserId,
                x.AdminUser.Name,
                x.ActionType.ToString(),
                x.EntityType,
                x.EntityId,
                x.Reason,
                x.BeforeJson,
                x.AfterJson,
                x.CreatedAt))
            .ToListAsync();

        return Ok(new AuditLogPagedResponse(items, total, page, pageSize));
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
}
