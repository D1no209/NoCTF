using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class MfaSsoAuthenticationProof : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "authenticated_at",
                table: "sso_flows",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "external_mfa_authenticated_at",
                table: "sso_flows",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "external_mfa_trust_policy_id",
                table: "sso_flows",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "authenticated_at",
                table: "sso_flows");

            migrationBuilder.DropColumn(
                name: "external_mfa_authenticated_at",
                table: "sso_flows");

            migrationBuilder.DropColumn(
                name: "external_mfa_trust_policy_id",
                table: "sso_flows");
        }
    }
}
