using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloQuestionMaterialOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "catalog_scope",
                table: "challenges",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.CreateTable(
                name: "live_solo_question_materials",
                columns: table => new
                {
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_template_stamp = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_question_materials", x => x.competition_challenge_id);
                    table.ForeignKey(
                        name: "fk_live_solo_question_materials_challenges_challenge_id",
                        column: x => x.challenge_id,
                        principalTable: "challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_question_materials_challenges_source_template_id",
                        column: x => x.source_template_id,
                        principalTable: "challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_question_materials_competition_challenges_competi",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_question_materials_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_question_materials_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_question_materials_challenge_id",
                table: "live_solo_question_materials",
                column: "challenge_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_question_materials_competition_id_source_template",
                table: "live_solo_question_materials",
                columns: new[] { "competition_id", "source_template_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_question_materials_created_by_user_id",
                table: "live_solo_question_materials",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_question_materials_source_template_id",
                table: "live_solo_question_materials",
                column: "source_template_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "live_solo_question_materials");

            migrationBuilder.DropColumn(
                name: "catalog_scope",
                table: "challenges");
        }
    }
}
