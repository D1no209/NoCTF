using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AwdConfigurationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "awd_effective_from_round",
                table: "competition_events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_effective_through_round",
                table: "competition_events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_rule_timing",
                table: "competition_events",
                type: "smallint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "awd_effective_from_round",
                table: "competition_events");

            migrationBuilder.DropColumn(
                name: "awd_effective_through_round",
                table: "competition_events");

            migrationBuilder.DropColumn(
                name: "awd_rule_timing",
                table: "competition_events");
        }
    }
}
