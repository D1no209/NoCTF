using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.Application.SystemProducers;

public sealed record KohProducerTarget(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    DateTimeOffset CompetitionStartTime,
    string CompetitionConfigurationJson,
    string ChallengeConfigurationJson);

public sealed record KohProducerSettings(
    int PollIntervalSeconds,
    Uri AgentUri,
    IReadOnlyDictionary<string, Guid> TeamIdentifiers);

public sealed record KohAgentObservation(string? ControllerId, bool Authoritative);

public interface IKohProducerTargetStore
{
    Task<IReadOnlyList<KohProducerTarget>> ListRunningAsync(CancellationToken cancellationToken);
}

public interface IKohProducerConfigurationCatalog
{
    KohProducerSettings Get(string competitionConfigurationJson, string challengeConfigurationJson);
}

public interface IKohAgentClient
{
    Task<KohAgentObservation> ObserveAsync(Uri agentUri, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed record SystemProducerSweepResult(int TargetCount, int CreatedCount, int FailedCount);

public sealed class ProduceKohObservations(
    IKohProducerTargetStore targets,
    IKohProducerConfigurationCatalog configurations,
    IKohAgentClient agent,
    RecordSystemScoringEvent record)
{
    public async Task<SystemProducerSweepResult> ExecuteAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var targetCount = 0;
        var createdCount = 0;
        var failedCount = 0;
        foreach (var target in await targets.ListRunningAsync(cancellationToken))
        {
            targetCount++;
            try
            {
                var settings = configurations.Get(
                    target.CompetitionConfigurationJson,
                    target.ChallengeConfigurationJson);
                var interval = CompetitionRoundClock.Interval(
                    target.CompetitionStartTime,
                    now,
                    settings.PollIntervalSeconds);
                var occurredAt = target.CompetitionStartTime.AddSeconds((long)interval * settings.PollIntervalSeconds);
                var sourceKey = $"koh:{target.CompetitionChallengeId:N}:interval:{interval}";
                RecordSystemScoringEventCommand command;
                try
                {
                    var observation = await agent.ObserveAsync(
                        settings.AgentUri,
                        TimeSpan.FromSeconds(Math.Clamp(settings.PollIntervalSeconds, 1, 30)),
                        cancellationToken);
                    command = Decision(target, settings, observation, occurredAt, sourceKey);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (TimeoutException)
                {
                    command = Failure(target, occurredAt, sourceKey, ScoringFailureCode.ProducerTimeout);
                }
                catch (Exception)
                {
                    command = Failure(target, occurredAt, sourceKey, ScoringFailureCode.ProducerUnavailable);
                }
                var result = await record.ExecuteAsync(command, cancellationToken);
                if (result.Created) createdCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Invalid persisted configuration cannot produce a stable interval fact.
                // Isolate the target so other competitions continue and retry it next sweep.
                failedCount++;
            }
        }
        return new(targetCount, createdCount, failedCount);
    }

    private static RecordSystemScoringEventCommand Decision(
        KohProducerTarget target,
        KohProducerSettings settings,
        KohAgentObservation observation,
        DateTimeOffset occurredAt,
        string sourceKey)
    {
        if (!observation.Authoritative)
            return Command(target, null, ScoringResult.Rejected, ScoringFailureCode.InvalidObservation, occurredAt, sourceKey);
        if (observation.ControllerId is null)
            return Command(target, null, ScoringResult.Wrong, null, occurredAt, sourceKey);
        if (string.IsNullOrWhiteSpace(observation.ControllerId))
            return Command(target, null, ScoringResult.Rejected, ScoringFailureCode.InvalidObservation, occurredAt, sourceKey);
        return settings.TeamIdentifiers.TryGetValue(observation.ControllerId, out var teamId)
            ? Command(target, teamId, ScoringResult.Correct, null, occurredAt, sourceKey)
            : Command(target, null, ScoringResult.Rejected, ScoringFailureCode.UnknownTeamIdentifier, occurredAt, sourceKey);
    }

    private static RecordSystemScoringEventCommand Failure(
        KohProducerTarget target,
        DateTimeOffset occurredAt,
        string sourceKey,
        ScoringFailureCode failureCode) =>
        Command(target, null, ScoringResult.PlatformFailed, failureCode, occurredAt, sourceKey);

    private static RecordSystemScoringEventCommand Command(
        KohProducerTarget target,
        Guid? teamId,
        ScoringResult result,
        ScoringFailureCode? failureCode,
        DateTimeOffset occurredAt,
        string sourceKey) =>
        new(target.CompetitionId, teamId, target.CompetitionChallengeId, ScoringEventKind.KohObservation,
            result, failureCode, occurredAt, "koh-agent-v1", sourceKey);
}

public static class CompetitionRoundClock
{
    public static long Interval(DateTimeOffset start, DateTimeOffset now, int durationSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationSeconds);
        if (now <= start) return 0;
        return (long)Math.Floor((now - start).TotalSeconds / durationSeconds);
    }
}
