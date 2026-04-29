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
    public int? GroupId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = false;
    public string? SignatureImagePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int FailedLoginAttempts { get; set; } = 0;
    public DateTime? LockedUntil { get; set; }
    public Branch? Branch { get; set; }
    public SalesGroup? Group { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
