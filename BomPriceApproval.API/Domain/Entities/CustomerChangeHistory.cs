namespace BomPriceApproval.API.Domain.Entities;

public class CustomerChangeHistory
{
    public int Id { get; set; }
    public int RequisitionId { get; set; }
    public int OldCustomerId { get; set; }
    public int NewCustomerId { get; set; }
    public int ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }

    public QuotationRequest Requisition { get; set; } = null!;
    public Customer OldCustomer { get; set; } = null!;
    public Customer NewCustomer { get; set; } = null!;
    public User ChangedBy { get; set; } = null!;
}
