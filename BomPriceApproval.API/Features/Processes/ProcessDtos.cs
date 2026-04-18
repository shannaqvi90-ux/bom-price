using System.ComponentModel.DataAnnotations;

namespace BomPriceApproval.API.Features.Processes;

public record CreateProcessRequest(
    [Required, MaxLength(200)] string Name,
    int DisplayOrder);

public record UpdateProcessRequest(
    [Required, MaxLength(200)] string Name,
    int DisplayOrder,
    bool IsActive);

public record ProcessResponse(int Id, string Name, int DisplayOrder, bool IsActive);
