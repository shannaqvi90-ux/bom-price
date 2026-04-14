namespace BomPriceApproval.API.Domain.Entities;

public class ItemLastCost
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public decimal CostPerKg { get; set; }
    public string CurrencyCode { get; set; } = "AED";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int UpdatedByUserId { get; set; }
    public Item Item { get; set; } = null!;
    public User UpdatedBy { get; set; } = null!;
}
