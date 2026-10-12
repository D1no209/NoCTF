using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awd.Scheduling;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Messaging;

public sealed class ClusterScheduleSource(
    NoCtfDbContext db,
    IAwdRoundConfigurationCatalog awdConfigurations,
    IKohProducerConfigurationCatalog kohConfigurations,
    ILogger<ClusterScheduleSource> logger,
    IEnumerable<IClusterScheduleContributor> contributors) : IClusterScheduleSource
{
    public async Task<IReadOnlyList<ClusterScheduleEntry>> RebuildAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var entries = new List<ClusterScheduleEntry>();
        foreach (var contributor in contributors)
            entries.AddRange(await contributor.RebuildAsync(now, cancellationToken));
        var competitions = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Status == CompetitionStatus.Running
                && competition.DeletedAt == null
                && (competition.Mode == GameMode.Awd || competition.Mode == GameMode.Koh))
            .Select(competition => new ActiveCompetition(
                competition.Id,
                competition.Mode,
                competition.ModeConfiguration!,
                competition.StartAt,
                competition.EndAt))
            .AsSplitQuery()
            .ToArrayAsync(cancellationToken);
        if (competitions.Length == 0)
            return entries;

        var competitionIds = competitions.Select(competition => competition.Id).ToArray();
        var challenges = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => competitionIds.Contains(challenge.CompetitionId)
                && challenge.IsPublished
                && challenge.DeletedAt == null)
            .Select(challenge => new ActiveChallenge(
                challenge.Id,
                challenge.CompetitionId,
                challenge.Rules!))
            .AsSplitQuery()
            .ToArrayAsync(cancellationToken);
        var challengeIds = challenges.Select(challenge => challenge.Id).ToArray();
        var rounds = await db.AwdRounds.AsNoTracking()
            .Where(round => competitionIds.Contains(round.CompetitionId)).ToArrayAsync(cancellationToken);
        var observations = challengeIds.Length == 0
            ? []
            : await db.GameplayFacts.AsNoTracking()
                .Where(fact => challengeIds.Contains(fact.CompetitionChallengeId)
                    && fact.Kind == GameplayFactKind.KohControlObservation)
                .Select(fact => new KohObservation(fact.CompetitionChallengeId, fact.OccurredAt))
                .ToArrayAsync(cancellationToken);

        var challengesByCompetition = challenges
            .GroupBy(challenge => challenge.CompetitionId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var latestObservations = observations
            .GroupBy(observation => observation.CompetitionChallengeId)
            .ToDictionary(group => group.Key, group => group.Max(item => item.OccurredAt));
        var awdCompetitionIds = competitions
            .Where(competition => competition.Mode == GameMode.Awd)
            .Select(competition => competition.Id)
            .ToArray();
        if (awdCompetitionIds.Length > 0
            && await db.RuntimeInstances.AsNoTracking().AnyAsync(runtime =>
                runtime.CompetitionId != null
                && awdCompetitionIds.Contains(runtime.CompetitionId.Value)
                && runtime.State == NoCTF.Domain.Runtime.RuntimeState.Running,
                cancellationToken))
        {
            entries.Add(new(
                "awd-checkers",
                ClusterScheduleKind.AwdChecker,
                now,
                TimeSpan.FromSeconds(1),
                new DispatchAwdCheckers(now)));
        }

        foreach (var competition in competitions)
        {
            var competitionChallenges = challengesByCompetition.GetValueOrDefault(competition.Id, []);
            var effectiveRuntime = await CompetitionEffectiveRuntimeReader.ReadAsync(
                db,
                competition.Id,
                competition.StartAt,
                competition.EndAt,
                now,
                cancellationToken);
            if (effectiveRuntime.RunningSince is not { } runningSince)
                continue;

            if (competition.Mode == GameMode.Awd)
            {
                AddAwdEntries(
                    competition,
                    rounds,
                    effectiveRuntime.Elapsed,
                    now,
                    entries);
            }
            else
            {
                AddKohEntries(
                    competition,
                    competitionChallenges,
                    latestObservations,
                    runningSince,
                    now,
                    entries);
            }
        }

        return entries;
    }

    private void AddAwdEntries(
        ActiveCompetition competition,
        IReadOnlyList<AwdRound> rounds,
        TimeSpan effectiveElapsed,
        DateTimeOffset now,
        ICollection<ClusterScheduleEntry> entries)
    {
        var configuration = (AwdCompetitionModeConfiguration)competition.Configuration;
        var settings = awdConfigurations.Get(configuration);
        var plan = AwdRoundTimeline.Plan(rounds.Where(round => round.CompetitionId == competition.Id).ToArray(),
            effectiveElapsed, TimeSpan.FromSeconds(settings.HardeningDurationSeconds),
            TimeSpan.FromSeconds(settings.RoundDurationSeconds), configuration.AwdOptions.RoundGapSeconds,
            configuration.AwdOptions.TotalRounds);
        if (plan.NextRunningPosition is null && plan.NewRounds.Count == 0)
            return;
        // Periodic current-round redispatch repairs a lost post-commit publication without
        // inventing historical flag deliveries. The durable round remains the schedule source.
        var delay = plan.NewRounds.Count > 0 ? TimeSpan.Zero
            : plan.NextRunningPosition!.Value - effectiveElapsed;
        if (plan.Current is not null && delay > TimeSpan.FromSeconds(5))
            delay = TimeSpan.FromSeconds(5);
        var dueAt = now + delay;
        if (dueAt >= competition.EndAt)
            return;
        logger.LogDebug("AWD competition {CompetitionId} next round coordination is due at {DueAt}.",
            competition.Id, dueAt);
        entries.Add(new($"awd-round:{competition.Id:N}", ClusterScheduleKind.AwdRound,
            dueAt, TimeSpan.FromSeconds(5),
            new AdvanceAwdRound(competition.Id, dueAt)));
    }

    private void AddKohEntries(
        ActiveCompetition competition,
        IReadOnlyList<ActiveChallenge> challenges,
        IReadOnlyDictionary<Guid, DateTimeOffset> latestObservations,
        DateTimeOffset runningSince,
        DateTimeOffset now,
        ICollection<ClusterScheduleEntry> entries)
    {
        foreach (var challenge in challenges)
        {
            var settings = kohConfigurations.Get(
                (KohCompetitionModeConfiguration)competition.Configuration,
                (KohCompetitionChallengeRules)challenge.Rules);
            var interval = TimeSpan.FromSeconds(settings.PollIntervalSeconds);
            var intendedDueAt = latestObservations.TryGetValue(challenge.Id, out var observedAt)
                ? observedAt.Add(interval)
                : now;
            if (intendedDueAt >= competition.EndAt)
                continue;
            var clamped = ClusterScheduleClock.ClampWithoutCatchUp(intendedDueAt, now, interval);
            entries.Add(new(
                $"koh-poll:{challenge.Id:N}",
                ClusterScheduleKind.KohPoll,
                clamped.DueAt,
                interval,
                PollKohChallenge.Create(
                    competition.Id,
                    challenge.Id,
                    runningSince,
                    clamped.DueAt),
                clamped.SkippedTicks));
        }
    }

    private sealed record ActiveCompetition(
        Guid Id,
        GameMode Mode,
        CompetitionModeConfiguration Configuration,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt);

    private sealed record ActiveChallenge(
        Guid Id,
        Guid CompetitionId,
        CompetitionChallengeRules Rules);

    private sealed record KohObservation(
        Guid CompetitionChallengeId,
        DateTimeOffset OccurredAt);
}
