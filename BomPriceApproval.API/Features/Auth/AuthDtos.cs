namespace BomPriceApproval.API.Features.Auth;

public record LoginRequest(string Email, string Password);
public record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
public record RefreshRequest(string RefreshToken);
