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

    // V3 cost components (per-KG breakdown for quotation PDF)
    public decimal? PrintingCostPerKg { get; set; }      // null when FG not printed
    public string? PrintingCostCurrency { get; set; }    // ISO-3 (AED, USD, EUR), null when no printing
    public decimal FohPerKg { get; set; }                // factory overhead per KG (always AED for V3)
    public decimal TransportPerKg { get; set; }          // always AED
    public decimal CommissionPerKg { get; set; }         // always AED

    public BomHeader BomHeader { get; set; } = null!;
    public User SubmittedBy { get; set; } = null!;
}
