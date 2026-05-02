using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SignatureToBytea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SignatureImagePath",
                table: "Users",
                newName: "SignatureMimeType");

            migrationBuilder.AddColumn<byte[]>(
                name: "SignatureImage",
                table: "Users",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignatureImage",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "SignatureMimeType",
                table: "Users",
                newName: "SignatureImagePath");
        }
    }
}
