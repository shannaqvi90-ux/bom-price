using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Domain.Entities;

public class QuotationRequest
{
    public int Id { get; set; }
    public string RefNo { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public int SalesPersonId { get; set; }
    public int CustomerId { get; set; }
    public string CurrencyCode { get; set; } = "AED";
    public decimal? ExchangeRateSnapshot { get; set; }
    public RequisitionStatus Status { get; set; } = RequisitionStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Branch Branch { get; set; } = null!;
    public User SalesPerson { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public ICollection<RequisitionItem> Items { get; set; } = [];
    public ICollection<QuotationApproval> Approvals { get; set; } = [];
}
