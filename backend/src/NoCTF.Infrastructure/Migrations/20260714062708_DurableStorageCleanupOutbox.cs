using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DurableStorageCleanupOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StorageCleanupItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    NotBefore = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockOwner = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageCleanupItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_templates_attachment_storage_key",
                table: "ChallengeTemplates",
                column: "AttachmentStorageKey",
                filter: "\"AttachmentStorageKey\" IS NOT NULL")
                .Annotation("Npgsql:IndexMethod", "hash");

            migrationBuilder.CreateIndex(
                name: "ix_challenge_templates_patch_template_storage_key",
                table: "ChallengeTemplates",
                column: "PatchTemplateStorageKey",
                filter: "\"PatchTemplateStorageKey\" IS NOT NULL")
                .Annotation("Npgsql:IndexMethod", "hash");

            migrationBuilder.CreateIndex(
                name: "ix_challenges_attachment_storage_key",
                table: "Challenges",
                column: "AttachmentStorageKey",
                filter: "\"AttachmentStorageKey\" IS NOT NULL")
                .Annotation("Npgsql:IndexMethod", "hash");

            migrationBuilder.CreateIndex(
                name: "ix_challenges_patch_template_storage_key",
                table: "Challenges",
                column: "PatchTemplateStorageKey",
                filter: "\"PatchTemplateStorageKey\" IS NOT NULL")
                .Annotation("Npgsql:IndexMethod", "hash");

            migrationBuilder.CreateIndex(
                name: "ix_awdpatchsubmissions_patch_archive_key",
                table: "AwdpPatchSubmissions",
                column: "PatchArchiveUrl",
                filter: "\"PatchArchiveUrl\" <> ''")
                .Annotation("Npgsql:IndexMethod", "hash");

            migrationBuilder.CreateIndex(
                name: "ix_storagecleanupitems_dispatch",
                table: "StorageCleanupItems",
                columns: new[] { "NotBefore", "LockedUntil" });

            migrationBuilder.CreateIndex(
                name: "ux_storagecleanupitems_storage_key",
                table: "StorageCleanupItems",
                column: "StorageKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StorageCleanupItems");

            migrationBuilder.DropIndex(
                name: "ix_challenge_templates_attachment_storage_key",
                table: "ChallengeTemplates");

            migrationBuilder.DropIndex(
                name: "ix_challenge_templates_patch_template_storage_key",
                table: "ChallengeTemplates");

            migrationBuilder.DropIndex(
                name: "ix_challenges_attachment_storage_key",
                table: "Challenges");

            migrationBuilder.DropIndex(
                name: "ix_challenges_patch_template_storage_key",
                table: "Challenges");

            migrationBuilder.DropIndex(
                name: "ix_awdpatchsubmissions_patch_archive_key",
                table: "AwdpPatchSubmissions");
        }
    }
}
