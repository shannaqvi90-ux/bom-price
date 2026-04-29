using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class V3_AddBomCostV3Fields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CommissionPerKg",
                table: "BomCosts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FohPerKg",
                table: "BomCosts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PrintingCostCurrency",
                table: "BomCosts",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PrintingCostPerKg",
                table: "BomCosts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TransportPerKg",
                table: "BomCosts",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommissionPerKg",
                table: "BomCosts");

            migrationBuilder.DropColumn(
                name: "FohPerKg",
                table: "BomCosts");

            migrationBuilder.DropColumn(
                name: "PrintingCostCurrency",
                table: "BomCosts");

            migrationBuilder.DropColumn(
                name: "PrintingCostPerKg",
                table: "BomCosts");

            migrationBuilder.DropColumn(
                name: "TransportPerKg",
                table: "BomCosts");
        }
    }
}
