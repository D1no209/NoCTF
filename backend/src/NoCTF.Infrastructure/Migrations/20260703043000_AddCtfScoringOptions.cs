using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260703043000_AddCtfScoringOptions")]
    public partial class AddCtfScoringOptions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Competitions"
                    ADD COLUMN IF NOT EXISTS "FirstBloodBonusPercent" double precision NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS "SecondBloodBonusPercent" double precision NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS "ThirdBloodBonusPercent" double precision NOT NULL DEFAULT 0;

                ALTER TABLE "Challenges"
                    ADD COLUMN IF NOT EXISTS "EnableBloodBonus" boolean NOT NULL DEFAULT false;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
