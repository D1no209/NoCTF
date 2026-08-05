using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.Configurations.Competitions;

public sealed class CompetitionEventConfiguration
    : IEntityTypeConfiguration<CompetitionEvent>
{
    public void Configure(EntityTypeBuilder<CompetitionEvent> builder)
    {
        builder.ToTable("competition_events");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Reason).HasMaxLength(512);
        // Domain intentionally has no EF Core dependency, so compound keyset/filter indexes
        // cannot use EF's IndexAttribute and are declared at the provider boundary.
        builder.HasIndex(item => new { item.CompetitionId, item.OccurredAt, item.Id });
        builder.HasIndex(item => new { item.CompetitionId, item.Kind, item.OccurredAt, item.Id });
        builder.HasIndex(item => new { item.CompetitionId, item.Level, item.OccurredAt, item.Id });
        builder.HasIndex(item => new { item.CompetitionId, item.TeamId, item.OccurredAt, item.Id });
        builder.HasIndex(item => new { item.CompetitionId, item.ActorUserId, item.OccurredAt, item.Id });
        builder.HasIndex(item => new
        {
            item.CompetitionId,
            item.CompetitionChallengeId,
            item.OccurredAt,
            item.Id
        });
        builder.HasIndex(item => new
        {
            item.CompetitionId,
            item.RuntimeInstanceId,
            item.OccurredAt,
            item.Id
        });
        builder.HasIndex(item => new
        {
            item.CompetitionId,
            item.ScoringEventId,
            item.OccurredAt,
            item.Id
        });
        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(item => item.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(item => item.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(item => item.RelatedUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>()
            .WithMany()
            .HasForeignKey(item => item.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompetitionChallenge>()
            .WithMany()
            .HasForeignKey(item => item.CompetitionChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompetitionChallengeHint>()
            .WithMany()
            .HasForeignKey(item => item.HintId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RuntimeInstance>()
            .WithMany()
            .HasForeignKey(item => item.RuntimeInstanceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Submission>()
            .WithMany()
            .HasForeignKey(item => item.SubmissionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ScoringEvent>()
            .WithMany()
            .HasForeignKey(item => item.ScoringEventId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompetitionQuestion>()
            .WithMany()
            .HasForeignKey(item => item.QuestionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
