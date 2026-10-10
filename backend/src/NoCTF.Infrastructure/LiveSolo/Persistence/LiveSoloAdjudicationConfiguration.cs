using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.LiveSolo.Persistence;

internal sealed class LiveSoloAdjudicationConfiguration : IEntityTypeConfiguration<LiveSoloAdjudication>
{
    public void Configure(EntityTypeBuilder<LiveSoloAdjudication> builder)
    {
        builder.ToTable("live_solo_adjudications");
        builder.HasIndex(x => new { x.MatchId, x.OccurredAt });
        builder.HasOne<LiveSoloMatch>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LiveSoloRound>().WithMany().HasForeignKey(x => x.RoundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.ForfeitingTeamId).OnDelete(DeleteBehavior.Restrict);
    }
}
