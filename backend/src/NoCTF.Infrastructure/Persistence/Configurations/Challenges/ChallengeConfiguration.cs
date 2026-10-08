using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Challenges;

internal sealed class ChallengeEntityConfiguration : IEntityTypeConfiguration<Challenge>
{
    public void Configure(EntityTypeBuilder<Challenge> builder)
    {
        builder.ToTable("challenges");
        builder.HasKey(challenge => challenge.Id);
        builder.HasAlternateKey(challenge => new { challenge.Id, challenge.Mode });
        builder.HasQueryFilter(challenge => challenge.DeletedAt == null);
        builder.Property(challenge => challenge.Title).HasMaxLength(160);
        builder.HasIndex(challenge => challenge.NormalizedTitle);
        builder.HasIndex(challenge => challenge.NormalizedDirection);
        builder.Property(challenge => challenge.Direction).HasMaxLength(96);
        builder.HasMany(challenge => challenge.Managers)
            .WithOne()
            .HasForeignKey(manager => manager.ChallengeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(challenge => challenge.Managers).AutoInclude();
        builder.HasDiscriminator(challenge => challenge.Mode)
            .HasValue<CtfChallenge>(GameMode.Ctf)
            .HasValue<AwdChallenge>(GameMode.Awd)
            .HasValue<AwdpChallenge>(GameMode.Awdp)
            .HasValue<KohChallenge>(GameMode.Koh)
            .HasValue<LiveSoloChallenge>(GameMode.LiveSolo);
        builder.Property(challenge => challenge.Mode)
            .HasConversion<GameModeStringConverter>()
            .HasMaxLength(8);
        builder.HasOne(challenge => challenge.Definition)
            .WithOne()
            .HasForeignKey<ChallengeDefinition>(definition => definition.ChallengeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(challenge => challenge.Definition).AutoInclude();
        builder.Property(challenge => challenge.Visibility).HasConversion<short>();
        builder.HasOne<NoCTF.Domain.Identity.User>().WithMany()
            .HasForeignKey(challenge => challenge.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(challenge => challenge.Attachments)
            .WithOne()
            .HasForeignKey(attachment => attachment.ChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ChallengeManagerConfiguration : IEntityTypeConfiguration<ChallengeManager>
{
    public void Configure(EntityTypeBuilder<ChallengeManager> builder)
    {
        builder.ToTable("challenge_managers");
        builder.HasKey(manager => new { manager.ChallengeId, manager.UserId });
    }
}

internal sealed class ChallengeAttachmentConfiguration
    : IEntityTypeConfiguration<ChallengeAttachment>
{
    public void Configure(EntityTypeBuilder<ChallengeAttachment> builder)
    {
        builder.ToTable("challenge_attachments");
        builder.HasKey(attachment => attachment.Id);
        builder.HasOne(attachment => attachment.File).WithMany()
            .HasForeignKey(attachment => attachment.FileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
