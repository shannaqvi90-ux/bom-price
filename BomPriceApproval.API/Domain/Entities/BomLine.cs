namespace BomPriceApproval.API.Domain.Entities;

public class BomLine
{
    public int Id { get; set; }
    public int BomHeaderId { get; set; }
    public int ProcessId { get; set; }
    public int RawMaterialItemId { get; set; }
    public decimal QtyPerKg { get; set; }
    public decimal WastagePct { get; set; }
    public BomHeader BomHeader { get; set; } = null!;
    public Process Process { get; set; } = null!;
    public Item RawMaterial { get; set; } = null!;

    // V3 — track accountant edits to sales' BOM (D24 diff visible to sales + MD)
    public int? LastModifiedByUserId { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public User? LastModifiedBy { get; set; }
}
