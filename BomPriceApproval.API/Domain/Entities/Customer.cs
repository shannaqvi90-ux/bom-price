namespace BomPriceApproval.API.Domain.Entities;

public class Customer
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public int? SalesPersonId { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // V23c P2 — soft-delete (Admin C8 anonymize-in-place)
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }
    public User? DeletedBy { get; set; }

    public User? SalesPerson { get; set; }
    public User CreatedBy { get; set; } = null!;
}
