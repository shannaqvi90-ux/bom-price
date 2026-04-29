using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class V3_AddApprovalItemMarginPerKg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_approval_items_sales_price_positive",
                table: "ApprovalItems");

            migrationBuilder.AddColumn<decimal>(
                name: "MarginPerKg",
                table: "ApprovalItems",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MarginPerKg",
                table: "ApprovalItems");

            migrationBuilder.AddCheckConstraint(
                name: "ck_approval_items_sales_price_positive",
                table: "ApprovalItems",
                sql: "\"SalesPricePerKgAed\" > 0");
        }
    }
}
