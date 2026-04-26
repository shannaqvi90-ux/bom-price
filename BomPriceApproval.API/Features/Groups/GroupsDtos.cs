namespace BomPriceApproval.API.Features.Groups;

public record CreateGroupRequest(string Name);
public record UpdateGroupRequest(string Name, bool IsActive);
public record GroupAdminResponse(int Id, string Name, bool IsActive);
