using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class StaffWebhookDeliveryClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "fk_competition_staff_webhook_events_competitions_competition_id",
                table: "competition_staff_webhook_events",
                column: "competition_id",
                principalTable: "competitions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_competition_staff_webhook_streams_competitions_competition_",
                table: "competition_staff_webhook_streams",
                column: "competition_id",
                principalTable: "competitions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_competition_staff_webhook_work_items_competitions_competiti",
                table: "competition_staff_webhook_work_items",
                column: "competition_id",
                principalTable: "competitions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_competition_staff_webhook_events_competitions_competition_id",
                table: "competition_staff_webhook_events");

            migrationBuilder.DropForeignKey(
                name: "fk_competition_staff_webhook_streams_competitions_competition_",
                table: "competition_staff_webhook_streams");

            migrationBuilder.DropForeignKey(
                name: "fk_competition_staff_webhook_work_items_competitions_competiti",
                table: "competition_staff_webhook_work_items");
        }
    }
}
