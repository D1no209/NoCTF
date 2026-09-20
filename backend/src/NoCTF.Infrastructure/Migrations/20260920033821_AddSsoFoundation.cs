using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSsoFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "external_identity_bound_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_identity_namespace",
                table: "users",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "external_identity_protocol",
                table: "users",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "external_identity_provider_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_identity_subject",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sso_configuration",
                table: "platform_settings",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    xml = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.UpdateData(
                table: "platform_settings",
                keyColumn: "id",
                keyValue: (short)1,
                column: "sso_configuration",
                value: "{\"schemaVersion\":1,\"enabled\":false,\"publicBaseUrl\":\"\",\"providers\":[]}");

            migrationBuilder.CreateIndex(
                name: "ix_users_external_identity_provider_id_external_identity_subje",
                table: "users",
                columns: new[] { "external_identity_provider_id", "external_identity_subject" },
                unique: true,
                filter: "external_identity_provider_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_external_identity_complete",
                table: "users",
                sql: "(\"external_identity_provider_id\" IS NULL AND \"external_identity_protocol\" IS NULL AND \"external_identity_namespace\" IS NULL AND \"external_identity_subject\" IS NULL AND \"external_identity_bound_at\" IS NULL) OR (\"external_identity_provider_id\" IS NOT NULL AND \"external_identity_protocol\" IS NOT NULL AND \"external_identity_namespace\" IS NOT NULL AND \"external_identity_subject\" IS NOT NULL AND \"external_identity_bound_at\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_platform_settings_sso_configuration",
                table: "platform_settings",
                sql: "jsonb_typeof(\"sso_configuration\") = 'object' AND (\"sso_configuration\" ->> 'schemaVersion')::integer = 1 AND jsonb_typeof(\"sso_configuration\" -> 'providers') = 'array' AND jsonb_array_length(\"sso_configuration\" -> 'providers') <= 16");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropIndex(
                name: "ix_users_external_identity_provider_id_external_identity_subje",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_external_identity_complete",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_platform_settings_sso_configuration",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "external_identity_bound_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "external_identity_namespace",
                table: "users");

            migrationBuilder.DropColumn(
                name: "external_identity_protocol",
                table: "users");

            migrationBuilder.DropColumn(
                name: "external_identity_provider_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "external_identity_subject",
                table: "users");

            migrationBuilder.DropColumn(
                name: "sso_configuration",
                table: "platform_settings");
        }
    }
}
