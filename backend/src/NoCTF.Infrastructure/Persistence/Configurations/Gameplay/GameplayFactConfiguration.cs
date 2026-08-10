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
        builder.Property(fact => fact.Kind).HasConversion<short>();
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
        builder.HasIndex(fact => fact.ReferenceId).IsUnique()
            .HasFilter("reference_kind = 0");
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

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_gameplay_facts_reference",
                "(reference_kind IS NULL) = (reference_id IS NULL)");
            table.HasCheckConstraint(
                "ck_gameplay_facts_state_result",
                "state <> 3 OR result IS NOT NULL");
            table.HasCheckConstraint(
                "ck_gameplay_facts_victim",
                "victim_team_id IS NULL OR kind = 0");
            table.HasCheckConstraint(
                "ck_gameplay_facts_shape",
                "(kind = 0 AND team_id IS NOT NULL AND actor_user_id IS NOT NULL AND value IS NOT NULL AND octet_length(value_sha256) = 32 AND (reference_kind IS NULL OR reference_kind = 2)) "
                + "OR (kind = 1 AND team_id IS NOT NULL AND actor_user_id IS NOT NULL AND value IS NOT NULL AND octet_length(value_sha256) = 32 AND reference_kind IS NULL) "
                + "OR (kind = 2 AND team_id IS NOT NULL AND actor_user_id IS NOT NULL AND value IS NULL AND value_sha256 IS NULL AND reference_kind = 0) "
                + "OR (kind = 3 AND team_id IS NOT NULL AND actor_user_id IS NOT NULL AND value IS NULL AND value_sha256 IS NULL AND reference_kind = 1) "
                + "OR (kind = 4 AND team_id IS NOT NULL AND actor_user_id IS NOT NULL AND value ~ '^-?(0|[1-9][0-9]*)$' AND value <> '0' AND value <> '-0' AND value::bigint BETWEEN -2147483648 AND 2147483647 AND value_sha256 IS NULL AND reference_kind IS NULL) "
                + "OR (kind = 5 AND team_id IS NOT NULL AND actor_user_id IS NULL AND value IS NULL AND value_sha256 IS NULL AND reference_kind IS NULL AND victim_team_id IS NULL) "
                + "OR (kind = 6 AND actor_user_id IS NULL AND value IS NULL AND value_sha256 IS NULL AND reference_kind IS NULL AND victim_team_id IS NULL)");
            table.HasCheckConstraint(
                "ck_gameplay_facts_result",
                "result IS NULL "
                + "OR (kind IN (0, 1) AND result IN (0, 1, 2, 3, 4)) "
                + "OR (kind = 2 AND result IN (0, 1, 3, 4)) "
                + "OR (kind = 3 AND result IN (2, 4, 5)) "
                + "OR (kind = 4 AND result = 6) "
                + "OR (kind = 5 AND result IN (7, 8)) "
                + "OR (kind = 6 AND result IN (9, 10))");
            table.HasCheckConstraint(
                "ck_gameplay_facts_koh_team",
                "kind <> 6 OR (result = 9 AND team_id IS NOT NULL) OR (result IS DISTINCT FROM 9 AND team_id IS NULL)");
        });
    }
}
