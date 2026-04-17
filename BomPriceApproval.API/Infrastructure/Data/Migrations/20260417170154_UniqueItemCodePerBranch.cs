using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueItemCodePerBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename duplicate Item codes within the same branch before creating
            // the unique index so that existing seed duplicates (e.g. HDPE-20)
            // do not block the migration.
            migrationBuilder.Sql(@"
                UPDATE ""Items"" i
                SET ""Code"" = i.""Code"" || '-' || i.""Id""
                WHERE i.""Id"" IN (
                    SELECT ""Id"" FROM (
                        SELECT ""Id"", ROW_NUMBER() OVER (PARTITION BY ""Code"", ""BranchId"" ORDER BY ""Id"") AS rn
                        FROM ""Items""
                    ) t WHERE t.rn > 1
                );
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Code_BranchId",
                table: "Items",
                columns: new[] { "Code", "BranchId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_Code_BranchId",
                table: "Items");
        }
    }
}
