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

    // V3 — MD margin/KG entered in quote currency (D6).
    // Source of truth for V3 approvals; final display price is computed at
    // PDF generation as MarginPerKg + total cost. Nullable because legacy
    // V2.3 rows use SalesPricePerKgAed instead.
    public decimal? MarginPerKg { get; set; }

    public QuotationApproval QuotationApproval { get; set; } = null!;
    public RequisitionItem RequisitionItem { get; set; } = null!;
}
