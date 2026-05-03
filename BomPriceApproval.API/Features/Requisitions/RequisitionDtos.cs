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
    int BranchId, string BranchName,
    int SalesPersonId, string SalesPersonName, DateTime CreatedAt);

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

// V3 — sales submits requisition + BOM in one payload (combined screen)
public record CreateRequisitionV3Request(
    int CustomerId,
    string QuotationCurrency,
    string? ReferenceNumber,
    string? Notes,
    List<FinishedGoodLine> FinishedGoods);

public record FinishedGoodLine(
    int ItemId,
    decimal ExpectedQtyKg,
    bool Printing,
    List<BomLineDto> BomLines);

public record BomLineDto(
    int ProcessId,
    int ItemId,
    decimal QtyPerKg,
    string? Micron);

// V3 — sales/admin cancellation with mandatory reason
public record CancelRequisitionRequest(string Reason);

// V3 — GET /api/requisitions/{id} response shape (matches bom-web V3Requisition TS type).
// Old RequisitionDetail record kept for backward-compat in non-GET paths
// (Phase C cutover will prune once all consumers migrate).
public record V3RequisitionDetail(
    int Id,
    string RefNo,
    string Status,
    string CurrencyCode,
    string? Notes,
    V3CustomerSummary Customer,
    V3SalesPersonSummary SalesPerson,
    List<V3FinishedGoodDto> FinishedGoods,
    // V3 cancellation context — populated for Status=Cancelled (V3 cutover or admin C1).
    // Without these, frontend renders "Cancelled" with no explanation.
    string? CancelReason,
    DateTime? CancelledAt,
    int? CancelledByUserId,
    // V3 D-3 — final pricing summary, populated only when Status is MdFinalSign or Signed.
    // Null in earlier statuses; consumed by the MD mobile pricing screen.
    V3FinalPrice? FinalPrice,
    // Set when the most recent prior approval was superseded (e.g. customer
    // rejected → status flipped back to MdPricing). Lets the MD see what
    // they previously priced vs what the customer pushed back on. Null when
    // no prior approval has been superseded.
    V3PreviousMargin? PreviousMargin);

public record V3PreviousMargin(
    DateTime SupersededAt,
    List<V3PreviousMarginItem> Items);

public record V3PreviousMarginItem(int RequisitionItemId, decimal MarginPerKg);

public record V3CustomerSummary(int Id, string Name, string Code);

public record V3SalesPersonSummary(int Id, string Name);

public record V3ItemSummary(int Id, string Code, string Description);

public record V3FinishedGoodDto(
    int Id,
    decimal ExpectedQty,
    bool HasPrinting,
    V3ItemSummary Item,
    List<V3BomLineDto>? BomLines,
    V3BomCostDto? Costs);

public record V3BomLineDto(
    int Id,
    decimal QtyPerKg,
    string? Micron,
    V3ItemSummary Item,
    int? LastModifiedByUserId,
    DateTime? LastModifiedAt);

public record V3BomCostDto(
    decimal TotalCostPerKg,
    decimal? PrintingCostPerKg,
    string? PrintingCostCurrency,
    decimal FohPerKg,
    decimal TransportPerKg,
    decimal CommissionPerKg,
    List<V3BomCostLineDto> Lines);

public record V3BomCostLineDto(
    int BomLineId,
    decimal WastagePercent,
    decimal? PurchaseValuePerKg,
    string? PurchaseCurrency);
