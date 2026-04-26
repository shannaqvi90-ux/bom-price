using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BomPriceApproval.API.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class V23a_BranchModelRework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Branches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BranchChangeHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequisitionId = table.Column<int>(type: "integer", nullable: false),
                    OldBranchId = table.Column<int>(type: "integer", nullable: false),
                    NewBranchId = table.Column<int>(type: "integer", nullable: false),
                    ChangedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchChangeHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchChangeHistories_Branches_NewBranchId",
                        column: x => x.NewBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BranchChangeHistories_Branches_OldBranchId",
                        column: x => x.OldBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BranchChangeHistories_QuotationRequests_RequisitionId",
                        column: x => x.RequisitionId,
                        principalTable: "QuotationRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BranchChangeHistories_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserBranches",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBranches", x => new { x.UserId, x.BranchId });
                    table.ForeignKey(
                        name: "FK_UserBranches_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserBranches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Branches",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "Branches",
                keyColumn: "Id",
                keyValue: 2,
                column: "IsActive",
                value: true);

            migrationBuilder.CreateIndex(
                name: "IX_BranchChangeHistories_ChangedByUserId",
                table: "BranchChangeHistories",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchChangeHistories_NewBranchId",
                table: "BranchChangeHistories",
                column: "NewBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchChangeHistories_OldBranchId",
                table: "BranchChangeHistories",
                column: "OldBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchChangeHistories_RequisitionId",
                table: "BranchChangeHistories",
                column: "RequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBranches_BranchId",
                table: "UserBranches",
                column: "BranchId");

            // Auto-assign every active Accountant to every active Branch.
            // Preserves Sara's pre-V23a cross-branch behavior across the cutover.
            migrationBuilder.Sql(@"
    INSERT INTO ""UserBranches"" (""UserId"", ""BranchId"", ""AssignedAt"")
    SELECT u.""Id"", b.""Id"", NOW() AT TIME ZONE 'UTC'
    FROM ""Users"" u
    CROSS JOIN ""Branches"" b
    WHERE u.""Role"" = 3  -- UserRole.Accountant
      AND u.""IsActive"" = TRUE
      AND b.""IsActive"" = TRUE;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""UserBranches"";");

            migrationBuilder.DropTable(
                name: "BranchChangeHistories");

            migrationBuilder.DropTable(
                name: "UserBranches");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Branches");
        }
    }
}
