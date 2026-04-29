using System.ComponentModel.DataAnnotations;
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Items;

public record CreateItemRequest(
    [Required, MaxLength(50)] string Code,
    [Required, MaxLength(500)] string Description,
    [Required] ItemType Type,
    [Range(0, 999999999)] decimal? LastPurchasePrice,
    int? BranchId = null);

public record UpdateItemRequest(
    [Required, MaxLength(50)] string Code,
    [Required, MaxLength(500)] string Description,
    [Required] ItemType Type,
    [Range(0, 999999999)] decimal? LastPurchasePrice);

public record UpdateItemStatusRequest(bool IsActive);
public record ItemResponse(int Id, string Code, string Description, string Type, int BranchId, bool IsActive, decimal? LastPurchasePrice);
public record SimilarItemResult(int Id, string Code, string Description);

public record LedgerHeadersResponse(List<string> Headers);

public record LedgerImportRequest(
    [Required, MaxLength(200)] string ItemCodeColumn,
    [Required, MaxLength(200)] string DateColumn,
    [Required, MaxLength(200)] string UnitPriceColumn,
    int BranchId);

public record LedgerImportResult(int Updated, int Skipped, List<string> UnmatchedCodes);
