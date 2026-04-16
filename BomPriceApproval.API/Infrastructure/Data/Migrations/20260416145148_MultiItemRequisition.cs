using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultiItemRequisition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create RequisitionItems table FIRST (before dropping columns we need)
            migrationBuilder.CreateTable(
                name: "RequisitionItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuotationRequestId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ExpectedQty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequisitionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequisitionItems_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RequisitionItems_QuotationRequests_QuotationRequestId",
                        column: x => x.QuotationRequestId,
                        principalTable: "QuotationRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequisitionItems_ItemId",
                table: "RequisitionItems",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RequisitionItems_QuotationRequestId",
                table: "RequisitionItems",
                column: "QuotationRequestId");

            // 2. Migrate existing data: create a RequisitionItem for each QuotationRequest
            migrationBuilder.Sql(@"
                INSERT INTO ""RequisitionItems"" (""QuotationRequestId"", ""ItemId"", ""ExpectedQty"", ""SortOrder"")
                SELECT ""Id"", ""ItemId"", ""ExpectedQty"", 1
                FROM ""QuotationRequests""
                WHERE ""ItemId"" IS NOT NULL;
            ");

            // 3. Update BomHeaders to point to the new RequisitionItem IDs
            migrationBuilder.Sql(@"
                UPDATE ""BomHeaders"" bh
                SET ""QuotationRequestId"" = ri.""Id""
                FROM ""RequisitionItems"" ri
                WHERE ri.""QuotationRequestId"" = bh.""QuotationRequestId"";
            ");

            // 4. Drop old FKs and indexes
            migrationBuilder.DropForeignKey(
                name: "FK_BomHeaders_Items_ItemId",
                table: "BomHeaders");

            migrationBuilder.DropForeignKey(
                name: "FK_BomHeaders_QuotationRequests_QuotationRequestId",
                table: "BomHeaders");

            migrationBuilder.DropForeignKey(
                name: "FK_QuotationRequests_Items_ItemId",
                table: "QuotationRequests");

            migrationBuilder.DropIndex(
                name: "IX_QuotationRequests_ItemId",
                table: "QuotationRequests");

            migrationBuilder.DropIndex(
                name: "IX_BomHeaders_ItemId",
                table: "BomHeaders");

            // 5. Drop old columns from QuotationRequests
            migrationBuilder.DropColumn(
                name: "ExpectedQty",
                table: "QuotationRequests");

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "QuotationRequests");

            // 6. Drop old columns from QuotationApprovals
            migrationBuilder.DropColumn(
                name: "MaterialCostPct",
                table: "QuotationApprovals");

            migrationBuilder.DropColumn(
                name: "OtherCostPct",
                table: "QuotationApprovals");

            migrationBuilder.DropColumn(
                name: "ProfitMarginPct",
                table: "QuotationApprovals");

            migrationBuilder.DropColumn(
                name: "SalesPricePerKgAed",
                table: "QuotationApprovals");

            migrationBuilder.DropColumn(
                name: "SalesPricePerKgForeign",
                table: "QuotationApprovals");

            // 7. Drop old ItemId from BomHeaders
            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "BomHeaders");

            // 8. Rename column and index
            migrationBuilder.RenameColumn(
                name: "QuotationRequestId",
                table: "BomHeaders",
                newName: "RequisitionItemId");

            migrationBuilder.RenameIndex(
                name: "IX_BomHeaders_QuotationRequestId",
                table: "BomHeaders",
                newName: "IX_BomHeaders_RequisitionItemId");

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalCostPerKg",
                table: "BomCosts",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            // 9. Create ApprovalItems table
            migrationBuilder.CreateTable(
                name: "ApprovalItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuotationApprovalId = table.Column<int>(type: "integer", nullable: false),
                    RequisitionItemId = table.Column<int>(type: "integer", nullable: false),
                    SalesPricePerKgAed = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SalesPricePerKgForeign = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    ProfitMarginPct = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    MaterialCostPct = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    OtherCostPct = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalItems_QuotationApprovals_QuotationApprovalId",
                        column: x => x.QuotationApprovalId,
                        principalTable: "QuotationApprovals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalItems_RequisitionItems_RequisitionItemId",
                        column: x => x.RequisitionItemId,
                        principalTable: "RequisitionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalItems_QuotationApprovalId",
                table: "ApprovalItems",
                column: "QuotationApprovalId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalItems_RequisitionItemId",
                table: "ApprovalItems",
                column: "RequisitionItemId",
                unique: true);

            // 10. Add new FK from BomHeaders → RequisitionItems
            migrationBuilder.AddForeignKey(
                name: "FK_BomHeaders_RequisitionItems_RequisitionItemId",
                table: "BomHeaders",
                column: "RequisitionItemId",
                principalTable: "RequisitionItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BomHeaders_RequisitionItems_RequisitionItemId",
                table: "BomHeaders");

            migrationBuilder.DropTable(
                name: "ApprovalItems");

            migrationBuilder.DropTable(
                name: "RequisitionItems");

            migrationBuilder.RenameColumn(
                name: "RequisitionItemId",
                table: "BomHeaders",
                newName: "QuotationRequestId");

            migrationBuilder.RenameIndex(
                name: "IX_BomHeaders_RequisitionItemId",
                table: "BomHeaders",
                newName: "IX_BomHeaders_QuotationRequestId");

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedQty",
                table: "QuotationRequests",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ItemId",
                table: "QuotationRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "MaterialCostPct",
                table: "QuotationApprovals",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OtherCostPct",
                table: "QuotationApprovals",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProfitMarginPct",
                table: "QuotationApprovals",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SalesPricePerKgAed",
                table: "QuotationApprovals",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SalesPricePerKgForeign",
                table: "QuotationApprovals",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ItemId",
                table: "BomHeaders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalCostPerKg",
                table: "BomCosts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.CreateIndex(
                name: "IX_QuotationRequests_ItemId",
                table: "QuotationRequests",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BomHeaders_ItemId",
                table: "BomHeaders",
                column: "ItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_BomHeaders_Items_ItemId",
                table: "BomHeaders",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BomHeaders_QuotationRequests_QuotationRequestId",
                table: "BomHeaders",
                column: "QuotationRequestId",
                principalTable: "QuotationRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_QuotationRequests_Items_ItemId",
                table: "QuotationRequests",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
