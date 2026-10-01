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
        builder.HasIndex(team => team.NormalizedName);
        builder.Property(team => team.TrackKey)
            .HasMaxLength(64)
            .HasDefaultValue(NoCTF.Domain.Competitions.CompetitionTrackConfiguration.DefaultTrackKey);
        builder.HasMany(team => team.Members)
            .WithOne()
            .HasForeignKey(member => member.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(team => team.Members).AutoInclude();
        builder.HasOne(team => team.CaptainMembership)
            .WithOne()
            .HasForeignKey<TeamCaptain>(captain => captain.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(team => team.CaptainMembership).AutoInclude();
        builder.Property(team => team.RegistrationStatus).HasConversion<short>();
        builder.Property(team => team.InvitationToken).HasMaxLength(32);
        builder.HasIndex(team => new { team.CompetitionId, team.RegistrationStatus });
        builder.HasIndex(team => new { team.CompetitionId, team.TrackKey });
        builder.HasIndex(team => team.InvitationToken).IsUnique();
        builder.HasOne<NoCTF.Domain.Competitions.Competition>().WithMany()
            .HasForeignKey(team => team.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(team => team.AvatarFile).WithMany()
            .HasForeignKey(team => team.AvatarFileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(team => team.WriteUpFile).WithMany()
            .HasForeignKey(team => team.WriteUpFileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("team_members");
        builder.HasKey(member => new { member.TeamId, member.UserId });
        builder.HasIndex(member => member.UserId);
        builder.HasIndex(member => new { member.CompetitionId, member.UserId });
        builder.HasOne(member => member.ActiveMembership).WithOne()
            .HasForeignKey<ActiveTeamMembership>(membership => new { membership.TeamId, membership.UserId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(member => member.ActiveMembership).AutoInclude();
        builder.HasOne<NoCTF.Domain.Identity.User>().WithMany()
            .HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ActiveTeamMembershipConfiguration : IEntityTypeConfiguration<ActiveTeamMembership>
{
    public void Configure(EntityTypeBuilder<ActiveTeamMembership> builder)
    {
        builder.ToTable("active_team_memberships");
        builder.HasKey(membership => new { membership.CompetitionId, membership.UserId });
        builder.HasIndex(membership => new { membership.TeamId, membership.UserId }).IsUnique();
    }
}

internal sealed class TeamCaptainConfiguration : IEntityTypeConfiguration<TeamCaptain>
{
    public void Configure(EntityTypeBuilder<TeamCaptain> builder)
    {
        builder.ToTable("team_captains");
        builder.HasKey(captain => captain.TeamId);
        builder.HasOne<TeamMember>().WithMany()
            .HasForeignKey(captain => new { captain.TeamId, captain.UserId })
            .HasPrincipalKey(member => new { member.TeamId, member.UserId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
