using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionChallengeTiming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "applied_timing_revision",
                table: "gameplay_facts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<short>(
                name: "time_eligibility",
                table: "gameplay_facts",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<Guid>(
                name: "applied_timing_revision",
                table: "competition_challenges",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "auto_open_at",
                table: "competition_challenges",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "opening_state",
                table: "competition_challenges",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<long>(
                name: "scoring_ends_at",
                table: "competition_challenges",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "submission_deadline_at",
                table: "competition_challenges",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "timing_revision",
                table: "competition_challenges",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "applied_timing_revision",
                table: "gameplay_facts");

            migrationBuilder.DropColumn(
                name: "time_eligibility",
                table: "gameplay_facts");

            migrationBuilder.DropColumn(
                name: "applied_timing_revision",
                table: "competition_challenges");

            migrationBuilder.DropColumn(
                name: "auto_open_at",
                table: "competition_challenges");

            migrationBuilder.DropColumn(
                name: "opening_state",
                table: "competition_challenges");

            migrationBuilder.DropColumn(
                name: "scoring_ends_at",
                table: "competition_challenges");

            migrationBuilder.DropColumn(
                name: "submission_deadline_at",
                table: "competition_challenges");

            migrationBuilder.DropColumn(
                name: "timing_revision",
                table: "competition_challenges");
        }
    }
}
