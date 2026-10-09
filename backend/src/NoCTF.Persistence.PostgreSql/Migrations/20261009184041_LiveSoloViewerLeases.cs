using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloViewerLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "live_solo_viewer_leases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_viewer_leases", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_viewer_leases_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_viewer_leases_live_solo_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "live_solo_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_solo_viewer_leases_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_viewer_leases_competition_id_expires_at",
                table: "live_solo_viewer_leases",
                columns: new[] { "competition_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_viewer_leases_match_id",
                table: "live_solo_viewer_leases",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_viewer_leases_user_id",
                table: "live_solo_viewer_leases",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "live_solo_viewer_leases");
        }
    }
}
