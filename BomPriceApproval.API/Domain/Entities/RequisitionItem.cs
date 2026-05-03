namespace BomPriceApproval.API.Domain.Entities;

public class RequisitionItem
{
    public int Id { get; set; }
    public int QuotationRequestId { get; set; }
    public int ItemId { get; set; }
    public decimal ExpectedQty { get; set; }
    public int SortOrder { get; set; }
    public bool HasPrinting { get; set; }
    public QuotationRequest QuotationRequest { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public DateTime? CostingStartedAt { get; set; }
    public BomHeader? BomHeader { get; set; }
    // V3 re-margin loop: each MD pricing iteration creates a new ApprovalItem
    // row. Was a 1:1 nav (V2.3 — one approval per req); now a collection so
    // SetMargin after a customer rejection doesn't violate the unique index.
    public ICollection<ApprovalItem> ApprovalItems { get; set; } = new List<ApprovalItem>();
}
