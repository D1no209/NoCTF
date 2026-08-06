using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalTeamProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "team_profile_id",
                table: "teams",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "team_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    avatar_url = table.Column<string>(type: "text", nullable: true),
                    captain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    invitation_token = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_profiles", x => x.id);
                    table.CheckConstraint("ck_team_profiles_captain_is_member", "captain_id = ANY(member_ids)");
                    table.CheckConstraint("ck_team_profiles_invitation_token_length", "char_length(invitation_token) = 32");
                    table.CheckConstraint("ck_team_profiles_members_not_empty", "cardinality(member_ids) > 0");
                    table.ForeignKey(
                        name: "fk_team_profiles_users_captain_id",
                        column: x => x.captain_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_teams_competition_id_team_profile_id",
                table: "teams",
                columns: new[] { "competition_id", "team_profile_id" },
                unique: true,
                filter: "team_profile_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_teams_team_profile_id",
                table: "teams",
                column: "team_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_profiles_captain_id",
                table: "team_profiles",
                column: "captain_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_profiles_invitation_token",
                table: "team_profiles",
                column: "invitation_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_team_profiles_member_ids",
                table: "team_profiles",
                column: "member_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_team_profiles_normalized_name",
                table: "team_profiles",
                column: "normalized_name",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_teams_team_profiles_team_profile_id",
                table: "teams",
                column: "team_profile_id",
                principalTable: "team_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_teams_team_profiles_team_profile_id",
                table: "teams");

            migrationBuilder.DropTable(
                name: "team_profiles");

            migrationBuilder.DropIndex(
                name: "ix_teams_competition_id_team_profile_id",
                table: "teams");

            migrationBuilder.DropIndex(
                name: "ix_teams_team_profile_id",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "team_profile_id",
                table: "teams");
        }
    }
}
