using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awd.Scheduling;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Messaging;

public sealed class PostgresClusterScheduleSource(
    NoCtfDbContext db,
    IAwdRoundConfigurationCatalog awdConfigurations,
    IKohProducerConfigurationCatalog kohConfigurations) : IClusterScheduleSource
{
    public async Task<IReadOnlyList<ClusterScheduleEntry>> RebuildAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var competitions = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Status == CompetitionStatus.Running
                && competition.DeletedAt == null
                && (competition.Mode == GameMode.Awd || competition.Mode == GameMode.Koh)
                && (competition.Mode != GameMode.Awd
                    || db.Teams.Any(team => team.CompetitionId == competition.Id
                        && team.RegistrationStatus == TeamRegistrationStatus.Approved
                        && !team.IsBanned
                        && team.DeletedAt == null)))
            .Select(competition => new ActiveCompetition(
                competition.Id,
                competition.Mode,
                competition.ConfigurationJson,
                competition.StartAt,
                competition.EndAt))
            .ToArrayAsync(cancellationToken);
        if (competitions.Length == 0)
            return [];

        var competitionIds = competitions.Select(competition => competition.Id).ToArray();
        var challenges = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => competitionIds.Contains(challenge.CompetitionId)
                && challenge.IsPublished
                && challenge.DeletedAt == null)
            .Select(challenge => new ActiveChallenge(
                challenge.Id,
                challenge.CompetitionId,
                challenge.RulesJson))
            .ToArrayAsync(cancellationToken);
        var challengeIds = challenges.Select(challenge => challenge.Id).ToArray();
        var flags = challengeIds.Length == 0
            ? []
            : await db.ChallengeFlags.AsNoTracking()
                .Where(flag => flag.CompetitionChallengeId != null
                    && challengeIds.Contains(flag.CompetitionChallengeId.Value)
                    && flag.SpecificationKind == SpecificationKind.AwdRound
                    && flag.SpecificationId != null
                    && flag.ValidStart != null
                    && flag.ValidUntil != null)
                .Select(flag => new AwdFlagWindow(
                    flag.CompetitionChallengeId!.Value,
                    flag.SpecificationId!.Value,
                    flag.ValidStart!.Value,
                    flag.ValidUntil!.Value))
                .ToArrayAsync(cancellationToken);
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
        var entries = new List<ClusterScheduleEntry>(challenges.Length);

        foreach (var competition in competitions)
        {
            if (!challengesByCompetition.TryGetValue(competition.Id, out var competitionChallenges))
                continue;
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
                    competitionChallenges,
                    flags,
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
        IReadOnlyList<ActiveChallenge> challenges,
        IReadOnlyList<AwdFlagWindow> flags,
        TimeSpan effectiveElapsed,
        DateTimeOffset now,
        ICollection<ClusterScheduleEntry> entries)
    {
        var settings = awdConfigurations.Get(competition.ConfigurationJson);
        var hardening = TimeSpan.FromSeconds(settings.HardeningDurationSeconds);
        var interval = TimeSpan.FromSeconds(settings.RoundDurationSeconds);
        foreach (var challenge in challenges)
        {
            var latest = LatestWindow(challenge.Id, flags);
            var plan = AwdRoundScheduler.PlanCurrentRound(
                now,
                competition.EndAt,
                effectiveElapsed,
                hardening,
                interval,
                latest);
            var intendedDueAt = plan.Kind switch
            {
                AwdRoundPlanKind.WaitingForHardening => now + (hardening - effectiveElapsed),
                AwdRoundPlanKind.Current => plan.Window!.Value.ValidUntil,
                AwdRoundPlanKind.Create => now,
                AwdRoundPlanKind.Finished => competition.EndAt,
                _ => throw new ArgumentOutOfRangeException(nameof(plan.Kind), plan.Kind, null)
            };
            if (intendedDueAt >= competition.EndAt)
                continue;
            var clamped = ClusterScheduleClock.ClampWithoutCatchUp(intendedDueAt, now, interval);
            entries.Add(new(
                $"awd-round:{challenge.Id:N}",
                ClusterScheduleKind.AwdRound,
                clamped.DueAt,
                interval,
                new AdvanceAwdRound(competition.Id, challenge.Id, clamped.DueAt),
                clamped.SkippedTicks));
        }
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
                competition.ConfigurationJson,
                challenge.RulesJson);
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

    private static AwdPersistedRoundWindow? LatestWindow(
        Guid competitionChallengeId,
        IReadOnlyList<AwdFlagWindow> flags)
    {
        var parsed = flags
            .Where(flag => flag.CompetitionChallengeId == competitionChallengeId)
            .Select(flag => new
            {
                Round = AwdRoundSpecificationId.Parse(flag.SpecificationId).Round,
                flag.ValidStart,
                flag.ValidUntil
            })
            .ToArray();
        if (parsed.Length == 0)
            return null;
        var latestRound = parsed.Max(flag => flag.Round);
        var windows = parsed
            .Where(flag => flag.Round == latestRound)
            .Select(flag => new { flag.ValidStart, flag.ValidUntil })
            .Distinct()
            .ToArray();
        return windows.Length == 1
            ? new(latestRound, windows[0].ValidStart, windows[0].ValidUntil)
            : null;
    }

    private sealed record ActiveCompetition(
        Guid Id,
        GameMode Mode,
        string ConfigurationJson,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt);

    private sealed record ActiveChallenge(
        Guid Id,
        Guid CompetitionId,
        string RulesJson);

    private sealed record AwdFlagWindow(
        Guid CompetitionChallengeId,
        Guid SpecificationId,
        DateTimeOffset ValidStart,
        DateTimeOffset ValidUntil);

    private sealed record KohObservation(
        Guid CompetitionChallengeId,
        DateTimeOffset OccurredAt);
}
