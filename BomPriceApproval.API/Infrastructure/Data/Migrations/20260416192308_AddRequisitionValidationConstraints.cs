using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRequisitionValidationConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RequisitionItems_QuotationRequestId",
                table: "RequisitionItems");

            migrationBuilder.CreateIndex(
                name: "IX_RequisitionItems_QuotationRequestId_ItemId",
                table: "RequisitionItems",
                columns: new[] { "QuotationRequestId", "ItemId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_requisition_items_expected_qty_positive",
                table: "RequisitionItems",
                sql: "\"ExpectedQty\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bom_lines_qty_per_kg_positive",
                table: "BomLines",
                sql: "\"QtyPerKg\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_approval_items_sales_price_positive",
                table: "ApprovalItems",
                sql: "\"SalesPricePerKgAed\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RequisitionItems_QuotationRequestId_ItemId",
                table: "RequisitionItems");

            migrationBuilder.DropCheckConstraint(
                name: "ck_requisition_items_expected_qty_positive",
                table: "RequisitionItems");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bom_lines_qty_per_kg_positive",
                table: "BomLines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_approval_items_sales_price_positive",
                table: "ApprovalItems");

            migrationBuilder.CreateIndex(
                name: "IX_RequisitionItems_QuotationRequestId",
                table: "RequisitionItems",
                column: "QuotationRequestId");
        }
    }
}
