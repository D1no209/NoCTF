using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
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
    public async Task<AwdpParticipantStateView?> FindAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var scope = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                && team.Members.Any(member => member.UserId == userId)
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
                    item.Competition,
                    Challenge = challenge,
                    TemplateId = challenge.ChallengeId
                })
            .Join(
                db.Challenges.AsNoTracking()
                    .Where(template => template.DeletedAt == null),
                item => item.TemplateId,
                template => template.Id,
                (item, template) => new
                {
                    item.TeamId,
                    item.Competition,
                    item.Challenge,
                    Template = template
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (scope is null)
            return null;

        if (scope.Competition.ModeConfiguration is not AwdpCompetitionModeConfiguration competitionConfiguration
            || scope.Challenge.Rules is not AwdpCompetitionChallengeRules challengeRules
            || scope.Template.Definition is not AwdpChallengeDefinition definition)
            return null;
        var configuration = AwdpConfigurationResolver.Resolve(
            competitionConfiguration,
            challengeRules,
            definition);

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
            .OrderByDescending(runtime => runtime.CreatedAt)
            .ThenByDescending(runtime => runtime.Id)
            .Select(runtime => new RuntimeInstanceView(
                runtime.Id,
                runtime.CompetitionId,
                runtime.CompetitionChallengeId,
                runtime.ChallengeId,
                runtime.TeamId,
                runtime.Purpose,
                runtime.RuntimeKind,
                runtime.RuntimeProvider,
                runtime.State,
                runtime.FailureCode,
                runtime.CreatedAt,
                runtime.RunningAt,
                runtime.ExpiresAt,
                runtime.StoppedAt, runtime.RunnerId,
                runtime.PublishedPorts.Select(port => new RuntimePublishedPortView(port.ServiceName, port.ContainerPort, port.HostPort)).ToArray(),
                null,
                null,
                null,
                null,
                runtime.AccessMode,
                runtime.AccessEndpoints.OrderBy(endpoint => endpoint.BindingIndex)
                    .Select(endpoint => new RuntimeAccessEndpointView(endpoint.BindingIndex,
                        endpoint.DirectAddress, endpoint.TargetHost, endpoint.TargetPort)).ToArray(),
                runtime.TrafficCaptureEnabled,
                runtime.TrafficCaptureLimitBytes,
                runtime.TrafficCaptureReservedBytes))
            .FirstOrDefaultAsync(cancellationToken);
        var fixRuntime = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => runtime.CompetitionId == competitionId
                && runtime.CompetitionChallengeId == competitionChallengeId
                && runtime.TeamId == scope.TeamId
                && runtime.Purpose == RuntimePurpose.AwdpTarget)
            .OrderByDescending(runtime => runtime.CreatedAt)
            .ThenByDescending(runtime => runtime.Id)
            .Select(runtime => new
            {
                runtime.Id,
                runtime.State,
                runtime.FailureCode,
                runtime.GameplayFactId,
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
            .Select(@event => new
            {
                @event.Id,
                @event.OccurredAt,
                From = @event.PreviousCompetitionStatus,
                To = @event.CompetitionStatus,
                @event.Automatic,
                @event.Reason
            })
            .ToListAsync(cancellationToken);
        var lifecycle = lifecycleEvents
            .Where(@event => @event.From is not null && @event.To is not null)
            .Select(@event => new CompetitionLifecycleTransition
            {
                Id = @event.Id,
                CompetitionId = competitionId,
                From = @event.From!.Value,
                To = @event.To!.Value,
                Automatic = @event.Automatic,
                Reason = @event.Reason,
                OccurredAt = @event.OccurredAt
            })
            .ToArray();
        var hasStarted = lifecycle.Any(transition => transition.To == CompetitionStatus.Running);
        var currentRound = hasStarted
            ? Round(lifecycle, now, configuration.RoundDurationSeconds,
                scope.Competition.Status == CompetitionStatus.Finished)
            : (int?)null;

        var firstBreak = facts.FirstOrDefault(fact => fact is
        {
            Kind: GameplayFactKind.BreakAttempt,
            Result: GameplayFactResult.Correct or GameplayFactResult.RightButDue,
            TimeEligibility: NoCTF.Domain.Challenges.GameplayFactTimeEligibility.Valid
        });
        var firstFix = facts.FirstOrDefault(fact => fact is
        {
            Kind: GameplayFactKind.FixAttempt,
            Result: GameplayFactResult.Correct or GameplayFactResult.RightButDue,
            TimeEligibility: NoCTF.Domain.Challenges.GameplayFactTimeEligibility.Valid
        });
        var latestBreak = facts
            .Where(fact => fact.Kind == GameplayFactKind.BreakAttempt)
            .OrderByDescending(fact => fact.OccurredAt)
            .ThenByDescending(fact => fact.Id)
            .FirstOrDefault();
        var acceptedFixAttempts = facts.Count(fact =>
            fact.Kind == GameplayFactKind.FixAttempt
            && fact.TimeEligibility == NoCTF.Domain.Challenges.GameplayFactTimeEligibility.Valid
            && fact.State != GameplayFactState.PlatformFailed);
        var maximumFixAttempts = configuration.MaxFixSubmissions > 0
            ? configuration.MaxFixSubmissions
            : (int?)null;
        var remainingFixAttempts = maximumFixAttempts is { } maximum
            ? Math.Max(0, maximum - acceptedFixAttempts)
            : (int?)null;

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
                fixRuntime?.CreatedAt,
                fixRuntime?.ExpiresAt,
                fixRuntime?.StoppedAt,
                latestFix?.UpdatedAt),
            Activation(firstFix, lifecycle, configuration.RoundDurationSeconds),
            maximumFixAttempts,
            acceptedFixAttempts,
            remainingFixAttempts);
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
        int durationSeconds,
        bool finished = false)
    {
        var seconds = Math.Max(0, AwdEffectiveRunningClock.Calculate(lifecycle, at).TotalSeconds);
        return finished
            ? Math.Max(1, checked((int)Math.Ceiling(seconds / durationSeconds)))
            : checked((int)(seconds / durationSeconds) + 1);
    }

}
