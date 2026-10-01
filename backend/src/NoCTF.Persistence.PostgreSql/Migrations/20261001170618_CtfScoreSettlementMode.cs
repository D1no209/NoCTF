using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class CtfScoreSettlementMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "score_settlement_mode",
                table: "competition_mode_configurations",
                type: "smallint",
                nullable: true,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "score_settlement_mode",
                table: "competition_challenge_rules",
                type: "smallint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "score_settlement_mode",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "score_settlement_mode",
                table: "competition_challenge_rules");
        }
    }
}
