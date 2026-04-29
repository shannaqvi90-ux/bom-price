using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class V3_AddCodeCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CodeCounters",
                columns: table => new
                {
                    Sequence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NextValue = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeCounters", x => x.Sequence);
                });

            migrationBuilder.Sql(@"
                INSERT INTO ""CodeCounters"" (""Sequence"", ""NextValue"") VALUES
                ('CUST', COALESCE(
                    (SELECT MAX(CAST(SPLIT_PART(""Code"", '-', 2) AS INTEGER)) + 1
                     FROM ""Customers""
                     WHERE ""Code"" ~ '^CUST-[0-9]+$'), 1)),
                ('FG', COALESCE(
                    (SELECT MAX(CAST(SPLIT_PART(""Code"", '-', 2) AS INTEGER)) + 1
                     FROM ""Items""
                     WHERE ""Code"" ~ '^FG-[0-9]+$' AND ""Type"" = 0), 1)),
                ('RM', COALESCE(
                    (SELECT MAX(CAST(SPLIT_PART(""Code"", '-', 2) AS INTEGER)) + 1
                     FROM ""Items""
                     WHERE ""Code"" ~ '^RM-[0-9]+$' AND ""Type"" = 1), 1));
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodeCounters");
        }
    }
}
