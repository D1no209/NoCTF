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
using NoCTF.Infrastructure.Authentication;
using Wolverine.Attributes;

namespace NoCTF.Runner.Messages;

public sealed record AwdCheckerWork(
    Guid RuntimeInstanceId,
    Guid GameplayFactId,
    RuntimeProvider Provider,
    RuntimeKind RuntimeKind,
    string ProviderReceiptJson,
    string TargetHost,
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyDictionary<string, string> Environment,
    Uri CallbackUrl,
    string CallbackToken,
    DateTimeOffset Deadline,
    TimeSpan Timeout,
    bool AllowRoot = false);

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
    IRuntimeProviderCatalog providers,
    IOptions<RunnerScoringOptions> scoringOptions,
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
                && runtime.State == RuntimeState.Running
                && runtime.RunnerId == message.RunnerId
                && runtime.ProviderReceiptJson != null)
            .Join(
                db.CompetitionChallenges.AsNoTracking(),
                runtime => runtime.CompetitionChallengeId,
                challenge => challenge.Id,
                (runtime, challenge) => new
                {
                    Runtime = runtime,
                    ChallengeRules = challenge.RulesJson,
                    challenge.ChallengeId
                })
            .Join(
                db.Challenges.AsNoTracking(),
                pair => pair.ChallengeId,
                challenge => challenge.Id,
                (pair, challenge) => new
                {
                    pair.Runtime,
                    pair.ChallengeRules,
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
                    CompetitionConfiguration = competition.ConfigurationJson,
                    competition.Status,
                    competition.Mode
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null
            || target.Mode != NoCTF.Domain.Competitions.GameMode.Awd
            || target.Status != NoCTF.Domain.Competitions.CompetitionStatus.Running)
            return null;
        var settings = configurations.Get(
            target.CompetitionConfiguration,
            target.ChallengeRules,
            target.ChallengeDefinition);
        if (settings.Checker is not { } checker
            || target.Runtime.RuntimeKind is not (RuntimeKind.Container or RuntimeKind.Compose))
            return null;
        var targetHost = await ResolveTargetHostAsync(
            target.Runtime.RuntimeKind,
            target.Runtime.RuntimeProvider,
            target.Runtime.ProviderReceiptJson!,
            target.Runtime.Id,
            AwdConfigurationUpgrader.ParseChallenge(target.ChallengeDefinition).Checker?.TargetServiceName,
            providers,
            cancellationToken);
        if (targetHost is null)
            return null;
        var baseUri = scoringOptions.Value.CallbackBaseUrl
            ?? throw new InvalidOperationException(
                "RunnerScoring:CallbackBaseUrl must be configured as an absolute HTTP(S) URI.");
        var callbackUrl = new Uri(baseUri, "/api/internal/v1/awd/check-results");
        var issuedAt = timeProvider.GetUtcNow();
        var remaining = message.Deadline - issuedAt;
        if (remaining <= TimeSpan.Zero)
            return null;
        var token = tokens.IssueAwdChecker(new(
            message.RunnerId,
            message.RuntimeInstanceId,
            message.GameplayFactId,
            message.Deadline,
            issuedAt));
        return new(
            message.RuntimeInstanceId,
            message.GameplayFactId,
            target.Runtime.RuntimeProvider,
            target.Runtime.RuntimeKind,
            target.Runtime.ProviderReceiptJson!,
            targetHost,
            checker.Image,
            checker.Command ?? [],
            checker.Environment ?? new Dictionary<string, string>(),
            callbackUrl,
            token,
            message.Deadline,
            TimeSpan.FromSeconds(checker.TimeoutSeconds) < remaining
                ? TimeSpan.FromSeconds(checker.TimeoutSeconds)
                : remaining,
            settings.CheckerAllowRoot);
    }

    private static async Task<string?> ResolveTargetHostAsync(
        RuntimeKind kind,
        RuntimeProvider provider,
        string providerReceiptJson,
        Guid runtimeInstanceId,
        string? targetServiceName,
        IRuntimeProviderCatalog providers,
        CancellationToken cancellationToken)
    {
        try
        {
            if (kind == RuntimeKind.Container)
            {
                var receipt = JsonSerializer.Deserialize<ContainerReceipt>(providerReceiptJson);
                return receipt is not null
                    && receipt.Provider == provider
                    && receipt.RuntimeInstanceId == runtimeInstanceId
                    ? receipt.InternalHost
                    : null;
            }

            var composeReceipt = JsonSerializer.Deserialize<ComposeReceipt>(providerReceiptJson);
            if (composeReceipt is null
                || composeReceipt.Provider != provider
                || composeReceipt.OperationId != runtimeInstanceId)
                return null;
            var status = await providers.Compose(provider).GetStatusAsync(
                composeReceipt,
                cancellationToken);
            if (status is null)
                return null;
            return status.Services.SingleOrDefault(service =>
                string.Equals(service.Name, targetServiceName, StringComparison.Ordinal))?.InternalHost
                ?? (targetServiceName is null && status.Services.Count == 1
                    ? status.Services[0].InternalHost
                    : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class AwdCheckerExecutor(
    IOneShotRuntimeProviderCatalog providers,
    IHttpClientFactory httpClients,
    AuxiliaryRuntimeCapacity? capacity = null)
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
            CreateOperationId(work.RuntimeInstanceId, work.GameplayFactId),
            work.Provider,
            work.Image,
            work.Command,
            environment,
            new Dictionary<string, string>(),
            new Dictionary<int, int>(),
            new RuntimeResourceLimits(256 * 1024 * 1024, 250_000_000, 128),
            new ContainerSecurityPolicy(true, true, !work.AllowRoot, ["ALL"], []),
            work.Timeout,
            OperationTimeout: work.Timeout,
            AllowInternalCallback: true,
            RuntimeInstanceId: work.RuntimeInstanceId,
            NetworkPurpose: ContainerNetworkPurpose.AwdChecker);
        var target = CreateTarget(work);
        if (target is null)
            return AwdCheckerExecutionOutcome.Superseded;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(work.Timeout);
        try
        {
            var result = capacity is null
                ? await providers.Attached(work.Provider).RunAttachedAsync(request, target, timeoutSource.Token)
                : await capacity.RunAsync(request,
                    new(RuntimeWorkloadKind.AwdChecker, work.RuntimeInstanceId, request.OperationId), work.GameplayFactId,
                    (reserved, token) => providers.Attached(work.Provider).RunAttachedAsync(reserved, target, token), timeoutSource.Token);
            if (result.ExitCode == 0)
                return AwdCheckerExecutionOutcome.Completed;
            await ReportPlatformStatusAsync(
                work,
                AwdServiceState.CheckerAbnormalExit,
                cancellationToken);
            return AwdCheckerExecutionOutcome.AbnormalExit;
        }
        catch (RunnerCapacityUnavailableException) when (capacity is not null)
        {
            await capacity.RecordAwdAdmissionFailureAsync(work.GameplayFactId, cancellationToken);
            return AwdCheckerExecutionOutcome.Superseded;
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
            var identity = new RuntimeResourceIdentity(work.RuntimeInstanceId);
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
            && !string.IsNullOrWhiteSpace(work.TargetHost)
                ? new(identity, receipt, work.TargetHost)
                : null;
    }

    private static Guid CreateOperationId(Guid runtimeInstanceId, Guid gameplayFactId)
    {
        Span<byte> input = stackalloc byte[32];
        runtimeInstanceId.TryWriteBytes(input[..16]);
        gameplayFactId.TryWriteBytes(input[16..]);
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
