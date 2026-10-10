using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloScopedAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "live_solo_attachment_assignments",
                columns: table => new
                {
                    round_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_attachment_assignments", x => new { x.round_question_id, x.team_id });
                    table.ForeignKey(
                        name: "fk_live_solo_attachment_assignments_challenge_attachment_attac",
                        column: x => x.attachment_id,
                        principalTable: "challenge_attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_attachment_assignments_live_solo_round_questions_",
                        column: x => x.round_question_id,
                        principalTable: "live_solo_round_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_attachment_assignments_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_attachment_assignments_attachment_id",
                table: "live_solo_attachment_assignments",
                column: "attachment_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_attachment_assignments_team_id",
                table: "live_solo_attachment_assignments",
                column: "team_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "live_solo_attachment_assignments");
        }
    }
}
