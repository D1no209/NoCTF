using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddTheoryIssuanceAndGameplayTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "competition_challenge_id",
                table: "gameplay_facts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "theory_paper_id",
                table: "gameplay_facts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "theory_issue_receipts",
                columns: table => new
                {
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paper_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_issue_receipts", x => new { x.participant_id, x.request_id });
                    table.ForeignKey(
                        name: "fk_theory_issue_receipts_theory_papers_participant_id_paper_id",
                        columns: x => new { x.participant_id, x.paper_id },
                        principalTable: "theory_papers",
                        principalColumns: new[] { "participant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gameplay_facts_theory_paper_id",
                table: "gameplay_facts",
                column: "theory_paper_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_theory_issue_receipts_participant_id_paper_id",
                table: "theory_issue_receipts",
                columns: new[] { "participant_id", "paper_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_gameplay_facts_theory_papers_theory_paper_id",
                table: "gameplay_facts",
                column: "theory_paper_id",
                principalTable: "theory_papers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_gameplay_facts_theory_papers_theory_paper_id",
                table: "gameplay_facts");

            migrationBuilder.DropTable(
                name: "theory_issue_receipts");

            migrationBuilder.DropIndex(
                name: "ix_gameplay_facts_theory_paper_id",
                table: "gameplay_facts");

            migrationBuilder.DropColumn(
                name: "theory_paper_id",
                table: "gameplay_facts");

            migrationBuilder.AlterColumn<Guid>(
                name: "competition_challenge_id",
                table: "gameplay_facts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
