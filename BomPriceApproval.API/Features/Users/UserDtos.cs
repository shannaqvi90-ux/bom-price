using System.ComponentModel.DataAnnotations;
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Users;

public record CreateUserRequest(
    [Required, MaxLength(200)] string Name,
    [Required, EmailAddress, MaxLength(255)] string Email,
    [Required, MaxLength(200)] string Password,
    [Required] UserRole Role,
    int? BranchId);

public record UpdateUserRequest(
    [Required, MaxLength(200)] string Name,
    [Required, EmailAddress, MaxLength(255)] string Email,
    [Required] UserRole Role,
    int? BranchId,
    bool IsActive);

public record UserResponse(int Id, string Name, string Email, string Role, int? BranchId, string? BranchName, bool IsActive);

public record SetUserBranchesRequest(IList<int> BranchIds);
