using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Items;

public record CreateItemRequest(string Code, string Description, ItemType Type, decimal? LastPurchasePrice);
public record UpdateItemRequest(string Code, string Description, ItemType Type, decimal? LastPurchasePrice);
public record UpdateItemStatusRequest(bool IsActive);
public record ItemResponse(int Id, string Code, string Description, string Type, int BranchId, bool IsActive, decimal? LastPurchasePrice);
public record SimilarItemResult(int Id, string Code, string Description);

public record LedgerHeadersResponse(List<string> Headers);
public record LedgerImportRequest(string ItemCodeColumn, string DateColumn, string UnitPriceColumn, int BranchId);
public record LedgerImportResult(int Updated, int Skipped, List<string> UnmatchedCodes);
