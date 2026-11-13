using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionCredentialBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "competition_credential_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    source_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_credential_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "competition_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    password_ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    password_version = table.Column<int>(type: "integer", nullable: false),
                    password_updated_at = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_credentials_competition_credential_batches_batc",
                        column: x => x.batch_id,
                        principalTable: "competition_credential_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_competition_credentials_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_credentials_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_competition_credential_batches_competition_id_created_at",
                table: "competition_credential_batches",
                columns: new[] { "competition_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_credentials_batch_id_user_id",
                table: "competition_credentials",
                columns: new[] { "batch_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_credentials_competition_id_user_id",
                table: "competition_credentials",
                columns: new[] { "competition_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_credentials_team_id",
                table: "competition_credentials",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_credentials_user_id",
                table: "competition_credentials",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "competition_credentials");

            migrationBuilder.DropTable(
                name: "competition_credential_batches");
        }
    }
}
