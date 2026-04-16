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
    public DbSet<BomHeader> BomHeaders => Set<BomHeader>();
    public DbSet<BomLine> BomLines => Set<BomLine>();
    public DbSet<BomCost> BomCosts => Set<BomCost>();
    public DbSet<QuotationApproval> QuotationApprovals => Set<QuotationApproval>();
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

        mb.Entity<QuotationApproval>()
            .HasOne(a => a.QuotationRequest)
            .WithOne(q => q.Approval)
            .HasForeignKey<QuotationApproval>(a => a.QuotationRequestId);

        mb.Entity<QuotationApproval>()
            .HasOne(a => a.ApprovedBy)
            .WithMany()
            .HasForeignKey(a => a.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<BomLine>()
            .HasOne(l => l.RawMaterial)
            .WithMany()
            .HasForeignKey(l => l.RawMaterialItemId);

        // Decimal precision
        mb.Entity<BomLine>().Property(b => b.QtyPerKg).HasPrecision(18, 6);
        mb.Entity<BomLine>().Property(b => b.WastagePct).HasPrecision(18, 4);
        mb.Entity<BomCost>().Property(b => b.RawMaterialCostTotal).HasPrecision(18, 4);
        mb.Entity<BomCost>().Property(b => b.LandedCostValue).HasPrecision(18, 4);
        mb.Entity<BomCost>().Property(b => b.FohAmount).HasPrecision(18, 4);
        mb.Entity<BomCost>().Property(b => b.TotalCostPerKg).HasPrecision(18, 4);
        mb.Entity<QuotationApproval>().Property(a => a.SalesPricePerKgAed).HasPrecision(18, 4);
        mb.Entity<QuotationApproval>().Property(a => a.SalesPricePerKgForeign).HasPrecision(18, 4);
        mb.Entity<QuotationApproval>().Property(a => a.ProfitMarginPct).HasPrecision(18, 4);
        mb.Entity<QuotationApproval>().Property(a => a.MaterialCostPct).HasPrecision(18, 4);
        mb.Entity<QuotationApproval>().Property(a => a.OtherCostPct).HasPrecision(18, 4);
        mb.Entity<ExchangeRate>().Property(e => e.RateToAed).HasPrecision(18, 6);
        mb.Entity<QuotationRequest>().Property(q => q.ExpectedQty).HasPrecision(18, 4);
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

        // BomCostLine — many per BomHeader and per BomLine (permanent history)
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
