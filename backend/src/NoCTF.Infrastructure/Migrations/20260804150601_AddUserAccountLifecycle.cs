using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAccountLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_normalized_email",
                table: "users");

            migrationBuilder.AddColumn<short>(
                name: "account_status",
                table: "users",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.CreateTable(
                name: "user_account_lifecycle_audits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_user_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<short>(type: "smallint", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_account_lifecycle_audits", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true,
                filter: "normalized_email <> ''");

            migrationBuilder.CreateIndex(
                name: "ix_user_account_lifecycle_audits_actor_user_id_occurred_at",
                table: "user_account_lifecycle_audits",
                columns: new[] { "actor_user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_account_lifecycle_audits_target_user_id_occurred_at",
                table: "user_account_lifecycle_audits",
                columns: new[] { "target_user_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_account_lifecycle_audits");

            migrationBuilder.DropIndex(
                name: "ix_users_normalized_email",
                table: "users");

            migrationBuilder.DropColumn(
                name: "account_status",
                table: "users");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true);
        }
    }
}
