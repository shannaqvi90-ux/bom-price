using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Domain.Entities;

public class CostingDraft
{
    public int Id { get; set; }
    public int BomHeaderId { get; set; }
    public string LinesJson { get; set; } = "[]";
    public LandedCostType LandedCostType { get; set; }
    public decimal LandedCostValue { get; set; }
    public decimal FohAmount { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public BomHeader BomHeader { get; set; } = null!;
}
