using Microsoft.EntityFrameworkCore;
using System.Buffers.Binary;
using System.Security.Cryptography;
using NoCTF.Application.Authentication;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Infrastructure.Persistence;
using Wolverine.Attributes;

namespace NoCTF.Runner.Messages;

public sealed record AwdCheckerWork(
    Guid RuntimeInstanceId,
    long CheckerSequence,
    RuntimeProvider Provider,
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyDictionary<string, string> Environment,
    Uri TargetUrl,
    Uri CallbackUrl,
    string CallbackToken,
    DateTimeOffset Deadline,
    TimeSpan Timeout);

public interface IAwdCheckerWorkReader
{
    Task<AwdCheckerWork?> ReadAsync(RunAwdChecker message, CancellationToken cancellationToken);
}

public interface IAwdCheckerExecutor
{
    Task<AwdCheckerExecutionOutcome> ExecuteAsync(
        AwdCheckerWork work,
        CancellationToken cancellationToken);
}

public enum AwdCheckerExecutionOutcome
{
    Completed,
    TimedOut
}

public sealed class AwdCheckerWorkReader(
    IServiceScopeFactory scopes,
    AwdCheckerConfigurationCatalog configurations,
    IRunnerScoringTokenIssuer tokens,
    IConfiguration configuration,
    TimeProvider timeProvider) : IAwdCheckerWorkReader
{
    public async Task<AwdCheckerWork?> ReadAsync(
        RunAwdChecker message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var target = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => runtime.Id == message.RuntimeInstanceId
                && runtime.CompetitionChallengeId == message.CompetitionChallengeId
                && runtime.Generation == message.Generation
                && runtime.ProcessingVersion == message.ProcessingVersion
                && runtime.CheckerSequence == message.CheckerSequence
                && runtime.CheckerDeadlineAt == message.Deadline
                && runtime.State == RuntimeState.Running
                && runtime.RunnerPool == message.RunnerPool
                && runtime.RunnerId == message.RunnerId
                && runtime.ControlCheckUrl != null)
            .Join(
                db.CompetitionChallenges.AsNoTracking(),
                runtime => runtime.CompetitionChallengeId,
                challenge => challenge.Id,
                (runtime, challenge) => new
                {
                    Runtime = runtime,
                    ChallengeConfiguration = challenge.ConfigurationJson,
                    challenge.Revision
                })
            .Join(
                db.Competitions.AsNoTracking(),
                pair => pair.Runtime.CompetitionId,
                competition => competition.Id,
                (pair, competition) => new
                {
                    pair.Runtime,
                    pair.ChallengeConfiguration,
                    pair.Revision,
                    CompetitionConfiguration = competition.ConfigurationJson,
                    competition.ConfigurationRevision,
                    competition.Status,
                    competition.Mode
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null
            || target.Mode != NoCTF.Domain.Competitions.GameMode.Awd
            || target.Status != NoCTF.Domain.Competitions.CompetitionStatus.Running
            || target.ConfigurationRevision != message.CompetitionConfigurationRevision
            || target.Revision != message.CompetitionChallengeRevision)
            return null;
        var settings = configurations.Get(
            target.CompetitionConfiguration,
            target.ChallengeConfiguration);
        if (settings.Checker is not { } checker
            || !Uri.TryCreate(target.Runtime.ControlCheckUrl, UriKind.Absolute, out var targetUrl))
            return null;
        var callbackBase = configuration["RunnerScoring:CallbackBaseUrl"] ?? "http://noctf-api";
        if (!Uri.TryCreate(callbackBase, UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException("RunnerScoring:CallbackBaseUrl must be an absolute URI.");
        var callbackUrl = new Uri(baseUri, "/api/internal/v1/awd/check-results");
        var issuedAt = timeProvider.GetUtcNow();
        var remaining = message.Deadline - issuedAt;
        if (remaining <= TimeSpan.Zero)
            return null;
        var token = tokens.IssueAwdChecker(new(
            message.RunnerId,
            message.RuntimeInstanceId,
            message.Generation,
            message.CheckerSequence,
            message.ProcessingVersion,
            message.Deadline,
            issuedAt));
        return new(
            message.RuntimeInstanceId,
            message.CheckerSequence,
            checker.Provider,
            checker.Image,
            checker.Command ?? [],
            checker.Environment ?? new Dictionary<string, string>(),
            targetUrl,
            callbackUrl,
            token,
            message.Deadline,
            TimeSpan.FromSeconds(checker.TimeoutSeconds) < remaining
                ? TimeSpan.FromSeconds(checker.TimeoutSeconds)
                : remaining);
    }
}

public sealed class AwdCheckerExecutor(IOneShotRuntimeProviderCatalog providers)
    : IAwdCheckerExecutor
{
    public async Task<AwdCheckerExecutionOutcome> ExecuteAsync(
        AwdCheckerWork work,
        CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string>(work.Environment, StringComparer.Ordinal)
        {
            ["NOCTF_TARGET_URL"] = work.TargetUrl.AbsoluteUri,
            ["NOCTF_CALLBACK_URL"] = work.CallbackUrl.AbsoluteUri,
            ["NOCTF_CALLBACK_TOKEN"] = work.CallbackToken
        };
        var request = new ContainerRequest(
            CreateOperationId(work.RuntimeInstanceId, work.CheckerSequence),
            work.Provider,
            work.Image,
            work.Command,
            environment,
            new Dictionary<string, string>
            {
                ["noctf.io/managed"] = "true",
                ["noctf.io/runtime-instance-id"] = work.RuntimeInstanceId.ToString("D"),
                ["noctf.io/purpose"] = "awd-checker"
            },
            new Dictionary<int, int>(),
            new RuntimeResourceLimits(256 * 1024 * 1024, 250_000_000, 128),
            new ContainerSecurityPolicy(true, true, true, ["ALL"], []),
            work.Timeout,
            OperationTimeout: work.Timeout);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(work.Timeout);
        try
        {
            _ = await providers.OneShot(work.Provider).RunAsync(
                request,
                timeoutSource.Token);
            return AwdCheckerExecutionOutcome.Completed;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            return AwdCheckerExecutionOutcome.TimedOut;
        }
    }

    private static Guid CreateOperationId(Guid runtimeInstanceId, long checkerSequence)
    {
        Span<byte> input = stackalloc byte[24];
        runtimeInstanceId.TryWriteBytes(input[..16]);
        BinaryPrimitives.WriteInt64BigEndian(input[16..], checkerSequence);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }
}

[NonTransactional]
public sealed class AwdCheckerHandler(
    IAwdCheckerWorkReader reader,
    IAwdCheckerExecutor executor,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    public Task Handle(RunAwdChecker message, CancellationToken cancellationToken) =>
        ExecuteAsync(message, cancellationToken);

    public async Task<MessageExecutionOutcome> ExecuteAsync(
        RunAwdChecker message,
        CancellationToken cancellationToken)
    {
        RunnerNodeAssignmentGuard.Validate(
            message,
            configuration["Runner:Pool"] ?? "default",
            configuration["Runner:Id"]
                ?? throw new InvalidOperationException("Runner:Id is required."));
        if (timeProvider.GetUtcNow() >= message.Deadline)
            return MessageExecutionOutcome.Superseded;
        var work = await reader.ReadAsync(message, cancellationToken);
        if (work is null)
            return MessageExecutionOutcome.Superseded;
        _ = await executor.ExecuteAsync(work, cancellationToken);
        return MessageExecutionOutcome.Applied;
    }
}
