using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Challenges.Timing;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

internal static class SpectatorFrameReader
{
    private const int HistoryLimit = 2000;

    public static async Task<SpectatorFrame?> ReadAsync(NoCtfDbContext db, string title,
        LeaderboardProjectionInput input, CancellationToken ct, ChallengeTimingProjectionOverride? timingOverride = null,
        Guid? currentRoundId = null)
    {
        if (input.Mode is not (GameMode.Awd or GameMode.Koh)) return null;
        var cutoff = input.ProjectedAt!.Value;
        var start = cutoff.AddMinutes(-30);
        var challenges = (input.Challenges ?? []).Where(x => x.IsPublished && !x.IsDeleted).ToArray();
        var challengeIds = challenges.Select(x => x.Id).ToArray();
        var overrideId = timingOverride?.CompetitionChallengeId;
        var overrideOpening = timingOverride?.Timing.AutoOpenAt;
        var overrideScoringEnd = timingOverride?.Timing.ScoringEndsAt;
        var overrideSubmissionEnd = timingOverride?.Timing.SubmissionDeadlineAt;
        var competitive = input.Teams.Where(x => !x.IsBanned && !x.IsDeleted && x.AffectsCompetitiveResults).Select(x => x.Id).ToArray();
        var facts = db.GameplayFacts.AsNoTracking().Where(x => x.CompetitionId == input.CompetitionId
            && x.CompetitionChallengeId != null && challengeIds.Contains(x.CompetitionChallengeId.Value) && x.OccurredAt <= cutoff
            && (x.Kind == GameplayFactKind.AwdServiceTransition
                || x.State == GameplayFactState.Completed || x.State == GameplayFactState.PlatformFailed));
        var eligible = facts.Where(x => x.Kind == GameplayFactKind.AwdServiceTransition
            || (x.CompetitionChallengeId == overrideId
                && (overrideOpening == null || x.OccurredAt >= overrideOpening)
                && (overrideScoringEnd == null || x.OccurredAt < overrideScoringEnd)
                && (x.Kind == GameplayFactKind.KohControlObservation || overrideSubmissionEnd == null || x.OccurredAt < overrideSubmissionEnd))
            || db.CompetitionChallenges.Any(c => c.Id == x.CompetitionChallengeId && c.Id != overrideId
                && (c.AutoOpenAt == null || x.OccurredAt >= c.AutoOpenAt)
                && (c.ScoringEndsAt == null || x.OccurredAt < c.ScoringEndsAt)
                && (x.Kind == GameplayFactKind.KohControlObservation || c.SubmissionDeadlineAt == null || x.OccurredAt < c.SubmissionDeadlineAt)));
        // Select only safe display fields; Flag, failure details, actor identity and addresses never enter the cache frame.
        var observations = eligible.Where(x => x.Kind == GameplayFactKind.KohControlObservation
            || x.Kind == GameplayFactKind.AwdServiceTransition)
            .Select(x => new Observation { Id = x.Id, Revision = x.ConcurrencyStamp, ChallengeId = x.CompetitionChallengeId!.Value, TeamId = x.TeamId,
                At = x.OccurredAt, Kind = x.Kind, State = x.State, Result = x.Result });
        var services = new List<SpectatorService>();
        var points = new List<SpectatorPoint>();
        if (input.Mode == GameMode.Awd)
        {
            var checkFacts = facts.Where(x => x.Kind == GameplayFactKind.AwdServiceTransition && x.TeamId != null && x.Awd != null);
            var checks = CheckRows(checkFacts);
            var latest = await checks.Where(x => currentRoundId != null && x.RoundId == currentRoundId)
                .GroupBy(x => new { x.ChallengeId, x.TeamId })
                .Select(g => g.OrderByDescending(x => x.ScheduledAt).ThenByDescending(x => x.Id).First()).ToArrayAsync(ct);
            var trusted = await CheckRows(AwdEvidenceCutoff.VisibleFacts(checkFacts, cutoff))
                .Where(x => x.Health != null && x.Health != AwdHealth.InternalError
                    && x.Execution == AwdCheckExecution.Completed && x.Settlement == AwdSettlementState.Final)
                .GroupBy(x => new { x.ChallengeId, x.TeamId })
                .Select(g => g.OrderByDescending(x => x.ObservedAt).ThenByDescending(x => x.Id).First()).ToArrayAsync(ct);
            var active = await db.AwdRoundRules.AsNoTracking().Where(x => x.RoundId == currentRoundId
                    && x.EffectiveAt <= cutoff && challengeIds.Contains(x.CompetitionChallengeId))
                .SelectMany(x => x.Teams.Select(team => new { team.TeamId, x.CompetitionChallengeId })).ToArrayAsync(ct);
            var enabled = active.Select(x => (x.TeamId, x.CompetitionChallengeId)).ToHashSet();
            var lookup = latest.ToDictionary(x => (x.TeamId!.Value, x.ChallengeId));
            var prior = trusted.ToDictionary(x => (x.TeamId!.Value, x.ChallengeId));
            foreach (var team in input.Teams.Where(x => !x.IsDeleted))
            foreach (var challenge in challenges)
            {
                lookup.TryGetValue((team.Id, challenge.Id), out var observation);
                prior.TryGetValue((team.Id, challenge.Id), out var previous);
                var visible = observation is not null && AwdEvidenceCutoff.IsVisible(observation.UpdatedAt,
                    observation.ObservedAt, observation.ReceivedAt, observation.AdjudicatedAt, observation.AttemptStartedAt, cutoff);
                services.Add(new(team.Id, challenge.Id, visible ? observation!.Health : null, visible ? observation!.ObservedAt : null)
                {
                    Enabled = enabled.Contains((team.Id, challenge.Id)),
                    Execution = visible ? observation!.Execution : AwdEvidenceCutoff.UnresolvedExecution(observation?.AttemptStartedAt, cutoff),
                    Settlement = visible ? observation!.Settlement : AwdSettlementState.Provisional,
                    LastTrustedHealth = previous?.Health, LastTrustedAt = previous?.ObservedAt
                });
            }
        }
        else
        {
            var latest = await observations.Where(x => x.Kind == GameplayFactKind.KohControlObservation)
                .GroupBy(x => x.ChallengeId).Select(g => g.OrderByDescending(x => x.At).ThenByDescending(x => x.Id).First()).ToArrayAsync(ct);
            var effective = await observations.Where(x => x.Kind == GameplayFactKind.KohControlObservation
                && x.State == GameplayFactState.Completed && (x.Result == GameplayFactResult.Uncontrolled
                    || x.Result == GameplayFactResult.Controlled && x.TeamId != null && competitive.Contains(x.TeamId.Value)))
                .GroupBy(x => x.ChallengeId).Select(g => g.OrderByDescending(x => x.At).ThenByDescending(x => x.Id).First()).ToArrayAsync(ct);
            var config = (KohCompetitionModeConfiguration)input.CompetitionConfiguration!;
            foreach (var challenge in challenges)
            {
                var check = latest.FirstOrDefault(x => x.ChallengeId == challenge.Id);
                var control = effective.FirstOrDefault(x => x.ChallengeId == challenge.Id);
                points.Add(new(challenge.Id, control?.Result == GameplayFactResult.Controlled ? control.TeamId : null,
                    check?.TeamId, Health(check), check?.At,
                    challenge.Rules?.PollIntervalSeconds ?? config.PollIntervalSeconds,
                    challenge.Rules?.ControlPointsPerInterval ?? config.ControlPointsPerInterval));
            }
        }
        SpectatorChange[] changes;
        if (input.Mode == GameMode.Awd)
        {
            // Same competitive tuple as the scoring projection. EarnsScore only affects
            // attribution of points, not whether a competitive attack took place.
            var attacks = eligible.Where(x => x.Kind == GameplayFactKind.FlagAttempt
                && (x.Result == GameplayFactResult.Correct || x.Result == GameplayFactResult.RightButDue)
                && x.State == GameplayFactState.Completed && x.Awd != null && x.Awd.ReceiptScorable == true
                && x.Awd.AttackOutcome == AwdAttackOutcome.Accepted && x.Awd.ReceivedRoundId != null
                && x.TeamId != null && competitive.Contains(x.TeamId.Value)
                && x.VictimTeamId != null && competitive.Contains(x.VictimTeamId.Value) && x.TeamId != x.VictimTeamId);
            // Current service compromise is independent of the bounded event feed.
            // The round id comes from the same canonical scoreboard generation.
            var currentAttacks = await attacks.Where(x => currentRoundId != null && x.Awd!.ReceivedRoundId == currentRoundId)
                .Select(x => new { Attacker = x.TeamId!.Value, Victim = x.VictimTeamId!.Value, Challenge = x.CompetitionChallengeId!.Value })
                .Distinct().ToArrayAsync(ct);
            var compromised = currentAttacks.ToLookup(x => (x.Victim, x.Challenge), x => x.Attacker);
            services = services.Select(service => service with
            {
                CompromisedBy = compromised[(service.TeamId, service.ChallengeId)].ToArray()
            }).ToList();
            changes = await attacks.Where(x => x.OccurredAt >= start && !attacks.Any(prior =>
                    prior.Awd!.ReceivedRoundId == x.Awd!.ReceivedRoundId && prior.TeamId == x.TeamId && prior.VictimTeamId == x.VictimTeamId
                    && prior.CompetitionChallengeId == x.CompetitionChallengeId
                    && (prior.OccurredAt < x.OccurredAt || prior.OccurredAt == x.OccurredAt && prior.Id.CompareTo(x.Id) < 0)))
                .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Take(HistoryLimit + 1)
                .Select(x => new SpectatorChange(x.Id, x.ConcurrencyStamp, x.OccurredAt,
                    SpectatorChangeKind.Attack, x.CompetitionChallengeId!.Value, x.TeamId, x.VictimTeamId, null)).ToArrayAsync(ct);
        }
        else
        {
            changes = await eligible.Where(x => x.Kind == GameplayFactKind.KohControlObservation && x.OccurredAt >= start)
                .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Take(HistoryLimit + 1)
                .Select(x => new SpectatorChange(x.Id, x.ConcurrencyStamp, x.OccurredAt,
                    x.State != GameplayFactState.Completed || x.Result != GameplayFactResult.Controlled && x.Result != GameplayFactResult.Uncontrolled
                        ? SpectatorChangeKind.DetectionFailed
                        : x.Result == GameplayFactResult.Uncontrolled ? SpectatorChangeKind.Uncontrolled : SpectatorChangeKind.Control,
                    x.CompetitionChallengeId!.Value, x.TeamId, null,
                    eligible.Where(c => c.Kind == GameplayFactKind.KohControlObservation
                        && c.CompetitionChallengeId == x.CompetitionChallengeId && c.State == GameplayFactState.Completed
                        && (c.OccurredAt < x.OccurredAt || c.OccurredAt == x.OccurredAt && c.Id.CompareTo(x.Id) <= 0)
                        && (c.Result == GameplayFactResult.Uncontrolled || c.Result == GameplayFactResult.Controlled
                            && c.TeamId != null && competitive.Contains(c.TeamId.Value)))
                        .OrderByDescending(c => c.OccurredAt).ThenByDescending(c => c.Id)
                        .Select(c => c.Result == GameplayFactResult.Uncontrolled ? null : c.TeamId).FirstOrDefault()))
                .ToArrayAsync(ct);
        }
        DateTimeOffset? hardeningEnd = null;
        if (input.CompetitionConfiguration is AwdCompetitionModeConfiguration awd && input.CompetitionStartTime is { } starts)
        {
            var elapsed = CompetitionEffectiveRuntimePolicy.Calculate(starts, DateTimeOffset.MaxValue, cutoff,
                (input.LifecycleAudits ?? []).Select(x => new CompetitionLifecycleMoment(x.Id, x.OccurredAt, x.From, x.To))).Elapsed;
            hardeningEnd = cutoff.AddSeconds(Math.Max(0, awd.HardeningDurationSeconds - elapsed.TotalSeconds));
        }
        return new(title, input.CompetitionStatus ?? CompetitionStatus.Published, hardeningEnd,
            services, points, changes.Take(HistoryLimit).ToArray(), start, changes.Length > HistoryLimit);
    }

    private static IQueryable<CheckObservation> CheckRows(IQueryable<GameplayFact> facts) => facts.Select(x => new CheckObservation
    {
        Id = x.Id, TeamId = x.TeamId, ChallengeId = x.CompetitionChallengeId!.Value, UpdatedAt = x.UpdatedAt,
        RoundId = x.Awd!.ReceivedRoundId, Health = x.Awd.Health, Execution = x.Awd.Execution, Settlement = x.Awd.Settlement,
        ScheduledAt = x.Awd.ScheduledAt, ObservedAt = x.Awd.ObservedAt, ReceivedAt = x.Awd.ReceivedAt,
        AdjudicatedAt = x.Awd.AdjudicatedAt, AttemptStartedAt = x.Awd.AttemptStartedAt
    });

    private sealed class CheckObservation
    {
        public Guid Id { get; init; }
        public Guid? TeamId { get; init; }
        public Guid ChallengeId { get; init; }
        public Guid? RoundId { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public AwdHealth? Health { get; init; }
        public AwdCheckExecution Execution { get; init; }
        public AwdSettlementState Settlement { get; init; }
        public DateTimeOffset? ScheduledAt { get; init; }
        public DateTimeOffset? ObservedAt { get; init; }
        public DateTimeOffset? ReceivedAt { get; init; }
        public DateTimeOffset? AdjudicatedAt { get; init; }
        public DateTimeOffset? AttemptStartedAt { get; init; }
    }

    private static SpectatorHealth Health(Observation? item) => item?.State != GameplayFactState.Completed
        ? SpectatorHealth.Unknown : item.Result switch
        {
            GameplayFactResult.ServiceUp or GameplayFactResult.Controlled or GameplayFactResult.Uncontrolled => SpectatorHealth.Healthy,
            GameplayFactResult.ServiceDown => SpectatorHealth.Down,
            _ => SpectatorHealth.Unknown
        };

    private sealed class Observation
    {
        public Guid Id { get; init; }
        public Guid Revision { get; init; }
        public Guid ChallengeId { get; init; }
        public Guid? TeamId { get; init; }
        public DateTimeOffset At { get; init; }
        public GameplayFactKind Kind { get; init; }
        public GameplayFactState State { get; init; }
        public GameplayFactResult? Result { get; init; }
    }
}
