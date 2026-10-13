using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AwdSecureIssuance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "delivery_mode",
                table: "awd_round_store_budgets",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<string>(
                name: "injection_command",
                table: "awd_round_store_budgets",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "injection_timeout_seconds",
                table: "awd_round_store_budgets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "preserves_historical_flags",
                table: "awd_round_store_budgets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "target_service_name",
                table: "awd_round_store_budgets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "flag_body_template",
                table: "awd_round_rules",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "flag_header",
                table: "awd_round_rules",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "delivery_attempt_id",
                table: "awd_flag_issues",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "awd_flag_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    protected_secret = table.Column<byte[]>(type: "bytea", maxLength: 256, nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_flag_keys", x => x.id);
                    table.UniqueConstraint("ak_awd_flag_keys_competition_id_version", x => new { x.competition_id, x.version });
                    table.ForeignKey(
                        name: "fk_awd_flag_keys_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_awd_flag_issues_competition_id_key_version",
                table: "awd_flag_issues",
                columns: new[] { "competition_id", "key_version" });

            migrationBuilder.AddForeignKey(
                name: "fk_awd_flag_issues_awd_flag_keys_competition_id_key_version",
                table: "awd_flag_issues",
                columns: new[] { "competition_id", "key_version" },
                principalTable: "awd_flag_keys",
                principalColumns: new[] { "competition_id", "version" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_awd_flag_issues_awd_flag_keys_competition_id_key_version",
                table: "awd_flag_issues");

            migrationBuilder.DropTable(
                name: "awd_flag_keys");

            migrationBuilder.DropIndex(
                name: "ix_awd_flag_issues_competition_id_key_version",
                table: "awd_flag_issues");

            migrationBuilder.DropColumn(
                name: "delivery_mode",
                table: "awd_round_store_budgets");

            migrationBuilder.DropColumn(
                name: "injection_command",
                table: "awd_round_store_budgets");

            migrationBuilder.DropColumn(
                name: "injection_timeout_seconds",
                table: "awd_round_store_budgets");

            migrationBuilder.DropColumn(
                name: "preserves_historical_flags",
                table: "awd_round_store_budgets");

            migrationBuilder.DropColumn(
                name: "target_service_name",
                table: "awd_round_store_budgets");

            migrationBuilder.DropColumn(
                name: "flag_body_template",
                table: "awd_round_rules");

            migrationBuilder.DropColumn(
                name: "flag_header",
                table: "awd_round_rules");

            migrationBuilder.DropColumn(
                name: "delivery_attempt_id",
                table: "awd_flag_issues");
        }
    }
}
