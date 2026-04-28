using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RequisitionItemCostingStartedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: dev DB had this column hand-added so the migration was authored empty.
            // Production DBs need the actual DDL. IF NOT EXISTS keeps it safe to re-run anywhere.
            migrationBuilder.Sql(
                @"ALTER TABLE ""RequisitionItems"" ADD COLUMN IF NOT EXISTS ""CostingStartedAt"" timestamp with time zone NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"ALTER TABLE ""RequisitionItems"" DROP COLUMN IF EXISTS ""CostingStartedAt"";");
        }
    }
}
