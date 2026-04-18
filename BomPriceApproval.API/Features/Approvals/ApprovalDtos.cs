using System.ComponentModel.DataAnnotations;

namespace BomPriceApproval.API.Features.Approvals;

// Note: SalesPricePerKgAed range is enforced by ApprovalsController with
// explicit "must be greater than 0" error messages for UI field-key consistency.
public record ApproveItemInput(int RequisitionItemId, decimal SalesPricePerKgAed);

public record ApproveRequest(
    [Required, MinLength(1)] List<ApproveItemInput> Items,
    [MaxLength(2000)] string? Notes);

public record RejectRequest(
    [Required, MaxLength(2000)] string Notes);

public record MdReviewItemCost(
    decimal RawMaterialCostPerKg, decimal LandedCostPerKg, decimal FohPerKg, decimal TotalCostPerKg,
    decimal MaterialCostPct, decimal LandedCostPct, decimal FohPct);

public record MdReviewItemDetail(
    int RequisitionItemId, string ItemDescription, decimal ExpectedQty,
    string CostStatus, MdReviewItemCost? Cost);

public record MdReviewDetail(
    string RefNo, string CustomerName,
    string CurrencyCode, decimal? ExchangeRate,
    bool ReadyForReview,
    List<MdReviewItemDetail> Items);
