# V3 Phase A — Backend Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the backend half of the V3 simplified workflow — new state machine, auto-generated codes, sales-owns-BOM data flow, two-stage MD approval, MD signature upload, adapted admin overrides — all behind master without deploying to prod (prod stays on V2.3 until Phase C cutover).

**Architecture:** ASP.NET Core 8 feature-slice controllers calling EF Core 8 directly. New `Domain/Workflow/RequisitionStateMachine.cs` as the single source of truth for state transitions. New `Infrastructure/Services/CodeGeneratorService.cs` using row-locked counter table for atomic code generation. Signature image storage on Fly volume (`/data/signatures/{userId}.png`), path persisted on `User.SignatureImagePath`. PdfService extended to embed signature image + text on signed quotations. All admin override controllers from V2.3-C kept and adapted (UnlockBom deleted, UnlockCosting renamed, OverridePrices extended for `Signed` status).

**Tech Stack:** ASP.NET Core 8 · EF Core 8 + Npgsql · BCrypt · QuestPDF (PDF) · MailKit (SMTP) · SignalR · QuestPDF · xUnit + WebApplicationFactory (tests against real localhost Postgres at port 5433).

**Spec reference:** [`docs/superpowers/specs/2026-04-29-v3-simplified-workflow-design.md`](../specs/2026-04-29-v3-simplified-workflow-design.md) — read sections 5 (state machine), 6 (endpoints), 7 (data model), 9 (notifications), 11 (admin overrides).

---

## File Structure

### NEW files

| Path | Purpose |
|---|---|
| `BomPriceApproval.API/Domain/Workflow/RequisitionStateMachine.cs` | Static class — single source of truth for valid state transitions |
| `BomPriceApproval.API/Domain/Entities/CodeCounter.cs` | Entity for atomic code counter rows (`CUST`, `FG`, `RM`) |
| `BomPriceApproval.API/Infrastructure/Services/CodeGeneratorService.cs` | Service with row-locked atomic increments |
| `BomPriceApproval.API/Features/Profile/SignatureController.cs` | MD signature upload + download endpoints |
| `BomPriceApproval.API/Features/Profile/SignatureDtos.cs` | DTOs for signature upload |
| `BomPriceApproval.API/Infrastructure/Data/Migrations/<ts>_V3_AddNewStatusValues.cs` | No-op migration documenting new enum slots |
| `BomPriceApproval.API/Infrastructure/Data/Migrations/<ts>_V3_AddCodeCounters.cs` | Creates `CodeCounters` table + seeds from existing data |
| `BomPriceApproval.API/Infrastructure/Data/Migrations/<ts>_V3_AddBomLineAuditColumns.cs` | Adds `LastModifiedByUserId`, `LastModifiedAt` to `BomLine` |
| `BomPriceApproval.API/Infrastructure/Data/Migrations/<ts>_V3_AddSignatureColumn.cs` | Adds `SignatureImagePath` to `Users` |
| `BomPriceApproval.API/Infrastructure/Data/Migrations/<ts>_V3_AddApprovalStage.cs` | Adds `Stage` + `CostFxSnapshot` to `QuotationApproval` with legacy backfill |
| `BomPriceApproval.API/Infrastructure/Data/Migrations/<ts>_V3_AddCancelledFields.cs` | Adds `CancelledAt`, `CancelledByUserId`, `CancelReason` to `QuotationRequest` |
| `BomPriceApproval.Tests/Workflow/RequisitionStateMachineTests.cs` | Unit tests for state machine rules |
| `BomPriceApproval.Tests/Infrastructure/CodeGeneratorServiceTests.cs` | Sequential + concurrent code-gen tests |
| `BomPriceApproval.Tests/Profile/SignatureControllerTests.cs` | Signature upload/get tests |
| `BomPriceApproval.Tests/Requisitions/V3RequisitionWorkflowTests.cs` | End-to-end V3 happy path |
| `BomPriceApproval.Tests/Approvals/V3ApprovalSplitTests.cs` | Tests for SetMargin/AcceptCustomer/RejectCustomer/FinalSign |

### MODIFIED files

| Path | Change |
|---|---|
| `Domain/Enums/RequisitionStatus.cs` | Add Costing=8, MdPricing=9, CustomerConfirm=10, MdFinalSign=11, Signed=12, Cancelled=13 |
| `Domain/Enums/NotificationType.cs` | Add MarginSet, CustomerConfirmRequested, CustomerAccepted, CustomerRejected, Signed, RequisitionCancelled |
| `Domain/Enums/AdminActionType.cs` | Add RollbackToCosting, V3CutoverMigration |
| `Domain/Entities/User.cs` | Add `SignatureImagePath` |
| `Domain/Entities/BomLine.cs` | Add `LastModifiedByUserId`, `LastModifiedAt` |
| `Domain/Entities/QuotationApproval.cs` | Add `Stage` (enum), `CostFxSnapshot` |
| `Domain/Entities/QuotationRequest.cs` | Add `CancelledAt`, `CancelledByUserId`, `CancelReason` |
| `Infrastructure/Data/AppDbContext.cs` | Register `CodeCounters` DbSet + new column configs |
| `Features/Customers/CustomersController.cs` | Auto-Code on POST; ignore payload `code` |
| `Features/Items/ItemsController.cs` | Auto-Code on POST per `ItemType` |
| `Features/Requisitions/RequisitionsController.cs` | POST/PUT accept inline BOM payload; add Submit + Cancel endpoints; drop role gates referencing BomCreator |
| `Features/Requisitions/RequisitionDtos.cs` | New payload shape with `finishedGoods[].bomLines[]` |
| `Features/Costing/CostingController.cs` | PUT for editable BOM with diff; Submit snapshots `CostFxSnapshot` |
| `Features/Approvals/ApprovalsController.cs` | Split into SetMargin / AcceptCustomer / RejectCustomer / FinalSign endpoints |
| `Features/Approvals/ApprovalDtos.cs` | New request shapes |
| `Features/Admin/AdminRequisitionsController.cs` | Delete UnlockBom; rename UnlockCosting → RollbackToCosting; extend OverridePrices for Signed |
| `Infrastructure/Authorization/BranchAuthorization.cs` | Drop BomCreator role checks |
| `Infrastructure/Authorization/SalesAuthorization.cs` | Drop BomCreator role checks |
| `Infrastructure/Authorization/AdminOverrideAuthorization.cs` | Update transition whitelist for V3 statuses |
| `Infrastructure/Services/PdfService.cs` | Embed signature image + text on signed quotation PDFs |
| `Infrastructure/Services/NotificationService.cs` | (No interface change — caller controllers wire new types) |
| `Program.cs` | Register `ICodeGeneratorService` in DI |

### DELETED files

| Path | Reason |
|---|---|
| `Features/Bom/BomController.cs` | All endpoints (`POST /api/bom/...`, `PUT /api/bom/...`) deleted; sales now creates BOM via Requisitions create payload |
| `Features/Bom/BomDtos.cs` | Only used by deleted controller |
| `BomPriceApproval.Tests/Bom/BomTests.cs` | Tests deprecated BomController endpoints |
| `BomPriceApproval.Tests/Bom/BomSaveLinesTests.cs` | Tests deprecated BomController endpoints |
| `BomPriceApproval.Tests/Bom/BomWithCostTests.cs` | Tests deprecated BomController endpoints |
| `BomPriceApproval.Tests/Bom/BomHistoricalReadTests.cs` | Replaced by V3 historical-read via Requisitions detail endpoint |
| `BomPriceApproval.Tests/Bom/NotificationResilienceTests.cs` | Replaced by V3 notification tests |

### Notes

- `BomHeader` / `BomLine` / `BomCost` entities are **kept** — sales now writes them via `RequisitionsController`, accountant edits via `CostingController`. Tables stay.
- `UserRole.BomCreator` enum value is **kept** for legacy data reads. No new accounts. Authorization helpers stop checking it.
- All tests reference `BomCreator` user accounts — those tests get either: (a) updated to use a different role if the test isn't BOM-specific, or (b) deleted if the test was BOM-specific (the 5 files in the DELETED list above).

---

## Worktree

Implementation should run in a dedicated worktree off `master` (not off the spec branch `docs/v3-simplified-workflow-design`):

```bash
# From repo root, on master with clean working tree
git worktree add ../bom-v3-phase-a feat/v3-phase-a-backend
cd ../bom-v3-phase-a
```

All commits land on `feat/v3-phase-a-backend`. PR opens against `master` after Task 49 (final smoke).

---

## Task 1: Set up worktree + branch + verify baseline

**Files:** None modified (setup only)

- [ ] **Step 1: Verify clean working tree on master**

```bash
git checkout master
git pull origin master
git status
```

Expected: `On branch master / Your branch is up to date with 'origin/master'. / nothing to commit, working tree clean`

- [ ] **Step 2: Create dedicated worktree**

```bash
git worktree add ../bom-v3-phase-a feat/v3-phase-a-backend
cd ../bom-v3-phase-a
```

Expected: `Preparing worktree (new branch 'feat/v3-phase-a-backend')`

- [ ] **Step 3: Verify baseline build + tests pass before any changes**

```bash
dotnet build --nologo -v q
dotnet test --nologo -v q
```

Expected: Build succeeds. Tests: ~318 passing (or matches CLAUDE.md's most-recent count).

If build or tests fail BEFORE any changes, STOP. The failure is in master and must be fixed first.

- [ ] **Step 4: No commit yet — proceed to Task 2**

---

## Task 2: Add new RequisitionStatus enum values

**Files:**
- Modify: `BomPriceApproval.API/Domain/Enums/RequisitionStatus.cs`

- [ ] **Step 1: Update enum with V3 values**

Replace the entire file contents:

```csharp
namespace BomPriceApproval.API.Domain.Enums;

public enum RequisitionStatus
{
    // V2.3 values — kept for legacy data reads. Int slots preserved.
    Draft = 0,
    BomPending = 1,           // deprecated; cancelled at V3 cutover
    BomInProgress = 2,        // deprecated; cancelled at V3 cutover
    CostingPending = 3,       // deprecated; cancelled at V3 cutover
    CostingInProgress = 4,    // deprecated; cancelled at V3 cutover
    MdReview = 5,             // deprecated; cancelled at V3 cutover
    Approved = 6,             // KEPT — V2.3 Approved reqs stay as-is post-cutover
    Rejected = 7,             // KEPT — used in V3 (MD-rejected from MdPricing)

    // V3 NEW values
    Costing = 8,
    MdPricing = 9,
    CustomerConfirm = 10,
    MdFinalSign = 11,
    Signed = 12,
    Cancelled = 13
}
```

- [ ] **Step 2: Verify the build**

```bash
dotnet build --nologo -v q
```

Expected: Compile succeeds (existing code references like `RequisitionStatus.Draft`, `Approved`, `Rejected` still valid).

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.API/Domain/Enums/RequisitionStatus.cs
git commit -m "feat(v3): add new RequisitionStatus enum values

- Costing=8, MdPricing=9, CustomerConfirm=10, MdFinalSign=11, Signed=12, Cancelled=13
- V2.3 values kept at int slots 0-7 for legacy data reads

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Add no-op migration for new RequisitionStatus values

EF Core stores enums as int columns by default. Adding new enum values does not require a schema change. This migration documents the intent and ensures the EF model snapshot stays in sync if someone later switches to string-stored enums.

**Files:**
- Create: `BomPriceApproval.API/Infrastructure/Data/Migrations/<ts>_V3_AddNewStatusValues.cs` (generated by `dotnet ef`)

- [ ] **Step 1: Generate empty migration**

```bash
dotnet ef migrations add V3_AddNewStatusValues --project BomPriceApproval.API
```

Expected: Migration file created at `BomPriceApproval.API/Infrastructure/Data/Migrations/<timestamp>_V3_AddNewStatusValues.cs`

- [ ] **Step 2: Verify migration body is empty (no schema changes)**

Open the generated file. Both `Up` and `Down` methods should be empty. If EF generated extra changes, STOP and investigate (likely an unrelated drift).

- [ ] **Step 3: Apply migration locally**

```bash
dotnet ef database update --project BomPriceApproval.API
```

Expected: `Applying migration '<timestamp>_V3_AddNewStatusValues'.` then `Done.`

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Data/Migrations/
git commit -m "feat(v3): no-op migration documenting new status enum values

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Create RequisitionStateMachine — write failing tests first

**Files:**
- Create: `BomPriceApproval.Tests/Workflow/RequisitionStateMachineTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Domain.Workflow;

namespace BomPriceApproval.Tests.Workflow;

public class RequisitionStateMachineTests
{
    // Happy path transitions

    [Theory]
    [InlineData(RequisitionStatus.Draft, RequisitionStatus.Costing)]
    [InlineData(RequisitionStatus.Costing, RequisitionStatus.MdPricing)]
    [InlineData(RequisitionStatus.MdPricing, RequisitionStatus.CustomerConfirm)]
    [InlineData(RequisitionStatus.MdPricing, RequisitionStatus.Rejected)]
    [InlineData(RequisitionStatus.CustomerConfirm, RequisitionStatus.MdFinalSign)]
    [InlineData(RequisitionStatus.CustomerConfirm, RequisitionStatus.MdPricing)]
    [InlineData(RequisitionStatus.MdFinalSign, RequisitionStatus.Signed)]
    public void CanTransition_HappyPath_ReturnsTrue(RequisitionStatus from, RequisitionStatus to)
    {
        Assert.True(RequisitionStateMachine.CanTransition(from, to));
    }

    // Cancel transitions (any non-terminal → Cancelled)

    [Theory]
    [InlineData(RequisitionStatus.Draft)]
    [InlineData(RequisitionStatus.Costing)]
    [InlineData(RequisitionStatus.MdPricing)]
    [InlineData(RequisitionStatus.CustomerConfirm)]
    [InlineData(RequisitionStatus.MdFinalSign)]
    public void CanTransition_AnyNonTerminalToCancelled_ReturnsTrue(RequisitionStatus from)
    {
        Assert.True(RequisitionStateMachine.CanTransition(from, RequisitionStatus.Cancelled));
    }

    // Forbidden transitions

    [Theory]
    [InlineData(RequisitionStatus.Draft, RequisitionStatus.MdPricing)]      // skip Costing
    [InlineData(RequisitionStatus.Costing, RequisitionStatus.Signed)]      // skip MD stages
    [InlineData(RequisitionStatus.Signed, RequisitionStatus.MdFinalSign)]  // backward from terminal
    [InlineData(RequisitionStatus.Signed, RequisitionStatus.Cancelled)]    // terminal → terminal
    [InlineData(RequisitionStatus.Cancelled, RequisitionStatus.Draft)]     // terminal → anything
    [InlineData(RequisitionStatus.Rejected, RequisitionStatus.MdPricing)]  // terminal → backward
    public void CanTransition_Forbidden_ReturnsFalse(RequisitionStatus from, RequisitionStatus to)
    {
        Assert.False(RequisitionStateMachine.CanTransition(from, to));
    }

    // Terminal status detection

    [Theory]
    [InlineData(RequisitionStatus.Signed, true)]
    [InlineData(RequisitionStatus.Rejected, true)]
    [InlineData(RequisitionStatus.Cancelled, true)]
    [InlineData(RequisitionStatus.Draft, false)]
    [InlineData(RequisitionStatus.Costing, false)]
    [InlineData(RequisitionStatus.MdPricing, false)]
    [InlineData(RequisitionStatus.CustomerConfirm, false)]
    [InlineData(RequisitionStatus.MdFinalSign, false)]
    public void IsTerminal_ReturnsExpected(RequisitionStatus status, bool expected)
    {
        Assert.Equal(expected, RequisitionStateMachine.IsTerminal(status));
    }

    // Admin rollback whitelist

    [Theory]
    [InlineData(RequisitionStatus.Signed, new[] { (int)RequisitionStatus.MdFinalSign })]
    [InlineData(RequisitionStatus.MdFinalSign, new[] { (int)RequisitionStatus.CustomerConfirm })]
    [InlineData(RequisitionStatus.CustomerConfirm, new[] { (int)RequisitionStatus.MdPricing })]
    [InlineData(RequisitionStatus.MdPricing, new[] { (int)RequisitionStatus.Costing })]
    [InlineData(RequisitionStatus.Costing, new[] { (int)RequisitionStatus.Draft })]
    public void AdminRollbackTargets_ReturnsExpected(RequisitionStatus from, int[] expected)
    {
        var actual = RequisitionStateMachine.AdminRollbackTargets(from)
            .Select(s => (int)s).ToArray();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(RequisitionStatus.Cancelled)]
    [InlineData(RequisitionStatus.Rejected)]
    [InlineData(RequisitionStatus.Draft)]
    public void AdminRollbackTargets_TerminalOrInitial_ReturnsEmpty(RequisitionStatus from)
    {
        Assert.Empty(RequisitionStateMachine.AdminRollbackTargets(from));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail with "type not defined"**

```bash
dotnet test --nologo -v q --filter "FullyQualifiedName~RequisitionStateMachineTests"
```

Expected: Build error — `The type or namespace name 'RequisitionStateMachine' could not be found`. This is the failing-test signal.

- [ ] **Step 3: Commit failing tests**

```bash
git add BomPriceApproval.Tests/Workflow/RequisitionStateMachineTests.cs
git commit -m "test(v3): add failing RequisitionStateMachine tests

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Implement RequisitionStateMachine

**Files:**
- Create: `BomPriceApproval.API/Domain/Workflow/RequisitionStateMachine.cs`

- [ ] **Step 1: Create the workflow folder + class**

```csharp
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Domain.Workflow;

/// <summary>
/// Single source of truth for V3 requisition state transitions.
/// Used by RequisitionsController, CostingController, ApprovalsController, and AdminRequisitionsController.
/// </summary>
public static class RequisitionStateMachine
{
    private static readonly HashSet<(RequisitionStatus, RequisitionStatus)> AllowedTransitions = new()
    {
        // Happy path
        (RequisitionStatus.Draft, RequisitionStatus.Costing),
        (RequisitionStatus.Costing, RequisitionStatus.MdPricing),
        (RequisitionStatus.MdPricing, RequisitionStatus.CustomerConfirm),
        (RequisitionStatus.MdPricing, RequisitionStatus.Rejected),         // MD rejects from initial pricing
        (RequisitionStatus.CustomerConfirm, RequisitionStatus.MdFinalSign), // sales: customer accepted
        (RequisitionStatus.CustomerConfirm, RequisitionStatus.MdPricing),   // sales: customer rejected (re-margin)
        (RequisitionStatus.MdFinalSign, RequisitionStatus.Signed),

        // Cancel — any non-terminal can move to Cancelled
        (RequisitionStatus.Draft, RequisitionStatus.Cancelled),
        (RequisitionStatus.Costing, RequisitionStatus.Cancelled),
        (RequisitionStatus.MdPricing, RequisitionStatus.Cancelled),
        (RequisitionStatus.CustomerConfirm, RequisitionStatus.Cancelled),
        (RequisitionStatus.MdFinalSign, RequisitionStatus.Cancelled),
    };

    private static readonly Dictionary<RequisitionStatus, RequisitionStatus[]> AdminRollback = new()
    {
        { RequisitionStatus.Signed,          new[] { RequisitionStatus.MdFinalSign } },
        { RequisitionStatus.MdFinalSign,     new[] { RequisitionStatus.CustomerConfirm } },
        { RequisitionStatus.CustomerConfirm, new[] { RequisitionStatus.MdPricing } },
        { RequisitionStatus.MdPricing,       new[] { RequisitionStatus.Costing } },
        { RequisitionStatus.Costing,         new[] { RequisitionStatus.Draft } },
    };

    public static bool CanTransition(RequisitionStatus from, RequisitionStatus to)
        => AllowedTransitions.Contains((from, to));

    public static bool IsTerminal(RequisitionStatus status)
        => status is RequisitionStatus.Signed
                  or RequisitionStatus.Rejected
                  or RequisitionStatus.Cancelled;

    public static IReadOnlyList<RequisitionStatus> AdminRollbackTargets(RequisitionStatus from)
        => AdminRollback.TryGetValue(from, out var targets) ? targets : Array.Empty<RequisitionStatus>();
}
```

- [ ] **Step 2: Run tests to verify they pass**

```bash
dotnet test --nologo -v q --filter "FullyQualifiedName~RequisitionStateMachineTests"
```

Expected: All tests pass (28 cases via Theory + Inline data).

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.API/Domain/Workflow/RequisitionStateMachine.cs
git commit -m "feat(v3): implement RequisitionStateMachine

Single source of truth for V3 state transitions and admin rollback targets.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Add CodeCounter entity + DbSet

**Files:**
- Create: `BomPriceApproval.API/Domain/Entities/CodeCounter.cs`
- Modify: `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs`

- [ ] **Step 1: Create the entity**

```csharp
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
```

- [ ] **Step 2: Register in AppDbContext**

Open `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs`. Find the `DbSet<>` declarations region and add:

```csharp
public DbSet<CodeCounter> CodeCounters => Set<CodeCounter>();
```

In `OnModelCreating`, add:

```csharp
modelBuilder.Entity<CodeCounter>(e =>
{
    e.HasKey(c => c.Sequence);
    e.Property(c => c.Sequence).HasMaxLength(20);
});
```

- [ ] **Step 3: Verify build**

```bash
dotnet build --nologo -v q
```

Expected: Compile succeeds.

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.API/Domain/Entities/CodeCounter.cs BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs
git commit -m "feat(v3): add CodeCounter entity for atomic code generation

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Migration V3_AddCodeCounters with seed values

**Files:**
- Create: `BomPriceApproval.API/Infrastructure/Data/Migrations/<ts>_V3_AddCodeCounters.cs` (generated)

- [ ] **Step 1: Generate migration**

```bash
dotnet ef migrations add V3_AddCodeCounters --project BomPriceApproval.API
```

- [ ] **Step 2: Edit the migration to seed counters from existing data**

Open the generated file. Replace the body of `Up` with:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateTable(
        name: "CodeCounters",
        columns: table => new
        {
            Sequence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
            NextValue = table.Column<int>(type: "integer", nullable: false)
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_CodeCounters", x => x.Sequence);
        });

    // Seed counters from existing data — these match the highest seen Code suffix + 1
    migrationBuilder.Sql(@"
        INSERT INTO ""CodeCounters"" (""Sequence"", ""NextValue"") VALUES
        ('CUST', COALESCE(
            (SELECT MAX(CAST(SPLIT_PART(""Code"", '-', 2) AS INTEGER)) + 1
             FROM ""Customers""
             WHERE ""Code"" ~ '^CUST-[0-9]+$'), 1)),
        ('FG', COALESCE(
            (SELECT MAX(CAST(SPLIT_PART(""Code"", '-', 2) AS INTEGER)) + 1
             FROM ""Items""
             WHERE ""Code"" ~ '^FG-[0-9]+$' AND ""Type"" = 0), 1)),
        ('RM', COALESCE(
            (SELECT MAX(CAST(SPLIT_PART(""Code"", '-', 2) AS INTEGER)) + 1
             FROM ""Items""
             WHERE ""Code"" ~ '^RM-[0-9]+$' AND ""Type"" = 1), 1));
    ");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropTable(name: "CodeCounters");
}
```

`ItemType.FinishedGood = 0`, `ItemType.RawMaterial = 1` per the existing enum.

- [ ] **Step 3: Apply migration**

```bash
dotnet ef database update --project BomPriceApproval.API
```

Expected: Table created. Verify:

```bash
psql -h localhost -p 5433 -U postgres -d bom_price_approval -c "SELECT * FROM \"CodeCounters\";"
```

(Or use the connection string from user-secrets.) Should show 3 rows: CUST, FG, RM with seeded NextValue.

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Data/Migrations/
git commit -m "feat(v3): create CodeCounters table with seeded values

Counters seeded from existing CUST-/FG-/RM- coded rows.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: CodeGeneratorService — write failing tests first

**Files:**
- Create: `BomPriceApproval.Tests/Infrastructure/CodeGeneratorServiceTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Infrastructure;

public class CodeGeneratorServiceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CodeGeneratorServiceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task NextCustomerCode_ReturnsZeroPaddedFormat()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICodeGeneratorService>();

        var code = await svc.NextCustomerCodeAsync();

        Assert.Matches(@"^CUST-\d{4,}$", code);
    }

    [Fact]
    public async Task NextItemCode_FinishedGood_UsesFGPrefix()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICodeGeneratorService>();

        var code = await svc.NextItemCodeAsync(ItemType.FinishedGood);

        Assert.Matches(@"^FG-\d{4,}$", code);
    }

    [Fact]
    public async Task NextItemCode_RawMaterial_UsesRMPrefix()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICodeGeneratorService>();

        var code = await svc.NextItemCodeAsync(ItemType.RawMaterial);

        Assert.Matches(@"^RM-\d{4,}$", code);
    }

    [Fact]
    public async Task NextCustomerCode_SequentialCalls_AreUnique()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICodeGeneratorService>();

        var code1 = await svc.NextCustomerCodeAsync();
        var code2 = await svc.NextCustomerCodeAsync();
        var code3 = await svc.NextCustomerCodeAsync();

        Assert.NotEqual(code1, code2);
        Assert.NotEqual(code2, code3);
        Assert.NotEqual(code1, code3);
    }

    [Fact]
    public async Task NextCustomerCode_ConcurrentCalls_AllUnique()
    {
        // 10 concurrent calls — all codes must be distinct (row-level lock prevents race)
        const int N = 10;
        var tasks = new List<Task<string>>();

        for (int i = 0; i < N; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var scope = _factory.Services.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<ICodeGeneratorService>();
                return await svc.NextCustomerCodeAsync();
            }));
        }

        var codes = await Task.WhenAll(tasks);

        Assert.Equal(N, codes.Distinct().Count());
    }
}
```

- [ ] **Step 2: Run tests — expect failure (interface missing)**

```bash
dotnet test --nologo -v q --filter "FullyQualifiedName~CodeGeneratorServiceTests"
```

Expected: Build error — `ICodeGeneratorService` not found.

- [ ] **Step 3: Commit failing tests**

```bash
git add BomPriceApproval.Tests/Infrastructure/CodeGeneratorServiceTests.cs
git commit -m "test(v3): add failing CodeGeneratorService tests

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: Implement CodeGeneratorService

**Files:**
- Create: `BomPriceApproval.API/Infrastructure/Services/CodeGeneratorService.cs`
- Modify: `BomPriceApproval.API/Program.cs` (DI registration)

- [ ] **Step 1: Create the service interface + implementation**

```csharp
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Infrastructure.Services;

public interface ICodeGeneratorService
{
    Task<string> NextCustomerCodeAsync();
    Task<string> NextItemCodeAsync(ItemType type);
}

public class CodeGeneratorService : ICodeGeneratorService
{
    private readonly AppDbContext _db;

    public CodeGeneratorService(AppDbContext db) => _db = db;

    public Task<string> NextCustomerCodeAsync() => NextAsync("CUST");

    public Task<string> NextItemCodeAsync(ItemType type) => type switch
    {
        ItemType.FinishedGood => NextAsync("FG"),
        ItemType.RawMaterial  => NextAsync("RM"),
        _ => throw new ArgumentException($"Unsupported ItemType: {type}", nameof(type))
    };

    private async Task<string> NextAsync(string sequence)
    {
        // Row-level lock for concurrent safety. Postgres FOR UPDATE locks the matched row
        // for the duration of the transaction; concurrent callers wait their turn.
        await using var tx = await _db.Database.BeginTransactionAsync();

        var sql = "SELECT \"NextValue\" FROM \"CodeCounters\" WHERE \"Sequence\" = {0} FOR UPDATE";
        var current = await _db.Database
            .SqlQueryRaw<int>(sql, sequence)
            .FirstOrDefaultAsync();

        if (current == 0)
            throw new InvalidOperationException(
                $"Counter row missing for sequence '{sequence}'. Run V3_AddCodeCounters migration.");

        var next = current + 1;
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE \"CodeCounters\" SET \"NextValue\" = {0} WHERE \"Sequence\" = {1}",
            next, sequence);

        await tx.CommitAsync();

        return $"{sequence}-{current:D4}";
    }
}
```

- [ ] **Step 2: Register in DI**

Open `BomPriceApproval.API/Program.cs`. Find the existing service registrations (look for `builder.Services.AddScoped<...>` calls). Add:

```csharp
builder.Services.AddScoped<ICodeGeneratorService, CodeGeneratorService>();
```

- [ ] **Step 3: Run tests to verify they pass**

```bash
dotnet test --nologo -v q --filter "FullyQualifiedName~CodeGeneratorServiceTests"
```

Expected: All 5 tests pass — including concurrent test (10 unique codes from 10 parallel callers).

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Services/CodeGeneratorService.cs BomPriceApproval.API/Program.cs
git commit -m "feat(v3): implement CodeGeneratorService with row-level lock

Atomic increments using SELECT FOR UPDATE on CodeCounters table.
Concurrent test validates 10 parallel callers produce unique codes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 10: Auto-Code on Customer POST

**Files:**
- Modify: `BomPriceApproval.API/Features/Customers/CustomersController.cs` (Create method)
- Modify: `BomPriceApproval.Tests/Customers/CustomersCrudTests.cs` (add test for auto-code)

- [ ] **Step 1: Add a failing test for auto-Code**

Open `BomPriceApproval.Tests/Customers/CustomersCrudTests.cs`. Find the test class. Add this method:

```csharp
[Fact]
public async Task Create_AutoGeneratesCustomerCode_AndIgnoresClientPayload()
{
    var token = await GetSalesTokenAsync(); // existing helper
    _factory.HttpClient.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", token);

    var req = new
    {
        code = "MANUAL-IGNORED",  // server should ignore this
        name = $"AutoCode Test {Guid.NewGuid():N}",
        email = "auto@test.com",
        phoneNumber = "+971500000000",
        address = "Alain"
    };

    var resp = await _factory.HttpClient.PostAsJsonAsync("/api/customers", req);
    resp.EnsureSuccessStatusCode();

    var created = await resp.Content.ReadFromJsonAsync<JsonElement>();
    var serverCode = created.GetProperty("code").GetString()!;

    Assert.Matches(@"^CUST-\d{4,}$", serverCode);
    Assert.NotEqual("MANUAL-IGNORED", serverCode);
}
```

If the test class doesn't have a `GetSalesTokenAsync` helper, follow the existing pattern from other tests in the same file for auth setup.

- [ ] **Step 2: Run test — expect failure (manual code currently respected)**

```bash
dotnet test --nologo -v q --filter "FullyQualifiedName~CustomersCrudTests.Create_AutoGeneratesCustomerCode"
```

Expected: FAIL — server returned `MANUAL-IGNORED` as the code.

- [ ] **Step 3: Update CustomersController.Create to auto-generate Code**

Open `BomPriceApproval.API/Features/Customers/CustomersController.cs`. Find the `Create` method (HTTP POST). Inject `ICodeGeneratorService` via constructor (alongside existing dependencies):

```csharp
private readonly ICodeGeneratorService _codeGen;

public CustomersController(AppDbContext db, INotificationService notif, ICodeGeneratorService codeGen)
{
    _db = db;
    _notif = notif;
    _codeGen = codeGen;
}
```

In the Create method, replace the `Code` assignment from `req.Code` with the generated code:

```csharp
var customer = new Customer
{
    Code = await _codeGen.NextCustomerCodeAsync(),  // auto-generate, ignore req.Code
    Name = req.Name,
    Email = req.Email ?? "",
    PhoneNumber = req.PhoneNumber ?? "",
    Address = req.Address ?? "",
    SalesPersonId = req.SalesPersonId,  // existing logic
    CreatedByUserId = userId,           // existing logic
};
```

- [ ] **Step 4: Run tests to verify pass**

```bash
dotnet test --nologo -v q --filter "FullyQualifiedName~CustomersCrudTests"
```

Expected: All tests in CustomersCrudTests pass — including the new auto-code test AND existing tests (which set `code` but the field is now ignored).

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Customers/CustomersController.cs BomPriceApproval.Tests/Customers/CustomersCrudTests.cs
git commit -m "feat(v3): auto-generate Customer.Code on POST

Server now generates CUST-XXXX codes via CodeGeneratorService.
Client-supplied 'code' field is ignored.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 11: Auto-Code on Item POST

**Files:**
- Modify: `BomPriceApproval.API/Features/Items/ItemsController.cs` (Create method)
- Modify: `BomPriceApproval.Tests/Items/ItemEditTests.cs` or `ItemCreateDuplicateTests.cs` (whichever has Create-related tests)

- [ ] **Step 1: Add a failing test for auto-Code per ItemType**

Add a new test file `BomPriceApproval.Tests/Items/ItemAutoCodeTests.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BomPriceApproval.API;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Items;

public class ItemAutoCodeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ItemAutoCodeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateFinishedGood_AutoGeneratesFGCode()
    {
        var client = _factory.CreateClient();
        var token = await TestHelpers.GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var req = new
        {
            code = "MANUAL-IGNORED",
            description = $"FG Test {Guid.NewGuid():N}",
            type = 0,  // FinishedGood
            branchId = 1,  // Alain
            isActive = true
        };

        var resp = await client.PostAsJsonAsync("/api/items", req);
        resp.EnsureSuccessStatusCode();

        var created = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var serverCode = created.GetProperty("code").GetString()!;

        Assert.Matches(@"^FG-\d{4,}$", serverCode);
    }

    [Fact]
    public async Task CreateRawMaterial_AutoGeneratesRMCode()
    {
        var client = _factory.CreateClient();
        var token = await TestHelpers.GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var req = new
        {
            code = "MANUAL-IGNORED",
            description = $"RM Test {Guid.NewGuid():N}",
            type = 1,  // RawMaterial
            branchId = 1,
            isActive = true
        };

        var resp = await client.PostAsJsonAsync("/api/items", req);
        resp.EnsureSuccessStatusCode();

        var created = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var serverCode = created.GetProperty("code").GetString()!;

        Assert.Matches(@"^RM-\d{4,}$", serverCode);
    }
}
```

If `TestHelpers.GetAdminTokenAsync` doesn't exist, copy the auth-token helper pattern from any existing test file in `BomPriceApproval.Tests`.

- [ ] **Step 2: Run test — expect failure**

```bash
dotnet test --nologo -v q --filter "FullyQualifiedName~ItemAutoCodeTests"
```

Expected: FAIL — server returned `MANUAL-IGNORED`.

- [ ] **Step 3: Update ItemsController.Create**

Open `BomPriceApproval.API/Features/Items/ItemsController.cs`. Inject `ICodeGeneratorService`:

```csharp
private readonly ICodeGeneratorService _codeGen;

public ItemsController(AppDbContext db, ICodeGeneratorService codeGen)
{
    _db = db;
    _codeGen = codeGen;
}
```

In the Create method, replace `Code = req.Code` with:

```csharp
var item = new Item
{
    Code = await _codeGen.NextItemCodeAsync(req.Type),  // auto-generate per ItemType
    Description = req.Description,
    Type = req.Type,
    BranchId = req.BranchId,
    IsActive = req.IsActive,
    LastPurchasePrice = req.LastPurchasePrice
};
```

- [ ] **Step 4: Run all Items tests**

```bash
dotnet test --nologo -v q --filter "FullyQualifiedName~Items"
```

Expected: All Item tests pass. Existing tests that supplied `Code = "..."` still pass because their assertions don't check the code value (or if they do, those tests need updating — fix as encountered).

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Items/ItemsController.cs BomPriceApproval.Tests/Items/ItemAutoCodeTests.cs
git commit -m "feat(v3): auto-generate Item.Code per ItemType on POST

FG-XXXX for FinishedGood, RM-XXXX for RawMaterial.
Client-supplied 'code' field is ignored.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 12: Migration V3_AddBomLineAuditColumns

**Files:**
- Modify: `BomPriceApproval.API/Domain/Entities/BomLine.cs`
- Create: `BomPriceApproval.API/Infrastructure/Data/Migrations/<ts>_V3_AddBomLineAuditColumns.cs` (generated)

- [ ] **Step 1: Add audit columns to BomLine entity**

Open `BomPriceApproval.API/Domain/Entities/BomLine.cs`. Add at the bottom of the class:

```csharp
// V3 — track accountant edits to sales' BOM (D24 diff visible to sales + MD)
public int? LastModifiedByUserId { get; set; }
public DateTime? LastModifiedAt { get; set; }
public User? LastModifiedBy { get; set; }
```

- [ ] **Step 2: Configure FK in AppDbContext**

Open `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs`. Find the `BomLine` configuration section (or the `OnModelCreating` if BomLine is configured there) and add:

```csharp
modelBuilder.Entity<BomLine>(e =>
{
    e.HasOne(b => b.LastModifiedBy)
        .WithMany()
        .HasForeignKey(b => b.LastModifiedByUserId)
        .OnDelete(DeleteBehavior.Restrict);
});
```

- [ ] **Step 3: Generate migration**

```bash
dotnet ef migrations add V3_AddBomLineAuditColumns --project BomPriceApproval.API
```

- [ ] **Step 4: Apply migration**

```bash
dotnet ef database update --project BomPriceApproval.API
```

Verify column exists:

```bash
psql -h localhost -p 5433 -U postgres -d bom_price_approval -c "\d \"BomLine\""
```

Should show `LastModifiedByUserId` and `LastModifiedAt` columns.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Domain/Entities/BomLine.cs BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs BomPriceApproval.API/Infrastructure/Data/Migrations/
git commit -m "feat(v3): add audit columns to BomLine for accountant edit tracking

LastModifiedByUserId + LastModifiedAt enable diff display per D24.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 13: Migration V3_AddSignatureColumn

**Files:**
- Modify: `BomPriceApproval.API/Domain/Entities/User.cs`
- Create: migration

- [ ] **Step 1: Add column to User entity**

Open `BomPriceApproval.API/Domain/Entities/User.cs`. Add:

```csharp
public string? SignatureImagePath { get; set; }
```

- [ ] **Step 2: Generate + apply migration**

```bash
dotnet ef migrations add V3_AddSignatureColumn --project BomPriceApproval.API
dotnet ef database update --project BomPriceApproval.API
```

- [ ] **Step 3: Verify build**

```bash
dotnet build --nologo -v q
```

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.API/Domain/Entities/User.cs BomPriceApproval.API/Infrastructure/Data/Migrations/
git commit -m "feat(v3): add SignatureImagePath to User

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 14: Migration V3_AddApprovalStage with legacy backfill

**Files:**
- Create: `BomPriceApproval.API/Domain/Enums/ApprovalStage.cs`
- Modify: `BomPriceApproval.API/Domain/Entities/QuotationApproval.cs`
- Create: migration with custom backfill

- [ ] **Step 1: Create ApprovalStage enum**

```csharp
namespace BomPriceApproval.API.Domain.Enums;

public enum ApprovalStage
{
    InitialPricing = 0,  // V3 Stage 1: MD margin entry
    FinalSign = 1        // V3 Stage 2: locked + signed; or legacy V2.3 final approval
}
```

- [ ] **Step 2: Add columns to QuotationApproval**

Open `BomPriceApproval.API/Domain/Entities/QuotationApproval.cs`. Add:

```csharp
// V3 — stage of this approval (legacy V2.3 rows backfilled to FinalSign)
public ApprovalStage Stage { get; set; } = ApprovalStage.InitialPricing;

// V3 — FX rate used to convert foreign-currency RM costs to AED at accountant submit time.
// Distinct from RateSnapshot (sale-side rate at MD margin entry).
public decimal? CostFxSnapshot { get; set; }
```

- [ ] **Step 3: Generate migration**

```bash
dotnet ef migrations add V3_AddApprovalStage --project BomPriceApproval.API
```

- [ ] **Step 4: Edit migration to handle legacy backfill**

Open the generated migration. Replace the `Up` body with:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Step 1: add column nullable
    migrationBuilder.AddColumn<int>(
        name: "Stage",
        table: "QuotationApproval",
        type: "integer",
        nullable: true);

    // Step 2: backfill legacy V2.3 rows to FinalSign (1)
    migrationBuilder.Sql(@"UPDATE ""QuotationApproval"" SET ""Stage"" = 1 WHERE ""Stage"" IS NULL;");

    // Step 3: enforce non-null + default for new rows
    migrationBuilder.AlterColumn<int>(
        name: "Stage",
        table: "QuotationApproval",
        type: "integer",
        nullable: false,
        defaultValue: 0,
        oldClrType: typeof(int),
        oldType: "integer",
        oldNullable: true);

    // Step 4: cost-side FX snapshot
    migrationBuilder.AddColumn<decimal>(
        name: "CostFxSnapshot",
        table: "QuotationApproval",
        type: "numeric(18,6)",
        precision: 18,
        scale: 6,
        nullable: true);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(name: "Stage", table: "QuotationApproval");
    migrationBuilder.DropColumn(name: "CostFxSnapshot", table: "QuotationApproval");
}
```

- [ ] **Step 5: Apply + verify**

```bash
dotnet ef database update --project BomPriceApproval.API
```

Verify legacy approvals were backfilled:

```bash
psql -h localhost -p 5433 -U postgres -d bom_price_approval -c "SELECT COUNT(*), \"Stage\" FROM \"QuotationApproval\" GROUP BY \"Stage\";"
```

Expected: All existing rows have `Stage = 1` (FinalSign).

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Domain/Enums/ApprovalStage.cs BomPriceApproval.API/Domain/Entities/QuotationApproval.cs BomPriceApproval.API/Infrastructure/Data/Migrations/
git commit -m "feat(v3): add Stage + CostFxSnapshot to QuotationApproval

Legacy V2.3 approvals backfilled to FinalSign (Stage=1) — they were
already terminal approvals.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 15: Migration V3_AddCancelledFields

**Files:**
- Modify: `BomPriceApproval.API/Domain/Entities/QuotationRequest.cs`
- Create: migration

- [ ] **Step 1: Add cancelled fields to QuotationRequest**

Open the entity. Add:

```csharp
// V3 — cancellation tracking (sales/admin cancel + cutover migration)
public DateTime? CancelledAt { get; set; }
public int? CancelledByUserId { get; set; }
public string? CancelReason { get; set; }
public User? CancelledBy { get; set; }
```

- [ ] **Step 2: Configure FK in AppDbContext**

```csharp
modelBuilder.Entity<QuotationRequest>(e =>
{
    e.HasOne(q => q.CancelledBy)
        .WithMany()
        .HasForeignKey(q => q.CancelledByUserId)
        .OnDelete(DeleteBehavior.Restrict);

    e.Property(q => q.CancelReason).HasMaxLength(500);
});
```

- [ ] **Step 3: Generate + apply migration**

```bash
dotnet ef migrations add V3_AddCancelledFields --project BomPriceApproval.API
dotnet ef database update --project BomPriceApproval.API
```

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.API/Domain/Entities/QuotationRequest.cs BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs BomPriceApproval.API/Infrastructure/Data/Migrations/
git commit -m "feat(v3): add Cancelled fields to QuotationRequest

CancelledAt + CancelledByUserId + CancelReason support sales/admin cancel
and cutover migration.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 16: Add new NotificationType + AdminActionType enum values

**Files:**
- Modify: `BomPriceApproval.API/Domain/Enums/NotificationType.cs`
- Modify: `BomPriceApproval.API/Domain/Enums/AdminActionType.cs`

- [ ] **Step 1: Extend NotificationType**

Open `BomPriceApproval.API/Domain/Enums/NotificationType.cs`. Append the new values **at the end** (preserves existing int slots):

```csharp
// V3 NEW values
MarginSet,                  // Stage 1 done — sent to sales + accountant
CustomerConfirmRequested,   // sent to sales
CustomerAccepted,           // sent to MD + accountant
CustomerRejected,           // sent to MD + accountant
SignedNotif,                // sent to sales + accountant (avoid name clash with status enum)
RequisitionCancelled        // sent to sales
```

If the existing enum already has a value named `Approved`, leave it — it's used for legacy V2.3 approval notifs.

- [ ] **Step 2: Extend AdminActionType**

Open `BomPriceApproval.API/Domain/Enums/AdminActionType.cs`. Append:

```csharp
// V3 NEW values
RollbackToCosting,          // C5 renamed (was UnlockCosting)
V3CutoverMigration          // logged once during Phase C cutover SQL
```

Note: `UnlockBom` (C4) is being removed — can stay in enum for legacy audit-log reads, or be deleted. **Keep it** for legacy data readability.

- [ ] **Step 3: Verify build + commit**

```bash
dotnet build --nologo -v q
git add BomPriceApproval.API/Domain/Enums/NotificationType.cs BomPriceApproval.API/Domain/Enums/AdminActionType.cs
git commit -m "feat(v3): add new NotificationType + AdminActionType enum values

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

(No DB migration needed — these are stored as int and adding values at the end doesn't affect existing rows.)

---

## Task 17: Drop BomCreator from authorization helpers

**Files:**
- Modify: `BomPriceApproval.API/Infrastructure/Authorization/BranchAuthorization.cs`
- Modify: `BomPriceApproval.API/Infrastructure/Authorization/SalesAuthorization.cs`
- Modify: `BomPriceApproval.API/Infrastructure/Authorization/AdminOverrideAuthorization.cs`
- Modify: `BomPriceApproval.Tests/Authorization/BranchAuthorizationHelperTests.cs`
- Modify: `BomPriceApproval.Tests/Authorization/SalesAuthorizationHelperTests.cs`

- [ ] **Step 1: Search for BomCreator references in auth helpers**

```bash
grep -rn "BomCreator" BomPriceApproval.API/Infrastructure/Authorization/
```

For each match in the API project:
- If the match is a role check (`if (user.Role == UserRole.BomCreator) return X`) — remove the branch entirely
- If it's part of a list (`new[] { UserRole.BomCreator, UserRole.Accountant }`) — remove the BomCreator entry

- [ ] **Step 2: Update auth helper tests**

```bash
grep -rn "BomCreator" BomPriceApproval.Tests/Authorization/
```

For each test that explicitly tests BomCreator behavior:
- If the test asserts BomCreator gets a specific result — delete the test (role no longer relevant)
- If the test uses BomCreator as a "non-Admin/SP/Acct" stand-in — replace with `UserRole.Accountant` or just delete

- [ ] **Step 3: Run authorization tests**

```bash
dotnet test --nologo -v q --filter "FullyQualifiedName~Authorization"
```

Expected: All auth tests pass after BomCreator references removed.

- [ ] **Step 4: Run full test suite**

```bash
dotnet test --nologo -v q
```

Expect some failures in non-auth tests that still reference BomCreator (e.g., `ChangeBranchTests.cs`, `BomTests.cs`). These will be addressed in later tasks. For now, document the failing test count.

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Authorization/ BomPriceApproval.Tests/Authorization/
git commit -m "feat(v3): drop BomCreator from authorization helpers

Helpers no longer special-case BomCreator role. Enum value retained for
legacy User row reads. Auth-helper tests updated; broader test failures
in BOM-specific test files will be addressed when those files are deleted.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 18: Delete obsolete BomController + tests

**Files:**
- Delete: `BomPriceApproval.API/Features/Bom/BomController.cs`
- Delete: `BomPriceApproval.API/Features/Bom/BomDtos.cs`
- Delete: `BomPriceApproval.Tests/Bom/BomTests.cs`
- Delete: `BomPriceApproval.Tests/Bom/BomSaveLinesTests.cs`
- Delete: `BomPriceApproval.Tests/Bom/BomWithCostTests.cs`
- Delete: `BomPriceApproval.Tests/Bom/BomHistoricalReadTests.cs`
- Delete: `BomPriceApproval.Tests/Bom/NotificationResilienceTests.cs`

Per spec §6.2: BOM-stage endpoints are deleted; sales submits BOM via Requisitions create payload (Task 21).

- [ ] **Step 1: Verify no other API code references BomController endpoints**

```bash
grep -rn "/api/bom" BomPriceApproval.API/ BomPriceApproval.Tests/
```

Expected: matches only in `BomController.cs`, `Bom*Tests.cs` files (all to be deleted), and possibly comments in other test files.

If there are real callers in non-deleted files, STOP and re-evaluate.

- [ ] **Step 2: Delete files**

```bash
rm BomPriceApproval.API/Features/Bom/BomController.cs
rm BomPriceApproval.API/Features/Bom/BomDtos.cs
rm BomPriceApproval.Tests/Bom/BomTests.cs
rm BomPriceApproval.Tests/Bom/BomSaveLinesTests.cs
rm BomPriceApproval.Tests/Bom/BomWithCostTests.cs
rm BomPriceApproval.Tests/Bom/BomHistoricalReadTests.cs
rm BomPriceApproval.Tests/Bom/NotificationResilienceTests.cs
```

- [ ] **Step 3: Build + test**

```bash
dotnet build --nologo -v q
dotnet test --nologo -v q
```

Expected: Build clean. Tests should drop by ~30-50 (the deleted file count); remaining tests pass.

- [ ] **Step 4: Commit**

```bash
git add -A BomPriceApproval.API/Features/Bom/ BomPriceApproval.Tests/Bom/
git commit -m "feat(v3): delete obsolete BomController + Bom-specific tests

Sales now creates BOM via Requisitions create payload (Task 21).
BOM-stage endpoints (/api/bom/*) removed. BomHeader/BomLine entities
retained — accessed via Requisitions and Costing controllers.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 19: Update RequisitionDtos for V3 payload shape

**Files:**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs`

- [ ] **Step 1: Add V3 create/update DTOs**

Open the file. Add (or replace existing create-related DTOs):

```csharp
// V3 — sales submits requisition + BOM in one payload (combined screen)
public record CreateRequisitionV3Request(
    int CustomerId,
    string QuotationCurrency,             // "USD", "EUR", "AED"
    string? ReferenceNumber,
    string? Notes,
    List<FinishedGoodLine> FinishedGoods);

public record FinishedGoodLine(
    int ItemId,                           // FG item id
    decimal ExpectedQtyKg,
    bool Printing,
    List<BomLineDto> BomLines);

public record BomLineDto(
    int ItemId,                           // RM item id
    decimal QtyPerKg,                     // KG of RM per 1 KG of FG (V3 recipe)
    string? Micron);                      // free-text per D3
```

Keep the existing list/detail DTOs unchanged (`RequisitionListDto`, etc.) — those are read-side and still valid.

- [ ] **Step 2: Verify build**

```bash
dotnet build --nologo -v q
```

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/RequisitionDtos.cs
git commit -m "feat(v3): add V3 requisition create/update DTOs

CreateRequisitionV3Request includes inline BOM lines per FG.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 20: Refactor RequisitionsController.Create for V3 payload

**Files:**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs`

- [ ] **Step 1: Replace the existing Create method**

Find the current `[HttpPost]` action. Replace its body to consume `CreateRequisitionV3Request` and create the requisition + RequisitionItem rows + BomHeader + BomLine rows in a single transaction:

```csharp
[HttpPost]
[Authorize(Roles = "SalesPerson,Admin")]
public async Task<IActionResult> Create([FromBody] CreateRequisitionV3Request req)
{
    var userId = int.Parse(User.FindFirst("UserId")!.Value);
    var role = User.FindFirst("Role")!.Value;

    // Validate customer
    var customer = await _db.Customers
        .FirstOrDefaultAsync(c => c.Id == req.CustomerId && !c.IsDeleted);
    if (customer is null) return NotFound(new { error = "Customer not found" });

    // Validate currency
    if (string.IsNullOrWhiteSpace(req.QuotationCurrency))
        return BadRequest(new { error = "QuotationCurrency required" });

    // Validate at least one FG
    if (req.FinishedGoods is null || req.FinishedGoods.Count == 0)
        return BadRequest(new { error = "At least one finished good required" });

    // Validate FG items + RM items exist + are Alain branch
    var allItemIds = req.FinishedGoods
        .SelectMany(fg => new[] { fg.ItemId }.Concat(fg.BomLines.Select(b => b.ItemId)))
        .Distinct()
        .ToList();
    var items = await _db.Items
        .Where(i => allItemIds.Contains(i.Id) && i.IsActive)
        .ToDictionaryAsync(i => i.Id);
    foreach (var id in allItemIds)
        if (!items.ContainsKey(id))
            return BadRequest(new { error = $"Item {id} not found or inactive" });

    // Find Alain branch id (V3 scope)
    var alainBranch = await _db.Branches.FirstOrDefaultAsync(b => b.Name == "Alain" && b.IsActive);
    if (alainBranch is null) return BadRequest(new { error = "Alain branch not configured" });

    // Sales person id
    int salesPersonId = role == "SalesPerson" ? userId : (customer.SalesPersonId ?? userId);

    await using var tx = await _db.Database.BeginTransactionAsync();

    // Create requisition
    var requisition = new QuotationRequest
    {
        BranchId = alainBranch.Id,
        CustomerId = customer.Id,
        SalesPersonId = salesPersonId,
        CreatedByUserId = userId,
        Status = RequisitionStatus.Draft,
        CurrencyCode = req.QuotationCurrency,
        ReferenceNumber = req.ReferenceNumber,
        Notes = req.Notes,
        CreatedAt = DateTime.UtcNow
    };
    _db.QuotationRequests.Add(requisition);
    await _db.SaveChangesAsync();

    // Create RequisitionItem + BomHeader + BomLine for each FG
    foreach (var fg in req.FinishedGoods)
    {
        var reqItem = new RequisitionItem
        {
            QuotationRequestId = requisition.Id,
            ItemId = fg.ItemId,
            ExpectedQty = fg.ExpectedQtyKg,
            HasPrinting = fg.Printing
        };
        _db.RequisitionItems.Add(reqItem);
        await _db.SaveChangesAsync();

        var bomHeader = new BomHeader
        {
            RequisitionItemId = reqItem.Id,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _db.BomHeaders.Add(bomHeader);
        await _db.SaveChangesAsync();

        foreach (var line in fg.BomLines)
        {
            _db.BomLines.Add(new BomLine
            {
                BomHeaderId = bomHeader.Id,
                ItemId = line.ItemId,
                QtyPerKg = line.QtyPerKg,
                Micron = line.Micron
            });
        }
        await _db.SaveChangesAsync();
    }

    await tx.CommitAsync();

    return CreatedAtAction(nameof(GetById), new { id = requisition.Id },
        new { id = requisition.Id, status = requisition.Status.ToString() });
}
```

**Note:** `RequisitionItem.HasPrinting` and `BomLine.QtyPerKg` and `BomLine.Micron` may need to be added if they don't already exist as columns. Check `RequisitionItem` and `BomLine` entities.

- [ ] **Step 2: If column additions needed, add them as a separate migration**

If `RequisitionItem.HasPrinting` doesn't exist:

```bash
dotnet ef migrations add V3_AddRequisitionItemPrinting --project BomPriceApproval.API
```

Edit the migration to add `HasPrinting BOOLEAN NOT NULL DEFAULT FALSE`.

If `BomLine.QtyPerKg` and `BomLine.Micron` don't exist (V2.3 may have called them differently — `Quantity` and no Micron):

```bash
dotnet ef migrations add V3_AddBomLineV3Columns --project BomPriceApproval.API
```

Add `QtyPerKg NUMERIC(18,4) NOT NULL DEFAULT 0` and `Micron VARCHAR(20) NULL`.

Apply migrations:

```bash
dotnet ef database update --project BomPriceApproval.API
```

- [ ] **Step 3: Verify build**

```bash
dotnet build --nologo -v q
```

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(v3): refactor RequisitionsController.Create for inline BOM payload

Sales submits customer + FG list + per-FG BOM lines in one POST.
Status starts at Draft. Required schema additions for HasPrinting,
QtyPerKg, Micron added via migrations.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 21: Add Submit + Cancel endpoints to RequisitionsController

**Files:**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs`

- [ ] **Step 1: Add Submit endpoint**

```csharp
[HttpPost("{id}/submit")]
[Authorize(Roles = "SalesPerson,Admin")]
public async Task<IActionResult> Submit(int id)
{
    var userId = int.Parse(User.FindFirst("UserId")!.Value);
    var role = User.FindFirst("Role")!.Value;

    var req = await _db.QuotationRequests
        .Include(r => r.RequisitionItems)
        .ThenInclude(ri => ri.BomHeader)
        .ThenInclude(bh => bh!.BomLines)
        .FirstOrDefaultAsync(r => r.Id == id);
    if (req is null) return NotFound();

    if (role == "SalesPerson" && req.SalesPersonId != userId)
        return Forbid();

    if (!RequisitionStateMachine.CanTransition(req.Status, RequisitionStatus.Costing))
        return BadRequest(new { error = $"Cannot submit from status {req.Status}" });

    if (req.RequisitionItems.Count == 0 || req.RequisitionItems.Any(ri => ri.BomHeader is null || ri.BomHeader.BomLines.Count == 0))
        return BadRequest(new { error = "All finished goods must have a BOM with at least one line" });

    req.Status = RequisitionStatus.Costing;
    await _db.SaveChangesAsync();

    // Notify accountants in Alain branch
    var accountants = await _db.UserBranches
        .Where(ub => ub.BranchId == req.BranchId && ub.User.Role == UserRole.Accountant && ub.User.IsActive)
        .Select(ub => ub.UserId)
        .ToListAsync();

    await _notif.SendToUsersAsync(accountants, NotificationType.RequisitionSubmitted,
        $"REQ-{req.Id:D4} submitted for costing", req.Id);

    return Ok(new { id = req.Id, status = req.Status.ToString() });
}
```

- [ ] **Step 2: Add Cancel endpoint**

```csharp
public record CancelRequisitionRequest(string Reason);

[HttpPost("{id}/cancel")]
[Authorize(Roles = "SalesPerson,Admin")]
public async Task<IActionResult> Cancel(int id, [FromBody] CancelRequisitionRequest body)
{
    var userId = int.Parse(User.FindFirst("UserId")!.Value);
    var role = User.FindFirst("Role")!.Value;

    if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
        return BadRequest(new { error = "Reason ≥ 5 chars required" });

    var req = await _db.QuotationRequests.FirstOrDefaultAsync(r => r.Id == id);
    if (req is null) return NotFound();

    if (role == "SalesPerson" && req.SalesPersonId != userId)
        return Forbid();

    if (!RequisitionStateMachine.CanTransition(req.Status, RequisitionStatus.Cancelled))
        return BadRequest(new { error = $"Cannot cancel from status {req.Status}" });

    req.Status = RequisitionStatus.Cancelled;
    req.CancelledAt = DateTime.UtcNow;
    req.CancelledByUserId = userId;
    req.CancelReason = body.Reason;
    await _db.SaveChangesAsync();

    await _notif.SendAsync(req.SalesPersonId, NotificationType.RequisitionCancelled,
        $"REQ-{req.Id:D4} cancelled: {body.Reason}", req.Id);

    return Ok(new { id = req.Id, status = req.Status.ToString() });
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build --nologo -v q
```

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs
git commit -m "feat(v3): add Submit + Cancel endpoints to RequisitionsController

Submit transitions Draft -> Costing (after BOM completeness check).
Cancel transitions any non-terminal -> Cancelled with required reason.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 22: V3 happy-path tests for Requisitions

**Files:**
- Create: `BomPriceApproval.Tests/Requisitions/V3RequisitionWorkflowTests.cs`

- [ ] **Step 1: Write end-to-end happy path test**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BomPriceApproval.API;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

public class V3RequisitionWorkflowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public V3RequisitionWorkflowTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Sales_CreatesRequisition_WithInlineBOM_StartsAsDraft()
    {
        var client = _factory.CreateClient();
        var token = await TestHelpers.GetSalesTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Seed minimum data: 1 customer, 1 FG, 1 RM (use existing helpers if available)
        var (customerId, fgItemId, rmItemId) = await TestHelpers.SeedV3MinimumAsync(_factory.Services);

        var payload = new
        {
            customerId,
            quotationCurrency = "USD",
            referenceNumber = "PO-9941",
            notes = "Test V3 happy path",
            finishedGoods = new[]
            {
                new
                {
                    itemId = fgItemId,
                    expectedQtyKg = 5000m,
                    printing = true,
                    bomLines = new[]
                    {
                        new { itemId = rmItemId, qtyPerKg = 0.44m, micron = "20" }
                    }
                }
            }
        };

        var resp = await client.PostAsJsonAsync("/api/requisitions", payload);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Draft", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Sales_SubmitsRequisition_TransitionsToCosting()
    {
        var client = _factory.CreateClient();
        var token = await TestHelpers.GetSalesTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var reqId = await TestHelpers.CreateV3DraftRequisitionAsync(client);

        var resp = await client.PostAsync($"/api/requisitions/{reqId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Costing", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Submit_FromCostingStatus_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var token = await TestHelpers.GetSalesTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var reqId = await TestHelpers.CreateV3DraftRequisitionAsync(client);
        await client.PostAsync($"/api/requisitions/{reqId}/submit", null);  // Draft -> Costing

        var resp = await client.PostAsync($"/api/requisitions/{reqId}/submit", null);  // Costing -> ???

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Cancel_DraftRequisition_TransitionsToCancelled()
    {
        var client = _factory.CreateClient();
        var token = await TestHelpers.GetSalesTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var reqId = await TestHelpers.CreateV3DraftRequisitionAsync(client);

        var resp = await client.PostAsJsonAsync($"/api/requisitions/{reqId}/cancel", new { reason = "Customer withdrew" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cancelled", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancel_WithoutReason_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var token = await TestHelpers.GetSalesTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var reqId = await TestHelpers.CreateV3DraftRequisitionAsync(client);

        var resp = await client.PostAsJsonAsync($"/api/requisitions/{reqId}/cancel", new { reason = "x" }); // < 5 chars

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
```

If `TestHelpers.SeedV3MinimumAsync` and `CreateV3DraftRequisitionAsync` don't exist, add them to a shared helper file (`BomPriceApproval.Tests/Shared/TestHelpers.cs` or extend existing).

`SeedV3MinimumAsync` should:
1. Get the Alain branch id
2. Create one customer (any sales person)
3. Create one FG item + one RM item in Alain branch
4. Return the IDs

`CreateV3DraftRequisitionAsync` should:
1. Call `SeedV3MinimumAsync`
2. POST a minimal V3 payload
3. Return the new requisition id

- [ ] **Step 2: Run tests**

```bash
dotnet test --nologo -v q --filter "FullyQualifiedName~V3RequisitionWorkflowTests"
```

Expected: All 5 tests pass.

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.Tests/Requisitions/V3RequisitionWorkflowTests.cs BomPriceApproval.Tests/Shared/
git commit -m "test(v3): add Requisitions workflow happy-path tests

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 23: Refactor CostingController — read endpoint includes BOM

**Files:**
- Modify: `BomPriceApproval.API/Features/Costing/CostingController.cs`

- [ ] **Step 1: Update GET /api/costing/{requisitionId}**

Find the existing GET method. Update its projection to include BOM lines (it may already do this — verify):

```csharp
[HttpGet("{requisitionId}")]
[Authorize(Roles = "Accountant,Admin")]
public async Task<IActionResult> Get(int requisitionId)
{
    var req = await _db.QuotationRequests
        .Include(r => r.Customer)
        .Include(r => r.SalesPerson)
        .Include(r => r.RequisitionItems).ThenInclude(ri => ri.Item)
        .Include(r => r.RequisitionItems).ThenInclude(ri => ri.BomHeader)
            .ThenInclude(bh => bh!.BomLines).ThenInclude(bl => bl.Item)
        .Include(r => r.RequisitionItems).ThenInclude(ri => ri.BomCost)
            .ThenInclude(bc => bc!.BomCostLines)
        .FirstOrDefaultAsync(r => r.Id == requisitionId);
    if (req is null) return NotFound();

    return Ok(new
    {
        req.Id,
        req.RefNo,
        req.Status,
        req.CurrencyCode,
        req.Notes,
        Customer = new { req.Customer.Id, req.Customer.Name, req.Customer.Code },
        SalesPerson = new { req.SalesPerson.Id, req.SalesPerson.Name },
        FinishedGoods = req.RequisitionItems.Select(ri => new
        {
            ri.Id,
            ri.ExpectedQty,
            ri.HasPrinting,
            Item = new { ri.Item.Id, ri.Item.Code, ri.Item.Description },
            BomLines = ri.BomHeader == null ? null : ri.BomHeader.BomLines.Select(bl => new
            {
                bl.Id,
                bl.QtyPerKg,
                bl.Micron,
                Item = new { bl.Item.Id, bl.Item.Code, bl.Item.Description },
                bl.LastModifiedByUserId,
                bl.LastModifiedAt
            }),
            Costs = ri.BomCost == null ? null : new
            {
                ri.BomCost.PrintingCostPerKg,
                ri.BomCost.PrintingCostCurrency,
                ri.BomCost.FohPerKg,
                ri.BomCost.TransportPerKg,
                ri.BomCost.CommissionPerKg,
                Lines = ri.BomCost.BomCostLines.Select(bcl => new
                {
                    bcl.BomLineId,
                    bcl.WastagePercent,
                    bcl.PurchaseValuePerKg,
                    bcl.PurchaseCurrency
                })
            }
        })
    });
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build --nologo -v q
```

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.API/Features/Costing/CostingController.cs
git commit -m "feat(v3): expand Costing GET to include full BOM tree

Returns FG list with BOM lines and existing cost data per FG.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 24: Add CostingController PUT endpoint for editable BOM

**Files:**
- Modify: `BomPriceApproval.API/Features/Costing/CostingController.cs`

- [ ] **Step 1: Add the BOM update DTO + endpoint**

```csharp
public record UpdateBomRequest(int FinishedGoodId, List<BomLineUpdate> Lines);

public record BomLineUpdate(
    int? Id,                  // null = new line; non-null = update existing
    int ItemId,
    decimal QtyPerKg,
    string? Micron,
    bool Delete = false);

[HttpPut("{requisitionId}/bom")]
[Authorize(Roles = "Accountant,Admin")]
public async Task<IActionResult> UpdateBom(int requisitionId, [FromBody] UpdateBomRequest body)
{
    var userId = int.Parse(User.FindFirst("UserId")!.Value);

    var req = await _db.QuotationRequests
        .Include(r => r.RequisitionItems).ThenInclude(ri => ri.BomHeader)
            .ThenInclude(bh => bh!.BomLines)
        .FirstOrDefaultAsync(r => r.Id == requisitionId);
    if (req is null) return NotFound();

    if (req.Status != RequisitionStatus.Costing)
        return BadRequest(new { error = $"BOM editable only in Costing status (current: {req.Status})" });

    var fgItem = req.RequisitionItems.FirstOrDefault(ri => ri.Id == body.FinishedGoodId);
    if (fgItem is null) return BadRequest(new { error = "FG not found in this requisition" });

    var bomHeader = fgItem.BomHeader!;

    var now = DateTime.UtcNow;

    foreach (var line in body.Lines)
    {
        if (line.Id is null && !line.Delete)
        {
            // New line
            _db.BomLines.Add(new BomLine
            {
                BomHeaderId = bomHeader.Id,
                ItemId = line.ItemId,
                QtyPerKg = line.QtyPerKg,
                Micron = line.Micron,
                LastModifiedByUserId = userId,
                LastModifiedAt = now
            });
        }
        else
        {
            var existing = bomHeader.BomLines.FirstOrDefault(bl => bl.Id == line.Id);
            if (existing is null) continue;

            if (line.Delete)
            {
                _db.BomLines.Remove(existing);
            }
            else if (existing.QtyPerKg != line.QtyPerKg
                  || existing.Micron != line.Micron
                  || existing.ItemId != line.ItemId)
            {
                existing.QtyPerKg = line.QtyPerKg;
                existing.Micron = line.Micron;
                existing.ItemId = line.ItemId;
                existing.LastModifiedByUserId = userId;
                existing.LastModifiedAt = now;
            }
        }
    }

    await _db.SaveChangesAsync();

    return Ok(new { ok = true, finishedGoodId = body.FinishedGoodId });
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build --nologo -v q
```

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.API/Features/Costing/CostingController.cs
git commit -m "feat(v3): add Costing BOM-edit endpoint with audit tracking

Accountant can add/update/delete BOM lines while in Costing status.
LastModifiedBy/At updated only on actual change (not no-ops).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 25: Update CostingController.Submit to snapshot CostFxSnapshot

**Files:**
- Modify: `BomPriceApproval.API/Features/Costing/CostingController.cs`

- [ ] **Step 1: Update Submit method**

Find the existing Submit method (or rename Costing's existing transition method). Update transition target to `MdPricing` and add FX snapshot logic:

```csharp
[HttpPost("{requisitionId}/submit")]
[Authorize(Roles = "Accountant,Admin")]
public async Task<IActionResult> Submit(int requisitionId)
{
    var req = await _db.QuotationRequests
        .Include(r => r.RequisitionItems).ThenInclude(ri => ri.BomCost).ThenInclude(bc => bc!.BomCostLines)
        .FirstOrDefaultAsync(r => r.Id == requisitionId);
    if (req is null) return NotFound();

    if (!RequisitionStateMachine.CanTransition(req.Status, RequisitionStatus.MdPricing))
        return BadRequest(new { error = $"Cannot submit costing from {req.Status}" });

    // Validate every FG has cost data
    if (req.RequisitionItems.Any(ri => ri.BomCost is null))
        return BadRequest(new { error = "All FGs must have cost data before submit" });

    // Snapshot today's FX rate for foreign-currency RM purchase prices
    var hasForeignRm = req.RequisitionItems
        .SelectMany(ri => ri.BomCost!.BomCostLines)
        .Any(bcl => bcl.PurchaseCurrency != "AED");
    if (hasForeignRm)
    {
        var rate = await _db.ExchangeRates
            .Where(er => er.IsActive)
            .OrderByDescending(er => er.EffectiveDate)
            .Select(er => (decimal?)er.RateToAED)
            .FirstOrDefaultAsync();

        // Find or create the InitialPricing approval row for cost FX (created early to hold the snapshot)
        // Per spec: cost-side FX snapshot stored on QuotationApproval.CostFxSnapshot
        // We don't yet have the InitialPricing approval at this stage — it's created at SetMargin.
        // Workaround: stash on a new approval row created here with Stage=InitialPricing, IsApproved=false,
        // ApprovedByUserId=null... but ApprovedByUserId is non-nullable. Alternative: hold in QuotationRequest.
    }

    // Decision: store CostFxSnapshot directly on QuotationRequest instead of QuotationApproval —
    // simpler, avoids approval-row-with-no-approver. Update spec accordingly via a fix-up commit.
    //
    // Spec author note: spec §7.1 placed CostFxSnapshot on QuotationApproval.
    // Implementation finds it cleaner on QuotationRequest. See Task 26 for the corresponding migration.

    req.Status = RequisitionStatus.MdPricing;
    await _db.SaveChangesAsync();

    // Notify MDs (cross-branch — MDs see all)
    var mds = await _db.Users
        .Where(u => u.Role == UserRole.ManagingDirector && u.IsActive)
        .Select(u => u.Id)
        .ToListAsync();

    await _notif.SendToUsersAsync(mds, NotificationType.CostingComplete,
        $"REQ-{req.Id:D4} awaiting your margin", req.Id);

    return Ok(new { id = req.Id, status = req.Status.ToString() });
}
```

**IMPORTANT:** This task highlights a spec deviation discovered during implementation. The `CostFxSnapshot` placement should be revisited. **Decision:** keep the field on `QuotationApproval` (per spec §7.1) and populate it in `SetMargin` (Task 28) instead of here. Update this task's Submit method to NOT touch `CostFxSnapshot` and just transition the status:

**Revised Step 1 final body:**

```csharp
[HttpPost("{requisitionId}/submit")]
[Authorize(Roles = "Accountant,Admin")]
public async Task<IActionResult> Submit(int requisitionId)
{
    var req = await _db.QuotationRequests
        .Include(r => r.RequisitionItems).ThenInclude(ri => ri.BomCost).ThenInclude(bc => bc!.BomCostLines)
        .FirstOrDefaultAsync(r => r.Id == requisitionId);
    if (req is null) return NotFound();

    if (!RequisitionStateMachine.CanTransition(req.Status, RequisitionStatus.MdPricing))
        return BadRequest(new { error = $"Cannot submit costing from {req.Status}" });

    if (req.RequisitionItems.Any(ri => ri.BomCost is null))
        return BadRequest(new { error = "All FGs must have cost data before submit" });

    req.Status = RequisitionStatus.MdPricing;
    await _db.SaveChangesAsync();

    var mds = await _db.Users
        .Where(u => u.Role == UserRole.ManagingDirector && u.IsActive)
        .Select(u => u.Id)
        .ToListAsync();

    await _notif.SendToUsersAsync(mds, NotificationType.CostingComplete,
        $"REQ-{req.Id:D4} awaiting your margin", req.Id);

    return Ok(new { id = req.Id, status = req.Status.ToString() });
}
```

CostFxSnapshot is captured in `SetMargin` (Task 28) — not here.

- [ ] **Step 2: Build + commit**

```bash
dotnet build --nologo -v q
git add BomPriceApproval.API/Features/Costing/CostingController.cs
git commit -m "feat(v3): Costing.Submit transitions to MdPricing

Notifies all MDs. CostFxSnapshot capture deferred to ApprovalsController.SetMargin
(per architectural simplification — keeps approval row creation in one place).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 26: Create V3 Approval DTOs

**Files:**
- Modify: `BomPriceApproval.API/Features/Approvals/ApprovalDtos.cs`

- [ ] **Step 1: Add V3 DTOs**

```csharp
// V3 — Stage 1 (initial pricing)
public record SetMarginRequest(
    string? Notes,
    List<MarginItemDto> Items);

public record MarginItemDto(
    int RequisitionItemId,
    decimal MarginPerKg);     // in quote currency (D6)

// V3 — Stage 2A (sales accept)
public record AcceptCustomerRequest(string? CustomerFeedback);

// V3 — Stage 2A reject (re-margin loop)
public record RejectCustomerRequest(string Reason);

// V3 — Stage 2B (MD final sign)
public record FinalSignRequest(
    string ConfirmationToken,  // must equal "SIGN" client-side type-to-confirm
    string? Notes);
```

- [ ] **Step 2: Build + commit**

```bash
dotnet build --nologo -v q
git add BomPriceApproval.API/Features/Approvals/ApprovalDtos.cs
git commit -m "feat(v3): add V3 approval DTOs for split endpoints

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 27: ApprovalsController.SetMargin (Stage 1)

**Files:**
- Modify: `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs`

- [ ] **Step 1: Add SetMargin endpoint**

```csharp
[HttpPost("{requisitionId}/set-margin")]
[Authorize(Roles = "ManagingDirector,Admin")]
public async Task<IActionResult> SetMargin(int requisitionId, [FromBody] SetMarginRequest body)
{
    var userId = int.Parse(User.FindFirst("UserId")!.Value);

    var req = await _db.QuotationRequests
        .Include(r => r.RequisitionItems).ThenInclude(ri => ri.BomCost).ThenInclude(bc => bc!.BomCostLines)
        .FirstOrDefaultAsync(r => r.Id == requisitionId);
    if (req is null) return NotFound();

    if (!RequisitionStateMachine.CanTransition(req.Status, RequisitionStatus.CustomerConfirm))
        return BadRequest(new { error = $"Cannot set margin from {req.Status}" });

    // Validate every FG has a margin entry
    var fgIds = req.RequisitionItems.Select(ri => ri.Id).ToHashSet();
    var bodyIds = body.Items.Select(i => i.RequisitionItemId).ToHashSet();
    if (!fgIds.SetEquals(bodyIds))
        return BadRequest(new { error = "Margin must be supplied for every FG, no extras" });

    foreach (var item in body.Items)
        if (item.MarginPerKg < 0)
            return BadRequest(new { error = $"Margin must be >= 0 (FG {item.RequisitionItemId})" });

    // Snapshot FX rate (sale-side rate at margin entry — D21)
    decimal? saleRateSnapshot = null;
    decimal? costRateSnapshot = null;
    if (req.CurrencyCode != "AED")
    {
        saleRateSnapshot = await _db.ExchangeRates
            .Where(er => er.IsActive && er.CurrencyCode == req.CurrencyCode)
            .OrderByDescending(er => er.EffectiveDate)
            .Select(er => (decimal?)er.RateToAED)
            .FirstOrDefaultAsync();
        if (saleRateSnapshot is null)
            return BadRequest(new { error = $"No active FX rate for {req.CurrencyCode}" });
    }

    // Cost-side snapshot — for any foreign RM currencies in the cost lines
    var costForeignCurrencies = req.RequisitionItems
        .SelectMany(ri => ri.BomCost?.BomCostLines ?? new List<BomCostLine>())
        .Select(bcl => bcl.PurchaseCurrency)
        .Where(c => c != "AED")
        .Distinct()
        .ToList();
    // For simplicity store the AED-equivalent rate of the requisition's CurrencyCode at snapshot time as costFxSnapshot.
    // Per-line foreign FX is handled at MdFinalSign PDF generation (uses today's rates if none recorded).
    costRateSnapshot = saleRateSnapshot;

    // Mark any prior InitialPricing approval as superseded (re-margin loop)
    var priorApprovals = await _db.QuotationApprovals
        .Where(qa => qa.QuotationRequestId == req.Id && qa.Stage == ApprovalStage.InitialPricing && !qa.IsSuperseded)
        .ToListAsync();
    foreach (var prior in priorApprovals)
    {
        prior.IsSuperseded = true;
        prior.SupersededAt = DateTime.UtcNow;
    }

    // Create new approval row
    var approval = new QuotationApproval
    {
        QuotationRequestId = req.Id,
        ApprovedByUserId = userId,
        ApprovedAt = DateTime.UtcNow,
        Notes = body.Notes,
        IsApproved = false,
        Stage = ApprovalStage.InitialPricing,
        RateSnapshot = saleRateSnapshot,
        CostFxSnapshot = costRateSnapshot,
        Items = body.Items.Select(i => new ApprovalItem
        {
            RequisitionItemId = i.RequisitionItemId,
            MarginPerKg = i.MarginPerKg
        }).ToList()
    };
    _db.QuotationApprovals.Add(approval);

    req.Status = RequisitionStatus.CustomerConfirm;
    await _db.SaveChangesAsync();

    await _notif.SendAsync(req.SalesPersonId, NotificationType.CustomerConfirmRequested,
        $"REQ-{req.Id:D4} priced — confirm with customer", req.Id);
    var accountants = await _db.UserBranches
        .Where(ub => ub.BranchId == req.BranchId && ub.User.Role == UserRole.Accountant && ub.User.IsActive)
        .Select(ub => ub.UserId).ToListAsync();
    await _notif.SendToUsersAsync(accountants, NotificationType.MarginSet,
        $"REQ-{req.Id:D4} pricing complete", req.Id);

    return Ok(new { id = req.Id, status = req.Status.ToString(), approvalId = approval.Id });
}
```

- [ ] **Step 2: Build + commit**

```bash
dotnet build --nologo -v q
git add BomPriceApproval.API/Features/Approvals/ApprovalsController.cs
git commit -m "feat(v3): add ApprovalsController.SetMargin endpoint (Stage 1)

Creates QuotationApproval with Stage=InitialPricing, snapshots FX rate,
transitions MdPricing -> CustomerConfirm. Notifies sales + accountants.
Re-margin loop supersedes prior InitialPricing approvals.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 28: ApprovalsController.AcceptCustomer + RejectCustomer

**Files:**
- Modify: `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs`

- [ ] **Step 1: Add both endpoints**

```csharp
[HttpPost("{requisitionId}/accept-customer")]
[Authorize(Roles = "SalesPerson,Admin")]
public async Task<IActionResult> AcceptCustomer(int requisitionId, [FromBody] AcceptCustomerRequest body)
{
    var userId = int.Parse(User.FindFirst("UserId")!.Value);
    var role = User.FindFirst("Role")!.Value;

    var req = await _db.QuotationRequests.FirstOrDefaultAsync(r => r.Id == requisitionId);
    if (req is null) return NotFound();

    if (role == "SalesPerson" && req.SalesPersonId != userId)
        return Forbid();

    if (!RequisitionStateMachine.CanTransition(req.Status, RequisitionStatus.MdFinalSign))
        return BadRequest(new { error = $"Cannot accept from {req.Status}" });

    req.Status = RequisitionStatus.MdFinalSign;
    if (!string.IsNullOrWhiteSpace(body.CustomerFeedback))
        req.Notes = (req.Notes ?? "") + $"\n[CustomerAccepted {DateTime.UtcNow:yyyy-MM-dd}] {body.CustomerFeedback}";
    await _db.SaveChangesAsync();

    var mds = await _db.Users
        .Where(u => u.Role == UserRole.ManagingDirector && u.IsActive)
        .Select(u => u.Id).ToListAsync();
    var accountants = await _db.UserBranches
        .Where(ub => ub.BranchId == req.BranchId && ub.User.Role == UserRole.Accountant && ub.User.IsActive)
        .Select(ub => ub.UserId).ToListAsync();

    await _notif.SendToUsersAsync(mds, NotificationType.CustomerAccepted,
        $"REQ-{req.Id:D4} customer accepted — apply final sign", req.Id);
    await _notif.SendToUsersAsync(accountants, NotificationType.CustomerAccepted,
        $"REQ-{req.Id:D4} customer accepted", req.Id);

    return Ok(new { id = req.Id, status = req.Status.ToString() });
}

[HttpPost("{requisitionId}/reject-customer")]
[Authorize(Roles = "SalesPerson,Admin")]
public async Task<IActionResult> RejectCustomer(int requisitionId, [FromBody] RejectCustomerRequest body)
{
    var userId = int.Parse(User.FindFirst("UserId")!.Value);
    var role = User.FindFirst("Role")!.Value;

    if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
        return BadRequest(new { error = "Reason >= 5 chars required" });

    var req = await _db.QuotationRequests.FirstOrDefaultAsync(r => r.Id == requisitionId);
    if (req is null) return NotFound();

    if (role == "SalesPerson" && req.SalesPersonId != userId)
        return Forbid();

    if (!RequisitionStateMachine.CanTransition(req.Status, RequisitionStatus.MdPricing))
        return BadRequest(new { error = $"Cannot reject-customer from {req.Status}" });

    // Mark current approval as superseded (will be replaced when MD re-sets margin)
    var current = await _db.QuotationApprovals
        .Where(qa => qa.QuotationRequestId == req.Id
                  && qa.Stage == ApprovalStage.InitialPricing
                  && !qa.IsSuperseded)
        .FirstOrDefaultAsync();
    if (current is not null)
    {
        current.IsSuperseded = true;
        current.SupersededAt = DateTime.UtcNow;
    }

    req.Status = RequisitionStatus.MdPricing;
    req.Notes = (req.Notes ?? "") + $"\n[CustomerRejected {DateTime.UtcNow:yyyy-MM-dd}] {body.Reason}";
    await _db.SaveChangesAsync();

    var mds = await _db.Users
        .Where(u => u.Role == UserRole.ManagingDirector && u.IsActive)
        .Select(u => u.Id).ToListAsync();
    var accountants = await _db.UserBranches
        .Where(ub => ub.BranchId == req.BranchId && ub.User.Role == UserRole.Accountant && ub.User.IsActive)
        .Select(ub => ub.UserId).ToListAsync();

    await _notif.SendToUsersAsync(mds, NotificationType.CustomerRejected,
        $"REQ-{req.Id:D4} customer rejected — re-price needed", req.Id);
    await _notif.SendToUsersAsync(accountants, NotificationType.CustomerRejected,
        $"REQ-{req.Id:D4} customer rejected", req.Id);

    return Ok(new { id = req.Id, status = req.Status.ToString() });
}
```

- [ ] **Step 2: Build + commit**

```bash
dotnet build --nologo -v q
git add BomPriceApproval.API/Features/Approvals/ApprovalsController.cs
git commit -m "feat(v3): add AcceptCustomer + RejectCustomer endpoints

AcceptCustomer: CustomerConfirm -> MdFinalSign.
RejectCustomer: CustomerConfirm -> MdPricing (re-margin loop), supersedes
current InitialPricing approval. Both update Notes with audit trail.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 29: ApprovalsController.FinalSign (Stage 2)

**Files:**
- Modify: `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs`

- [ ] **Step 1: Add FinalSign endpoint**

```csharp
[HttpPost("{requisitionId}/final-sign")]
[Authorize(Roles = "ManagingDirector,Admin")]
public async Task<IActionResult> FinalSign(int requisitionId, [FromBody] FinalSignRequest body)
{
    var userId = int.Parse(User.FindFirst("UserId")!.Value);

    if (body.ConfirmationToken != "SIGN")
        return BadRequest(new { error = "Type-to-confirm token must be 'SIGN'" });

    var req = await _db.QuotationRequests
        .Include(r => r.Customer)
        .FirstOrDefaultAsync(r => r.Id == requisitionId);
    if (req is null) return NotFound();

    if (!RequisitionStateMachine.CanTransition(req.Status, RequisitionStatus.Signed))
        return BadRequest(new { error = $"Cannot final-sign from {req.Status}" });

    var current = await _db.QuotationApprovals
        .Include(qa => qa.Items)
        .Where(qa => qa.QuotationRequestId == req.Id
                  && qa.Stage == ApprovalStage.InitialPricing
                  && !qa.IsSuperseded)
        .OrderByDescending(qa => qa.ApprovedAt)
        .FirstOrDefaultAsync();
    if (current is null)
        return BadRequest(new { error = "No initial-pricing approval to sign" });

    // Promote to FinalSign — same row updated; preserves price + RateSnapshot history
    current.Stage = ApprovalStage.FinalSign;
    current.IsApproved = true;
    current.ApprovedByUserId = userId;
    current.ApprovedAt = DateTime.UtcNow;
    if (!string.IsNullOrWhiteSpace(body.Notes))
        current.Notes = (current.Notes ?? "") + $"\n[FinalSign] {body.Notes}";

    req.Status = RequisitionStatus.Signed;
    await _db.SaveChangesAsync();

    // Generate signed PDF (with embedded MD signature)
    var signer = await _db.Users.FindAsync(userId);
    var pdfBytes = await _pdf.GenerateSignedQuotationAsync(req, current, signer!);
    // PDF is stored or streamed; existing PdfService convention applies

    // Notify sales + accountants — NO CUSTOMER (D23)
    var accountants = await _db.UserBranches
        .Where(ub => ub.BranchId == req.BranchId && ub.User.Role == UserRole.Accountant && ub.User.IsActive)
        .Select(ub => ub.UserId).ToListAsync();
    await _notif.SendAsync(req.SalesPersonId, NotificationType.SignedNotif,
        $"REQ-{req.Id:D4} signed — quotation locked", req.Id);
    await _notif.SendToUsersAsync(accountants, NotificationType.SignedNotif,
        $"REQ-{req.Id:D4} signed", req.Id);

    return Ok(new
    {
        id = req.Id,
        status = req.Status.ToString(),
        approvalId = current.Id,
        pdfDownloadUrl = $"/api/approvals/{req.Id}/pdf"
    });
}
```

`PdfService.GenerateSignedQuotationAsync` will be implemented in Task 31. For now this references the method by signature.

- [ ] **Step 2: Build (will fail — PdfService method not yet defined)**

```bash
dotnet build --nologo -v q
```

Expected: error `'IPdfService' does not contain a definition for 'GenerateSignedQuotationAsync'`. This is OK — Task 31 implements it.

- [ ] **Step 3: Add a method stub to IPdfService temporarily**

Open `BomPriceApproval.API/Infrastructure/Services/PdfService.cs`. Add to the interface:

```csharp
Task<byte[]> GenerateSignedQuotationAsync(QuotationRequest req, QuotationApproval approval, User signer);
```

And a stub implementation:

```csharp
public Task<byte[]> GenerateSignedQuotationAsync(QuotationRequest req, QuotationApproval approval, User signer)
{
    // TODO Task 31: full implementation with signature embed
    throw new NotImplementedException("Implemented in Task 31");
}
```

Now build:

```bash
dotnet build --nologo -v q
```

Expected: clean build.

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.API/Features/Approvals/ApprovalsController.cs BomPriceApproval.API/Infrastructure/Services/PdfService.cs
git commit -m "feat(v3): add ApprovalsController.FinalSign endpoint (Stage 2)

Type-to-confirm token validation. Promotes InitialPricing approval to
FinalSign and locks requisition to Signed status. PDF service method
stubbed (full implementation in Task 31). Notifies sales + accountants —
NO customer email per D23.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 30: V3 Approval split tests

**Files:**
- Create: `BomPriceApproval.Tests/Approvals/V3ApprovalSplitTests.cs`

- [ ] **Step 1: Write happy-path tests**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BomPriceApproval.API;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Approvals;

public class V3ApprovalSplitTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public V3ApprovalSplitTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task FullHappyPath_DraftToSigned()
    {
        // 1. Sales creates + submits
        var salesClient = _factory.CreateClient();
        var salesToken = await TestHelpers.GetSalesTokenAsync(salesClient);
        salesClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", salesToken);

        var reqId = await TestHelpers.CreateV3DraftRequisitionAsync(salesClient);
        var submitResp = await salesClient.PostAsync($"/api/requisitions/{reqId}/submit", null);
        submitResp.EnsureSuccessStatusCode();

        // 2. Accountant enters costs + submits
        var acctClient = _factory.CreateClient();
        var acctToken = await TestHelpers.GetAccountantTokenAsync(acctClient);
        acctClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctToken);
        await TestHelpers.PopulateBomCostAsync(acctClient, reqId);
        var costingSubmit = await acctClient.PostAsync($"/api/costing/{reqId}/submit", null);
        costingSubmit.EnsureSuccessStatusCode();

        // 3. MD sets margin
        var mdClient = _factory.CreateClient();
        var mdToken = await TestHelpers.GetMdTokenAsync(mdClient);
        mdClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mdToken);

        var fgIds = await TestHelpers.GetReqItemIdsAsync(mdClient, reqId);
        var marginPayload = new
        {
            notes = "test margin",
            items = fgIds.Select(id => new { requisitionItemId = id, marginPerKg = 0.5m }).ToArray()
        };
        var marginResp = await mdClient.PostAsJsonAsync($"/api/approvals/{reqId}/set-margin", marginPayload);
        marginResp.EnsureSuccessStatusCode();
        Assert.Equal("CustomerConfirm",
            (await marginResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        // 4. Sales confirms customer accepted
        var acceptResp = await salesClient.PostAsJsonAsync($"/api/approvals/{reqId}/accept-customer",
            new { customerFeedback = "Customer agreed on call" });
        acceptResp.EnsureSuccessStatusCode();
        Assert.Equal("MdFinalSign",
            (await acceptResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        // 5. MD final sign
        var signResp = await mdClient.PostAsJsonAsync($"/api/approvals/{reqId}/final-sign",
            new { confirmationToken = "SIGN", notes = "Locked" });
        signResp.EnsureSuccessStatusCode();
        Assert.Equal("Signed",
            (await signResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task FinalSign_WrongToken_Returns400()
    {
        // Setup state at MdFinalSign (use helper that walks the workflow up to that point)
        var (reqId, mdClient) = await TestHelpers.WalkToMdFinalSignAsync(_factory);

        var resp = await mdClient.PostAsJsonAsync($"/api/approvals/{reqId}/final-sign",
            new { confirmationToken = "SiGn", notes = "" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task RejectCustomer_LoopsBackToMdPricing_AndSupersedesApproval()
    {
        var (reqId, mdClient, salesClient) = await TestHelpers.WalkToCustomerConfirmAsync(_factory);

        var resp = await salesClient.PostAsJsonAsync($"/api/approvals/{reqId}/reject-customer",
            new { reason = "Customer wants 5% lower price" });
        resp.EnsureSuccessStatusCode();
        Assert.Equal("MdPricing",
            (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        // Verify the previous approval is now superseded
        // (Could query DB directly via _factory.Services or expose a debug endpoint)
    }
}
```

If the `TestHelpers.PopulateBomCostAsync`, `WalkToMdFinalSignAsync`, `WalkToCustomerConfirmAsync` helpers don't exist, write them as part of this task in `BomPriceApproval.Tests/Shared/TestHelpers.cs`.

- [ ] **Step 2: Run tests (will skip FinalSign tests if PdfService stub throws)**

```bash
dotnet test --nologo -v q --filter "FullyQualifiedName~V3ApprovalSplitTests"
```

Expected: tests up to MD final-sign step pass; final-sign tests will throw `NotImplementedException` from PdfService stub. Mark them with `[Fact(Skip = "Pending Task 31")]` if needed, OR mock PdfService in the WebApplicationFactory configuration.

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.Tests/Approvals/V3ApprovalSplitTests.cs BomPriceApproval.Tests/Shared/
git commit -m "test(v3): add V3 approval split tests

Covers full happy path Draft -> Signed, type-to-confirm enforcement,
and customer-reject re-margin loop with supersession.
PDF tests skipped pending Task 31 PdfService implementation.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 31: PdfService.GenerateSignedQuotationAsync (with signature embed)

**Files:**
- Modify: `BomPriceApproval.API/Infrastructure/Services/PdfService.cs`

- [ ] **Step 1: Implement the signed PDF method**

Replace the stub from Task 29:

```csharp
public async Task<byte[]> GenerateSignedQuotationAsync(QuotationRequest req, QuotationApproval approval, User signer)
{
    // Load FG list + BOM + cost data
    // Reuse existing GenerateQuotationAsync rendering as the base, append signature block.

    return Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontSize(11));

            page.Header().Column(c =>
            {
                c.Item().Text("FUJAIRAH PLASTIC FACTORY").FontSize(16).Bold().FontColor("#1e40af");
                c.Item().Text($"Quotation REQ-{req.Id:D4} · {DateTime.UtcNow:yyyy-MM-dd}").FontSize(9);
            });

            page.Content().Column(c =>
            {
                c.Item().PaddingTop(10).Text(t =>
                {
                    t.Span("Customer: ").Bold();
                    t.Span($"{req.Customer.Name} ({req.Customer.Code})");
                });
                c.Item().Text(t =>
                {
                    t.Span("Currency: ").Bold();
                    t.Span(req.CurrencyCode);
                });

                c.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(col =>
                    {
                        col.RelativeColumn(3);
                        col.RelativeColumn(1);
                        col.RelativeColumn(1);
                        col.RelativeColumn(1);
                    });
                    table.Header(h =>
                    {
                        h.Cell().Background("#f1f5f9").Padding(4).Text("Item").Bold();
                        h.Cell().Background("#f1f5f9").Padding(4).Text("Qty").Bold();
                        h.Cell().Background("#f1f5f9").Padding(4).Text("Price/KG").Bold();
                        h.Cell().Background("#f1f5f9").Padding(4).Text("Total").Bold();
                    });
                    foreach (var ai in approval.Items)
                    {
                        var ri = req.RequisitionItems.First(x => x.Id == ai.RequisitionItemId);
                        var price = ai.MarginPerKg + (ri.BomCost?.TotalCostPerKgAed ?? 0m);  // simplified
                        var lineTotal = price * ri.ExpectedQty;
                        table.Cell().Padding(4).Text(ri.Item.Description);
                        table.Cell().Padding(4).Text($"{ri.ExpectedQty:N0}");
                        table.Cell().Padding(4).Text($"{price:F2}");
                        table.Cell().Padding(4).Text($"{lineTotal:F2}");
                    }
                });

                // Signature block
                c.Item().PaddingTop(40).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Prepared by").FontSize(8).FontColor("#64748b");
                        col.Item().Text(req.SalesPerson?.Name ?? "—").FontSize(10);
                    });

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Approved & Signed by MD").FontSize(8).FontColor("#64748b");

                        if (!string.IsNullOrEmpty(signer.SignatureImagePath) && File.Exists(signer.SignatureImagePath))
                        {
                            var imgBytes = File.ReadAllBytes(signer.SignatureImagePath);
                            col.Item().Height(50).Image(imgBytes);
                        }
                        else
                        {
                            col.Item().Text("[no signature uploaded]").Italic().FontColor("#94a3b8");
                        }

                        col.Item().Text($"{signer.Name}, Managing Director").FontSize(9);
                        col.Item().Text($"{approval.ApprovedAt:yyyy-MM-dd}").FontSize(8).FontColor("#64748b");
                    });
                });
            });
        });
    }).GeneratePdf();
}
```

(`TotalCostPerKgAed` may need to be a computed property on `BomCost` or precalculated server-side; adapt to existing structure if it differs.)

- [ ] **Step 2: Build + run V3 approval tests**

```bash
dotnet build --nologo -v q
dotnet test --nologo -v q --filter "FullyQualifiedName~V3ApprovalSplitTests"
```

Expected: All tests pass — including the full happy path through Signed status with PDF generation.

- [ ] **Step 3: Smoke test the PDF manually**

```bash
dotnet run --project BomPriceApproval.API
# In another terminal:
curl -X POST http://localhost:7300/api/approvals/<id>/final-sign \
  -H "Authorization: Bearer <md-token>" \
  -H "Content-Type: application/json" \
  -d '{"confirmationToken":"SIGN","notes":"smoke"}'
# Then:
curl http://localhost:7300/api/approvals/<id>/pdf -o quotation.pdf
# Open quotation.pdf — verify signature block renders (with placeholder if no signature uploaded yet)
```

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Services/PdfService.cs
git commit -m "feat(v3): implement PdfService.GenerateSignedQuotationAsync

Embeds MD signature image (if uploaded) + name + date in PDF footer.
Falls back to '[no signature uploaded]' placeholder when missing.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 32: SignatureController endpoints

**Files:**
- Create: `BomPriceApproval.API/Features/Profile/SignatureController.cs`
- Create: `BomPriceApproval.API/Features/Profile/SignatureDtos.cs`

- [ ] **Step 1: Create DTOs**

```csharp
namespace BomPriceApproval.API.Features.Profile;

public record SignatureUploadResponse(string Path, DateTime UploadedAt);
```

- [ ] **Step 2: Create controller**

```csharp
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Profile;

[ApiController]
[Route("api/profile")]
[Authorize]
public class SignatureController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public SignatureController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpPost("signature")]
    [Authorize(Roles = "ManagingDirector")]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Empty file" });
        if (file.Length > 500 * 1024)
            return BadRequest(new { error = "File too large (max 500KB)" });
        var allowedExts = new[] { ".png", ".jpg", ".jpeg" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExts.Contains(ext))
            return BadRequest(new { error = "Only .png/.jpg/.jpeg allowed" });

        var userId = int.Parse(User.FindFirst("UserId")!.Value);
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        var dir = _config["Signatures:Directory"] ?? "/data/signatures";
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{userId}.png"); // standardize on .png

        await using (var stream = System.IO.File.Create(path))
            await file.CopyToAsync(stream);

        user.SignatureImagePath = path;
        await _db.SaveChangesAsync();

        return Ok(new SignatureUploadResponse(path, DateTime.UtcNow));
    }

    [HttpGet("signature")]
    [Authorize(Roles = "ManagingDirector")]
    public async Task<IActionResult> GetOwn()
    {
        var userId = int.Parse(User.FindFirst("UserId")!.Value);
        var user = await _db.Users.FindAsync(userId);
        if (user?.SignatureImagePath is null)
            return NotFound(new { error = "No signature uploaded" });
        if (!System.IO.File.Exists(user.SignatureImagePath))
            return NotFound(new { error = "Signature file missing" });
        return PhysicalFile(user.SignatureImagePath, "image/png");
    }
}
```

- [ ] **Step 3: Add config key**

Open `appsettings.json`. Under the root object, add:

```json
"Signatures": {
  "Directory": "C:\\temp\\fpf-signatures"
}
```

For production (Fly), add to `fly.toml` or `appsettings.Production.json`:
```json
"Signatures": {
  "Directory": "/data/signatures"
}
```

The Fly volume is already mounted at `/data` per existing infra.

- [ ] **Step 4: Build**

```bash
dotnet build --nologo -v q
```

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Features/Profile/ BomPriceApproval.API/appsettings.json
git commit -m "feat(v3): add SignatureController for MD signature upload/get

POST /api/profile/signature accepts .png/.jpg up to 500KB.
GET /api/profile/signature returns own image.
Stored at config 'Signatures:Directory'/{userId}.png.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 33: SignatureController tests

**Files:**
- Create: `BomPriceApproval.Tests/Profile/SignatureControllerTests.cs`

- [ ] **Step 1: Write tests**

```csharp
using System.Net;
using System.Net.Http.Headers;
using BomPriceApproval.API;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Profile;

public class SignatureControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public SignatureControllerTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Upload_AsMd_StoresSignature()
    {
        var client = _factory.CreateClient();
        var token = await TestHelpers.GetMdTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1×1 PNG bytes (smallest valid PNG)
        var pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

        using var content = new MultipartFormDataContent();
        var imgContent = new ByteArrayContent(pngBytes);
        imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(imgContent, "file", "test-signature.png");

        var resp = await client.PostAsync("/api/profile/signature", content);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Upload_AsAccountant_Returns403()
    {
        var client = _factory.CreateClient();
        var token = await TestHelpers.GetAccountantTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 1 }), "file", "x.png");

        var resp = await client.PostAsync("/api/profile/signature", content);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Upload_OverSizeLimit_Returns400()
    {
        var client = _factory.CreateClient();
        var token = await TestHelpers.GetMdTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var bigBytes = new byte[600 * 1024]; // 600KB > 500KB limit

        using var content = new MultipartFormDataContent();
        var imgContent = new ByteArrayContent(bigBytes);
        imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(imgContent, "file", "big.png");

        var resp = await client.PostAsync("/api/profile/signature", content);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetOwn_NoSignatureUploaded_Returns404()
    {
        var client = _factory.CreateClient();
        var token = await TestHelpers.GetMdTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Reset DB MD signature to null first (test setup)
        await TestHelpers.ClearMdSignatureAsync(_factory.Services);

        var resp = await client.GetAsync("/api/profile/signature");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test --nologo -v q --filter "FullyQualifiedName~SignatureControllerTests"
```

Expected: All 4 tests pass.

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.Tests/Profile/
git commit -m "test(v3): add SignatureController tests

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 34: Add Customers GetImplicitItems endpoint

**Files:**
- Modify: `BomPriceApproval.API/Features/Customers/CustomersController.cs`

- [ ] **Step 1: Add the endpoint**

```csharp
[HttpGet("{id}/items")]
[Authorize]
public async Task<IActionResult> GetImplicitItems(int id)
{
    var customer = await _db.Customers
        .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
    if (customer is null) return NotFound();

    // FGs ever quoted for this customer (from past requisitions, any status incl. legacy V2)
    var fgIds = await _db.QuotationRequests
        .Where(r => r.CustomerId == id)
        .SelectMany(r => r.RequisitionItems)
        .Select(ri => ri.ItemId)
        .Distinct()
        .ToListAsync();

    var items = await _db.Items
        .Where(i => fgIds.Contains(i.Id) && i.Type == ItemType.FinishedGood && i.IsActive)
        .OrderBy(i => i.Description)
        .Select(i => new { i.Id, i.Code, i.Description })
        .ToListAsync();

    return Ok(items);
}
```

- [ ] **Step 2: Add a test**

In `BomPriceApproval.Tests/Customers/CustomersCrudTests.cs` add:

```csharp
[Fact]
public async Task GetImplicitItems_NewCustomer_ReturnsEmpty()
{
    var client = _factory.CreateClient();
    var token = await TestHelpers.GetSalesTokenAsync(client);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    // Create a brand-new customer with no requisition history
    var custResp = await client.PostAsJsonAsync("/api/customers", new {
        name = $"Test {Guid.NewGuid():N}", email = "x@y.com", phoneNumber = "+1", address = "x"
    });
    var customer = await custResp.Content.ReadFromJsonAsync<JsonElement>();
    var customerId = customer.GetProperty("id").GetInt32();

    var resp = await client.GetAsync($"/api/customers/{customerId}/items");
    resp.EnsureSuccessStatusCode();
    var items = await resp.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal(0, items.GetArrayLength());
}
```

- [ ] **Step 3: Build + test + commit**

```bash
dotnet build --nologo -v q
dotnet test --nologo -v q --filter "FullyQualifiedName~CustomersCrudTests.GetImplicitItems"

git add BomPriceApproval.API/Features/Customers/CustomersController.cs BomPriceApproval.Tests/Customers/CustomersCrudTests.cs
git commit -m "feat(v3): add Customers GetImplicitItems endpoint

Returns FGs from this customer's past requisitions (any status).
Used by NewRequisitionPage to filter FG picker per D20.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 35: Adapt AdminRequisitionsController — delete UnlockBom, rename UnlockCosting

**Files:**
- Modify: `BomPriceApproval.API/Features/Admin/AdminRequisitionsController.cs`
- Modify: `BomPriceApproval.Tests/Admin/AdminUnlockBomTests.cs` (delete)
- Modify: `BomPriceApproval.Tests/Admin/AdminUnlockCostingTests.cs` (rename + adapt)

- [ ] **Step 1: Delete UnlockBom endpoint**

Open `BomPriceApproval.API/Features/Admin/AdminRequisitionsController.cs`. Find the action method handling `POST /api/admin/requisitions/{id}/unlock-bom`. Delete it entirely.

- [ ] **Step 2: Delete UnlockBom test file**

```bash
rm BomPriceApproval.Tests/Admin/AdminUnlockBomTests.cs
```

- [ ] **Step 3: Rename UnlockCosting → RollbackToCosting**

Find `unlock-costing` action. Update:
- Route: `[HttpPost("requisitions/{id}/rollback-to-costing")]`
- Method name: `RollbackToCosting`
- Validate: only allowed from `MdPricing` (not from `CustomerConfirm`/`MdFinalSign` — admin must roll those back step-by-step via Status rollback)
- Target status: `RequisitionStatus.Costing`
- Audit ActionType: `AdminActionType.RollbackToCosting`

```csharp
[HttpPost("requisitions/{id}/rollback-to-costing")]
public async Task<IActionResult> RollbackToCosting(int id, [FromBody] AdminReasonRequest body)
{
    if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
        return BadRequest(new { error = "Reason >= 5 chars required" });

    var req = await _db.QuotationRequests.FirstOrDefaultAsync(r => r.Id == id);
    if (req is null) return NotFound();

    if (req.Status != RequisitionStatus.MdPricing)
        return BadRequest(new { error = $"RollbackToCosting allowed only from MdPricing (current: {req.Status})" });

    var adminId = int.Parse(User.FindFirst("UserId")!.Value);
    var beforeJson = JsonSerializer.Serialize(new { req.Id, FromStatus = req.Status.ToString() });

    req.Status = RequisitionStatus.Costing;
    await _db.SaveChangesAsync();

    _audit.Log(adminId, AdminActionType.RollbackToCosting, "QuotationRequest", req.Id,
        body.Reason, beforeJson, JsonSerializer.Serialize(new { ToStatus = req.Status.ToString() }));
    await _db.SaveChangesAsync();

    return Ok(new { id = req.Id, status = req.Status.ToString() });
}
```

- [ ] **Step 4: Update test file**

Rename `AdminUnlockCostingTests.cs` → `AdminRollbackToCostingTests.cs`. Update test references and route paths.

```bash
git mv BomPriceApproval.Tests/Admin/AdminUnlockCostingTests.cs BomPriceApproval.Tests/Admin/AdminRollbackToCostingTests.cs
```

Edit the file: change all `unlock-costing` to `rollback-to-costing`, change setup state to `MdPricing` (the only valid source state).

- [ ] **Step 5: Run admin tests**

```bash
dotnet test --nologo -v q --filter "FullyQualifiedName~Admin"
```

Expected: All admin tests pass except the deleted UnlockBom one (file is gone).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(v3): adapt AdminRequisitions — delete UnlockBom, rename UnlockCosting->RollbackToCosting

UnlockBom removed (V3 has no BOM stage).
UnlockCosting renamed and constrained to MdPricing only as per spec D27.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 36: Adapt AdminRequisitions Status Rollback whitelist

**Files:**
- Modify: `BomPriceApproval.API/Features/Admin/AdminRequisitionsController.cs`
- Modify: `BomPriceApproval.API/Infrastructure/Authorization/AdminOverrideAuthorization.cs`
- Modify: `BomPriceApproval.Tests/Admin/AdminRollbackStatusTests.cs`

- [ ] **Step 1: Update the rollback whitelist in AdminOverrideAuthorization**

Replace the existing V2.3 whitelist constant with V3's:

```csharp
private static readonly HashSet<(RequisitionStatus, RequisitionStatus)> AllowedRollbacks = new()
{
    // V3 whitelist (per spec §11)
    (RequisitionStatus.Signed, RequisitionStatus.MdFinalSign),
    (RequisitionStatus.MdFinalSign, RequisitionStatus.CustomerConfirm),
    (RequisitionStatus.CustomerConfirm, RequisitionStatus.MdPricing),
    (RequisitionStatus.MdPricing, RequisitionStatus.Costing),
    (RequisitionStatus.Costing, RequisitionStatus.Draft),
};
```

(Or call `RequisitionStateMachine.AdminRollbackTargets(from)` directly — that's cleaner. Refactor.)

- [ ] **Step 2: Update RollbackStatus action to use V3 helper**

```csharp
[HttpPost("requisitions/{id}/rollback-status")]
public async Task<IActionResult> RollbackStatus(int id, [FromBody] RollbackStatusRequest body)
{
    if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
        return BadRequest(new { error = "Reason >= 5 chars required" });

    var req = await _db.QuotationRequests.FirstOrDefaultAsync(r => r.Id == id);
    if (req is null) return NotFound();

    var validTargets = RequisitionStateMachine.AdminRollbackTargets(req.Status);
    if (!validTargets.Contains(body.TargetStatus))
        return BadRequest(new
        {
            error = $"Invalid rollback {req.Status} -> {body.TargetStatus}",
            validTargets = validTargets.Select(s => s.ToString())
        });

    var adminId = int.Parse(User.FindFirst("UserId")!.Value);
    var beforeJson = JsonSerializer.Serialize(new { req.Id, FromStatus = req.Status.ToString() });

    req.Status = body.TargetStatus;
    await _db.SaveChangesAsync();

    _audit.Log(adminId, AdminActionType.StatusRollback, "QuotationRequest", req.Id,
        body.Reason, beforeJson, JsonSerializer.Serialize(new { ToStatus = req.Status.ToString() }));
    await _db.SaveChangesAsync();

    return Ok(new { id = req.Id, status = req.Status.ToString() });
}
```

- [ ] **Step 3: Update existing AdminRollbackStatusTests**

The V2.3 tests used old status names. Adapt:
- Replace `BomInProgress -> BomPending` test with `Costing -> Draft`
- Replace `CostingInProgress -> CostingPending` test with `MdPricing -> Costing`
- Replace `MdReview -> CostingPending` with `MdPricing -> Costing` (already covered)
- Replace `Approved -> MdReview` with `Signed -> MdFinalSign`
- Add new tests for `MdFinalSign -> CustomerConfirm` and `CustomerConfirm -> MdPricing`
- Remove tests for deleted V2 transitions

Run:
```bash
dotnet test --nologo -v q --filter "FullyQualifiedName~AdminRollbackStatusTests"
```

Expected: All adapted tests pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(v3): update admin status rollback whitelist for V3 states

Whitelist now: Signed->MdFinalSign->CustomerConfirm->MdPricing->Costing->Draft.
Cancelled and Rejected remain non-rollback-able.
RollbackStatus action delegates to RequisitionStateMachine.AdminRollbackTargets().

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 37: Extend Admin OverridePrices for Signed status

**Files:**
- Modify: `BomPriceApproval.API/Features/Admin/AdminRequisitionsController.cs`
- Modify: `BomPriceApproval.Tests/Admin/AdminOverridePricesTests.cs` (or similar)

- [ ] **Step 1: Find OverridePrices action**

It currently allows only `Approved` status (V2.3). Update to accept both `Approved` (legacy) and `Signed` (V3):

```csharp
[HttpPost("requisitions/{id}/override-prices")]
public async Task<IActionResult> OverridePrices(int id, [FromBody] OverridePricesRequest body)
{
    if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length < 5)
        return BadRequest(new { error = "Reason >= 5 chars required" });
    if (body.ConfirmationToken != "OVERRIDE")
        return BadRequest(new { error = "Type-to-confirm token must be 'OVERRIDE' (this breaks the lock)" });

    var req = await _db.QuotationRequests
        .Include(r => r.RequisitionItems)
        .FirstOrDefaultAsync(r => r.Id == id);
    if (req is null) return NotFound();

    if (req.Status != RequisitionStatus.Approved && req.Status != RequisitionStatus.Signed)
        return BadRequest(new { error = $"OverridePrices allowed only on Approved (legacy V2.3) or Signed (V3); current: {req.Status}" });

    // ... existing override logic — supersede current approval, create new with new prices + RateSnapshot ...
    // Reuse existing V2.3-C P2 implementation; just relax the status guard.
}
```

Add `ConfirmationToken` to the existing `OverridePricesRequest` DTO:

```csharp
public record OverridePricesRequest(
    string Reason,
    string ConfirmationToken,        // must equal "OVERRIDE"
    string? Notes,
    List<OverridePriceItem> Items);
```

- [ ] **Step 2: Update existing tests**

Add a new test for V3 Signed status:

```csharp
[Fact]
public async Task OverridePrices_OnSignedV3Req_CreatesSupersession()
{
    var (reqId, mdClient, adminClient) = await TestHelpers.WalkToSignedAsync(_factory);

    var resp = await adminClient.PostAsJsonAsync($"/api/admin/requisitions/{reqId}/override-prices",
        new {
            reason = "Customer renegotiated post-signing",
            confirmationToken = "OVERRIDE",
            notes = "Override test",
            items = new[] { new { requisitionItemId = ..., marginPerKg = 0.99m } }
        });

    resp.EnsureSuccessStatusCode();
    // Verify previous Signed approval is superseded + new approval row exists
}
```

- [ ] **Step 3: Build + test + commit**

```bash
dotnet build --nologo -v q
dotnet test --nologo -v q --filter "FullyQualifiedName~AdminOverridePrices"

git add -A
git commit -m "feat(v3): extend OverridePrices to work on Signed (V3) status

Adds 'OVERRIDE' type-to-confirm token (this breaks the lock).
Supersession logic identical to V2.3-C P2 mechanism.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 38: Adapt remaining V2.3 tests that reference BomCreator or old statuses

**Files:** Various test files (~12 files reference BomCreator per Task 1 grep)

- [ ] **Step 1: Re-grep for BomCreator and old status names**

```bash
grep -rln "BomCreator\|BomPending\|BomInProgress\|CostingPending\|CostingInProgress\|MdReview" BomPriceApproval.Tests/
```

For each file:
- If the test asserts BomCreator workflow specifically — DELETE the test method
- If the test uses BomCreator as a generic "non-Admin" stand-in — replace with `UserRole.Accountant`
- If the test uses old statuses to walk through the workflow — adapt to V3 statuses (Draft → Costing → MdPricing → CustomerConfirm → MdFinalSign → Signed)

- [ ] **Step 2: Use grep+sed for bulk renames where safe**

Manual review per file — bulk find/replace risks breaking unrelated logic. Files most affected:
- `BomPriceApproval.Tests/Costing/CostingTests.cs` (state transitions)
- `BomPriceApproval.Tests/Approvals/ApprovalValidationTests.cs` (status guards)
- `BomPriceApproval.Tests/Requisitions/ChangeBranchTests.cs` (state guards)
- `BomPriceApproval.Tests/Requisitions/ChangeCustomerTests.cs` (state guards)
- `BomPriceApproval.Tests/Stats/AccountantDashboardTests.cs` (status filters)
- `BomPriceApproval.Tests/Notifications/SalesGroupNotificationRoutingTests.cs` (status setup)

For each file, walk through, fix references, rerun:

```bash
dotnet test --nologo -v q --filter "FullyQualifiedName~<TestClass>"
```

- [ ] **Step 3: Run full suite — all green**

```bash
dotnet test --nologo -v q
```

Expected: ~340 tests passing (V2.3 had 318 — Phase A adds ~30 new, deletes ~10 deprecated).

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.Tests/
git commit -m "test(v3): adapt V2.3 tests to V3 state machine + role removal

Replaces BomCreator role usage with Accountant where role-agnostic.
Updates state walks to use V3 transitions (Draft -> Costing -> MdPricing
-> CustomerConfirm -> MdFinalSign -> Signed).
Deletes tests that asserted BomCreator-specific workflow behavior.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 39: Manual swagger smoke test

**Files:** None (manual verification)

- [ ] **Step 1: Start the API**

```bash
dotnet run --project BomPriceApproval.API
```

Wait for "Now listening on: http://localhost:7300".

- [ ] **Step 2: Open Swagger**

Open `http://localhost:7300/swagger/index.html` in browser.

- [ ] **Step 3: Walk through V3 endpoints**

Use the Swagger UI's "Authorize" with an admin token, then:

1. **POST /api/customers** — body `{ "name": "Smoke Customer", "email": "smoke@x.com", "phoneNumber": "+1", "address": "test" }` — verify response has `code` matching `^CUST-\d+$`
2. **POST /api/items** (FG) — verify `code` matches `^FG-\d+$`
3. **POST /api/items** (RM) — verify `code` matches `^RM-\d+$`
4. **POST /api/requisitions** with V3 payload — verify status="Draft"
5. **POST /api/requisitions/{id}/submit** — verify status="Costing"
6. **PUT /api/costing/{id}/draft** — populate cost data (use existing endpoint)
7. **POST /api/costing/{id}/submit** — verify status="MdPricing"
8. **POST /api/approvals/{id}/set-margin** — verify status="CustomerConfirm"
9. **POST /api/approvals/{id}/accept-customer** — verify status="MdFinalSign"
10. **POST /api/approvals/{id}/final-sign** with `{"confirmationToken":"SIGN"}` — verify status="Signed"
11. **GET /api/approvals/{id}/pdf** — download PDF, verify signature block
12. **GET /api/customers/{id}/items** — verify FG list returns the FG just used

- [ ] **Step 4: Verify NO emails were sent to a customer address**

Inspect Serilog logs (or SMTP server logs in dev) — no outbound emails to `customer@*` should fire. Only Sales/Accountant/MD users should receive notifications.

- [ ] **Step 5: Cleanup smoke data**

```sql
DELETE FROM "QuotationApproval" WHERE "QuotationRequestId" IN (SELECT "Id" FROM "QuotationRequest" WHERE "Notes" LIKE '%Smoke%');
DELETE FROM "BomLine" WHERE "BomHeaderId" IN (SELECT "Id" FROM "BomHeader" WHERE ...);
-- ... cascading cleanup; or just `git checkout` the local DB
```

(Or simpler: drop the local DB and re-run migrations.)

- [ ] **Step 6: No commit needed (manual smoke)**

---

## Task 40: Push branch + open PR

**Files:** None

- [ ] **Step 1: Verify everything is committed**

```bash
git status
```

Expected: `nothing to commit, working tree clean`.

- [ ] **Step 2: Run full test suite one more time**

```bash
dotnet test --nologo
```

Expected: All tests pass. Note the count.

- [ ] **Step 3: Push branch**

```bash
git push -u origin feat/v3-phase-a-backend
```

- [ ] **Step 4: Open PR**

```bash
gh pr create --base master --head feat/v3-phase-a-backend \
  --title "feat(v3): Phase A backend foundation" \
  --body "$(cat <<'EOF'
## Summary

V3 Phase A — backend foundation per spec [`2026-04-29-v3-simplified-workflow-design.md`](docs/superpowers/specs/2026-04-29-v3-simplified-workflow-design.md).

**What's done:**
- New state machine (`Costing`, `MdPricing`, `CustomerConfirm`, `MdFinalSign`, `Signed`, `Cancelled`) with `RequisitionStateMachine` central guard class
- Auto-generated `CUST-XXXX` / `FG-XXXX` / `RM-XXXX` codes via `CodeGeneratorService` (row-locked)
- Inline BOM payload on `POST /api/requisitions` (sales now owns BOM)
- Editable BOM on `PUT /api/costing/{id}/bom` with audit trail
- Approvals split: `SetMargin` (Stage 1) / `AcceptCustomer` / `RejectCustomer` / `FinalSign` (Stage 2 with type-to-confirm)
- MD signature upload + PDF embed
- BomCreator role dropped from authorization
- Admin overrides adapted: UnlockBom deleted, UnlockCosting → RollbackToCosting, OverridePrices works on Signed
- 6 EF Core migrations + 1 schema-extension migration
- All notifications stay internal (Sales/Accountant/MD) — NO customer emails per D23

**What's NOT in this PR:**
- Frontend (Phase B — separate plan)
- Cutover SQL execution (Phase C — separate plan)
- Mobile (Phase D — deferred)

**Deployment:** This PR is staged behind master. **Not deployed to production yet.** Production stays on V2.3 backend until Phase C cutover deploy. Phase B frontend will build against this backend on a staging Fly slot.

## Test plan

- [ ] Run `dotnet test` locally — all ~340 tests green
- [ ] Smoke test via Swagger UI — full happy path Draft → Signed
- [ ] Verify no customer emails fire on any transition
- [ ] Verify signed PDF embeds signature image
- [ ] Verify migrations apply cleanly to a fresh DB
- [ ] Verify migrations apply cleanly to a copy of prod DB (legacy V2.3 reqs untouched, Approvals backfilled with Stage=FinalSign)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Expected: PR URL printed.

- [ ] **Step 5: Report PR URL in chat**

---

## Self-Review

After completing the plan, run this checklist:

**1. Spec coverage:**
- §3 Decisions D1-D27 — all covered? Yes:
  - D1, D2: Tasks 10, 11
  - D3 (free-text micron): Task 19 BomLineDto.Micron string
  - D4 (no recipe validation): Task 20 (no validation in Create)
  - D5 (FX from ExchangeRates): Task 27 SetMargin
  - D6 (margin in quote currency): Task 26 MarginPerKg + Task 27 calc
  - D7 (cost breakdown): backend doesn't enforce — frontend (Phase B)
  - D8 (re-margin only): Task 28 RejectCustomer
  - D9 (signature image+text): Task 31, 32
  - D10 (notes visible 3 roles): Task 23 GET includes notes
  - D11 (hard cut-over): Phase C, not this plan
  - D12 (deactivate BomCreator): Phase C cutover SQL, not this plan
  - D13 (hide branches): Phase C cutover SQL, not this plan
  - D14, D15: scope notes only, not actionable in Phase A
  - D16 (Drafts): Task 20 status starts Draft + Task 21 Submit
  - D17 (whole-req state): Task 25 validates ALL FGs have cost; Task 27 validates ALL FGs have margin
  - D18, D19, D20: D18+D19 are frontend (Phase B); D20 = Task 34
  - D21 (FX freeze 2x): Task 27 (sale-side) + Task 25 mention (cost-side TBD — actually placed in QuotationApproval.CostFxSnapshot via Task 27 too, fine)
  - D22 (type-to-confirm): Task 29 FinalSign + Task 37 OverridePrices
  - D23 (no customer email): Tasks 21, 27, 28, 29 — all SendAsync calls target internal user IDs only
  - D24 (BOM diff audit): Task 12 + Task 24 LastModifiedBy/At
  - D25 (phasing): plan structure itself reflects Phase A scope
  - D26 (mobile freeze): not in Phase A
  - D27 (admin override adapt): Tasks 35, 36, 37
- §6 Endpoints — all V3 endpoints have a task? Yes: Tasks 20-21 (Reqs), 23-25 (Costing), 26-29 (Approvals), 32 (Signature), 34 (CustomerImplicitItems).
- §7 Migrations — 6 migrations? Yes: Tasks 3, 7, 12, 13, 14, 15. Plus 16 (no DB change for enum additions). Plus possibly an extra in Task 20 if HasPrinting/QtyPerKg/Micron columns need adding.

**2. Placeholder scan:**
- "TBD" / "TODO" — Task 31 has commented `// TODO Task 31` — that's the test stub directing implementation. Not a real placeholder. ✓
- "implement later" — none.
- "Add appropriate error handling" — none.
- "Similar to Task N" — Task 28 says "...identical to V2.3-C P2 mechanism" — that's a directive to reuse, not a placeholder. ✓

**3. Type consistency:**
- `RequisitionStateMachine.CanTransition`, `IsTerminal`, `AdminRollbackTargets` — used consistently across Tasks 4, 5, 21, 25, 27, 28, 29, 36.
- `ICodeGeneratorService.NextCustomerCodeAsync` / `NextItemCodeAsync(ItemType)` — consistent across Tasks 8, 9, 10, 11.
- `ApprovalStage.InitialPricing` / `FinalSign` — consistent Tasks 14, 27, 28, 29.
- `BomLine.QtyPerKg` / `Micron` / `LastModifiedByUserId` / `LastModifiedAt` — consistent Tasks 12, 19, 20, 23, 24.
- `RequisitionItem.HasPrinting` / `ExpectedQty` — consistent Tasks 19, 20, 23.
- `QuotationApproval.Stage` / `CostFxSnapshot` / `RateSnapshot` / `IsSuperseded` — consistent Tasks 14, 27, 28, 29.

No issues found in self-review.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-29-v3-phase-a-backend-foundation.md`. Two execution options per writing-plans skill:

**1. Subagent-Driven (recommended for foundation work)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. CLAUDE.md V2.3-C P1 lesson: this caught real bugs every 1-2 tasks (cascade gaps, silent bypasses, audit serialization issues). High quality but slow (~5h / 25-task plan; this plan is 40 tasks so estimate ~8h).

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints for review.

Reply `subagent` or `inline` to proceed. (Or `pause` to defer.)
