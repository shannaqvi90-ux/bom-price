using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CostingEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BomCostLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BomHeaderId = table.Column<int>(type: "integer", nullable: false),
                    BomLineId = table.Column<int>(type: "integer", nullable: false),
                    CostPerKg = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CurrencyCode = table.Column<string>(type: "text", nullable: false),
                    CostPerKgInQuoteCurrency = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BomCostLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BomCostLines_BomHeaders_BomHeaderId",
                        column: x => x.BomHeaderId,
                        principalTable: "BomHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BomCostLines_BomLines_BomLineId",
                        column: x => x.BomLineId,
                        principalTable: "BomLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CostingDrafts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BomHeaderId = table.Column<int>(type: "integer", nullable: false),
                    LinesJson = table.Column<string>(type: "text", nullable: false),
                    LandedCostType = table.Column<int>(type: "integer", nullable: false),
                    LandedCostValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    FohAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostingDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostingDrafts_BomHeaders_BomHeaderId",
                        column: x => x.BomHeaderId,
                        principalTable: "BomHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemLastCosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    CostPerKg = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CurrencyCode = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemLastCosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemLastCosts_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemLastCosts_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BomCostLines_BomHeaderId",
                table: "BomCostLines",
                column: "BomHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_BomCostLines_BomLineId",
                table: "BomCostLines",
                column: "BomLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CostingDrafts_BomHeaderId",
                table: "CostingDrafts",
                column: "BomHeaderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemLastCosts_ItemId",
                table: "ItemLastCosts",
                column: "ItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemLastCosts_UpdatedByUserId",
                table: "ItemLastCosts",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BomCostLines");

            migrationBuilder.DropTable(
                name: "CostingDrafts");

            migrationBuilder.DropTable(
                name: "ItemLastCosts");
        }
    }
}
