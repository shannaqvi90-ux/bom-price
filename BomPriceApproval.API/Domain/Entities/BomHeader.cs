namespace BomPriceApproval.API.Domain.Entities;

public class BomHeader
{
    public int Id { get; set; }
    public int RequisitionItemId { get; set; }
    public int CreatedByUserId { get; set; }
    public decimal TotalCostPerKg { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public RequisitionItem RequisitionItem { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
    public ICollection<BomLine> Lines { get; set; } = [];
    public BomCost? Cost { get; set; }
}
