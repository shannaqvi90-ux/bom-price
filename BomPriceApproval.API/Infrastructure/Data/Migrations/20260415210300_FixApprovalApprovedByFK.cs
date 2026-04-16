using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixApprovalApprovedByFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuotationApprovals_Users_ApprovedById",
                table: "QuotationApprovals");

            migrationBuilder.DropIndex(
                name: "IX_QuotationApprovals_ApprovedById",
                table: "QuotationApprovals");

            migrationBuilder.DropColumn(
                name: "ApprovedById",
                table: "QuotationApprovals");

            migrationBuilder.CreateIndex(
                name: "IX_QuotationApprovals_ApprovedByUserId",
                table: "QuotationApprovals",
                column: "ApprovedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_QuotationApprovals_Users_ApprovedByUserId",
                table: "QuotationApprovals",
                column: "ApprovedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuotationApprovals_Users_ApprovedByUserId",
                table: "QuotationApprovals");

            migrationBuilder.DropIndex(
                name: "IX_QuotationApprovals_ApprovedByUserId",
                table: "QuotationApprovals");

            migrationBuilder.AddColumn<int>(
                name: "ApprovedById",
                table: "QuotationApprovals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_QuotationApprovals_ApprovedById",
                table: "QuotationApprovals",
                column: "ApprovedById");

            migrationBuilder.AddForeignKey(
                name: "FK_QuotationApprovals_Users_ApprovedById",
                table: "QuotationApprovals",
                column: "ApprovedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
