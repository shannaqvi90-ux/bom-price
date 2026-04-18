namespace BomPriceApproval.API.Domain.Entities;

public class QuotationApproval
{
    public int Id { get; set; }
    public int QuotationRequestId { get; set; }
    public int ApprovedByUserId { get; set; }
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public bool IsApproved { get; set; }
    public bool IsSuperseded { get; set; }
    public DateTime? SupersededAt { get; set; }
    public QuotationRequest QuotationRequest { get; set; } = null!;
    public User ApprovedBy { get; set; } = null!;
    public ICollection<ApprovalItem> Items { get; set; } = [];
}
