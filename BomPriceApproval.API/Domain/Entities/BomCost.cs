using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Domain.Entities;

public class BomCost
{
    public int Id { get; set; }
    public int BomHeaderId { get; set; }
    public decimal RawMaterialCostTotal { get; set; }
    public LandedCostType LandedCostType { get; set; }
    public decimal LandedCostValue { get; set; }
    public decimal FohAmount { get; set; }
    public decimal TotalCostPerKg { get; set; }
    public int SubmittedByUserId { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public BomHeader BomHeader { get; set; } = null!;
    public User SubmittedBy { get; set; } = null!;
}
