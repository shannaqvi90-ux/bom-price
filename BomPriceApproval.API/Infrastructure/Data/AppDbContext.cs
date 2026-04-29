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
    public DbSet<RevokedJti> RevokedJtis => Set<RevokedJti>();
    public DbSet<CustomerChangeHistory> CustomerChangeHistories => Set<CustomerChangeHistory>();
    public DbSet<UserBranch> UserBranches => Set<UserBranch>();
    public DbSet<BranchChangeHistory> BranchChangeHistories => Set<BranchChangeHistory>();
    public DbSet<CodeCounter> CodeCounters => Set<CodeCounter>();
    public DbSet<SalesGroup> SalesGroups => Set<SalesGroup>();
    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Branch>().HasData(
            new Branch { Id = 1, Name = "Fujairah", IsActive = true },
            new Branch { Id = 2, Name = "Al Ain", IsActive = true }
        );

        mb.Entity<QuotationRequest>()
            .Property(q => q.RefNo)
            .HasComputedColumnSql("'REQ-' || LPAD(\"Id\"::text, 4, '0')", stored: true);

        mb.Entity<User>().HasIndex(u => u.Email).IsUnique();

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
        mb.Entity<Customer>()
            .HasOne(c => c.DeletedBy)
            .WithMany()
            .HasForeignKey(c => c.DeletedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        mb.Entity<Customer>().HasIndex(c => c.IsDeleted);

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

        // QuotationApproval → QuotationRequest (many:1 — superseded rows are kept as history)
        mb.Entity<QuotationApproval>()
            .HasOne(a => a.QuotationRequest)
            .WithMany(q => q.Approvals)
            .HasForeignKey(a => a.QuotationRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        // Fast lookup of the current (non-superseded) approval per requisition
        mb.Entity<QuotationApproval>()
            .HasIndex(a => a.QuotationRequestId)
            .HasFilter("\"IsSuperseded\" = false")
            .HasDatabaseName("ix_quotation_approvals_current");

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
        mb.Entity<QuotationApproval>().Property(a => a.RateSnapshot).HasPrecision(18, 6);
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

        mb.Entity<RevokedJti>().Property(r => r.Jti).HasMaxLength(32);
        mb.Entity<RevokedJti>().HasIndex(r => r.Jti).IsUnique();
        mb.Entity<RevokedJti>().HasIndex(r => r.ExpiresAt);

        mb.Entity<CustomerChangeHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.ChangedAt).HasDefaultValueSql("now() at time zone 'utc'");

            e.HasOne(x => x.Requisition)
                .WithMany()
                .HasForeignKey(x => x.RequisitionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.OldCustomer)
                .WithMany()
                .HasForeignKey(x => x.OldCustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.NewCustomer)
                .WithMany()
                .HasForeignKey(x => x.NewCustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.ChangedBy)
                .WithMany()
                .HasForeignKey(x => x.ChangedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.RequisitionId);
            e.HasIndex(x => x.ChangedAt).IsDescending();
        });

        mb.Entity<UserBranch>(e =>
        {
            e.HasKey(ub => new { ub.UserId, ub.BranchId });
            e.HasOne(ub => ub.User)
                .WithMany()
                .HasForeignKey(ub => ub.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ub => ub.Branch)
                .WithMany()
                .HasForeignKey(ub => ub.BranchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<BranchChangeHistory>(e =>
        {
            e.HasOne(h => h.Requisition)
                .WithMany()
                .HasForeignKey(h => h.RequisitionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(h => h.OldBranch).WithMany().HasForeignKey(h => h.OldBranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(h => h.NewBranch).WithMany().HasForeignKey(h => h.NewBranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(h => h.ChangedBy).WithMany().HasForeignKey(h => h.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.Property(h => h.ChangedAt).HasColumnType("timestamptz");
            e.Property(h => h.Reason).HasMaxLength(500);
            e.Property(h => h.ChangedAt).HasDefaultValueSql("now() at time zone 'utc'");
            e.HasIndex(h => h.RequisitionId);
            e.HasIndex(h => h.ChangedAt).IsDescending();
        });

        mb.Entity<SalesGroup>(e =>
        {
            e.HasKey(g => g.Id);
            e.Property(g => g.Name).HasMaxLength(100).IsRequired();
        });

        mb.Entity<User>()
            .HasOne(u => u.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(u => u.GroupId)
            .OnDelete(DeleteBehavior.Restrict);  // group with members can't be deleted

        mb.Entity<User>().HasIndex(u => u.GroupId);

        mb.Entity<AdminAuditLog>(e =>
        {
            e.Property(x => x.EntityType).HasMaxLength(50).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
            e.Property(x => x.BeforeJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.AfterJson).HasColumnType("jsonb");
            // Stored as string for forensic readability — survives enum value reordering across migrations.
            e.Property(x => x.ActionType).HasConversion<string>().HasMaxLength(50);

            e.HasOne(x => x.AdminUser)
                .WithMany()
                .HasForeignKey(x => x.AdminUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => new { x.AdminUserId, x.CreatedAt }).IsDescending(false, true);
            e.HasIndex(x => x.CreatedAt).IsDescending();
        });

        // Optimistic concurrency via PostgreSQL system xmin column.
        // No migration needed — xmin is always present on every row.
        //
        // Note: QuotationRequest is intentionally NOT xmin-guarded — status
        // transitions in CostingController.SubmitItem use an explicit SELECT
        // FOR UPDATE (serialisable lock) that provides stronger concurrency
        // control, and its FromSqlInterpolated("SELECT *") does not surface
        // the xmin system column to EF.
        mb.Entity<RefreshToken>().UseXminAsConcurrencyToken();
        mb.Entity<BomHeader>().UseXminAsConcurrencyToken();
        mb.Entity<QuotationApproval>().UseXminAsConcurrencyToken();

        mb.Entity<PushSubscription>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.Endpoint).IsUnique();
            e.Property(x => x.Endpoint).HasMaxLength(2048);
            e.Property(x => x.P256dh).HasMaxLength(512);
            e.Property(x => x.Auth).HasMaxLength(512);
            e.Property(x => x.UserAgent).HasMaxLength(512);
            e.HasOne(x => x.User)
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<CodeCounter>(e =>
        {
            e.HasKey(c => c.Sequence);
            e.Property(c => c.Sequence).HasMaxLength(20);
        });
    }
}
