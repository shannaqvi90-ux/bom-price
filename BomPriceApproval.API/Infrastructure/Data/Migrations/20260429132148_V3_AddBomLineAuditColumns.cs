using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class V3_AddBomLineAuditColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAt",
                table: "BomLines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastModifiedByUserId",
                table: "BomLines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_LastModifiedByUserId",
                table: "BomLines",
                column: "LastModifiedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BomLines_Users_LastModifiedByUserId",
                table: "BomLines",
                column: "LastModifiedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BomLines_Users_LastModifiedByUserId",
                table: "BomLines");

            migrationBuilder.DropIndex(
                name: "IX_BomLines_LastModifiedByUserId",
                table: "BomLines");

            migrationBuilder.DropColumn(
                name: "LastModifiedAt",
                table: "BomLines");

            migrationBuilder.DropColumn(
                name: "LastModifiedByUserId",
                table: "BomLines");
        }
    }
}
