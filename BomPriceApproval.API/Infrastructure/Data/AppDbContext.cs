using BomPriceApproval.API.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Process> Processes => Set<Process>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<QuotationRequest> QuotationRequests => Set<QuotationRequest>();
    public DbSet<RequisitionItem> RequisitionItems => Set<RequisitionItem>();
    public DbSet<BomHeader> BomHeaders => Set<BomHeader>();
    public DbSet<BomLine> BomLines => Set<BomLine>();
    public DbSet<BomCost> BomCosts => Set<BomCost>();
    public DbSet<QuotationApproval> QuotationApprovals => Set<QuotationApproval>();
    public DbSet<ApprovalItem> ApprovalItems => Set<ApprovalItem>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<CostingDraft> CostingDrafts => Set<CostingDraft>();
    public DbSet<BomCostLine> BomCostLines => Set<BomCostLine>();
    public DbSet<ItemLastCost> ItemLastCosts => Set<ItemLastCost>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Branch>().HasData(
            new Branch { Id = 1, Name = "Fujairah" },
            new Branch { Id = 2, Name = "Al Ain" }
        );

        mb.Entity<QuotationRequest>()
            .Property(q => q.RefNo)
            .HasComputedColumnSql("'REQ-' || LPAD(\"Id\"::text, 4, '0')", stored: true);

        mb.Entity<Customer>().HasIndex(c => c.Code).IsUnique();

        mb.Entity<Item>()
            .HasIndex(i => new { i.Code, i.BranchId })
            .IsUnique();
        mb.Entity<Customer>()
            .HasOne(c => c.SalesPerson)
            .WithMany()
            .HasForeignKey(c => c.SalesPersonId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<Customer>()
            .HasOne(c => c.CreatedBy)
            .WithMany()
            .HasForeignKey(c => c.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // RequisitionItem → QuotationRequest (many:1)
        mb.Entity<QuotationRequest>()
            .HasMany(q => q.Items)
            .WithOne(ri => ri.QuotationRequest)
            .HasForeignKey(ri => ri.QuotationRequestId);

        // BomHeader → RequisitionItem (1:1)
        mb.Entity<BomHeader>()
            .HasOne(b => b.RequisitionItem)
            .WithOne(ri => ri.BomHeader)
            .HasForeignKey<BomHeader>(b => b.RequisitionItemId);

        mb.Entity<BomHeader>()
            .HasOne(b => b.CreatedBy)
            .WithMany()
            .HasForeignKey(b => b.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<BomCost>()
            .HasOne(c => c.BomHeader)
            .WithOne(h => h.Cost)
            .HasForeignKey<BomCost>(c => c.BomHeaderId);
        mb.Entity<BomCost>()
            .HasOne(c => c.SubmittedBy)
            .WithMany()
            .HasForeignKey(c => c.SubmittedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<ExchangeRate>()
            .HasOne(e => e.SetBy)
            .WithMany()
            .HasForeignKey(e => e.SetByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // QuotationApproval → QuotationRequest (1:1)
        mb.Entity<QuotationApproval>()
            .HasOne(a => a.QuotationRequest)
            .WithOne(q => q.Approval)
            .HasForeignKey<QuotationApproval>(a => a.QuotationRequestId);

        mb.Entity<QuotationApproval>()
            .HasOne(a => a.ApprovedBy)
            .WithMany()
            .HasForeignKey(a => a.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ApprovalItem → QuotationApproval (many:1)
        mb.Entity<QuotationApproval>()
            .HasMany(a => a.Items)
            .WithOne(ai => ai.QuotationApproval)
            .HasForeignKey(ai => ai.QuotationApprovalId);

        // ApprovalItem → RequisitionItem (1:1)
        mb.Entity<ApprovalItem>()
            .HasOne(a => a.RequisitionItem)
            .WithOne(ri => ri.ApprovalItem)
            .HasForeignKey<ApprovalItem>(a => a.RequisitionItemId);

        mb.Entity<BomLine>()
            .HasOne(l => l.RawMaterial)
            .WithMany()
            .HasForeignKey(l => l.RawMaterialItemId);

        // ─── Validation constraints ──────────────────────────────────────────
        mb.Entity<RequisitionItem>()
            .HasIndex(ri => new { ri.QuotationRequestId, ri.ItemId })
            .IsUnique();

        mb.Entity<RequisitionItem>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_requisition_items_expected_qty_positive",
                "\"ExpectedQty\" > 0"));

        mb.Entity<BomLine>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_bom_lines_qty_per_kg_positive",
                "\"QtyPerKg\" > 0"));

        mb.Entity<ApprovalItem>()
            .ToTable(t => t.HasCheckConstraint(
                "ck_approval_items_sales_price_positive",
                "\"SalesPricePerKgAed\" > 0"));

        // Decimal precision
        mb.Entity<RequisitionItem>().Property(ri => ri.ExpectedQty).HasPrecision(18, 4);
        mb.Entity<BomLine>().Property(b => b.QtyPerKg).HasPrecision(18, 6);
        mb.Entity<BomLine>().Property(b => b.WastagePct).HasPrecision(18, 4);
        mb.Entity<BomCost>().Property(b => b.RawMaterialCostTotal).HasPrecision(18, 4);
        mb.Entity<BomCost>().Property(b => b.LandedCostValue).HasPrecision(18, 4);
        mb.Entity<BomCost>().Property(b => b.FohAmount).HasPrecision(18, 4);
        mb.Entity<ApprovalItem>().Property(a => a.SalesPricePerKgAed).HasPrecision(18, 4);
        mb.Entity<ApprovalItem>().Property(a => a.SalesPricePerKgForeign).HasPrecision(18, 4);
        mb.Entity<ApprovalItem>().Property(a => a.ProfitMarginPct).HasPrecision(18, 4);
        mb.Entity<ApprovalItem>().Property(a => a.MaterialCostPct).HasPrecision(18, 4);
        mb.Entity<ApprovalItem>().Property(a => a.OtherCostPct).HasPrecision(18, 4);
        mb.Entity<ExchangeRate>().Property(e => e.RateToAed).HasPrecision(18, 6);
        mb.Entity<QuotationRequest>().Property(q => q.ExchangeRateSnapshot).HasPrecision(18, 6);
        mb.Entity<BomHeader>().Property(b => b.TotalCostPerKg).HasPrecision(18, 4);
        mb.Entity<Item>().Property(i => i.LastPurchasePrice).HasPrecision(18, 4);

        // CostingDraft — one per BomHeader
        mb.Entity<CostingDraft>()
            .HasOne(d => d.BomHeader)
            .WithOne()
            .HasForeignKey<CostingDraft>(d => d.BomHeaderId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<CostingDraft>().HasIndex(d => d.BomHeaderId).IsUnique();

        // BomCostLine — many per BomHeader and per BomLine
        mb.Entity<BomCostLine>()
            .HasOne(l => l.BomHeader)
            .WithMany()
            .HasForeignKey(l => l.BomHeaderId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<BomCostLine>()
            .HasOne(l => l.BomLine)
            .WithMany()
            .HasForeignKey(l => l.BomLineId)
            .OnDelete(DeleteBehavior.Restrict);

        // ItemLastCost — one per Item
        mb.Entity<ItemLastCost>()
            .HasOne(l => l.Item)
            .WithOne()
            .HasForeignKey<ItemLastCost>(l => l.ItemId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<ItemLastCost>()
            .HasOne(l => l.UpdatedBy)
            .WithMany()
            .HasForeignKey(l => l.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<ItemLastCost>().HasIndex(l => l.ItemId).IsUnique();

        mb.Entity<CostingDraft>().Property(d => d.LandedCostValue).HasPrecision(18, 4);
        mb.Entity<CostingDraft>().Property(d => d.FohAmount).HasPrecision(18, 4);
        mb.Entity<BomCostLine>().Property(l => l.CostPerKg).HasPrecision(18, 4);
        mb.Entity<BomCostLine>().Property(l => l.CostPerKgInQuoteCurrency).HasPrecision(18, 4);
        mb.Entity<BomCostLine>().Property(l => l.CostPerKgInAed).HasPrecision(18, 4);
        mb.Entity<ItemLastCost>().Property(l => l.CostPerKg).HasPrecision(18, 4);
    }
}
