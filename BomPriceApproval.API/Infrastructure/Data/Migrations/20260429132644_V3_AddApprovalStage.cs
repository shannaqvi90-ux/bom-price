using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class V3_AddApprovalStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: add column nullable so legacy rows aren't constrained yet
            migrationBuilder.AddColumn<int>(
                name: "Stage",
                table: "QuotationApprovals",
                type: "integer",
                nullable: true);

            // Step 2: backfill legacy V2.3 approvals to FinalSign (1)
            migrationBuilder.Sql(@"UPDATE ""QuotationApprovals"" SET ""Stage"" = 1 WHERE ""Stage"" IS NULL;");

            // Step 3: enforce non-null + default for new rows
            migrationBuilder.AlterColumn<int>(
                name: "Stage",
                table: "QuotationApprovals",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            // Step 4: cost-side FX snapshot
            migrationBuilder.AddColumn<decimal>(
                name: "CostFxSnapshot",
                table: "QuotationApprovals",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Stage", table: "QuotationApprovals");
            migrationBuilder.DropColumn(name: "CostFxSnapshot", table: "QuotationApprovals");
        }
    }
}
