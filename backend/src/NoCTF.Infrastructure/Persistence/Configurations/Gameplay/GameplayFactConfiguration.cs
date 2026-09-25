using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Infrastructure.Persistence.Configurations.Gameplay;

internal sealed class GameplayFactConfiguration : IEntityTypeConfiguration<GameplayFact>
{
    public void Configure(EntityTypeBuilder<GameplayFact> builder)
    {
        builder.ToTable("gameplay_facts");
        builder.HasKey(fact => fact.Id);
        builder.HasDiscriminator(fact => fact.Kind)
            .HasValue<FlagAttemptGameplayFact>(GameplayFactKind.FlagAttempt)
            .HasValue<BreakAttemptGameplayFact>(GameplayFactKind.BreakAttempt)
            .HasValue<FixAttemptGameplayFact>(GameplayFactKind.FixAttempt)
            .HasValue<HintUnlockGameplayFact>(GameplayFactKind.HintUnlock)
            .HasValue<ManualAdjustmentGameplayFact>(GameplayFactKind.ManualAdjustment)
            .HasValue<AwdServiceTransitionGameplayFact>(GameplayFactKind.AwdServiceTransition)
            .HasValue<KohControlObservationGameplayFact>(GameplayFactKind.KohControlObservation);
        builder.Property(fact => fact.ReferenceKind).HasConversion<short>();
        builder.Property(fact => fact.State).HasConversion<short>();
        builder.Property(fact => fact.Result).HasConversion<short>();
        builder.Property(fact => fact.FailureCode).HasConversion<short>();
        builder.HasIndex(fact => new { fact.CompetitionId, fact.OccurredAt, fact.Id });
        builder.HasIndex(fact => new
        {
            fact.CompetitionId,
            fact.TeamId,
            fact.CompetitionChallengeId,
            fact.Kind,
            fact.OccurredAt,
            fact.Id
        });
        builder.HasIndex(fact => new { fact.ReferenceKind, fact.ReferenceId });
        builder.HasOne<NoCTF.Domain.Competitions.Competition>().WithMany()
            .HasForeignKey(fact => fact.CompetitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Challenges.CompetitionChallenge>().WithMany()
            .HasForeignKey(fact => fact.CompetitionChallengeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Teams.Team>().WithMany()
            .HasForeignKey(fact => fact.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Teams.Team>().WithMany()
            .HasForeignKey(fact => fact.VictimTeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Identity.User>().WithMany()
            .HasForeignKey(fact => fact.ActorUserId).OnDelete(DeleteBehavior.Restrict);

    }
}
