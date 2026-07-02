using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260702152000_AddChallengeInstanceLifecycle")]
    public partial class AddChallengeInstanceLifecycle : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "AwdGameBoxes"
                    ADD COLUMN IF NOT EXISTS "PortMappingsJson" text NOT NULL DEFAULT '{}',
                    ADD COLUMN IF NOT EXISTS "ExpiresAt" timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS "LastInstanceActionAt" timestamp with time zone NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
