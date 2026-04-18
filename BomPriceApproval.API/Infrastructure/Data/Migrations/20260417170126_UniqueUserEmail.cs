using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueUserEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Normalize all emails to lowercase and rename duplicates before adding the unique index.
            migrationBuilder.Sql(@"
                UPDATE ""Users"" u
                SET ""Email"" = LOWER(u.""Email"")
                WHERE u.""Email"" != LOWER(u.""Email"");

                UPDATE ""Users"" u
                SET ""Email"" = u.""Email"" || '.dup' || u.""Id""
                WHERE u.""Id"" IN (
                    SELECT ""Id"" FROM (
                        SELECT ""Id"", ROW_NUMBER() OVER (PARTITION BY LOWER(""Email"") ORDER BY ""Id"") AS rn
                        FROM ""Users""
                    ) t WHERE t.rn > 1
                );
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");
        }
    }
}
