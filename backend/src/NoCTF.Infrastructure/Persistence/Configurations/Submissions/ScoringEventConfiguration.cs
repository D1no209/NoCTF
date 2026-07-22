using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Submissions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Submissions;

internal sealed class ScoringEventConfiguration : IEntityTypeConfiguration<ScoringEvent>
{
    public void Configure(EntityTypeBuilder<ScoringEvent> builder)
    {
        builder.ToTable("scoring_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProcessedWorkerId).HasMaxLength(128);
        builder.Property(x => x.EvaluatorVersion).HasMaxLength(128);
        builder.Property(x => x.SourceKey).HasMaxLength(256);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompetitionId, x.OccurredAt, x.Id });
        builder.HasIndex(x => x.SubmissionId);
        builder.HasIndex(x => new { x.CompetitionId, x.TeamId, x.CompetitionChallengeId, x.Kind, x.OccurredAt });
        builder.HasIndex(x => new { x.CompetitionId, x.Kind, x.SourceKey }).IsUnique()
            .HasFilter("\"IsDeleted\" = FALSE AND \"SourceKey\" IS NOT NULL");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.HasOne(x => x.Submission).WithMany().HasForeignKey(x => x.SubmissionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Challenges.CompetitionChallenge>()
            .WithMany()
            .HasForeignKey(scoringEvent => scoringEvent.CompetitionChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
