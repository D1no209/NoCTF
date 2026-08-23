using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awd.Scheduling;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.GameplayFacts.Awdp;

public sealed class AwdpParticipantStateReader(NoCtfDbContext db) : IAwdpParticipantStateReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AwdpParticipantStateView?> FindAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var scope = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                && team.MemberIds.Contains(userId)
                && team.DeletedAt == null
                && !team.IsBanned
                && team.RegistrationStatus == TeamRegistrationStatus.Approved)
            .Join(
                db.Competitions.AsNoTracking()
                    .Where(competition => competition.Id == competitionId
                        && competition.Mode == GameMode.Awdp
                        && competition.DeletedAt == null),
                team => team.CompetitionId,
                competition => competition.Id,
                (team, competition) => new { Team = team, Competition = competition })
            .Join(
                db.CompetitionChallenges.AsNoTracking()
                    .Where(challenge => challenge.Id == competitionChallengeId
                        && challenge.CompetitionId == competitionId
                        && challenge.IsPublished
                        && challenge.DeletedAt == null),
                item => item.Competition.Id,
                challenge => challenge.CompetitionId,
                (item, challenge) => new
                {
                    TeamId = item.Team.Id,
                    item.Competition.ConfigurationJson,
                    item.Competition.Status,
                    ChallengeId = challenge.Id
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (scope is null)
            return null;

        var configuration = AwdpConfigurationParser.ParseCompetition(scope.ConfigurationJson);

        var facts = await db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == competitionId
                && fact.CompetitionChallengeId == competitionChallengeId
                && fact.TeamId == scope.TeamId
                && (fact.Kind == GameplayFactKind.BreakAttempt
                    || fact.Kind == GameplayFactKind.FixAttempt))
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.Id)
            .ToListAsync(cancellationToken);

        var attackRuntime = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => runtime.CompetitionId == competitionId
                && runtime.CompetitionChallengeId == competitionChallengeId
                && runtime.TeamId == scope.TeamId
                && (runtime.Purpose == RuntimePurpose.Player
                    || runtime.Purpose == RuntimePurpose.AwdpAttack))
            .OrderByDescending(runtime => runtime.Generation)
            .Select(runtime => new RuntimeInstanceView(
                runtime.Id,
                runtime.CompetitionId,
                runtime.CompetitionChallengeId,
                runtime.TeamId,
                runtime.Purpose,
                runtime.Generation,
                runtime.RuntimeKind,
                runtime.RuntimeProvider,
                runtime.RunnerPool,
                runtime.State,
                runtime.FailureCode,
                runtime.Urls,
                runtime.CreatedAt,
                runtime.RunningAt,
                runtime.ExpiresAt,
                runtime.StoppedAt))
            .FirstOrDefaultAsync(cancellationToken);

        var fixRuntime = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => runtime.CompetitionId == competitionId
                && runtime.CompetitionChallengeId == competitionChallengeId
                && runtime.TeamId == scope.TeamId
                && runtime.Purpose == RuntimePurpose.AwdpTarget)
            .OrderByDescending(runtime => runtime.Generation)
            .Select(runtime => new
            {
                runtime.Id,
                runtime.State,
                runtime.FailureCode,
                runtime.GameplayFactId,
                runtime.AwdpFixStage,
                runtime.CreatedAt,
                runtime.ExpiresAt,
                runtime.StoppedAt
            })
            .FirstOrDefaultAsync(cancellationToken);
        var latestFix = fixRuntime?.GameplayFactId is Guid fixFactId
            ? facts.SingleOrDefault(fact => fact.Id == fixFactId)
            : null;
        var patchUploadId = fixRuntime is null
            ? null
            : await db.PatchUploads.AsNoTracking()
                .Where(upload => upload.RuntimeInstanceId == fixRuntime.Id)
                .Select(upload => (Guid?)upload.Id)
                .SingleOrDefaultAsync(cancellationToken);

        var lifecycleEvents = await db.CompetitionEvents.AsNoTracking()
            .Where(@event => @event.CompetitionId == competitionId
                && @event.Kind == CompetitionEventKind.CompetitionLifecycleChanged)
            .OrderBy(@event => @event.OccurredAt)
            .ThenBy(@event => @event.Id)
            .Select(@event => new { @event.Id, @event.OccurredAt, @event.PayloadJson })
            .ToListAsync(cancellationToken);
        var lifecycle = lifecycleEvents
            .Select(@event => ParseLifecycle(@event.Id, competitionId, @event.OccurredAt, @event.PayloadJson))
            .Where(transition => transition is not null)
            .Select(transition => transition!)
            .ToArray();
        var hasStarted = lifecycle.Any(transition => transition.To == CompetitionStatus.Running);
        var currentRound = hasStarted
            ? Round(lifecycle, now, configuration.RoundDurationSeconds)
            : (int?)null;

        var firstBreak = facts.FirstOrDefault(fact => fact is
        {
            Kind: GameplayFactKind.BreakAttempt,
            Result: GameplayFactResult.Correct
        });
        var firstFix = facts.FirstOrDefault(fact => fact is
        {
            Kind: GameplayFactKind.FixAttempt,
            Result: GameplayFactResult.Correct
        });
        var latestBreak = facts
            .Where(fact => fact.Kind == GameplayFactKind.BreakAttempt)
            .OrderByDescending(fact => fact.OccurredAt)
            .ThenByDescending(fact => fact.Id)
            .FirstOrDefault();

        return new(
            currentRound,
            attackRuntime,
            latestBreak is null ? null : ToStatus(latestBreak),
            Activation(firstBreak, lifecycle, configuration.RoundDurationSeconds),
            new(
                fixRuntime?.Id,
                fixRuntime?.State,
                fixRuntime?.FailureCode,
                latestFix?.Id,
                patchUploadId,
                latestFix?.State,
                GameplayFactResultDisclosure.PlayerResult(latestFix?.Result, latestFix?.FailureCode),
                GameplayFactResultDisclosure.PlayerFailureCode(latestFix?.FailureCode),
                fixRuntime?.AwdpFixStage,
                fixRuntime?.CreatedAt,
                fixRuntime?.ExpiresAt,
                fixRuntime?.StoppedAt,
                latestFix?.UpdatedAt),
            Activation(firstFix, lifecycle, configuration.RoundDurationSeconds));
    }

    private static GameplayFactStatusView ToStatus(GameplayFact fact) =>
        new(
            fact.Id,
            fact.CompetitionId,
            fact.TeamId,
            fact.CompetitionChallengeId,
            fact.Kind,
            fact.State,
            GameplayFactResultDisclosure.PlayerResult(fact.Result, fact.FailureCode),
            GameplayFactResultDisclosure.PlayerFailureCode(fact.FailureCode),
            fact.OccurredAt,
            fact.UpdatedAt);

    private static AwdpAchievementActivationView? Activation(
        GameplayFact? fact,
        IReadOnlyList<CompetitionLifecycleTransition> lifecycle,
        int durationSeconds) =>
        fact is null
            ? null
            : new(fact.Id, Round(lifecycle, fact.OccurredAt, durationSeconds), fact.OccurredAt);

    private static int Round(
        IReadOnlyList<CompetitionLifecycleTransition> lifecycle,
        DateTimeOffset at,
        int durationSeconds)
    {
        var seconds = Math.Max(0, AwdEffectiveRunningClock.Calculate(lifecycle, at).TotalSeconds);
        return checked((int)(seconds / durationSeconds) + 1);
    }

    private static CompetitionLifecycleTransition? ParseLifecycle(
        Guid id,
        Guid competitionId,
        DateTimeOffset occurredAt,
        string payloadJson)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<LifecyclePayload>(payloadJson, JsonOptions);
            return payload is null
                ? null
                : new()
                {
                    Id = id,
                    CompetitionId = competitionId,
                    From = payload.From,
                    To = payload.To,
                    Automatic = payload.Automatic,
                    Reason = payload.Reason,
                    OccurredAt = occurredAt
                };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record LifecyclePayload(
        int SchemaVersion,
        CompetitionStatus From,
        CompetitionStatus To,
        bool Automatic,
        string? Reason);
}
