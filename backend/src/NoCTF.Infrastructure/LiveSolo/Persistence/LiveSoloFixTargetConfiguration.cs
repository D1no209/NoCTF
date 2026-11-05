using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.LiveSolo.Persistence;

internal sealed class LiveSoloFixTargetConfiguration : IEntityTypeConfiguration<LiveSoloFixTargetOwnership>
{
    public void Configure(EntityTypeBuilder<LiveSoloFixTargetOwnership> builder)
    {
        builder.ToTable("live_solo_fix_targets");
        builder.HasIndex(value => new { value.RoundQuestionId, value.Side });
        builder.HasOne<RuntimeInstance>().WithMany().HasForeignKey(value => value.RuntimeInstanceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LiveSoloRoundQuestion>().WithMany().HasForeignKey(value => value.RoundQuestionId).OnDelete(DeleteBehavior.Cascade);
    }
}
