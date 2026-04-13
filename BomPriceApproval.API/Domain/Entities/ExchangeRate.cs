namespace BomPriceApproval.API.Domain.Entities;

public class ExchangeRate
{
    public int Id { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string CurrencyName { get; set; } = string.Empty;
    public decimal RateToAed { get; set; }
    public int SetByUserId { get; set; }
    public DateTime EffectiveDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public User SetBy { get; set; } = null!;
}
