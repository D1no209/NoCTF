using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AwdGenerationEpochs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "generation_epoch_active_ticks",
                table: "awd_round_rules",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "generation_epoch_id",
                table: "awd_round_rules",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "generation_epoch_round",
                table: "awd_round_rules",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "generation_epoch_active_ticks",
                table: "awd_round_rules");

            migrationBuilder.DropColumn(
                name: "generation_epoch_id",
                table: "awd_round_rules");

            migrationBuilder.DropColumn(
                name: "generation_epoch_round",
                table: "awd_round_rules");
        }
    }
}
