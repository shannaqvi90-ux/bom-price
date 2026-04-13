using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Requisitions;

public record CreateRequisitionRequest(int CustomerId, int ItemId, decimal ExpectedQty, string CurrencyCode = "AED");

public record RequisitionListItem(
    int Id, string RefNo, string Status, string ItemDescription,
    string CustomerName, decimal ExpectedQty, string CurrencyCode,
    string BranchName, string SalesPersonName, DateTime CreatedAt);

public record RequisitionDetail(
    int Id, string RefNo, string Status,
    int ItemId, string ItemDescription,
    int CustomerId, string CustomerName, string CustomerEmail, string CustomerPhone, string CustomerAddress,
    decimal ExpectedQty, string CurrencyCode, decimal? ExchangeRateSnapshot,
    int BranchId, string BranchName,
    int SalesPersonId, string SalesPersonName,
    DateTime CreatedAt, DateTime UpdatedAt,
    BomSummary? Bom,
    ApprovalSummary? Approval);

public record BomSummary(int Id, decimal TotalCostPerKg, bool HasCost);
public record ApprovalSummary(decimal SalesPriceAed, decimal? SalesPriceForeign, decimal ProfitMarginPct, bool IsApproved);
