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

    [HttpPost("requisitions/{id}/rollback-status")]
    public async Task<IActionResult> RollbackStatus(int id, [FromBody] RollbackStatusRequest? body)
    {
        if (body is null)
            return BadRequest(new { error = "Request body is required" });
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
            return BadRequest(new { error = "Reason is required (min 5 chars)" });

        var req = await db.QuotationRequests.FindAsync(id);
        if (req is null) return NotFound();

        if (!Infrastructure.Authorization.AdminOverrideAuthorization.CanRollback(req.Status, body.TargetStatus))
            return BadRequest(new { error = $"Cannot rollback {req.Status} → {body.TargetStatus}" });

        var before = new { req.Id, req.Status };
        req.Status = body.TargetStatus;
        var after = new { req.Id, req.Status };

        audit.Log(CurrentUserId, AdminActionType.RollbackStatus, "Requisition", id, body.Reason, before, after);
        await db.SaveChangesAsync();

        var recipientIds = await db.Users
            .Where(u => u.Id == req.SalesPersonId
                || (u.BranchId == req.BranchId && (u.Role == UserRole.BomCreator || u.Role == UserRole.Accountant))
                || u.Role == UserRole.ManagingDirector)
            .Select(u => u.Id).ToListAsync();

        foreach (var uid in recipientIds)
        {
            await notify.SendAsync(uid,
                $"Requisition {req.RefNo} status rolled back to {req.Status} by Admin",
                referenceId: id,
                referenceType: nameof(NotificationType.StatusRolledBack));
        }

        return Ok(new { req.Id, req.Status });
    }

    [HttpPost("requisitions/{id}/reassign-sp")]
    public async Task<IActionResult> ReassignSp(int id, [FromBody] ReassignSpRequest? body)
    {
        if (body is null)
            return BadRequest(new { error = "Request body is required" });
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
            return BadRequest(new { error = "Reason is required (min 5 chars)" });

        var req = await db.QuotationRequests.FindAsync(id);
        if (req is null) return NotFound();

        var newSp = await db.Users.FindAsync(body.NewSalesPersonId);
        if (newSp is null || newSp.Role != UserRole.SalesPerson || !newSp.IsActive)
            return BadRequest(new { error = "Target user must be an active SalesPerson" });

        var oldSpId = req.SalesPersonId;
        var before = new { req.Id, OldSalesPersonId = oldSpId };
        req.SalesPersonId = newSp.Id;
        var after = new { req.Id, NewSalesPersonId = newSp.Id };

        audit.Log(CurrentUserId, AdminActionType.ReassignSp, "Requisition", id, body.Reason, before, after);
        await db.SaveChangesAsync();

        var recipientIds = await db.Users
            .Where(u => u.Id == oldSpId || u.Id == newSp.Id || u.Role == UserRole.ManagingDirector)
            .Select(u => u.Id).ToListAsync();

        foreach (var uid in recipientIds)
        {
            await notify.SendAsync(uid,
                $"Requisition {req.RefNo} reassigned by Admin",
                referenceId: id,
                referenceType: nameof(NotificationType.SalesPersonReassigned));
        }

        return Ok(new { req.Id, req.SalesPersonId });
    }

    [HttpPost("requisitions/{id}/unlock-bom")]
    public async Task<IActionResult> UnlockBom(int id, [FromBody] UnlockBomRequest? body)
    {
        if (body is null)
            return BadRequest(new { error = "Request body is required" });
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
            return BadRequest(new { error = "Reason is required (min 5 chars)" });

        var req = await db.QuotationRequests.FindAsync(id);
        if (req is null) return NotFound();

        if (!Infrastructure.Authorization.AdminOverrideAuthorization.CanUnlockBom(req.Status))
            return BadRequest(new { error = $"Cannot unlock BOM from status {req.Status}" });

        var before = new { req.Id, req.Status };
        req.Status = RequisitionStatus.BomInProgress;
        var after = new { req.Id, req.Status };

        audit.Log(CurrentUserId, AdminActionType.UnlockBom, "Requisition", id, body.Reason, before, after);
        await db.SaveChangesAsync();

        var recipientIds = await db.Users
            .Where(u => u.BranchId == req.BranchId && (u.Role == UserRole.BomCreator || u.Role == UserRole.Accountant))
            .Select(u => u.Id).ToListAsync();

        foreach (var uid in recipientIds)
        {
            await notify.SendAsync(uid,
                $"BOM for requisition {req.RefNo} has been unlocked by Admin",
                referenceId: id,
                referenceType: nameof(NotificationType.BomUnlocked));
        }

        return Ok(new { req.Id, req.Status });
    }

    [HttpPost("requisitions/{id}/unlock-costing")]
    public async Task<IActionResult> UnlockCosting(int id, [FromBody] UnlockCostingRequest? body)
    {
        if (body is null)
            return BadRequest(new { error = "Request body is required" });
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
            return BadRequest(new { error = "Reason is required (min 5 chars)" });

        var req = await db.QuotationRequests.FindAsync(id);
        if (req is null) return NotFound();

        if (!Infrastructure.Authorization.AdminOverrideAuthorization.CanUnlockCosting(req.Status))
            return BadRequest(new { error = $"Cannot unlock costing from status {req.Status}" });

        var before = new { req.Id, req.Status };
        req.Status = RequisitionStatus.CostingInProgress;
        var after = new { req.Id, req.Status };

        audit.Log(CurrentUserId, AdminActionType.UnlockCosting, "Requisition", id, body.Reason, before, after);
        await db.SaveChangesAsync();

        var recipientIds = await db.Users
            .Where(u => (u.BranchId == req.BranchId && u.Role == UserRole.Accountant) || u.Role == UserRole.ManagingDirector)
            .Select(u => u.Id).ToListAsync();

        foreach (var uid in recipientIds)
        {
            await notify.SendAsync(uid,
                $"Costing for requisition {req.RefNo} has been unlocked by Admin",
                referenceId: id,
                referenceType: nameof(NotificationType.CostingUnlocked));
        }

        return Ok(new { req.Id, req.Status });
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
}
