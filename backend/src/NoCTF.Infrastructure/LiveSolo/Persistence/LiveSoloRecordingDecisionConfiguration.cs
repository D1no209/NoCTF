using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Persistence;

internal sealed class LiveSoloRecordingDecisionConfiguration : IEntityTypeConfiguration<LiveSoloRecordingDecision>
{
    public void Configure(EntityTypeBuilder<LiveSoloRecordingDecision> builder)
    {
        builder.ToTable("live_solo_recording_decisions");
        builder.HasIndex(x => new { x.RecordingId, x.OccurredAt });
        builder.HasOne<LiveSoloMediaSession>().WithMany().HasForeignKey(x => x.MediaSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
