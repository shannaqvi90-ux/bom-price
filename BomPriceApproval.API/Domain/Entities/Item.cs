using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Domain.Entities;

public class Item
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ItemType Type { get; set; }
    public int BranchId { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal? LastPurchasePrice { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Branch Branch { get; set; } = null!;
}
