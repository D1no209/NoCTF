using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloAdjudications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "live_solo_adjudications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    forfeiting_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<short>(type: "smallint", nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false),
                    previous_match_state = table.Column<short>(type: "smallint", nullable: false),
                    match_state = table.Column<short>(type: "smallint", nullable: false),
                    previous_round_state = table.Column<short>(type: "smallint", nullable: true),
                    round_state = table.Column<short>(type: "smallint", nullable: true),
                    previous_left_wins = table.Column<int>(type: "integer", nullable: false),
                    previous_right_wins = table.Column<int>(type: "integer", nullable: false),
                    left_wins = table.Column<int>(type: "integer", nullable: false),
                    right_wins = table.Column<int>(type: "integer", nullable: false),
                    previous_timeline_revision = table.Column<long>(type: "bigint", nullable: true),
                    timeline_revision = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_adjudications", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_adjudications_live_solo_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "live_solo_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_adjudications_live_solo_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "live_solo_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_adjudications_teams_forfeiting_team_id",
                        column: x => x.forfeiting_team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_adjudications_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_adjudications_actor_user_id",
                table: "live_solo_adjudications",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_adjudications_forfeiting_team_id",
                table: "live_solo_adjudications",
                column: "forfeiting_team_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_adjudications_match_id_occurred_at",
                table: "live_solo_adjudications",
                columns: new[] { "match_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_adjudications_round_id",
                table: "live_solo_adjudications",
                column: "round_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "live_solo_adjudications");
        }
    }
}
