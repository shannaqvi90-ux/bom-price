using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Users;

public record CreateUserRequest(string Name, string Email, string Password, UserRole Role, int? BranchId);
public record UpdateUserRequest(string Name, string Email, UserRole Role, int? BranchId, bool IsActive);
public record UserResponse(int Id, string Name, string Email, string Role, int? BranchId, string? BranchName, bool IsActive);
