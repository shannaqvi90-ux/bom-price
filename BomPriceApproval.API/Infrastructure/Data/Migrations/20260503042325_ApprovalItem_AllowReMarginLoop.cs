using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalItem_AllowReMarginLoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApprovalItems_QuotationApprovalId",
                table: "ApprovalItems");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalItems_RequisitionItemId",
                table: "ApprovalItems");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalItems_QuotationApprovalId_RequisitionItemId",
                table: "ApprovalItems",
                columns: new[] { "QuotationApprovalId", "RequisitionItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalItems_RequisitionItemId",
                table: "ApprovalItems",
                column: "RequisitionItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApprovalItems_QuotationApprovalId_RequisitionItemId",
                table: "ApprovalItems");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalItems_RequisitionItemId",
                table: "ApprovalItems");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalItems_QuotationApprovalId",
                table: "ApprovalItems",
                column: "QuotationApprovalId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalItems_RequisitionItemId",
                table: "ApprovalItems",
                column: "RequisitionItemId",
                unique: true);
        }
    }
}
