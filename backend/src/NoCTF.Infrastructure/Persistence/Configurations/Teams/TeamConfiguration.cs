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
        builder.HasQueryFilter(team => !team.Deletion.IsDeleted);
        builder.Property(team => team.Name).HasMaxLength(128);
        builder.Property(team => team.InvitationToken).HasMaxLength(32);
        builder.OwnsOne(team => team.Ban);
        builder.OwnsOne(team => team.Deletion);
        builder.HasIndex(team => new { team.CompetitionId, team.Name }).IsUnique();
        builder.HasIndex(team => new { team.CompetitionId, team.RegistrationStatus });
        builder.HasIndex(team => team.InvitationToken).IsUnique();
        builder.OwnsMany(team => team.Members, members =>
        {
            members.ToTable("team_members");
            members.WithOwner().HasForeignKey("TeamId");
            members.HasKey(member => member.Id);
            members.HasIndex("TeamId", nameof(TeamMember.MemberOrder)).IsUnique();
            members.HasIndex("TeamId", nameof(TeamMember.UserId)).IsUnique();
        });
    }
}
