namespace BomPriceApproval.API.Features.Approvals;

public record ApproveRequest(decimal SalesPricePerKgAed, string? Notes);
public record RejectRequest(string Notes);

public record ApprovalDetailResponse(
    int Id, decimal SalesPricePerKgAed, decimal? SalesPricePerKgForeign,
    decimal ProfitMarginPct, decimal MaterialCostPct, decimal OtherCostPct,
    bool IsApproved, string? Notes, DateTime ApprovedAt);

public record MdReviewDetail(
    string RefNo, string ItemDescription, string CustomerName,
    decimal ExpectedQty, string CurrencyCode, decimal? ExchangeRate,
    decimal RawMaterialCostPerKg, decimal LandedCostPerKg, decimal FohPerKg,
    decimal TotalCostPerKg, decimal MaterialCostPct, decimal LandedCostPct, decimal FohPct);
