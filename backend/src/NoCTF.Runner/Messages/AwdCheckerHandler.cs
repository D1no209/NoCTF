using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NoCTF.Application.Authentication;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
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
    RuntimeKind RuntimeKind,
    int Generation,
    string ProviderReceiptJson,
    string TargetHost,
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyDictionary<string, string> Environment,
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
    AbnormalExit,
    TimedOut,
    Superseded
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
                && runtime.AwdCheckerTargetHost != null
                && runtime.ProviderReceiptJson != null)
            .Join(
                db.CompetitionChallenges.AsNoTracking(),
                runtime => runtime.CompetitionChallengeId,
                challenge => challenge.Id,
                (runtime, challenge) => new
                {
                    Runtime = runtime,
                    ChallengeRules = challenge.RulesJson,
                    challenge.ChallengeId,
                    challenge.Revision
                })
            .Join(
                db.Challenges.AsNoTracking(),
                pair => pair.ChallengeId,
                challenge => challenge.Id,
                (pair, challenge) => new
                {
                    pair.Runtime,
                    pair.ChallengeRules,
                    pair.Revision,
                    ChallengeDefinition = challenge.DefinitionJson
                })
            .Join(
                db.Competitions.AsNoTracking(),
                pair => pair.Runtime.CompetitionId,
                competition => competition.Id,
                (pair, competition) => new
                {
                    pair.Runtime,
                    pair.ChallengeRules,
                    pair.ChallengeDefinition,
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
            target.ChallengeRules,
            target.ChallengeDefinition);
        if (settings.Checker is not { } checker
            || string.IsNullOrWhiteSpace(target.Runtime.AwdCheckerTargetHost)
            || target.Runtime.RuntimeKind is not (RuntimeKind.Container or RuntimeKind.Compose))
            return null;
        var callbackBase = configuration["RunnerScoring:CallbackBaseUrl"];
        if (!Uri.TryCreate(callbackBase, UriKind.Absolute, out var baseUri)
            || (!string.Equals(
                    baseUri.Scheme,
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    baseUri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "RunnerScoring:CallbackBaseUrl must be configured as an absolute HTTP(S) URI.");
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
            target.Runtime.RuntimeProvider,
            target.Runtime.RuntimeKind,
            target.Runtime.Generation,
            target.Runtime.ProviderReceiptJson!,
            target.Runtime.AwdCheckerTargetHost,
            checker.Image,
            checker.Command ?? [],
            checker.Environment ?? new Dictionary<string, string>(),
            callbackUrl,
            token,
            message.Deadline,
            TimeSpan.FromSeconds(checker.TimeoutSeconds) < remaining
                ? TimeSpan.FromSeconds(checker.TimeoutSeconds)
                : remaining);
    }
}

public sealed class AwdCheckerExecutor(
    IOneShotRuntimeProviderCatalog providers,
    IHttpClientFactory httpClients)
    : IAwdCheckerExecutor
{
    public async Task<AwdCheckerExecutionOutcome> ExecuteAsync(
        AwdCheckerWork work,
        CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string>(work.Environment, StringComparer.Ordinal)
        {
            ["NOCTF_TARGET_HOST"] = work.TargetHost,
            ["NOCTF_CALLBACK_URL"] = work.CallbackUrl.AbsoluteUri,
            ["NOCTF_CALLBACK_TOKEN"] = work.CallbackToken
        };
        var request = new ContainerRequest(
            CreateOperationId(work.RuntimeInstanceId, work.CheckerSequence),
            work.Provider,
            work.Image,
            work.Command,
            environment,
            new Dictionary<string, string>(),
            new Dictionary<int, int>(),
            new RuntimeResourceLimits(256 * 1024 * 1024, 250_000_000, 128),
            new ContainerSecurityPolicy(true, true, true, ["ALL"], []),
            work.Timeout,
            OperationTimeout: work.Timeout,
            AllowInternalCallback: true,
            Generation: work.Generation,
            RuntimeInstanceId: work.RuntimeInstanceId,
            NetworkPurpose: ContainerNetworkPurpose.AwdChecker);
        var target = CreateTarget(work);
        if (target is null)
            return AwdCheckerExecutionOutcome.Superseded;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(work.Timeout);
        try
        {
            var result = await providers.Attached(work.Provider).RunAttachedAsync(
                request,
                target,
                timeoutSource.Token);
            if (result.ExitCode == 0)
                return AwdCheckerExecutionOutcome.Completed;
            await ReportPlatformStatusAsync(
                work,
                AwdServiceState.CheckerAbnormalExit,
                cancellationToken);
            return AwdCheckerExecutionOutcome.AbnormalExit;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            await ReportPlatformStatusAsync(
                work,
                AwdServiceState.CheckerTimedOut,
                cancellationToken);
            return AwdCheckerExecutionOutcome.TimedOut;
        }
    }

    private async Task ReportPlatformStatusAsync(
        AwdCheckerWork work,
        AwdServiceState status,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, work.CallbackUrl)
        {
            Content = JsonContent.Create(new { State = status.ToString() })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", work.CallbackToken);
        using var response = await httpClients.CreateClient().SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static AttachedRuntimeTarget? CreateTarget(AwdCheckerWork work)
    {
        try
        {
            var identity = new RuntimeResourceIdentity(work.RuntimeInstanceId, work.Generation);
            return work.RuntimeKind switch
            {
                RuntimeKind.Container => CreateContainerTarget(work, identity),
                RuntimeKind.Compose => CreateComposeTarget(work, identity),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AttachedContainerRuntimeTarget? CreateContainerTarget(
        AwdCheckerWork work,
        RuntimeResourceIdentity identity)
    {
        var receipt = JsonSerializer.Deserialize<ContainerReceipt>(work.ProviderReceiptJson);
        return receipt is not null
            && receipt.Provider == work.Provider
            && receipt.RuntimeInstanceId == identity.RuntimeInstanceId
            && receipt.Generation == identity.Generation
            && !string.IsNullOrWhiteSpace(receipt.NetworkId)
                ? new(identity, receipt)
                : null;
    }

    private static AttachedComposeRuntimeTarget? CreateComposeTarget(
        AwdCheckerWork work,
        RuntimeResourceIdentity identity)
    {
        var receipt = JsonSerializer.Deserialize<ComposeReceipt>(work.ProviderReceiptJson);
        return receipt is not null
            && receipt.Provider == work.Provider
            && receipt.OperationId == identity.RuntimeInstanceId
            && receipt.Generation == identity.Generation
            && !string.IsNullOrWhiteSpace(work.TargetHost)
                ? new(identity, receipt, work.TargetHost)
                : null;
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
    IOptions<RunnerOptions> runnerOptions,
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
            runnerOptions.Value.Pool,
            runnerOptions.Value.Id);
        if (timeProvider.GetUtcNow() >= message.Deadline)
            return MessageExecutionOutcome.Superseded;
        var work = await reader.ReadAsync(message, cancellationToken);
        if (work is null)
            return MessageExecutionOutcome.Superseded;
        var outcome = await executor.ExecuteAsync(work, cancellationToken);
        return outcome == AwdCheckerExecutionOutcome.Superseded
            ? MessageExecutionOutcome.Superseded
            : MessageExecutionOutcome.Applied;
    }
}
