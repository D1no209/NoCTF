using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDataExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_exports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<short>(type: "smallint", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    include_protected_flags = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    purge_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    length = table.Column<long>(type: "bigint", nullable: true),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    failure_code = table.Column<short>(type: "smallint", nullable: true),
                    failure_detail = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_exports", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_data_exports_purge_at",
                table: "data_exports",
                column: "purge_at");

            migrationBuilder.CreateIndex(
                name: "ix_data_exports_requested_by_user_id_scope_competition_id_requ",
                table: "data_exports",
                columns: new[] { "requested_by_user_id", "scope", "competition_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_data_exports_status",
                table: "data_exports",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_exports");
        }
    }
}
