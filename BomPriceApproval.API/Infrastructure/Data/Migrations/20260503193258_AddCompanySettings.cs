using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    CompanyName = table.Column<string>(type: "text", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    Telephone = table.Column<string>(type: "text", nullable: false),
                    Trn = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Website = table.Column<string>(type: "text", nullable: false),
                    QuotationValidityDays = table.Column<int>(type: "integer", nullable: false),
                    TermsAndConditions = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanySettings_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "CompanySettings",
                columns: new[] { "Id", "Address", "CompanyName", "Email", "QuotationValidityDays", "Telephone", "TermsAndConditions", "Trn", "UpdatedAt", "UpdatedByUserId", "Website" },
                values: new object[] { 1, "Fujairah, United Arab Emirates", "FUJAIRAH PLASTIC FACTORY", "info@fujairahplastic.com", 30, "", "This quotation is valid for 30 days from the date of issue.\nPrices are subject to change without prior notice after the validity period.\nPayment terms as per mutually agreed contract.\nDelivery: Ex-Works Fujairah unless otherwise agreed in writing.\nAll disputes are subject to the jurisdiction of UAE courts.", "", new DateTime(2026, 5, 3, 0, 0, 0, 0, DateTimeKind.Utc), null, "" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanySettings_UpdatedByUserId",
                table: "CompanySettings",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanySettings");
        }
    }
}
