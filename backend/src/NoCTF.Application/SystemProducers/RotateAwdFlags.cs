using System.Security.Cryptography;
using NoCTF.Domain.Competitions;
using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Application.SystemProducers;

public sealed record AwdFlagRotationTarget(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid TeamId,
    Guid ChallengeInstanceId,
    long ChallengeRevision,
    DateTimeOffset StartTime,
    string CompetitionConfigurationJson,
    string ChallengeConfigurationJson,
    ContainerReceipt Runtime);

public sealed record AwdRoundSettings(int RoundDurationSeconds, int TotalRounds, int FlagValidityRounds);
public sealed record AwdFlagInjectionSettings(IReadOnlyList<string> Command, int TimeoutSeconds);
public sealed record AwdFlagInjectionClaim(Guid FlagId, Guid ClaimToken, string Flag);

public interface IAwdFlagRotationStore
{
    Task<IReadOnlyList<AwdFlagRotationTarget>> ListRunningAsync(CancellationToken cancellationToken);
    Task<AwdFlagInjectionClaim?> TryClaimAsync(
        AwdFlagRotationTarget target,
        DateTimeOffset validStart,
        DateTimeOffset validEnd,
        string flag,
        DateTimeOffset staleBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<bool> ActivateAsync(
        Guid competitionId,
        AwdFlagInjectionClaim claim,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<bool> MarkRuntimeFailedAsync(
        Guid competitionId,
        Guid challengeInstanceId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IAwdRoundConfigurationCatalog
{
    AwdRoundSettings Get(string competitionConfigurationJson);
}

public interface IAwdFlagInjectionConfigurationCatalog
{
    AwdFlagInjectionSettings? Get(string challengeConfigurationJson);
}

public interface IAwdFlagInjector
{
    Task<ContainerExecResult> InjectAsync(
        ContainerReceipt runtime,
        AwdFlagInjectionSettings settings,
        string flag,
        CancellationToken cancellationToken);
}

public sealed class RotateAwdFlags(
    IAwdFlagRotationStore targets,
    IAwdRoundConfigurationCatalog configurations,
    IAwdFlagInjectionConfigurationCatalog injectionConfigurations,
    IAwdFlagInjector injector)
{
    public async Task<SystemProducerSweepResult> ExecuteAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var targetCount = 0;
        var createdCount = 0;
        var failedCount = 0;
        foreach (var target in await targets.ListRunningAsync(cancellationToken))
        {
            targetCount++;
            try
            {
                var settings = configurations.Get(target.CompetitionConfigurationJson);
                var injection = injectionConfigurations.Get(target.ChallengeConfigurationJson)
                    ?? throw new InvalidOperationException("AWD FlagInjection is not configured.");
                var round = CompetitionRoundClock.Interval(target.StartTime, now, settings.RoundDurationSeconds) + 1;
                if (round is < 1 or > long.MaxValue || round > settings.TotalRounds) continue;
                var validStart = target.StartTime.AddSeconds((round - 1) * (long)settings.RoundDurationSeconds);
                var validEnd = validStart.AddSeconds((long)settings.RoundDurationSeconds * settings.FlagValidityRounds);
                var claim = await targets.TryClaimAsync(target, validStart, validEnd, CreateFlag(),
                    now.AddSeconds(-injection.TimeoutSeconds - 30L), now, cancellationToken);
                if (claim is null) continue;
                var result = await injector.InjectAsync(target.Runtime, injection, claim.Flag, cancellationToken);
                if (result.TimedOut)
                {
                    if (!await targets.MarkRuntimeFailedAsync(
                            target.CompetitionId, target.ChallengeInstanceId, now, cancellationToken))
                        throw new InvalidOperationException("Timed-out AWD runtime could not be marked failed.");
                    failedCount++;
                }
                else if (result.ExitCode == 0
                         && await targets.ActivateAsync(target.CompetitionId, claim, now, cancellationToken))
                    createdCount++;
                else
                    failedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { failedCount++; }
        }
        return new(targetCount, createdCount, failedCount);
    }

    private static string CreateFlag() => $"flag{{{Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()}}}";
}
