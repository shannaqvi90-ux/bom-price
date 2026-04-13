namespace BomPriceApproval.API.Domain.Entities;

public class QuotationApproval
{
    public int Id { get; set; }
    public int QuotationRequestId { get; set; }
    public decimal SalesPricePerKgAed { get; set; }
    public decimal? SalesPricePerKgForeign { get; set; }
    public decimal ProfitMarginPct { get; set; }
    public decimal MaterialCostPct { get; set; }
    public decimal OtherCostPct { get; set; }
    public int ApprovedByUserId { get; set; }
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public bool IsApproved { get; set; }
    public QuotationRequest QuotationRequest { get; set; } = null!;
    public User ApprovedBy { get; set; } = null!;
}
