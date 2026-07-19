using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.Configurations.Teams;

internal sealed class TeamInvitationConfiguration : IEntityTypeConfiguration<TeamInvitation>
{
    public void Configure(EntityTypeBuilder<TeamInvitation> builder)
    {
        builder.ToTable("team_invitations");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompetitionId, x.InvitedUserId })
            .IsUnique().HasFilter("\"Status\" = 0");
        builder.HasIndex(x => new { x.TeamId, x.Status, x.ExpiresAt });
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
    }
}
