using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.Configurations.Teams;

internal sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("teams");
        builder.HasKey(team => team.Id);
        builder.HasQueryFilter(team => team.DeletedAt == null);
        builder.Property(team => team.Name).HasMaxLength(128);
        builder.Property(team => team.NormalizedName).HasMaxLength(128);
        builder.Property(team => team.MemberIds).HasColumnType("uuid[]");
        builder.Property(team => team.RegistrationStatus).HasConversion<short>();
        builder.Property(team => team.InvitationToken).HasMaxLength(32);
        builder.HasIndex(team => new { team.CompetitionId, team.NormalizedName }).IsUnique()
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(team => new { team.CompetitionId, team.RegistrationStatus });
        builder.HasIndex(team => new { team.CompetitionId, team.TeamProfileId }).IsUnique()
            .HasFilter("team_profile_id IS NOT NULL AND deleted_at IS NULL");
        builder.HasIndex(team => team.InvitationToken).IsUnique();
        builder.HasIndex(team => team.MemberIds).HasMethod("gin");
        builder.HasOne<NoCTF.Domain.Competitions.Competition>().WithMany()
            .HasForeignKey(team => team.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TeamProfile>().WithMany()
            .HasForeignKey(team => team.TeamProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Identity.User>().WithMany()
            .HasForeignKey(team => team.CaptainId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_teams_captain_is_member",
                "captain_id = ANY(member_ids)");
            table.HasCheckConstraint(
                "ck_teams_members_not_empty",
                "cardinality(member_ids) > 0");
            table.HasCheckConstraint(
                "ck_teams_invitation_token_length",
                "char_length(invitation_token) = 32");
        });
    }
}
