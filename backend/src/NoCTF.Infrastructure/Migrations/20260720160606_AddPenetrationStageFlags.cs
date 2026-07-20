using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPenetrationStageFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_challenge_flags_CompetitionId_ChallengeId_TeamId_ValidStart~",
                table: "challenge_flags");

            migrationBuilder.AddColumn<Guid>(
                name: "ChallengeInstanceId",
                table: "submissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlagHash",
                table: "submissions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FlagLength",
                table: "submissions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChallengeInstanceId",
                table: "challenge_flags",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StageId",
                table: "challenge_flags",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_challenge_flags_CompetitionId_ChallengeId_TeamId_StageId_Ch~",
                table: "challenge_flags",
                columns: new[] { "CompetitionId", "ChallengeId", "TeamId", "StageId", "ChallengeInstanceId", "ValidStart", "ValidEnd" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_challenge_flags_CompetitionId_ChallengeId_TeamId_StageId_Ch~",
                table: "challenge_flags");

            migrationBuilder.DropColumn(
                name: "ChallengeInstanceId",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "FlagHash",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "FlagLength",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "ChallengeInstanceId",
                table: "challenge_flags");

            migrationBuilder.DropColumn(
                name: "StageId",
                table: "challenge_flags");

            migrationBuilder.CreateIndex(
                name: "IX_challenge_flags_CompetitionId_ChallengeId_TeamId_ValidStart~",
                table: "challenge_flags",
                columns: new[] { "CompetitionId", "ChallengeId", "TeamId", "ValidStart", "ValidEnd" });
        }
    }
}
