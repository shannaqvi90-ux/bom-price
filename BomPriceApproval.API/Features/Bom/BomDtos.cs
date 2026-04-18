using System.ComponentModel.DataAnnotations;

namespace BomPriceApproval.API.Features.Bom;

// Note: QtyPerKg and WastagePct bounds are enforced by BomController with
// explicit error messages (WastagePct >= 0, no upper bound per business rule).
public record BomLineInput(int ProcessId, int RawMaterialItemId, decimal QtyPerKg, decimal WastagePct);

public record SubmitBomRequest([Required] List<BomLineInput> Lines);
public record SaveBomLinesRequest([Required] List<BomLineInput> Lines);

public record BomLineResponse(int Id, int ProcessId, string ProcessName, int RawMaterialItemId,
    string RawMaterialDescription, decimal QtyPerKg, decimal WastagePct,
    decimal? CostPerKg, string? CurrencyCode, decimal? CostPerKgInAed, decimal? ContributionAed);

public record BomItemResponse(
    int RequisitionItemId, int ItemId, string ItemDescription, decimal ExpectedQty, int SortOrder,
    int? BomHeaderId, string BomStatus,
    List<BomLineResponse> Lines, decimal TotalCostPerKg, DateTime? SubmittedAt);

public record BomReviewResponse(
    int RequisitionId, string RefNo, string RequisitionStatus,
    List<BomItemResponse> Items);
