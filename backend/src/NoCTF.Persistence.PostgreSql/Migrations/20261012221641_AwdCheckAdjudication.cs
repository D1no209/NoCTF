using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AwdCheckAdjudication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "awd_adjudication_action",
                table: "competition_events",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_health",
                table: "competition_events",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_settlement",
                table: "competition_events",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "previous_awd_health",
                table: "competition_events",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "previous_awd_settlement",
                table: "competition_events",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "adjudicated_at",
                table: "awd_fact_details",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "evidence_complete",
                table: "awd_fact_details",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "awd_adjudication_action",
                table: "competition_events");

            migrationBuilder.DropColumn(
                name: "awd_health",
                table: "competition_events");

            migrationBuilder.DropColumn(
                name: "awd_settlement",
                table: "competition_events");

            migrationBuilder.DropColumn(
                name: "previous_awd_health",
                table: "competition_events");

            migrationBuilder.DropColumn(
                name: "previous_awd_settlement",
                table: "competition_events");

            migrationBuilder.DropColumn(
                name: "adjudicated_at",
                table: "awd_fact_details");

            migrationBuilder.DropColumn(
                name: "evidence_complete",
                table: "awd_fact_details");
        }
    }
}
