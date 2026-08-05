using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamBanAppeals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "parent_event_id",
                table: "competition_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_competition_id_parent_event_id_occurred_",
                table: "competition_events",
                columns: new[] { "competition_id", "parent_event_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_parent_event_id",
                table: "competition_events",
                column: "parent_event_id");

            migrationBuilder.AddForeignKey(
                name: "fk_competition_events_competition_events_parent_event_id",
                table: "competition_events",
                column: "parent_event_id",
                principalTable: "competition_events",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_competition_events_competition_events_parent_event_id",
                table: "competition_events");

            migrationBuilder.DropIndex(
                name: "ix_competition_events_competition_id_parent_event_id_occurred_",
                table: "competition_events");

            migrationBuilder.DropIndex(
                name: "ix_competition_events_parent_event_id",
                table: "competition_events");

            migrationBuilder.DropColumn(
                name: "parent_event_id",
                table: "competition_events");
        }
    }
}
