using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Domain.Entities;

public class QuotationRequest
{
    public int Id { get; set; }
    public string RefNo { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public int SalesPersonId { get; set; }
    public int CustomerId { get; set; }
    public string CurrencyCode { get; set; } = "AED";
    public decimal? ExchangeRateSnapshot { get; set; }
    // V3 — sales-supplied free-text fields
    public string? Notes { get; set; }
    public string? ReferenceNumber { get; set; }
    public RequisitionStatus Status { get; set; } = RequisitionStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool MdPricingNotifiedAfterEdit { get; set; } = false;
    public Branch Branch { get; set; } = null!;
    public User SalesPerson { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public ICollection<RequisitionItem> Items { get; set; } = [];
    public ICollection<QuotationApproval> Approvals { get; set; } = [];

    // V3 — cancellation tracking (sales/admin cancel + cutover migration)
    public DateTime? CancelledAt { get; set; }
    public int? CancelledByUserId { get; set; }
    public string? CancelReason { get; set; }
    public User? CancelledBy { get; set; }
}
