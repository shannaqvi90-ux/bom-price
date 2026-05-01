namespace BomPriceApproval.API.Features.Requisitions;

public record V3FinalPriceItem(
    int RequisitionItemId,
    int ItemId,
    string Description,
    decimal ExpectedQty,
    decimal CostPerKg,
    decimal MarginPerKg,
    decimal SalePerKg,
    decimal SalePerKgAed,
    decimal TotalAed);

public record V3FinalPrice(
    decimal TotalAed,
    string CurrencyCode,
    decimal? RateSnapshot,
    List<V3FinalPriceItem> PerFg);
