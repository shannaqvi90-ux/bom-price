using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Items;

public record CreateItemRequest(string Code, string Description, ItemType Type);
public record ItemResponse(int Id, string Code, string Description, string Type, int BranchId, bool IsActive);
public record SimilarItemResult(int Id, string Code, string Description);
