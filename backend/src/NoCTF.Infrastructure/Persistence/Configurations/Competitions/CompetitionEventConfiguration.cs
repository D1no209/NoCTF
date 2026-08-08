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
        builder.Property(item => item.Kind).HasConversion<short>();
        builder.Property(item => item.Level).HasConversion<short>();
        builder.Property(item => item.Visibility).HasConversion<short>();
        builder.Property(item => item.SubjectType).HasConversion<short>();
        builder.Property(item => item.RelatedType).HasConversion<short>();
        builder.Property(item => item.PayloadJson).HasColumnType("jsonb");
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
        }).HasFilter("related_type IS NOT NULL");
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
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_competition_events_related_reference",
                "(related_type IS NULL) = (related_id IS NULL)");
            table.HasCheckConstraint(
                "ck_competition_events_payload",
                "jsonb_typeof(payload_json) = 'object' AND payload_json ? 'schemaVersion'");
        });
    }
}
