using System.ComponentModel.DataAnnotations;

namespace BomPriceApproval.API.Features.Requisitions;

// Note: ExpectedQty range is enforced by RequisitionsController with explicit
// "must be greater than 0" error messages for consistency with UI field-key format.
public record RequisitionItemInput(int ItemId, decimal ExpectedQty);

public record CreateRequisitionRequest(
    int? BranchId,
    int CustomerId,
    [Required, MinLength(1)] List<RequisitionItemInput> Items,
    [RegularExpression("^[A-Z]{3}$", ErrorMessage = "Currency code must be 3 uppercase letters.")] string CurrencyCode = "AED");

public record AddRequisitionItemRequest(int ItemId, decimal ExpectedQty);

public record RequisitionItemDto(int Id, int ItemId, string ItemDescription, decimal ExpectedQty, int SortOrder);

public record RequisitionListItem(
    int Id, string RefNo, string Status, int ItemCount,
    string CustomerName, string CurrencyCode,
    string BranchName, string SalesPersonName, DateTime CreatedAt);

public record RequisitionDetail(
    int Id, string RefNo, string Status,
    int CustomerId, string CustomerName, string CustomerEmail, string CustomerPhone, string CustomerAddress,
    string CurrencyCode, decimal? ExchangeRateSnapshot,
    int BranchId, string BranchName,
    int SalesPersonId, string SalesPersonName,
    DateTime CreatedAt, DateTime UpdatedAt,
    List<RequisitionItemDto> Items,
    ApprovalSummary? Approval);

public record ApprovalSummary(
    bool IsApproved,
    string? Notes,
    DateTime ApprovedAt,
    List<ApprovalItemPrice>? Items);

public record ApprovalItemPrice(
    int RequisitionItemId,
    decimal PricePerKg,
    decimal? PricePerKgForeign);

public record ResubmitRequisitionRequest(
    [Required, MinLength(1)] List<RequisitionItemInput> Items);

public record ChangeCustomerRequest(
    [Required] int CustomerId,
    [MaxLength(500)] string? Reason);

public record CustomerChangeHistoryResponse(
    int Id,
    int OldCustomerId,
    string OldCustomerName,
    int NewCustomerId,
    string NewCustomerName,
    int ChangedByUserId,
    string ChangedByUserName,
    DateTime ChangedAt,
    string? Reason);

public record ChangeBranchRequest(int BranchId, string? Reason);

public record BranchChangeHistoryResponse(
    int Id,
    int OldBranchId,
    string OldBranchName,
    int NewBranchId,
    string NewBranchName,
    int ChangedByUserId,
    string ChangedByUserName,
    DateTime ChangedAt,
    string? Reason);
