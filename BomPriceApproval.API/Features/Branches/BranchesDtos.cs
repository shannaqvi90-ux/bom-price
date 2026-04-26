namespace BomPriceApproval.API.Features.Branches;

public record CreateBranchRequest(string Name);
public record UpdateBranchRequest(string Name, bool IsActive);
public record BranchAdminResponse(int Id, string Name, bool IsActive);
