namespace BomPriceApproval.API.Features.Processes;

public record CreateProcessRequest(string Name, int DisplayOrder);
public record UpdateProcessRequest(string Name, int DisplayOrder, bool IsActive);
public record ProcessResponse(int Id, string Name, int DisplayOrder, bool IsActive);
