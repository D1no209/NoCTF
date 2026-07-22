using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Application.SystemProducers;

public sealed record AwdCheckerTarget(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid TeamId,
    DateTimeOffset StartTime,
    string CompetitionConfigurationJson,
    string ChallengeConfigurationJson,
    Uri TargetUri,
    long ChallengeRevision = 0);

public sealed record AwdCheckerSettings(int RoundDurationSeconds, RunnerJobConfiguration? Checker);

public interface IAwdCheckerTargetStore
{
    Task<IReadOnlyList<AwdCheckerTarget>> ListRunningAsync(CancellationToken cancellationToken);
}

public sealed record ProducerDispatchClaim(Guid OperationId, Guid ClaimToken);

public interface IProducerDispatchClaimStore
{
    Task<ProducerDispatchClaim?> TryBeginAsync(
        Guid competitionId,
        string operationKey,
        DateTimeOffset staleBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<bool> CompleteAsync(
        Guid competitionId,
        string operationKey,
        ProducerDispatchClaim claim,
        bool succeeded,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IAwdCheckerConfigurationCatalog
{
    AwdCheckerSettings Get(string competitionConfigurationJson, string challengeConfigurationJson);
}

public interface IAwdCheckerCallbackFactory
{
    RunnerScoringCallback Create(AwdCheckerTarget target, string sourceKey);
}

public sealed class ProduceAwdChecks(
    IAwdCheckerTargetStore targets,
    IAwdCheckerConfigurationCatalog configurations,
    IAwdCheckerCallbackFactory callbacks,
    IProducerDispatchClaimStore claims,
    IOneShotJobRunner runner)
{
    public async Task<SystemProducerSweepResult> ExecuteAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var targetCount = 0;
        var dispatched = 0;
        var failed = 0;
        foreach (var target in await targets.ListRunningAsync(cancellationToken))
        {
            targetCount++;
            ProducerDispatchClaim? claim = null;
            string? operationKey = null;
            try
            {
                var settings = configurations.Get(target.CompetitionConfigurationJson, target.ChallengeConfigurationJson);
                if (settings.Checker is null) continue;
                var round = CompetitionRoundClock.Interval(target.StartTime, now, settings.RoundDurationSeconds) + 1;
                var sourceKey = $"awd:{target.CompetitionId:N}:{target.CompetitionChallengeId:N}:{target.TeamId:N}:round:{round}:revision:{target.ChallengeRevision}:checker";
                operationKey = sourceKey;
                claim = await claims.TryBeginAsync(target.CompetitionId, operationKey,
                    now - TimeSpan.FromSeconds(settings.Checker.TimeoutSeconds)
                        - RunnerScoringCallbackDeliveryPolicy.DispatchLeaseBuffer,
                    now, cancellationToken);
                if (claim is null) continue;
                var environment = new Dictionary<string, string>(
                    settings.Checker.Environment ?? new Dictionary<string, string>(), StringComparer.Ordinal)
                {
                    ["TARGET_HOST"] = target.TargetUri.Host,
                    ["TARGET_PORT"] = target.TargetUri.IsDefaultPort ? string.Empty : target.TargetUri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
                var expiresAt = now + TimeSpan.FromSeconds(settings.Checker.TimeoutSeconds)
                    + RunnerScoringCallbackDeliveryPolicy.DispatchLeaseBuffer;
                var labels = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["noctf.io/job-kind"] = "awd-checker",
                    ["noctf.io/expires-at"] = expiresAt.ToUnixTimeSeconds().ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                };
                var request = new ContainerRequest(claim.OperationId, settings.Checker.Provider, settings.Checker.Image,
                    settings.Checker.Command ?? [], environment,
                    labels, new Dictionary<int, int>(),
                    new(268_435_456, 500_000_000, 128), new(true, true, true, ["ALL"], []), null,
                    callbacks.Create(target, sourceKey),
                    OperationTimeout: TimeSpan.FromSeconds(settings.Checker.TimeoutSeconds));
                await runner.RunAsync(request, cancellationToken);
                if (!await claims.CompleteAsync(target.CompetitionId, operationKey, claim, true,
                        DateTimeOffset.UtcNow, cancellationToken))
                    throw new InvalidOperationException("AWD checker dispatch claim completion was rejected.");
                dispatched++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                if (claim is not null && operationKey is not null)
                {
                    try
                    {
                        await claims.CompleteAsync(target.CompetitionId, operationKey, claim, false,
                            DateTimeOffset.UtcNow, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch { }
                }
                failed++;
            }
        }
        return new(targetCount, dispatched, failed);
    }
}
