using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixShadowForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BomCosts_Users_SubmittedById",
                table: "BomCosts");

            migrationBuilder.DropForeignKey(
                name: "FK_ExchangeRates_Users_SetById",
                table: "ExchangeRates");

            migrationBuilder.DropIndex(
                name: "IX_ExchangeRates_SetById",
                table: "ExchangeRates");

            migrationBuilder.DropIndex(
                name: "IX_BomCosts_SubmittedById",
                table: "BomCosts");

            migrationBuilder.DropColumn(
                name: "SetById",
                table: "ExchangeRates");

            migrationBuilder.DropColumn(
                name: "SubmittedById",
                table: "BomCosts");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_SetByUserId",
                table: "ExchangeRates",
                column: "SetByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BomCosts_SubmittedByUserId",
                table: "BomCosts",
                column: "SubmittedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BomCosts_Users_SubmittedByUserId",
                table: "BomCosts",
                column: "SubmittedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ExchangeRates_Users_SetByUserId",
                table: "ExchangeRates",
                column: "SetByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BomCosts_Users_SubmittedByUserId",
                table: "BomCosts");

            migrationBuilder.DropForeignKey(
                name: "FK_ExchangeRates_Users_SetByUserId",
                table: "ExchangeRates");

            migrationBuilder.DropIndex(
                name: "IX_ExchangeRates_SetByUserId",
                table: "ExchangeRates");

            migrationBuilder.DropIndex(
                name: "IX_BomCosts_SubmittedByUserId",
                table: "BomCosts");

            migrationBuilder.AddColumn<int>(
                name: "SetById",
                table: "ExchangeRates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubmittedById",
                table: "BomCosts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_SetById",
                table: "ExchangeRates",
                column: "SetById");

            migrationBuilder.CreateIndex(
                name: "IX_BomCosts_SubmittedById",
                table: "BomCosts",
                column: "SubmittedById");

            migrationBuilder.AddForeignKey(
                name: "FK_BomCosts_Users_SubmittedById",
                table: "BomCosts",
                column: "SubmittedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ExchangeRates_Users_SetById",
                table: "ExchangeRates",
                column: "SetById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
