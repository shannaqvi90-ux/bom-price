using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public int? BranchId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Branch? Branch { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
