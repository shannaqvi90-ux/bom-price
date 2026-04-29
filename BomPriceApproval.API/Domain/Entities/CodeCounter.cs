namespace BomPriceApproval.API.Domain.Entities;

/// <summary>
/// Atomic counter for sequence-based code generation.
/// Sequences: "CUST" (customers), "FG" (finished goods items), "RM" (raw material items).
/// Updated via row-level lock in CodeGeneratorService.
/// </summary>
public class CodeCounter
{
    public string Sequence { get; set; } = string.Empty;  // PK
    public int NextValue { get; set; }
}
