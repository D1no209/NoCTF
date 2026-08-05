using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCheatIncidentAdjudication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "scoring_event_id",
                table: "competition_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_competition_id_scoring_event_id_occurred",
                table: "competition_events",
                columns: new[] { "competition_id", "scoring_event_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_scoring_event_id",
                table: "competition_events",
                column: "scoring_event_id");

            migrationBuilder.AddForeignKey(
                name: "fk_competition_events_scoring_events_scoring_event_id",
                table: "competition_events",
                column: "scoring_event_id",
                principalTable: "scoring_events",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_competition_events_scoring_events_scoring_event_id",
                table: "competition_events");

            migrationBuilder.DropIndex(
                name: "ix_competition_events_competition_id_scoring_event_id_occurred",
                table: "competition_events");

            migrationBuilder.DropIndex(
                name: "ix_competition_events_scoring_event_id",
                table: "competition_events");

            migrationBuilder.DropColumn(
                name: "scoring_event_id",
                table: "competition_events");
        }
    }
}
