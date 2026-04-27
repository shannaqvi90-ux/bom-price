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
