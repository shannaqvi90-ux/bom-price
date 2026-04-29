using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Domain.Entities;

public class QuotationApproval
{
    public int Id { get; set; }
    public int QuotationRequestId { get; set; }
    public int ApprovedByUserId { get; set; }
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public bool IsApproved { get; set; }
    public bool IsSuperseded { get; set; }
    public DateTime? SupersededAt { get; set; }

    // V23c P2 — per-approval rate snapshot (D7: re-snap on C6 override).
    // Original approval inherits the value from QuotationRequest.ExchangeRateSnapshot;
    // C6 override creates a new QuotationApproval row with the current rate.
    // Null when CurrencyCode == "AED".
    public decimal? RateSnapshot { get; set; }

    // V3 — stage of this approval (legacy V2.3 rows backfilled to FinalSign)
    public ApprovalStage Stage { get; set; } = ApprovalStage.InitialPricing;

    // V3 — FX rate used to convert foreign-currency RM costs to AED at accountant submit time.
    // Distinct from RateSnapshot (sale-side rate at MD margin entry).
    public decimal? CostFxSnapshot { get; set; }

    public QuotationRequest QuotationRequest { get; set; } = null!;
    public User ApprovedBy { get; set; } = null!;
    public ICollection<ApprovalItem> Items { get; set; } = [];
}
