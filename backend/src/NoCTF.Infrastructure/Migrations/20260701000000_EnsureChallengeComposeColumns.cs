using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260701000000_EnsureChallengeComposeColumns")]
    public partial class EnsureChallengeComposeColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Challenges"
                    ADD COLUMN IF NOT EXISTS "ContainerMode" integer NOT NULL DEFAULT 0;

                ALTER TABLE "Challenges"
                    ADD COLUMN IF NOT EXISTS "ComposeProjectName" text NULL;

                ALTER TABLE "Challenges"
                    ADD COLUMN IF NOT EXISTS "ComposeYaml" text NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
