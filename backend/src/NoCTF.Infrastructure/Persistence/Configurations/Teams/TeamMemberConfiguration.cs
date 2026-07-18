using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.Configurations.Teams;

internal sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("team_members");
        builder.HasKey(member => member.Id);
        builder.HasIndex(member => new { member.CompetitionId, member.UserId }).IsUnique();
        builder.HasIndex(member => new { member.TeamId, member.UserId }).IsUnique();
    }
}
