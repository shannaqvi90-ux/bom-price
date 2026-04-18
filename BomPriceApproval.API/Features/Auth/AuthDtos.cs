using System.ComponentModel.DataAnnotations;

namespace BomPriceApproval.API.Features.Auth;

// Note: no [EmailAddress] on LoginRequest.Email — the login flow is intentionally
// timing-uniform across "user not found" / "wrong password" / "malformed email".
// Adding [EmailAddress] here would short-circuit validation and leak timing info.
public record LoginRequest(
    [Required, MaxLength(255)] string Email,
    [Required, MaxLength(200)] string Password);

public record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);

public record RefreshRequest([Required, MaxLength(500)] string RefreshToken);
