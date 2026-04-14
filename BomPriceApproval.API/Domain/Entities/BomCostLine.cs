namespace BomPriceApproval.API.Domain.Entities;

public class BomCostLine
{
    public int Id { get; set; }
    public int BomHeaderId { get; set; }
    public int BomLineId { get; set; }
    public decimal CostPerKg { get; set; }
    public string CurrencyCode { get; set; } = "AED";
    public decimal CostPerKgInQuoteCurrency { get; set; }
    public decimal CostPerKgInAed { get; set; }
    public BomHeader BomHeader { get; set; } = null!;
    public BomLine BomLine { get; set; } = null!;
}
