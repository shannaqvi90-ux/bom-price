using System.ComponentModel.DataAnnotations;
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Costing;

// Note: decimal ranges (CostPerKg, LandedCostValue, FohAmount) are enforced
// by CostingController with explicit error messages for UI field-key consistency.
public record RawMaterialCostInput(
    int BomLineId,
    decimal CostPerKg,
    [Required, RegularExpression("^[A-Z]{3}$", ErrorMessage = "Currency code must be 3 uppercase letters.")] string CurrencyCode);

public record SubmitCostingRequest(
    [Required] List<RawMaterialCostInput> RawMaterialCosts,
    LandedCostType LandedCostType,
    decimal LandedCostValue,
    decimal FohAmount);

public record CostingDraftLineInput(
    int BomLineId,
    decimal CostPerKg,
    [Required, RegularExpression("^[A-Z]{3}$", ErrorMessage = "Currency code must be 3 uppercase letters.")] string CurrencyCode);

public record SaveCostingDraftRequest(
    [Required] List<CostingDraftLineInput> Lines,
    LandedCostType LandedCostType,
    decimal LandedCostValue,
    decimal FohAmount);

public record LastCostInfo(decimal CostPerKg, string CurrencyCode, DateTime UpdatedAt);

public record CostingBomLineResponse(
    int BomLineId, int ProcessId, string ProcessName,
    int RawMaterialItemId, string RawMaterialDescription,
    decimal QtyPerKg, decimal WastagePct, LastCostInfo? LastCost);

public record CostingDraftResponse(
    List<CostingDraftLineInput> Lines,
    LandedCostType LandedCostType,
    decimal LandedCostValue,
    decimal FohAmount);

public record CostingSummary(
    int Id, decimal RawMaterialCostTotal, string LandedCostType,
    decimal LandedCostValue, decimal FohAmount, decimal TotalCostPerKg, DateTime? SubmittedAt);

public record CostingItemResponse(
    int RequisitionItemId, int ItemId, string ItemDescription, decimal ExpectedQty,
    int? BomHeaderId, string CostStatus,
    CostingSummary? Cost,
    List<CostingBomLineResponse> BomLines,
    CostingDraftResponse? Draft);

public record CostingReviewResponse(int RequisitionId, List<CostingItemResponse> Items);

// V3 — editable BOM via PUT /api/costing/{requisitionId}/bom
// New-line creation is rejected with 400 in Phase A — V3 cost-entity reshape
// (ProcessId nullability + new BomCost/BomCostLine fields) lands in a later phase.
public record BomLineUpdate(
    int? Id,
    int ItemId,
    decimal QtyPerKg,
    string? Micron,
    bool Delete = false);

public record UpdateBomRequest(int FinishedGoodId, List<BomLineUpdate> Lines);

