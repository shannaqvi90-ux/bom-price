namespace BomPriceApproval.API.Domain.Entities;

public class ApprovalItem
{
    public int Id { get; set; }
    public int QuotationApprovalId { get; set; }
    public int RequisitionItemId { get; set; }
    public decimal SalesPricePerKgAed { get; set; }
    public decimal? SalesPricePerKgForeign { get; set; }
    public decimal ProfitMarginPct { get; set; }
    public decimal MaterialCostPct { get; set; }
    public decimal OtherCostPct { get; set; }
    public QuotationApproval QuotationApproval { get; set; } = null!;
    public RequisitionItem RequisitionItem { get; set; } = null!;
}
