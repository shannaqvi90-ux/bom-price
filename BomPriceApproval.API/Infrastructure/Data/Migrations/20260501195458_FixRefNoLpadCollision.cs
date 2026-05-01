using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixRefNoLpadCollision : Migration
    {
        // PG LPAD truncates from the right when the input is longer than the
        // length argument, so 4-digit padding caused IDs >= 10000 to collide
        // (e.g. 13150 -> "REQ-1315", colliding with ID 1315). Bumping to 6
        // gives headroom to ID 999,999 — well past any plausible volume.
        // Stored generated columns on PG can't have their expression altered
        // in place, so the migration drops + re-adds the column. RefNo is
        // not referenced by FKs or indexes, so the drop is safe.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""QuotationRequests"" DROP COLUMN ""RefNo"";
                ALTER TABLE ""QuotationRequests"" ADD COLUMN ""RefNo"" text
                    GENERATED ALWAYS AS ('REQ-' || LPAD(""Id""::text, 6, '0')) STORED NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""QuotationRequests"" DROP COLUMN ""RefNo"";
                ALTER TABLE ""QuotationRequests"" ADD COLUMN ""RefNo"" text
                    GENERATED ALWAYS AS ('REQ-' || LPAD(""Id""::text, 4, '0')) STORED NOT NULL;
            ");
        }
    }
}
