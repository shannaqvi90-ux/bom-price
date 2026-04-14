using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixBomLineRawMaterialFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BomLines_Items_RawMaterialId",
                table: "BomLines");

            migrationBuilder.DropIndex(
                name: "IX_BomLines_RawMaterialId",
                table: "BomLines");

            migrationBuilder.DropColumn(
                name: "RawMaterialId",
                table: "BomLines");

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_RawMaterialItemId",
                table: "BomLines",
                column: "RawMaterialItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_BomLines_Items_RawMaterialItemId",
                table: "BomLines",
                column: "RawMaterialItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BomLines_Items_RawMaterialItemId",
                table: "BomLines");

            migrationBuilder.DropIndex(
                name: "IX_BomLines_RawMaterialItemId",
                table: "BomLines");

            migrationBuilder.AddColumn<int>(
                name: "RawMaterialId",
                table: "BomLines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_RawMaterialId",
                table: "BomLines",
                column: "RawMaterialId");

            migrationBuilder.AddForeignKey(
                name: "FK_BomLines_Items_RawMaterialId",
                table: "BomLines",
                column: "RawMaterialId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
