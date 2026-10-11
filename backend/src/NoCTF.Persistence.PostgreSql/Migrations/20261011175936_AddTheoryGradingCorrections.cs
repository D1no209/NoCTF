using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddTheoryGradingCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "applied_grading_revision",
                table: "theory_paper_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "theory_grading_overrides",
                columns: table => new
                {
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    target_revision = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_revision = table.Column<Guid>(type: "uuid", nullable: true),
                    mode = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_grading_overrides", x => new { x.activity_id, x.revision_id });
                    table.ForeignKey(
                        name: "fk_theory_grading_overrides_theory_activities_activity_id",
                        column: x => x.activity_id,
                        principalTable: "theory_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_grading_overrides_theory_question_revisions_revision",
                        column: x => x.revision_id,
                        principalTable: "theory_question_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_grading_options",
                columns: table => new
                {
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    option_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_grading_options", x => new { x.activity_id, x.revision_id, x.option_id });
                    table.ForeignKey(
                        name: "fk_theory_grading_options_theory_grading_overrides_activity_id",
                        columns: x => new { x.activity_id, x.revision_id },
                        principalTable: "theory_grading_overrides",
                        principalColumns: new[] { "activity_id", "revision_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_theory_grading_options_theory_question_option_option_id",
                        column: x => x.option_id,
                        principalTable: "theory_question_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_theory_grading_options_option_id",
                table: "theory_grading_options",
                column: "option_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_grading_overrides_revision_id",
                table: "theory_grading_overrides",
                column: "revision_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "theory_grading_options");

            migrationBuilder.DropTable(
                name: "theory_grading_overrides");

            migrationBuilder.DropColumn(
                name: "applied_grading_revision",
                table: "theory_paper_items");
        }
    }
}
