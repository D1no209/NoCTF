using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Storage;

namespace NoCTF.Infrastructure.LiveSolo.Persistence;

internal sealed class LiveSoloOpenedContentConfiguration : IEntityTypeConfiguration<LiveSoloOpenedContent>
{
    public void Configure(EntityTypeBuilder<LiveSoloOpenedContent> builder)
    {
        builder.ToTable("live_solo_opened_contents");
        builder.HasOne<LiveSoloRoundQuestion>().WithOne(x => x.OpenedContent)
            .HasForeignKey<LiveSoloOpenedContent>(x => x.RoundQuestionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Tags).WithOne().HasForeignKey(x => x.RoundQuestionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Hints).WithOne().HasForeignKey(x => x.RoundQuestionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Attachments).WithOne().HasForeignKey(x => x.RoundQuestionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Tags).AutoInclude();
        builder.Navigation(x => x.Hints).AutoInclude();
        builder.Navigation(x => x.Attachments).AutoInclude();
    }
}

internal sealed class LiveSoloOpenedTagConfiguration : IEntityTypeConfiguration<LiveSoloOpenedTag>
{
    public void Configure(EntityTypeBuilder<LiveSoloOpenedTag> builder)
    {
        builder.ToTable("live_solo_opened_tags"); builder.HasKey(x => new { x.RoundQuestionId, x.Position });
    }
}

internal sealed class LiveSoloOpenedHintConfiguration : IEntityTypeConfiguration<LiveSoloOpenedHint>
{
    public void Configure(EntityTypeBuilder<LiveSoloOpenedHint> builder)
    {
        builder.ToTable("live_solo_opened_hints"); builder.HasKey(x => new { x.RoundQuestionId, x.HintId });
        builder.HasIndex(x => new { x.RoundQuestionId, x.Position }).IsUnique();
    }
}

internal sealed class LiveSoloOpenedAttachmentConfiguration : IEntityTypeConfiguration<LiveSoloOpenedAttachment>
{
    public void Configure(EntityTypeBuilder<LiveSoloOpenedAttachment> builder)
    {
        builder.ToTable("live_solo_opened_attachments"); builder.HasKey(x => new { x.RoundQuestionId, x.AttachmentId });
        builder.HasIndex(x => new { x.RoundQuestionId, x.Position }).IsUnique();
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
    }
}
