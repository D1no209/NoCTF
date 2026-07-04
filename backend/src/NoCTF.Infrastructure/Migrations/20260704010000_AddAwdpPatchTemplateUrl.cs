using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260704010000_AddAwdpPatchTemplateUrl")]
    public partial class AddAwdpPatchTemplateUrl : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "ChallengeTemplates"
                    ADD COLUMN IF NOT EXISTS "PatchTemplateUrl" text NULL;

                ALTER TABLE "Challenges"
                    ADD COLUMN IF NOT EXISTS "PatchTemplateUrl" text NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
