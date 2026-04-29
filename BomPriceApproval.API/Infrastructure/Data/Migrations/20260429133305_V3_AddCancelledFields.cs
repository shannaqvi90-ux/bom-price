using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class V3_AddCancelledFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "QuotationRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "QuotationRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancelledByUserId",
                table: "QuotationRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuotationRequests_CancelledByUserId",
                table: "QuotationRequests",
                column: "CancelledByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_QuotationRequests_Users_CancelledByUserId",
                table: "QuotationRequests",
                column: "CancelledByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuotationRequests_Users_CancelledByUserId",
                table: "QuotationRequests");

            migrationBuilder.DropIndex(
                name: "IX_QuotationRequests_CancelledByUserId",
                table: "QuotationRequests");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "QuotationRequests");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "QuotationRequests");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "QuotationRequests");
        }
    }
}
