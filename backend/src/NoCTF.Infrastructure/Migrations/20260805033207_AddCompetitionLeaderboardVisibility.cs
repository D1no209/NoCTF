using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionLeaderboardVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "frozen_leaderboard_snapshot_json",
                table: "competitions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "leaderboard_visibility",
                table: "competitions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "leaderboard_visibility_applied_at",
                table: "competitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "leaderboard_visibility_revision",
                table: "competitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "leaderboard_visibility_starts_at",
                table: "competitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "competition_leaderboard_visibility_audits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from = table.Column<short>(type: "smallint", nullable: false),
                    to = table.Column<short>(type: "smallint", nullable: false),
                    data_cutoff_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    automatic = table.Column<bool>(type: "boolean", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_leaderboard_visibility_audits", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_leaderboard_visibility_audits_competitions_comp",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_competitions_leaderboard_visibility_leaderboard_visibility_",
                table: "competitions",
                columns: new[] { "leaderboard_visibility", "leaderboard_visibility_starts_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_competitions_leaderboard_visibility_state",
                table: "competitions",
                sql: "leaderboard_visibility BETWEEN 0 AND 2 AND ((leaderboard_visibility = 0 AND leaderboard_visibility_starts_at IS NULL AND frozen_leaderboard_snapshot_json IS NULL) OR (leaderboard_visibility = 1 AND leaderboard_visibility_starts_at IS NOT NULL AND ((leaderboard_visibility_applied_at IS NULL AND frozen_leaderboard_snapshot_json IS NULL) OR (leaderboard_visibility_applied_at IS NOT NULL AND frozen_leaderboard_snapshot_json IS NOT NULL))) OR (leaderboard_visibility = 2 AND leaderboard_visibility_starts_at IS NOT NULL AND frozen_leaderboard_snapshot_json IS NULL))");

            migrationBuilder.CreateIndex(
                name: "ix_competition_leaderboard_visibility_audits_competition_id_oc",
                table: "competition_leaderboard_visibility_audits",
                columns: new[] { "competition_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "competition_leaderboard_visibility_audits");

            migrationBuilder.DropIndex(
                name: "ix_competitions_leaderboard_visibility_leaderboard_visibility_",
                table: "competitions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_competitions_leaderboard_visibility_state",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "frozen_leaderboard_snapshot_json",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "leaderboard_visibility",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "leaderboard_visibility_applied_at",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "leaderboard_visibility_revision",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "leaderboard_visibility_starts_at",
                table: "competitions");
        }
    }
}
