namespace BomPriceApproval.API.Domain.Entities;

public class BranchChangeHistory
{
    public int Id { get; set; }
    public int RequisitionId { get; set; }
    public QuotationRequest Requisition { get; set; } = null!;
    public int OldBranchId { get; set; }
    public Branch OldBranch { get; set; } = null!;
    public int NewBranchId { get; set; }
    public Branch NewBranch { get; set; } = null!;
    public int ChangedByUserId { get; set; }
    public User ChangedBy { get; set; } = null!;
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }
}
