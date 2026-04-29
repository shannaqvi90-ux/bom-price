using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Admin;

public record DeleteRequisitionRequest(string Reason);
public record RollbackStatusRequest(RequisitionStatus TargetStatus, string Reason);
public record ReassignSpRequest(int NewSalesPersonId, string Reason);
public record RollbackToCostingRequest(string Reason);
public record ResetPasswordRequest(string Reason);
public record ResetPasswordResponse(string TempPassword);

public record AuditLogItemDto(
    int Id,
    int AdminUserId,
    string AdminUserName,
    string ActionType,
    string EntityType,
    int EntityId,
    string Reason,
    string BeforeJson,
    string? AfterJson,
    DateTime CreatedAt);

public record AuditLogPagedResponse(
    IReadOnlyList<AuditLogItemDto> Items,
    int Total,
    int Page,
    int PageSize);
