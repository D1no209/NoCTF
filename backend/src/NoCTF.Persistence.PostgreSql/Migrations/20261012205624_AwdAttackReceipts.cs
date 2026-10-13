using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AwdAttackReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "attack_outcome",
                table: "awd_fact_details",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "receipt_scorable",
                table: "awd_fact_details",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "received_active_ticks",
                table: "awd_fact_details",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "received_round_number",
                table: "awd_fact_details",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attack_outcome",
                table: "awd_fact_details");

            migrationBuilder.DropColumn(
                name: "receipt_scorable",
                table: "awd_fact_details");

            migrationBuilder.DropColumn(
                name: "received_active_ticks",
                table: "awd_fact_details");

            migrationBuilder.DropColumn(
                name: "received_round_number",
                table: "awd_fact_details");
        }
    }
}
