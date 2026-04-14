using System.ComponentModel.DataAnnotations;
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Costing;

public record RawMaterialCostInput(int BomLineId, decimal CostPerKg, string CurrencyCode);

public record SubmitCostingRequest(
    [Required] List<RawMaterialCostInput> RawMaterialCosts,
    LandedCostType LandedCostType,
    decimal LandedCostValue,
    decimal FohAmount);

public record CostingDraftLineInput(int BomLineId, decimal CostPerKg, string CurrencyCode);

public record SaveCostingDraftRequest(
    [Required] List<CostingDraftLineInput> Lines,
    LandedCostType LandedCostType,
    decimal LandedCostValue,
    decimal FohAmount);

public record LastCostInfo(decimal CostPerKg, string CurrencyCode, DateTime UpdatedAt);

public record CostingBomLineResponse(
    int BomLineId,
    int ProcessId,
    string ProcessName,
    int RawMaterialItemId,
    string RawMaterialDescription,
    decimal QtyPerKg,
    decimal WastagePct,
    LastCostInfo? LastCost);

public record CostingDraftResponse(
    List<CostingDraftLineInput> Lines,
    LandedCostType LandedCostType,
    decimal LandedCostValue,
    decimal FohAmount);

public record CostingDetailResponse(
    int Id,
    decimal RawMaterialCostTotal,
    string LandedCostType,
    decimal LandedCostValue,
    decimal FohAmount,
    decimal TotalCostPerKg,
    DateTime? SubmittedAt,
    List<CostingBomLineResponse> BomLines,
    CostingDraftResponse? Draft);
