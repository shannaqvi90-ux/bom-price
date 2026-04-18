using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSupersededFieldsToQuotationApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuotationApprovals_QuotationRequestId",
                table: "QuotationApprovals");

            migrationBuilder.AddColumn<bool>(
                name: "IsSuperseded",
                table: "QuotationApprovals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAt",
                table: "QuotationApprovals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_quotation_approvals_current",
                table: "QuotationApprovals",
                column: "QuotationRequestId",
                filter: "\"IsSuperseded\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_quotation_approvals_current",
                table: "QuotationApprovals");

            migrationBuilder.DropColumn(
                name: "IsSuperseded",
                table: "QuotationApprovals");

            migrationBuilder.DropColumn(
                name: "SupersededAt",
                table: "QuotationApprovals");

            migrationBuilder.CreateIndex(
                name: "IX_QuotationApprovals_QuotationRequestId",
                table: "QuotationApprovals",
                column: "QuotationRequestId",
                unique: true);
        }
    }
}
