namespace BomPriceApproval.API.Features.Requisitions;

public record RequisitionItemInput(int ItemId, decimal ExpectedQty);

public record CreateRequisitionRequest(int CustomerId, List<RequisitionItemInput> Items, string CurrencyCode = "AED");

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

public record ApprovalSummary(bool IsApproved, string? Notes, DateTime ApprovedAt);
