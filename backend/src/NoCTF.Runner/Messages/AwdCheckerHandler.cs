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
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;
using Wolverine.Attributes;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Runner.Messages;

public sealed record AwdCheckerWork(
    Guid RuntimeInstanceId,
    Guid GameplayFactId,
    RuntimeProvider Provider,
    RuntimeKind RuntimeKind,
    RuntimeReceiptData ProviderReceipt,
    string TargetHost,
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyDictionary<string, string> Environment,
    Uri CallbackUrl,
    string CallbackToken,
    DateTimeOffset Deadline,
    TimeSpan Timeout,
    string TargetServiceName,
    Guid AttemptId,
    IReadOnlyList<AwdExpectedFlag> Flags,
    Uri TaskUrl,
    IReadOnlyDictionary<string, string> Targets)
{
    public IReadOnlyList<CheckItemKind> Requirements { get; init; } = [];
    public int ReadyTimeoutSeconds { get; init; }
    public override string ToString() => $"AwdCheckerWork {{ RuntimeInstanceId = {RuntimeInstanceId}, GameplayFactId = {GameplayFactId}, AttemptId = {AttemptId}, CallbackToken = [REDACTED], Flags = [REDACTED] }}";
}

public interface IAwdCheckerWorkReader
{
    Task<AwdCheckerWork?> ReadAsync(RunAwdChecker message, CancellationToken cancellationToken);
    Task<bool> RequiresMutationAsync(RunAwdChecker message, CancellationToken ct);
    Task RecordWaitAsync(RunAwdChecker message, DateTimeOffset startedAt, DateTimeOffset endedAt, CancellationToken ct);
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
    IDbContextFactory<NoCtfDbContext> contexts,
    AwdCheckerConfigurationCatalog configurations,
    IRunnerScoringTokenIssuer tokens,
    IRuntimeProviderCatalog providers,
    IOptions<RunnerScoringOptions> scoringOptions,
    TimeProvider timeProvider) : IAwdCheckerWorkReader
{
    public async Task<bool> RequiresMutationAsync(RunAwdChecker message, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await db.GameplayFacts.AnyAsync(fact => fact.Id == message.GameplayFactId && fact.Awd != null
            && fact.Awd.AttemptId == message.AttemptId && fact.Awd.Flags.Any(flag => flag.RequiresWrite), ct);
    }

    public async Task RecordWaitAsync(RunAwdChecker message, DateTimeOffset startedAt, DateTimeOffset endedAt, CancellationToken ct)
    {
        if (startedAt >= message.Deadline || endedAt < message.Deadline) return;
        await using var db = await contexts.CreateDbContextAsync(ct);
        await NoCTF.Infrastructure.Competitions.Awd.AwdMaintenanceEvidence.RecordCheckerWaitAsync(db,
            message, startedAt, endedAt, ct);
    }

    public async Task<AwdCheckerWork?> ReadAsync(
        RunAwdChecker message,
        CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var fact = await db.GameplayFacts.Include(item => item.Awd).ThenInclude(details => details!.Flags)
            .SingleOrDefaultAsync(item => item.Id == message.GameplayFactId, cancellationToken);
        if (fact is not { Kind: GameplayFactKind.CheckObservation, Awd: { } attempt }
            || attempt.Execution != AwdCheckExecution.Pending || attempt.AttemptId != message.AttemptId
            || attempt.RuntimeId != message.RuntimeInstanceId || attempt.Deadline != message.Deadline
            || attempt.ReceiptDeadline != message.ReceiptDeadline)
            return null;
        if (attempt.Flags.Any(flag => flag.RequiresWrite) && await db.GameplayFacts.AnyAsync(value => value.FixExecution != null
                && value.FixExecution.RuntimeInstanceId == message.RuntimeInstanceId && value.FixExecution.MutationBlocked, cancellationToken)) return null;
        var target = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => runtime.Id == message.RuntimeInstanceId
                && runtime.CompetitionChallengeId == message.CompetitionChallengeId
                && runtime.State == RuntimeState.Running
                && runtime.RunnerId == message.RunnerId
                && runtime.ProviderReceipt != null)
            .Join(
                db.CompetitionChallenges.AsNoTracking(),
                runtime => runtime.CompetitionChallengeId,
                challenge => challenge.Id,
                (runtime, challenge) => new
                {
                    Runtime = runtime,
                    Challenge = challenge,
                    challenge.ChallengeId
                })
            .Join(
                db.Challenges.AsNoTracking(),
                pair => pair.ChallengeId,
                challenge => challenge.Id,
                (pair, challenge) => new
                {
                    pair.Runtime,
                    pair.Challenge,
                    Template = challenge
                })
            .Join(
                db.Competitions.AsNoTracking(),
                pair => pair.Runtime.CompetitionId,
                competition => competition.Id,
                (pair, competition) => new
                {
                    pair.Runtime,
                    pair.Challenge,
                    pair.Template,
                    Competition = competition
                })
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null
            || target.Competition.Mode != GameMode.Awd
            || target.Competition.Status != CompetitionStatus.Running
            || target.Competition.ModeConfiguration is not AwdCompetitionModeConfiguration competition
            || target.Challenge.Rules is not AwdCompetitionChallengeRules rules
            || target.Template.Definition is not ChallengeDefinition { Check: not null } definition)
            return null;
        var settings = configurations.Get(competition, rules, definition);
        if (settings.Checker is not { } checker
            || target.Runtime.RuntimeKind is not (RuntimeKind.Container))
            return null;
        var targetHost = await ResolveTargetHostAsync(
            target.Runtime.RuntimeKind,
            target.Runtime.RuntimeProvider,
            target.Runtime.ProviderReceipt!.ToData(),
            target.Runtime.Id,
            settings.TargetServiceName,
            providers,
            cancellationToken);
        if (targetHost is null)
            return null;
        var baseUri = scoringOptions.Value.CallbackBaseUrl
            ?? throw new InvalidOperationException(
                "RunnerScoring:CallbackBaseUrl must be configured as an absolute HTTP(S) URI.");
        var callbackUrl = new Uri(baseUri, "/api/internal/v1/capabilities/check-results");
        var flags = attempt.Flags.Select(flag => new AwdExpectedFlag(flag.FlagId, flag.Historical,
            flag.Obligation, flag.RequiresWrite)).ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        // Recheck after provider discovery: a pause/reset may have committed while that read was in flight.
        if (!await db.RuntimeInstances.AnyAsync(runtime => runtime.Id == message.RuntimeInstanceId
                && runtime.State == RuntimeState.Running && runtime.RunnerId == message.RunnerId
                && db.Competitions.Any(competition => competition.Id == runtime.CompetitionId
                    && competition.Status == CompetitionStatus.Running)
                && db.CompetitionChallenges.Any(challenge => challenge.Id == runtime.CompetitionChallengeId
                    && challenge.IsPublished && challenge.DeletedAt == null), cancellationToken))
            return null;
        var issuedAt = timeProvider.GetUtcNow();
        var remaining = message.Deadline - issuedAt;
        if (remaining <= TimeSpan.Zero) return null;
        var timeout = TimeSpan.FromSeconds(checker.TimeoutSeconds) < remaining
            ? TimeSpan.FromSeconds(checker.TimeoutSeconds) : remaining;
        attempt.Execution = AwdCheckExecution.Running;
        attempt.AttemptStartedAt = issuedAt;
        attempt.AttemptDeadline = issuedAt + timeout;
        var snapshot = await db.AwdRoundRules.AsNoTracking().SingleAsync(rule => rule.Id == attempt.RulesId, cancellationToken);
        attempt.ReceiptDeadline = attempt.AttemptDeadline.Value.AddSeconds(snapshot.Options.CallbackGraceSeconds);
        fact.State = GameplayFactState.Processing;
        fact.UpdatedAt = issuedAt;
        fact.ConcurrencyStamp = Guid.NewGuid();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (TransactionFailureClassifier.IsRetryable(exception)) { return null; }
        var token = tokens.IssueAwdChecker(new(
            message.RunnerId,
            message.RuntimeInstanceId,
            message.GameplayFactId,
            attempt.AttemptDeadline.Value,
            issuedAt, message.AttemptId, attempt.ReceiptDeadline.Value));
        return new(
            message.RuntimeInstanceId,
            message.GameplayFactId,
            target.Runtime.RuntimeProvider,
            target.Runtime.RuntimeKind,
            target.Runtime.ProviderReceipt!.ToData(),
            targetHost.Value.Host,
            checker.Image,
            checker.Command ?? [],
            checker.Environment ?? new Dictionary<string, string>(),
            callbackUrl,
            token,
            attempt.AttemptDeadline.Value, timeout, settings.TargetServiceName!, message.AttemptId, flags,
            new Uri(baseUri, "/api/internal/v1/awd/check-task"), targetHost.Value.Services)
        {
            Requirements = new[] { CheckItemKind.Connection, CheckItemKind.Functionality }
                .Concat(flags.Where(flag => flag.RequiresWrite).Select(_ => CheckItemKind.FlagWrite))
                .Concat(flags.Select(flag => flag.Historical ? CheckItemKind.HistoricalFlagRead : CheckItemKind.CurrentFlagRead)).Distinct().ToArray(),
            ReadyTimeoutSeconds = definition.Check.ReadyTimeoutSeconds
        };
    }

    private static async Task<(string Host, IReadOnlyDictionary<string, string> Services)?> ResolveTargetHostAsync(
        RuntimeKind kind,
        RuntimeProvider provider,
        RuntimeReceiptData providerReceipt,
        Guid runtimeInstanceId,
        string? targetServiceName,
        IRuntimeProviderCatalog providers,
        CancellationToken cancellationToken)
    {
        if (kind != RuntimeKind.Container || providerReceipt is not ContainerRuntimeReceiptData receipt
            || receipt.Provider != provider || receipt.OperationId != runtimeInstanceId || targetServiceName is null) return null;
        var status = await providers.Runtime(provider).GetStatusAsync(receipt.ToReceipt(), cancellationToken);
        if (status?.Services.SingleOrDefault(service => service.Name == targetServiceName)?.InternalHost is not { } host) return null;
        return (host, status.Services.Where(service => service.InternalHost is not null)
            .ToDictionary(service => service.Name, service => service.InternalHost!, StringComparer.Ordinal));
    }

}

public sealed class AwdCheckerExecutor(
    ExecuteCheck check,
    IHttpClientFactory httpClients,
    TimeProvider? timeProvider = null)
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
            ["NOCTF_CALLBACK_TOKEN"] = work.CallbackToken,
            ["NOCTF_AWD_TASK_URL"] = work.TaskUrl.AbsoluteUri,
            ["NOCTF_AWD_TARGETS"] = JsonSerializer.Serialize(work.Targets),
            ["NOCTF_AWD_DEADLINE"] = work.Deadline.ToString("O")
        };
        if (work.RuntimeKind != RuntimeKind.Container || work.ProviderReceipt is not ContainerRuntimeReceiptData receipt
            || receipt.Provider != work.Provider || receipt.OperationId != work.RuntimeInstanceId)
            return AwdCheckerExecutionOutcome.Superseded;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remaining = work.Deadline - (timeProvider ?? TimeProvider.System).GetUtcNow();
        if (remaining <= TimeSpan.Zero) timeoutSource.Cancel();
        else timeoutSource.CancelAfter(remaining < work.Timeout ? remaining : work.Timeout);
        try
        {
            timeoutSource.Token.ThrowIfCancellationRequested();
            var result = await check.ExecuteAsync(new(work.RuntimeInstanceId, work.Provider, work.Image, work.Command, environment,
                receipt.ServiceReceipt(work.TargetServiceName), work.TargetHost, work.ReadyTimeoutSeconds,
                work.CallbackUrl, work.CallbackToken, remaining < work.Timeout ? remaining : work.Timeout, ScopeId: work.GameplayFactId)
                { Use = CheckExecutionUse.Observation, ExecutionId = work.AttemptId, Requirements = work.Requirements }, null, timeoutSource.Token);
            if (result.Succeeded)
                return AwdCheckerExecutionOutcome.Completed;
            var timedOut = result.FailureCode == CapabilityExecutionFailure.DeadlineExceeded;
            await ReportPlatformStatusAsync(work, timedOut ? AwdCheckReason.CheckerTimedOut
                : result.FailureCode == CapabilityExecutionFailure.TargetExecutionFailed ? AwdCheckReason.CheckerCrashed : AwdCheckReason.InfrastructureFailure,
                cancellationToken);
            if (timedOut) return AwdCheckerExecutionOutcome.TimedOut;
            return AwdCheckerExecutionOutcome.AbnormalExit;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            await ReportPlatformStatusAsync(
                work,
                AwdCheckReason.CheckerTimedOut,
                cancellationToken);
            return AwdCheckerExecutionOutcome.TimedOut;
        }
    }

    private async Task ReportPlatformStatusAsync(
        AwdCheckerWork work,
        AwdCheckReason reason,
        CancellationToken cancellationToken)
    {
        var failure = reason switch
        {
            AwdCheckReason.CheckerTimedOut => CheckFailureReason.CheckerTimedOut,
            AwdCheckReason.CheckerCrashed => CheckFailureReason.CheckerCrashed,
            AwdCheckReason.InfrastructureFailure => CheckFailureReason.InfrastructureFailure,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };
        var probes = new List<CheckProbeEvidence>
        {
            new(CheckItemKind.Connection, null, CheckProbeResult.PlatformFailure, failure),
            new(CheckItemKind.Functionality, null, CheckProbeResult.Unknown, failure)
        };
        foreach (var flag in work.Flags)
        {
            if (flag.RequiresWrite) probes.Add(new(CheckItemKind.FlagWrite, flag.FlagId, CheckProbeResult.Unknown, failure));
            probes.Add(new(flag.Historical ? CheckItemKind.HistoricalFlagRead : CheckItemKind.CurrentFlagRead,
                flag.FlagId, CheckProbeResult.Unknown, failure));
        }
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        using var request = new HttpRequestMessage(HttpMethod.Post, work.CallbackUrl)
        {
            Content = JsonContent.Create(new
            {
                Probes = probes.Select(probe => new { Kind = probe.Kind.ToString(), probe.FlagId, Result = probe.Result.ToString(), Reason = probe.Reason.ToString() }),
                ObservedAt = now < work.Deadline ? now : work.Deadline
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", work.CallbackToken);
        using var response = await httpClients.CreateClient().SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

}

[NonTransactional]
public sealed class AwdCheckerHandler(
    IAwdCheckerWorkReader reader,
    IAwdCheckerExecutor executor,
    IOptions<RunnerOptions> runnerOptions,
    TimeProvider timeProvider,
    RunnerResourceMutationCoordinator mutations)
{
    public async Task<MessageExecutionOutcome> Handle(
        RunAwdChecker message,
        CancellationToken cancellationToken)
    {
        RunnerNodeAssignmentGuard.Validate(
            message,
            runnerOptions.Value.Pool,
            runnerOptions.Value.Id);
        var waitStartedAt = timeProvider.GetUtcNow();
        using var targetMutation = await reader.RequiresMutationAsync(message, cancellationToken)
            ? await mutations.EnterRuntimeAsync(message.RuntimeInstanceId, cancellationToken) : null;
        if (targetMutation?.Waited == true)
            await reader.RecordWaitAsync(message, waitStartedAt, timeProvider.GetUtcNow(), cancellationToken);
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
