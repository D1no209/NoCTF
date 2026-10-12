using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Competitions.Awd;

internal sealed class AwdFlagKeyConfiguration : IEntityTypeConfiguration<AwdFlagKey>
{
    public void Configure(EntityTypeBuilder<AwdFlagKey> builder)
    {
        builder.HasAlternateKey(key => new { key.CompetitionId, key.Version });
        builder.HasOne<Competition>().WithMany().HasForeignKey(key => key.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AwdRoundConfiguration : IEntityTypeConfiguration<AwdRound>
{
    public void Configure(EntityTypeBuilder<AwdRound> builder)
    {
        builder.HasIndex(round => new { round.CompetitionId, round.Number }).IsUnique();
        builder.HasAlternateKey(round => new { round.Id, round.CompetitionId });
        builder.HasIndex(round => new { round.CompetitionId, round.StartsAt, round.EndsAt });
        builder.HasOne<Competition>().WithMany().HasForeignKey(round => round.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AwdRoundRulesConfiguration : IEntityTypeConfiguration<AwdRoundRules>
{
    public void Configure(EntityTypeBuilder<AwdRoundRules> builder)
    {
        builder.HasIndex(rules => new { rules.RoundId, rules.CompetitionChallengeId, rules.Revision }).IsUnique();
        builder.HasAlternateKey(rules => new { rules.Id, rules.RoundId, rules.CompetitionChallengeId, rules.CompetitionId });
        builder.HasOne<AwdRound>().WithMany().HasForeignKey(rules => new { rules.RoundId, rules.CompetitionId })
            .HasPrincipalKey(round => new { round.Id, round.CompetitionId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompetitionChallenge>().WithMany().HasForeignKey(rules => new { rules.CompetitionChallengeId, rules.CompetitionId })
            .HasPrincipalKey(challenge => new { challenge.Id, challenge.CompetitionId })
            .OnDelete(DeleteBehavior.Restrict);
        AwdPolicyColumns.Configure(builder.ComplexProperty(rules => rules.Options));
        builder.HasMany(rules => rules.Stores).WithOne().HasForeignKey(store => store.RulesId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(rules => rules.Stores).AutoInclude();
        builder.OwnsMany(rules => rules.Teams, teams =>
        {
            teams.ToTable("awd_round_team_eligibility");
            teams.WithOwner().HasForeignKey(team => team.RulesId);
            teams.HasKey(team => new { team.RulesId, team.TeamId });
            teams.Property(team => team.Competitive).HasDefaultValue(true);
        });
    }
}

internal sealed class AwdRoundStoreBudgetConfiguration : IEntityTypeConfiguration<AwdRoundStoreBudget>
{
    public void Configure(EntityTypeBuilder<AwdRoundStoreBudget> builder)
    {
        builder.ToTable("awd_round_store_budgets");
        builder.HasKey(store => new { store.RulesId, store.StoreId });
        builder.Property(store => store.Weight).HasPrecision(18, 6);
    }
}

internal sealed class AwdFlagIssueConfiguration : IEntityTypeConfiguration<AwdFlagIssue>
{
    public void Configure(EntityTypeBuilder<AwdFlagIssue> builder)
    {
        builder.HasOne<ChallengeFlag>().WithOne(flag => flag.AwdIssue)
            .HasForeignKey<AwdFlagIssue>(issue => issue.FlagId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(issue => new
        {
            issue.CompetitionId, issue.CompetitionChallengeId, issue.TeamId,
            issue.RoundId, issue.StoreId, issue.GenerationSlot, issue.Ordinal
        }).IsUnique();
        builder.HasIndex(issue => issue.Digest).IsUnique();
        builder.HasOne<AwdFlagKey>().WithMany().HasForeignKey(issue => new { issue.CompetitionId, issue.KeyVersion })
            .HasPrincipalKey(key => new { key.CompetitionId, key.Version }).OnDelete(DeleteBehavior.Restrict);
        builder.HasAlternateKey(issue => new { issue.FlagId, issue.CompetitionId });
        builder.HasIndex(issue => new { issue.CompetitionId, issue.State, issue.DeliveryState });
        builder.HasOne<AwdRoundRules>().WithMany()
            .HasForeignKey(issue => new { issue.RulesId, issue.RoundId, issue.CompetitionChallengeId, issue.CompetitionId })
            .HasPrincipalKey(rules => new { rules.Id, rules.RoundId, rules.CompetitionChallengeId, rules.CompetitionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(issue => new { issue.TeamId, issue.CompetitionId })
            .HasPrincipalKey(team => new { team.Id, team.CompetitionId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AwdRoundStoreBudget>().WithMany().HasForeignKey(issue => new { issue.RulesId, issue.StoreId })
            .HasPrincipalKey(store => new { store.RulesId, store.StoreId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AwdFactDetailsConfiguration : IEntityTypeConfiguration<AwdFactDetails>
{
    public void Configure(EntityTypeBuilder<AwdFactDetails> builder)
    {
        builder.HasOne<GameplayFact>().WithOne(fact => fact.Awd)
            .HasForeignKey<AwdFactDetails>(details => details.GameplayFactId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(details => new { details.ReceivedRoundId, details.FinalScheduledCheck, details.Execution });
        builder.HasOne<ChallengeFlag>().WithMany().HasForeignKey(details => details.FlagId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AwdRound>().WithMany().HasForeignKey(details => details.GenerationRoundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AwdRound>().WithMany().HasForeignKey(details => details.ReceivedRoundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AwdRoundRules>().WithMany().HasForeignKey(details => details.RulesId).OnDelete(DeleteBehavior.Restrict);
        builder.OwnsMany(details => details.Flags, flags =>
        {
            flags.ToTable("awd_check_flag_evidence");
            flags.WithOwner().HasForeignKey(flag => flag.GameplayFactId);
            flags.HasKey(flag => new { flag.GameplayFactId, flag.FlagId });
            flags.HasOne<ChallengeFlag>().WithMany().HasForeignKey(flag => flag.FlagId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class AwdCaptureIdentityConfiguration : IEntityTypeConfiguration<AwdCaptureIdentity>
{
    public void Configure(EntityTypeBuilder<AwdCaptureIdentity> builder)
    {
        builder.HasKey(capture => new { capture.FlagId, capture.AttackingTeamId });
        builder.HasIndex(capture => capture.GameplayFactId).IsUnique();
        builder.HasOne<AwdFlagIssue>().WithMany().HasForeignKey(capture => new { capture.FlagId, capture.CompetitionId })
            .HasPrincipalKey(issue => new { issue.FlagId, issue.CompetitionId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(capture => new { capture.AttackingTeamId, capture.CompetitionId })
            .HasPrincipalKey(team => new { team.Id, team.CompetitionId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GameplayFact>().WithMany().HasForeignKey(capture => capture.GameplayFactId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AwdDefinitionConfiguration : IEntityTypeConfiguration<AwdChallengeDefinition>
{
    public void Configure(EntityTypeBuilder<AwdChallengeDefinition> builder)
    {
        builder.OwnsMany(definition => definition.FlagStores, stores =>
        {
            stores.ToTable("awd_flag_stores");
            stores.WithOwner().HasForeignKey(store => store.ChallengeId);
            stores.HasKey(store => new { store.ChallengeId, store.Id });
            stores.HasIndex(store => new { store.ChallengeId, store.Name }).IsUnique();
            stores.HasIndex(store => new { store.ChallengeId, store.Position }).IsUnique();
        });
    }
}

internal sealed class AwdChallengeRulesConfiguration : IEntityTypeConfiguration<AwdCompetitionChallengeRules>
{
    public void Configure(EntityTypeBuilder<AwdCompetitionChallengeRules> builder)
    {
        var overrides = builder.ComplexProperty(rules => rules.AwdOverrides);
        foreach (var property in overrides.Metadata.ComplexType.GetProperties()
                     .Where(property => property.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(18);
            property.SetScale(6);
        }
        builder.OwnsMany(rules => rules.StoreRules, stores =>
        {
            stores.ToTable("awd_store_rules");
            stores.WithOwner().HasForeignKey(store => store.CompetitionChallengeId);
            stores.HasKey(store => new { store.CompetitionChallengeId, store.StoreId });
            stores.Property(store => store.Weight).HasPrecision(18, 6);
        });
    }
}

internal static class AwdPolicyColumns
{
    internal static void Configure(ComplexPropertyBuilder<AwdRuleOptions> builder)
    {
        foreach (var property in builder.Metadata.ComplexType.GetProperties()
                     .Where(property => property.ClrType == typeof(decimal)))
        {
            property.SetPrecision(18);
            property.SetScale(6);
        }
    }
}

internal sealed class AwdScoringRuleChangeConfiguration : IEntityTypeConfiguration<AwdScoringRuleChange>
{
    public void Configure(EntityTypeBuilder<AwdScoringRuleChange> builder)
    {
        builder.HasOne<NoCTF.Domain.Competitions.Events.CompetitionEvent>().WithMany()
            .HasForeignKey(change => change.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompetitionChallenge>().WithMany()
            .HasForeignKey(change => new { change.CompetitionChallengeId, change.CompetitionId })
            .HasPrincipalKey(challenge => new { challenge.Id, challenge.CompetitionId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(change => new { change.CompetitionId, change.AppliedAt });
        builder.HasIndex(change => new { change.CompetitionChallengeId, change.Sequence }).IsUnique();
        var policy = builder.ComplexProperty(change => change.Policy);
        foreach (var property in policy.Metadata.ComplexType.GetProperties().Where(property => property.ClrType == typeof(decimal)))
        {
            property.SetPrecision(18);
            property.SetScale(6);
        }
    }
}
