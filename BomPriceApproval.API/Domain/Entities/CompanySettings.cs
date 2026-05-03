namespace BomPriceApproval.API.Domain.Entities;

public class CompanySettings
{
    public int Id { get; set; }                          // always 1 (singleton)
    public string CompanyName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string Trn { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public int QuotationValidityDays { get; set; } = 30;
    public string TermsAndConditions { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
}
