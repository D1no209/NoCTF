using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionProgression : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "competition_badges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    image_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_badges", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_badges_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_badges_files_image_file_id",
                        column: x => x.image_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competition_progressions",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    show_player_map = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_progressions", x => x.competition_id);
                    table.ForeignKey(
                        name: "fk_competition_progressions_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "team_progression_badge_states",
                columns: table => new
                {
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    badge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    graph_revision = table.Column<long>(type: "bigint", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    evaluated_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_progression_badge_states", x => new { x.team_id, x.badge_id });
                    table.ForeignKey(
                        name: "fk_team_progression_badge_states_competition_badges_badge_id",
                        column: x => x.badge_id,
                        principalTable: "competition_badges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_team_progression_badge_states_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_badge_grants",
                columns: table => new
                {
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    badge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    awarded_at = table.Column<long>(type: "bigint", nullable: false),
                    revoked_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_badge_grants", x => new { x.team_id, x.badge_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_user_badge_grants_competition_badges_badge_id",
                        column: x => x.badge_id,
                        principalTable: "competition_badges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_badge_grants_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_badge_grants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_badge_transitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    badge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    graph_revision = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_badge_transitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_badge_transitions_competition_badges_badge_id",
                        column: x => x.badge_id,
                        principalTable: "competition_badges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_badge_transitions_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_badge_transitions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competition_progression_nodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_x = table.Column<double>(type: "double precision", nullable: false),
                    position_y = table.Column<double>(type: "double precision", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    competition_badge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_progression_nodes", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_progression_nodes_competition_badges_competitio",
                        column: x => x.competition_badge_id,
                        principalTable: "competition_badges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_progression_nodes_competition_challenges_compet",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_progression_nodes_competition_progressions_comp",
                        column: x => x.competition_id,
                        principalTable: "competition_progressions",
                        principalColumn: "competition_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_progression_edges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    condition = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_progression_edges", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_progression_edges_competition_progressions_comp",
                        column: x => x.competition_id,
                        principalTable: "competition_progressions",
                        principalColumn: "competition_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_competition_progression_edges_progression_nodes_source_node",
                        column: x => x.source_node_id,
                        principalTable: "competition_progression_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_progression_edges_progression_nodes_target_node",
                        column: x => x.target_node_id,
                        principalTable: "competition_progression_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "team_progression_node_states",
                columns: table => new
                {
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    graph_revision = table.Column<long>(type: "bigint", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    complete = table.Column<bool>(type: "boolean", nullable: false),
                    evaluated_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_progression_node_states", x => new { x.team_id, x.node_id });
                    table.ForeignKey(
                        name: "fk_team_progression_node_states_competition_progression_nodes_",
                        column: x => x.node_id,
                        principalTable: "competition_progression_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_team_progression_node_states_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_competition_badges_competition_id",
                table: "competition_badges",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_badges_image_file_id",
                table: "competition_badges",
                column: "image_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_progression_edges_competition_id_source_node_id",
                table: "competition_progression_edges",
                columns: new[] { "competition_id", "source_node_id", "target_node_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_progression_edges_source_node_id",
                table: "competition_progression_edges",
                column: "source_node_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_progression_edges_target_node_id",
                table: "competition_progression_edges",
                column: "target_node_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_progression_nodes_competition_badge_id",
                table: "competition_progression_nodes",
                column: "competition_badge_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_progression_nodes_competition_challenge_id",
                table: "competition_progression_nodes",
                column: "competition_challenge_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_progression_nodes_competition_id",
                table: "competition_progression_nodes",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_progression_badge_states_badge_id",
                table: "team_progression_badge_states",
                column: "badge_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_progression_badge_states_competition_id_badge_id",
                table: "team_progression_badge_states",
                columns: new[] { "competition_id", "badge_id" });

            migrationBuilder.CreateIndex(
                name: "ix_team_progression_node_states_competition_id_team_id",
                table: "team_progression_node_states",
                columns: new[] { "competition_id", "team_id" });

            migrationBuilder.CreateIndex(
                name: "ix_team_progression_node_states_node_id",
                table: "team_progression_node_states",
                column: "node_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_badge_grants_badge_id",
                table: "user_badge_grants",
                column: "badge_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_badge_grants_competition_id_user_id_active",
                table: "user_badge_grants",
                columns: new[] { "competition_id", "user_id", "active" });

            migrationBuilder.CreateIndex(
                name: "ix_user_badge_grants_user_id",
                table: "user_badge_grants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_badge_transitions_badge_id",
                table: "user_badge_transitions",
                column: "badge_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_badge_transitions_competition_id_team_id_occurred_at",
                table: "user_badge_transitions",
                columns: new[] { "competition_id", "team_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_badge_transitions_team_id",
                table: "user_badge_transitions",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_badge_transitions_user_id_badge_id_occurred_at",
                table: "user_badge_transitions",
                columns: new[] { "user_id", "badge_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "competition_progression_edges");

            migrationBuilder.DropTable(
                name: "team_progression_badge_states");

            migrationBuilder.DropTable(
                name: "team_progression_node_states");

            migrationBuilder.DropTable(
                name: "user_badge_grants");

            migrationBuilder.DropTable(
                name: "user_badge_transitions");

            migrationBuilder.DropTable(
                name: "competition_progression_nodes");

            migrationBuilder.DropTable(
                name: "competition_badges");

            migrationBuilder.DropTable(
                name: "competition_progressions");
        }
    }
}
