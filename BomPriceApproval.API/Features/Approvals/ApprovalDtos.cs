namespace BomPriceApproval.API.Features.Approvals;

public record ApproveItemInput(int RequisitionItemId, decimal SalesPricePerKgAed);
public record ApproveRequest(List<ApproveItemInput> Items, string? Notes);
public record RejectRequest(string Notes);

public record MdReviewItemDetail(
    int RequisitionItemId, string ItemDescription, decimal ExpectedQty,
    decimal RawMaterialCostPerKg, decimal LandedCostPerKg, decimal FohPerKg, decimal TotalCostPerKg,
    decimal MaterialCostPct, decimal LandedCostPct, decimal FohPct);

public record MdReviewDetail(
    string RefNo, string CustomerName,
    string CurrencyCode, decimal? ExchangeRate,
    List<MdReviewItemDetail> Items);
