using System.Security.Claims;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Admin;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController(AppDbContext db, AdminAuditLogger audit, NotificationService notify) : ControllerBase
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

    [HttpDelete("requisitions/{id}")]
    public async Task<IActionResult> DeleteRequisition(int id, [FromBody] DeleteRequisitionRequest? body)
    {
        if (body is null)
            return BadRequest(new { error = "Request body is required" });

        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
            return BadRequest(new { error = "Reason is required (min 5 chars)" });

        var req = await db.QuotationRequests
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (req is null) return NotFound();

        var snapshot = new
        {
            req.Id,
            req.RefNo,
            req.Status,
            req.SalesPersonId,
            req.BranchId,
            req.CustomerId,
            ItemCount = req.Items.Count,
            BomHeaderCount = await db.BomHeaders.CountAsync(b => b.RequisitionItem.QuotationRequestId == id),
            ApprovalCount = await db.QuotationApprovals.CountAsync(a => a.QuotationRequestId == id)
        };

        var spId = req.SalesPersonId;
        var branchId = req.BranchId;
        var refNo = req.RefNo;

        db.QuotationRequests.Remove(req);
        audit.Log(CurrentUserId, AdminActionType.DeleteRequisition, "Requisition", id, body.Reason, snapshot, after: (object?)null);
        await db.SaveChangesAsync();

        // Notify SP + branch BomCreators/Accountants + all MDs
        var recipientIds = await db.Users
            .Where(u => (u.Id == spId)
                || (u.BranchId == branchId && (u.Role == UserRole.BomCreator || u.Role == UserRole.Accountant))
                || u.Role == UserRole.ManagingDirector)
            .Select(u => u.Id)
            .ToListAsync();

        foreach (var uid in recipientIds)
        {
            await notify.SendAsync(uid, $"Requisition {refNo} was deleted by Admin",
                referenceId: id,
                referenceType: nameof(NotificationType.RequisitionDeleted));
        }

        return NoContent();
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
}
