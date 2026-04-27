namespace BomPriceApproval.API.Features.Admin;

public record OverridePricesRequest(
    string Reason,
    IReadOnlyList<OverridePricesItem> Items);

public record OverridePricesItem(
    int RequisitionItemId,
    decimal SalesPricePerKgAed,
    decimal? SalesPricePerKgForeign,
    decimal ProfitMarginPct,
    decimal MaterialCostPct,
    decimal OtherCostPct);

public record OverridePricesResponse(
    int NewApprovalId,
    int SupersededApprovalId,
    int? EmailSentToSpUserId);

public record CurrentApprovalItemDto(
    int RequisitionItemId,
    string ItemDescription,
    decimal ExpectedQty,
    decimal SalesPricePerKgAed,
    decimal? SalesPricePerKgForeign,
    decimal ProfitMarginPct,
    decimal MaterialCostPct,
    decimal OtherCostPct);

public record CurrentApprovalResponse(
    int Id,
    int QuotationRequestId,
    string RefNo,
    string CurrencyCode,
    decimal? RateSnapshot,
    DateTime ApprovedAt,
    int ApprovedByUserId,
    string? Notes,
    IReadOnlyList<CurrentApprovalItemDto> Items);
