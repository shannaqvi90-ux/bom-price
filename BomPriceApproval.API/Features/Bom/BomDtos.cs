using System.ComponentModel.DataAnnotations;

namespace BomPriceApproval.API.Features.Bom;

public record BomLineInput(int ProcessId, int RawMaterialItemId, decimal QtyPerKg, decimal WastagePct);
public record SubmitBomRequest([Required] List<BomLineInput> Lines);
public record SaveBomLinesRequest([Required] List<BomLineInput> Lines);

public record BomLineResponse(int Id, int ProcessId, string ProcessName, int RawMaterialItemId,
    string RawMaterialDescription, decimal QtyPerKg, decimal WastagePct,
    decimal? CostPerKg, string? CurrencyCode, decimal? CostPerKgInAed, decimal? ContributionAed);

public record BomDetailResponse(int Id, int QuotationRequestId, string RefNo,
    string ItemDescription, List<BomLineResponse> Lines, decimal TotalCostPerKg, DateTime? SubmittedAt);
