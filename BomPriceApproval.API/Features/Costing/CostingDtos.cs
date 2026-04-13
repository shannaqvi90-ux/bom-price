using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Costing;

public record RawMaterialCostInput(int BomLineId, decimal CostPerKg);

public record SubmitCostingRequest(
    List<RawMaterialCostInput> RawMaterialCosts,
    LandedCostType LandedCostType,
    decimal LandedCostValue,
    decimal FohAmount);

public record CostingDetailResponse(
    int Id, decimal RawMaterialCostTotal, string LandedCostType,
    decimal LandedCostValue, decimal FohAmount, decimal TotalCostPerKg,
    DateTime SubmittedAt);
