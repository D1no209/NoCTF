using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Persistence;

internal sealed class LiveSoloProgramCaptureConfiguration : IEntityTypeConfiguration<LiveSoloProgramCapture>
{
    public void Configure(EntityTypeBuilder<LiveSoloProgramCapture> builder)
    {
        builder.ToTable("live_solo_program_captures"); builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.MediaSessionId });
        builder.HasIndex(x => x.MediaSessionId); builder.HasIndex(x => new { x.State, x.RequestedAt });
        builder.HasOne<LiveSoloMediaSession>().WithMany().HasForeignKey(x => x.MediaSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class LiveSoloProgramFrameConfiguration : IEntityTypeConfiguration<LiveSoloProgramFrame>
{
    public void Configure(EntityTypeBuilder<LiveSoloProgramFrame> builder)
    {
        builder.ToTable("live_solo_program_frames"); builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.MediaSessionId });
        builder.HasIndex(x => new { x.MediaSessionId, x.OccurredAt }).IsUnique();
        builder.HasOne<LiveSoloMediaSession>().WithMany().HasForeignKey(x => x.MediaSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Questions).WithOne().HasForeignKey(x => x.FrameId).OnDelete(DeleteBehavior.Cascade);
    }
}
internal sealed class LiveSoloProgramFrameQuestionConfiguration : IEntityTypeConfiguration<LiveSoloProgramFrameQuestion>
{
    public void Configure(EntityTypeBuilder<LiveSoloProgramFrameQuestion> builder)
    {
        builder.ToTable("live_solo_program_frame_questions"); builder.HasKey(x => new { x.FrameId, x.Position });
        builder.HasOne<LiveSoloRoundQuestion>().WithMany().HasForeignKey(x => x.RoundQuestionId).OnDelete(DeleteBehavior.Restrict);
    }
}
