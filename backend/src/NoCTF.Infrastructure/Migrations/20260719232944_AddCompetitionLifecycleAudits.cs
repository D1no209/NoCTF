using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionLifecycleAudits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "competition_lifecycle_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    From = table.Column<int>(type: "integer", nullable: false),
                    To = table.Column<int>(type: "integer", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Automatic = table.Column<bool>(type: "boolean", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competition_lifecycle_audits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_competition_lifecycle_audits_competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_competition_lifecycle_audits_CompetitionId_OccurredAt",
                table: "competition_lifecycle_audits",
                columns: new[] { "CompetitionId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "competition_lifecycle_audits");
        }
    }
}
