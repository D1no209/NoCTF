using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.Infrastructure.Persistence;
using Wolverine.Attributes;

namespace NoCTF.Worker;

[NonTransactional]
public sealed class KohPollingHandler(
    NoCtfDbContext db,
    IKohControlClient client,
    IKohProducerConfigurationCatalog configurations,
    TimeProvider timeProvider)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<RecordKohObservation?> Handle(
        PollKohChallenge message,
        CancellationToken cancellationToken)
    {
        var target = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == message.CompetitionChallengeId
                && challenge.CompetitionId == message.CompetitionId
                && challenge.IsPublished
                && challenge.DeletedAt == null)
            .Join(
                db.Competitions.AsNoTracking(),
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { Challenge = challenge, Competition = competition })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null
            || target.Competition.Mode != GameMode.Koh
            || target.Competition.Status != CompetitionStatus.Running
            || target.Competition.RunningSince != message.RunningSince)
            return null;

        var settings = configurations.Get(
            target.Competition.ConfigurationJson,
            target.Challenge.RulesJson);
        var timeout = TimeSpan.FromSeconds(Math.Min(settings.PollIntervalSeconds, 30));
        var controlUrls = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => runtime.CompetitionId == message.CompetitionId
                && runtime.CompetitionChallengeId == message.CompetitionChallengeId
                && runtime.TeamId == null
                && runtime.State == RuntimeState.Running
                && runtime.ControlCheckUrl != null)
            .OrderByDescending(runtime => runtime.Generation)
            .Select(runtime => runtime.ControlCheckUrl!)
            .Take(2)
            .ToArrayAsync(cancellationToken);

        KohControlResponse response;
        if (controlUrls.Length != 1
            || !Uri.TryCreate(controlUrls[0], UriKind.Absolute, out var controlUrl)
            || controlUrl.Scheme is not ("http" or "https"))
        {
            response = KohControlResponse.Unavailable();
        }
        else
        {
            response = await client.ObserveAsync(controlUrl, timeout, cancellationToken);
        }

        var observedAt = timeProvider.GetUtcNow();
        var decision = await DecideAsync(
            message.CompetitionChallengeId,
            response,
            observedAt,
            cancellationToken);
        return new(
            message.CompetitionId,
            message.CompetitionChallengeId,
            decision.TeamId,
            decision.Result,
            decision.FailureCode,
            target.Competition.ConfigurationRevision,
            target.Challenge.Revision,
            message.RunningSince,
            message.DueAt,
            observedAt);
    }

    private async Task<KohDecision> DecideAsync(
        Guid competitionChallengeId,
        KohControlResponse response,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        if (response.Kind == KohControlResponseKind.Timeout)
            return new(null, null, GameplayFactFailureCode.ProducerTimeout);
        if (response.Kind == KohControlResponseKind.Unavailable)
            return new(null, null, GameplayFactFailureCode.ProducerUnavailable);
        if (response.Body.Length is 0 or > 4096 || response.Body.Span.Contains((byte)0))
            return new(null, GameplayFactResult.Uncontrolled, null);

        string flag;
        try
        {
            flag = StrictUtf8.GetString(response.Body.Span);
        }
        catch (DecoderFallbackException)
        {
            return new(null, GameplayFactResult.Uncontrolled, null);
        }

        var hash = SHA256.HashData(response.Body.Span);
        var matches = await db.ChallengeFlags.AsNoTracking()
            .Where(candidate => candidate.CompetitionChallengeId == competitionChallengeId
                && candidate.TeamId != null
                && candidate.SpecificationKind == NoCTF.Domain.Challenges.SpecificationKind.RuntimeDefinition
                && candidate.SpecificationId == competitionChallengeId
                && candidate.FlagSha256 == hash
                && candidate.Flag == flag
                && candidate.DeletedAt == null
                && (candidate.ValidStart == null || candidate.ValidStart <= observedAt)
                && (candidate.ValidUntil == null || candidate.ValidUntil > observedAt))
            .Select(candidate => candidate.TeamId!.Value)
            .Distinct()
            .Take(2)
            .ToArrayAsync(cancellationToken);
        return matches.Length switch
        {
            0 => new(null, GameplayFactResult.Uncontrolled, null),
            1 => new(matches[0], GameplayFactResult.Controlled, null),
            _ => new(null, null, GameplayFactFailureCode.AmbiguousFlagMatch)
        };
    }

    private sealed record KohDecision(
        Guid? TeamId,
        GameplayFactResult? Result,
        GameplayFactFailureCode? FailureCode);
}

public sealed class KohObservationHandler(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    IKohProducerConfigurationCatalog configurations,
    TimeProvider timeProvider)
{
    public async Task Handle(
        RecordKohObservation message,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var target = await db.CompetitionChallenges
            .Where(challenge => challenge.Id == message.CompetitionChallengeId
                && challenge.CompetitionId == message.CompetitionId
                && challenge.IsPublished
                && challenge.DeletedAt == null)
            .Join(
                db.Competitions,
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { Challenge = challenge, Competition = competition })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null
            || target.Competition.Mode != GameMode.Koh
            || target.Competition.Status != CompetitionStatus.Running
            || target.Competition.RunningSince != message.RunningSince)
            return;

        var settings = configurations.Get(
            target.Competition.ConfigurationJson,
            target.Challenge.RulesJson);
        var currentRevision = target.Competition.ConfigurationRevision;
        var currentChallengeRevision = target.Challenge.Revision;
        if (currentRevision == message.CompetitionConfigurationRevision
            && currentChallengeRevision == message.CompetitionChallengeRevision)
        {
            db.GameplayFacts.Add(new GameplayFact
            {
                Id = Guid.CreateVersion7(message.ObservedAt),
                CompetitionId = message.CompetitionId,
                CompetitionChallengeId = message.CompetitionChallengeId,
                TeamId = message.TeamId,
                Kind = GameplayFactKind.KohControlObservation,
                State = message.Result is null
                    ? GameplayFactState.PlatformFailed
                    : GameplayFactState.Completed,
                Result = message.Result,
                FailureCode = message.FailureCode,
                OccurredAt = message.ObservedAt,
                UpdatedAt = timeProvider.GetUtcNow()
            });
            target.Competition.LeaderboardDirty = true;
        }

        var nextDue = KohPollSchedule.NextDue(
            message.DueAt,
            timeProvider.GetUtcNow(),
            TimeSpan.FromSeconds(settings.PollIntervalSeconds));
        await outbox.ScheduleAsync(new PollKohChallenge(
            message.CompetitionId,
            message.CompetitionChallengeId,
            currentRevision,
            currentChallengeRevision,
            message.RunningSince,
            nextDue), nextDue);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }
}
