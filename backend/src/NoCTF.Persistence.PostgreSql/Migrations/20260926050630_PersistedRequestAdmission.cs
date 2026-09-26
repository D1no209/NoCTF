using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class PersistedRequestAdmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "request_admission_leases",
                columns: table => new
                {
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request_admission_leases", x => new { x.key_hash, x.lease_id });
                });

            migrationBuilder.CreateTable(
                name: "request_admission_windows",
                columns: table => new
                {
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request_admission_windows", x => x.key_hash);
                });

            migrationBuilder.CreateIndex(
                name: "ix_request_admission_leases_expires_at",
                table: "request_admission_leases",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_request_admission_leases_lease_id",
                table: "request_admission_leases",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_request_admission_windows_expires_at",
                table: "request_admission_windows",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "request_admission_leases");

            migrationBuilder.DropTable(
                name: "request_admission_windows");
        }
    }
}
