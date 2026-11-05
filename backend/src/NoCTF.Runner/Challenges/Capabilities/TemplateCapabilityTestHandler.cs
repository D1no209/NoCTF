using System.Security.Cryptography;
using System.Text.Json;
using FluentStorage.Storage;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Storage;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Testing;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Challenges.Testing;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using Wolverine.Attributes;

namespace NoCTF.Runner.Challenges.Capabilities;

[NonTransactional]
public sealed class TemplateCapabilityTestHandler(TemplateCapabilityTestExecutionStore tests, ExecuteFix fix, ExecuteCheck check,
    IStore objects, FixArchivePreparer preparer, IRunnerScoringTokenIssuer tokens, IOptions<RunnerOptions> runner,
    IOptions<RunnerScoringOptions> scoring, IOptions<FixVerificationOptions> options, IRunnerCapacityGate capacity,
    RunnerResourceMutationCoordinator mutations, TimeProvider clock, IHostApplicationLifetime lifetime,
    ILogger<TemplateCapabilityTestHandler> logger)
{
    public async Task Handle(RunTemplateCapabilityTest message, CancellationToken ct)
    {
        RunnerNodeAssignmentGuard.Validate(message, runner.Value.Pool, runner.Value.Id);
        using var targetMutation = await mutations.EnterRuntimeAsync(message.RuntimeInstanceId, ct);
        if (!await capacity.CanMutateAsync(message.RuntimeInstanceId, runner.Value.Id, ct)) return;
        var work = await tests.ClaimAsync(message, ct);
        if (work is null) return;
        var directory = Path.GetFullPath(Path.Combine(options.Value.WorkDirectory, $"template-{work.ExecutionId:N}-{work.RuntimeInstanceId:N}"));
        Directory.CreateDirectory(directory);
        CapabilityExecutionFailure? failure = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var remaining = work.Deadline - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero) deadline.Cancel(); else deadline.CancelAfter(remaining);
        try
        {
            var tarPath = Path.Combine(directory, "prepared.tar");
            if (work.Kind != TemplateCapabilityTestKind.Check)
            {
                var file = work.Archive ?? throw new InvalidOperationException("An admitted Fix test requires its immutable archive.");
                var sourcePath = Path.Combine(directory, "upload.tar.gz");
                await using (var source = await objects.OpenRead(file.ObjectKey, deadline.Token)
                    ?? throw new InvalidDataException("The accepted test archive is unavailable."))
                await using (var copy = new FileStream(sourcePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
                {
                    await source.CopyToAsync(copy, deadline.Token);
                    copy.Position = 0;
                    if (copy.Length != file.ByteLength || !CryptographicOperations.FixedTimeEquals(
                        await SHA256.HashDataAsync(copy, deadline.Token), file.Sha256))
                        throw new InvalidDataException("The accepted test archive failed its integrity check.");
                    copy.Position = 0;
                    await preparer.PrepareTarAsync(copy, file.FileName, work.Fix!.Entrypoint, Path.Combine(directory, "extracted"), tarPath, deadline.Token);
                }
                await using var tar = File.OpenRead(tarPath);
                var result = await fix.ExecuteAsync(new(work.ExecutionId, work.RuntimeInstanceId, work.Fix!,
                    work.Receipt.ServiceReceipt(work.Fix!.TargetServiceName), tar, work.Deadline,
                    Scope: CapabilityExecutionScope.TemplateTest), deadline.Token);
                if (!result.Succeeded) failure = result.FailureCode;
                else if (!await capacity.CanMutateAsync(work.RuntimeInstanceId, runner.Value.Id, deadline.Token))
                    failure = CapabilityExecutionFailure.TargetSuperseded;
                else if (!await tests.RecordFixAsync(result.Value!, runner.Value.Id, deadline.Token))
                {
                    await tests.CompleteWorkAsync(work.RuntimeInstanceId, work.ExecutionId, runner.Value.Id, result.Value!.ExecutionFailure, deadline.Token);
                    return;
                }
            }
            if (failure is null && work.Kind != TemplateCapabilityTestKind.Fix
                && !await capacity.CanMutateAsync(work.RuntimeInstanceId, runner.Value.Id, deadline.Token))
                failure = CapabilityExecutionFailure.TargetSuperseded;
            if (failure is null && work.Kind != TemplateCapabilityTestKind.Fix)
            {
                var definition = work.Check ?? throw new InvalidOperationException("An admitted Check test requires its capability definition.");
                var receipt = work.Receipt.ServiceReceipt(definition.TargetServiceName);
                var host = work.Receipt.Services.Single(service => service.Name == definition.TargetServiceName).InternalHost
                    ?? throw new InvalidOperationException("The Check target service has no internal address.");
                var origin = scoring.Value.CallbackBaseUrl ?? throw new InvalidOperationException("RunnerScoring:CallbackBaseUrl is required.");
                var now = clock.GetUtcNow();
                var timeout = TimeSpan.FromSeconds(definition.TimeoutSeconds);
                if (work.Deadline - now < timeout) timeout = work.Deadline - now;
                var environment = definition.Environment.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);
                environment["NOCTF_CHECK_FLAGS"] = JsonSerializer.Serialize(work.Flags.Select(flag => new { id = flag.Id, flag = flag.Flag,
                    historical = work.Requirements.Any(value => value.Kind == CheckItemKind.HistoricalFlagRead && value.FlagId == flag.Id),
                    privateReadContext = work.FlagContexts.SingleOrDefault(context => context.FlagId == flag.Id)?.PrivateReadContext,
                    publicLocator = work.FlagContexts.SingleOrDefault(context => context.FlagId == flag.Id)?.PublicLocator }));
                var input = new CheckExecutionInput(work.RuntimeInstanceId, receipt.Provider, definition.Image,
                    definition.Command.OrderBy(value => value.Position).Select(value => value.Value).ToArray(), environment, receipt, host,
                    definition.ReadyTimeoutSeconds, new Uri(origin, "/api/internal/v1/capabilities/check-results"),
                    tokens.IssueTemplateCheckResult(runner.Value.Id, work.ExecutionId, work.RuntimeInstanceId, work.Deadline, now),
                    timeout, definition.AcceptsFixInput, work.ExecutionId)
                {
                    Scope = CapabilityExecutionScope.TemplateTest, ExecutionId = work.ExecutionId,
                    Requirements = work.Requirements.Select(value => value.Kind).Distinct().ToArray()
                };
                if (work.Kind == TemplateCapabilityTestKind.FixAndCheck && definition.AcceptsFixInput)
                {
                    await using var tar = File.OpenRead(tarPath);
                    var result = await check.ExecuteAsync(input, new OneShotInputArchive(tar, OneShotInputArchive.RootDestinationPath), deadline.Token);
                    failure = result.FailureCode;
                }
                else failure = (await check.ExecuteAsync(input, null, deadline.Token)).FailureCode;
            }
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { failure = CapabilityExecutionFailure.DeadlineExceeded; }
        catch (InvalidDataException) { failure = CapabilityExecutionFailure.InvalidEvidence; }
        catch (Exception exception)
        {
            failure = CapabilityExecutionFailure.ExecutionInterrupted;
            logger.LogError("Template capability test {ExecutionId} failed with {FailureType}.", work.ExecutionId, exception.GetType().Name);
        }
        finally
        {
            try { Directory.Delete(directory, true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { logger.LogWarning("Template test scratch cleanup failed for {ExecutionId} with {FailureType}.", work.ExecutionId, exception.GetType().Name); }
        }
        using var completion = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        completion.CancelAfter(TimeSpan.FromSeconds(30));
        await tests.CompleteWorkAsync(work.RuntimeInstanceId, work.ExecutionId, runner.Value.Id, failure, completion.Token);
    }
}
