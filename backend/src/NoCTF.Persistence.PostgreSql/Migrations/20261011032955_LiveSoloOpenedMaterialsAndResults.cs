using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloOpenedMaterialsAndResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "end_reason",
                table: "live_solo_rounds",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<int>(
                name: "result_left_wins",
                table: "live_solo_rounds",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "result_revision",
                table: "live_solo_rounds",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "result_right_wins",
                table: "live_solo_rounds",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "end_reason",
                table: "live_solo_matches",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<int>(
                name: "result_left_wins",
                table: "live_solo_matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "result_revision",
                table: "live_solo_matches",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "result_right_wins",
                table: "live_solo_matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "result_updated_at",
                table: "live_solo_matches",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "live_solo_opened_contents",
                columns: table => new
                {
                    round_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    direction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    random_attachment = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_opened_contents", x => x.round_question_id);
                    table.ForeignKey(
                        name: "fk_live_solo_opened_contents_live_solo_round_questions_round_q",
                        column: x => x.round_question_id,
                        principalTable: "live_solo_round_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_opened_attachments",
                columns: table => new
                {
                    round_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_opened_attachments", x => new { x.round_question_id, x.attachment_id });
                    table.ForeignKey(
                        name: "fk_live_solo_opened_attachments_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_opened_attachments_live_solo_opened_content_round",
                        column: x => x.round_question_id,
                        principalTable: "live_solo_opened_contents",
                        principalColumn: "round_question_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_opened_hints",
                columns: table => new
                {
                    round_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    published_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_opened_hints", x => new { x.round_question_id, x.hint_id });
                    table.ForeignKey(
                        name: "fk_live_solo_opened_hints_live_solo_opened_contents_round_ques",
                        column: x => x.round_question_id,
                        principalTable: "live_solo_opened_contents",
                        principalColumn: "round_question_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_opened_tags",
                columns: table => new
                {
                    round_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_opened_tags", x => new { x.round_question_id, x.position });
                    table.ForeignKey(
                        name: "fk_live_solo_opened_tags_live_solo_opened_contents_round_quest",
                        column: x => x.round_question_id,
                        principalTable: "live_solo_opened_contents",
                        principalColumn: "round_question_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_opened_attachments_file_id",
                table: "live_solo_opened_attachments",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_opened_attachments_round_question_id_position",
                table: "live_solo_opened_attachments",
                columns: new[] { "round_question_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_opened_hints_round_question_id_position",
                table: "live_solo_opened_hints",
                columns: new[] { "round_question_id", "position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "live_solo_opened_attachments");

            migrationBuilder.DropTable(
                name: "live_solo_opened_hints");

            migrationBuilder.DropTable(
                name: "live_solo_opened_tags");

            migrationBuilder.DropTable(
                name: "live_solo_opened_contents");

            migrationBuilder.DropColumn(
                name: "end_reason",
                table: "live_solo_rounds");

            migrationBuilder.DropColumn(
                name: "result_left_wins",
                table: "live_solo_rounds");

            migrationBuilder.DropColumn(
                name: "result_revision",
                table: "live_solo_rounds");

            migrationBuilder.DropColumn(
                name: "result_right_wins",
                table: "live_solo_rounds");

            migrationBuilder.DropColumn(
                name: "end_reason",
                table: "live_solo_matches");

            migrationBuilder.DropColumn(
                name: "result_left_wins",
                table: "live_solo_matches");

            migrationBuilder.DropColumn(
                name: "result_revision",
                table: "live_solo_matches");

            migrationBuilder.DropColumn(
                name: "result_right_wins",
                table: "live_solo_matches");

            migrationBuilder.DropColumn(
                name: "result_updated_at",
                table: "live_solo_matches");
        }
    }
}
