using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Persistence.Configurations.Competitions;

public sealed class CompetitionEventConfiguration
    : IEntityTypeConfiguration<CompetitionEvent>
{
    public void Configure(EntityTypeBuilder<CompetitionEvent> builder)
    {
        builder.ToTable("competition_events");
        builder.HasKey(item => item.Id);
        var discriminator = builder.HasDiscriminator(item => item.Kind);
        foreach (var entry in CompetitionEventGeneratedCatalog.Entries)
            discriminator.HasValue(entry.Leaf, entry.Kind);
        builder.Property(item => item.Level).HasConversion<short>();
        builder.Property(item => item.Visibility).HasConversion<short>();
        builder.Property(item => item.SubjectType).HasConversion<short>();
        builder.Property(item => item.RelatedType).HasConversion<short>();
        builder.Property(item => item.CompetitionStatus).HasConversion<short>();
        builder.Property(item => item.PreviousCompetitionStatus).HasConversion<short>();
        builder.Property(item => item.LeaderboardVisibility).HasConversion<short>();
        builder.Property(item => item.PreviousLeaderboardVisibility).HasConversion<short>();
        builder.Property(item => item.CompetitionAccessMode).HasConversion<short>();
        builder.Property(item => item.PreviousCompetitionAccessMode).HasConversion<short>();
        builder.Property(item => item.CompetitionAudienceChangeKind).HasConversion<short>();
        builder.Property(item => item.TeamRegistrationStatus).HasConversion<short>();
        builder.Property(item => item.GameplayFactKind).HasConversion<short>();
        builder.Property(item => item.GameplayFactState).HasConversion<short>();
        builder.Property(item => item.GameplayFactResult).HasConversion<short>();
        builder.Property(item => item.RuntimeState).HasConversion<short>();
        builder.Property(item => item.RuntimeCleanupResult).HasConversion<short>();
        builder.Property(item => item.QuestionStatus).HasConversion<short>();
        builder.Property(item => item.AwdpFixOutcome).HasConversion<short>();
        builder.Property(item => item.GameplayFactFailureCode).HasConversion<short>();
        builder.OwnsMany(item => item.TrackKeys, keys =>
        {
            keys.ToTable("competition_event_track_keys");
            keys.WithOwner().HasForeignKey("competition_event_id");
            keys.HasKey(key => key.Id);
            keys.HasIndex("competition_event_id", nameof(CompetitionEventTrackKey.Position)).IsUnique();
        });
        // Domain intentionally has no EF Core dependency, so compound keyset/filter indexes
        // cannot use EF's IndexAttribute and are declared at the provider boundary.
        builder.HasIndex(item => new { item.CompetitionId, item.OccurredAt, item.Id });
        builder.HasIndex(item => new { item.CompetitionId, item.Kind, item.OccurredAt, item.Id });
        builder.HasIndex(item => new { item.CompetitionId, item.Level, item.OccurredAt, item.Id });
        builder.HasIndex(item => new { item.CompetitionId, item.Visibility, item.OccurredAt, item.Id });
        builder.HasIndex(item => new
        {
            item.CompetitionId,
            item.SubjectType,
            item.SubjectId,
            item.OccurredAt,
            item.Id
        });
        builder.HasIndex(item => new
        {
            item.CompetitionId,
            item.RelatedType,
            item.RelatedId,
            item.OccurredAt,
            item.Id
        });
        builder.HasIndex(item => new { item.ParentEventId, item.OccurredAt, item.Id });
        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(item => item.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(item => item.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompetitionEvent>()
            .WithMany()
            .HasForeignKey(item => item.ParentEventId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
