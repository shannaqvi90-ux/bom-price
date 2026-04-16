namespace BomPriceApproval.API.Domain.Entities;

public class RequisitionItem
{
    public int Id { get; set; }
    public int QuotationRequestId { get; set; }
    public int ItemId { get; set; }
    public decimal ExpectedQty { get; set; }
    public int SortOrder { get; set; }
    public QuotationRequest QuotationRequest { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public BomHeader? BomHeader { get; set; }
    public ApprovalItem? ApprovalItem { get; set; }
}
