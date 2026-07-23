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
        builder.Property(x => x.Kind).HasConversion<short>();
        builder.Property(x => x.Result).HasConversion<short>();
        builder.Property(x => x.FailureCode).HasConversion<short>();
        builder.Property(x => x.SpecificationKind).HasConversion<short>();
        builder.HasIndex(x => new { x.CompetitionId, x.OccurredAt, x.Id });
        builder.HasIndex(x => x.SubmissionId);
        builder.HasIndex(x => new { x.CompetitionId, x.TeamId, x.CompetitionChallengeId, x.Kind, x.OccurredAt });
        builder.HasIndex(x => x.SubmissionId).IsUnique()
            .HasFilter("submission_id IS NOT NULL AND deleted_at IS NULL");
        builder.HasQueryFilter(x => x.DeletedAt == null);
        builder.HasOne(x => x.Submission).WithMany().HasForeignKey(x => x.SubmissionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Challenges.CompetitionChallenge>()
            .WithMany()
            .HasForeignKey(scoringEvent => scoringEvent.CompetitionChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Competitions.Competition>().WithMany()
            .HasForeignKey(scoringEvent => scoringEvent.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Teams.Team>().WithMany()
            .HasForeignKey(scoringEvent => scoringEvent.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Teams.Team>().WithMany()
            .HasForeignKey(scoringEvent => scoringEvent.VictimTeamId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
