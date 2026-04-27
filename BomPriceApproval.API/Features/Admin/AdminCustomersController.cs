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
public class AdminCustomersController(AppDbContext db, AdminAuditLogger audit, NotificationService notify) : ControllerBase
{
    private static readonly RequisitionStatus[] ActiveWorkflowStatuses =
    [
        RequisitionStatus.BomPending,
        RequisitionStatus.BomInProgress,
        RequisitionStatus.CostingPending,
        RequisitionStatus.CostingInProgress,
        RequisitionStatus.MdReview,
    ];

    [HttpDelete("customers/{id}")]
    public async Task<IActionResult> HardDeleteCustomer(int id, [FromBody] HardDeleteCustomerRequest? body)
    {
        if (body is null)
            return BadRequest(new { error = "Request body is required" });
        if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
            return BadRequest(new { error = "Reason is required (min 5 chars)" });

        var customer = await db.Customers.FindAsync(id);
        if (customer is null) return NotFound();
        if (customer.IsDeleted)
            return Conflict(new { error = "Customer is already deleted" });

        // D13: block on active-workflow reqs only; Approved / Rejected don't block
        var blockingReqs = await db.QuotationRequests
            .Where(r => r.CustomerId == id && ActiveWorkflowStatuses.Contains(r.Status))
            .Select(r => r.Id)
            .ToListAsync();
        if (blockingReqs.Count > 0)
        {
            return Conflict(new HardDeleteCustomerBlockedResponse(
                "Customer has in-flight requisitions. Resolve them first.",
                blockingReqs));
        }

        // D15: audit BeforeJson includes the full Customer row + array of all referencing req-IDs
        var allReferencingReqIds = await db.QuotationRequests
            .Where(r => r.CustomerId == id)
            .Select(r => r.Id)
            .ToListAsync();

        var before = new
        {
            customer.Id,
            customer.Code,
            customer.Name,
            customer.Email,
            customer.PhoneNumber,
            customer.Address,
            customer.SalesPersonId,
            customer.CreatedByUserId,
            customer.CreatedAt,
            ReferencedRequisitionIds = allReferencingReqIds,
        };

        var oldSalesPersonId = customer.SalesPersonId;

        // D11: anonymize-in-place + IsDeleted flag
        // R4: Code unique index → use [deleted-{id}] not empty string
        var deletionStamp = DateTime.UtcNow.ToString("yyyy-MM-dd");
        customer.IsDeleted = true;
        customer.DeletedAt = DateTime.UtcNow;
        customer.DeletedByUserId = CurrentUserId;
        customer.Name = $"[Deleted {deletionStamp}]";
        customer.Code = $"[deleted-{customer.Id}]";
        customer.Email = "";
        customer.PhoneNumber = "";
        customer.Address = "";
        customer.SalesPersonId = null;

        var after = new
        {
            customer.Id,
            customer.Code,
            customer.Name,
            customer.Email,
            customer.PhoneNumber,
            customer.Address,
            customer.SalesPersonId,
            customer.IsDeleted,
            customer.DeletedAt,
            customer.DeletedByUserId,
        };

        audit.Log(
            CurrentUserId,
            AdminActionType.HardDeleteCustomer,
            "Customer",
            id,
            body.Reason,
            before,
            after);

        await db.SaveChangesAsync();

        // D14: notif to (a) old SP, (b) SP's V23b group peers, (c) Accountants in branches with reqs for this customer
        var recipients = new HashSet<int>();
        if (oldSalesPersonId is int spId)
        {
            recipients.Add(spId);
            var spGroupId = await db.Users
                .Where(u => u.Id == spId)
                .Select(u => u.GroupId)
                .FirstOrDefaultAsync();
            if (spGroupId is int gid)
            {
                var groupPeerIds = await db.Users
                    .Where(u => u.Role == UserRole.SalesPerson
                                && u.GroupId == gid
                                && u.IsActive)
                    .Select(u => u.Id)
                    .ToListAsync();
                foreach (var peerId in groupPeerIds) recipients.Add(peerId);
            }
        }

        var customerReqBranches = await db.QuotationRequests
            .Where(r => r.CustomerId == id)
            .Select(r => r.BranchId)
            .Distinct()
            .ToListAsync();

        if (customerReqBranches.Count > 0)
        {
            var accountantIds = await db.Users
                .Where(u => u.Role == UserRole.Accountant && u.IsActive)
                .Where(u => db.UserBranches.Any(ub =>
                    ub.UserId == u.Id && customerReqBranches.Contains(ub.BranchId)))
                .Select(u => u.Id)
                .ToListAsync();
            foreach (var aid in accountantIds) recipients.Add(aid);
        }

        recipients.Remove(CurrentUserId); // don't notify self (admin)

        if (recipients.Count > 0)
        {
            await notify.SendToUsersAsync(
                recipients,
                $"Customer #{id} was deleted by Admin",
                referenceId: id,
                referenceType: nameof(NotificationType.CustomerDeleted));
        }

        return NoContent();
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
}
