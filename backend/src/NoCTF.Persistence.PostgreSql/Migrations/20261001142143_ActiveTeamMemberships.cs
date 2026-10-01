using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ActiveTeamMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_team_members_competition_id_user_id",
                table: "team_members");

            migrationBuilder.CreateTable(
                name: "active_team_memberships",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_active_team_memberships", x => new { x.competition_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_active_team_memberships_team_members_team_id_user_id",
                        columns: x => new { x.team_id, x.user_id },
                        principalTable: "team_members",
                        principalColumns: new[] { "team_id", "user_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_team_members_competition_id_user_id",
                table: "team_members",
                columns: new[] { "competition_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_active_team_memberships_team_id_user_id",
                table: "active_team_memberships",
                columns: new[] { "team_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "active_team_memberships");

            migrationBuilder.DropIndex(
                name: "ix_team_members_competition_id_user_id",
                table: "team_members");

            migrationBuilder.CreateIndex(
                name: "ix_team_members_competition_id_user_id",
                table: "team_members",
                columns: new[] { "competition_id", "user_id" },
                unique: true);
        }
    }
}
