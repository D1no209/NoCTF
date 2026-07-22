using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;

namespace NoCTF.Runner.Endpoints;

public sealed class RunAwdpVerificationRequest
{
    public Guid OperationId { get; set; }
    public Guid UploadId { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public ChallengeRuntimeTemplate Runtime { get; set; } = default!;
    public string PatchEntrypoint { get; set; } = "fix.sh";
    public List<string> PatchCommand { get; set; } = ["/bin/sh", "{entrypoint}"];
    public int PatchTimeoutSeconds { get; set; } = 60;
    public int TargetPort { get; set; }
    public int ReadyTimeoutSeconds { get; set; } = 30;
    public RunnerJobConfiguration Checker { get; set; } = default!;
    public RunnerScoringCallback Callback { get; set; } = default!;
}

public sealed record AwdpVerificationRunnerResult(string Phase, int ExitCode, bool TimedOut);

public sealed class RunAwdpVerificationEndpoint(
    IContainerRuntimeProviderCatalog providers,
    IOneShotRuntimeProviderCatalog oneShotProviders,
    IRunnerScoringTokenIssuer tokens,
    FixArchivePreparer archives,
    IHttpClientFactory clients,
    IConfiguration configuration)
    : Endpoint<RunAwdpVerificationRequest, Results<Ok<AwdpVerificationRunnerResult>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/jobs/awdp-verification");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<AwdpVerificationRunnerResult>, ProblemHttpResult>> ExecuteAsync(
        RunAwdpVerificationRequest request, CancellationToken cancellationToken)
    {
        if (request.Runtime is null || request.Checker is null
            || request.Checker.Provider != request.Runtime.Provider || request.UploadId == Guid.Empty
            || request.SubmissionId == Guid.Empty || request.TargetPort is < 1 or > 65535
            || request.ReadyTimeoutSeconds <= 0)
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid AWDP verification job.");

        var operationId = request.OperationId == Guid.Empty ? request.SubmissionId : request.OperationId;
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"noctf-awdp-{operationId:N}");
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(
            request.PatchTimeoutSeconds + request.ReadyTimeoutSeconds + request.Checker.TimeoutSeconds + 600L);
        ContainerReceipt? target = null;
        string? networkId = null;
        try
        {
            Directory.CreateDirectory(temporaryRoot);
            var download = await DownloadArchiveAsync(request, cancellationToken);
            using (download.Response)
            await using (download.Stream)
                await archives.PrepareTarAsync(download.Stream, download.FileName,
                    Path.Combine(temporaryRoot, "expanded"), Path.Combine(temporaryRoot, "payload.tar"), cancellationToken);

            var lifecycle = providers.Containers(request.Runtime.Provider);
            var sandbox = lifecycle as IContainerSandboxLifecycle
                ?? throw new InvalidOperationException("Selected provider does not support AWDP sandbox operations.");
            networkId = await sandbox.CreateIsolatedNetworkAsync(operationId, expiresAt, cancellationToken);
            var targetPorts = request.Runtime.Provider == RuntimeProvider.Kubernetes
                ? new Dictionary<int, int> { [request.TargetPort] = request.TargetPort }
                : new Dictionary<int, int>();
            target = await lifecycle.CreateAsync(new(operationId, request.Runtime.Provider, request.Runtime.Image,
                request.Runtime.Command ?? [], request.Runtime.Environment ?? new Dictionary<string, string>(),
                Labels(request, expiresAt), targetPorts,
                request.Runtime.Limits ?? new(268_435_456, 500_000_000, 128),
                new(true, false, true, ["ALL"], []), null, NetworkName: networkId), cancellationToken);
            await using (var tar = File.OpenRead(Path.Combine(temporaryRoot, "payload.tar")))
                await sandbox.CopyArchiveAsync(target, tar, cancellationToken);

            var entrypoint = "/noctf/fix/" + request.PatchEntrypoint.Replace('\\', '/').TrimStart('/');
            var command = request.PatchCommand.Select(item => item.Replace("{entrypoint}", entrypoint, StringComparison.Ordinal)).ToArray();
            var patch = await sandbox.ExecAsync(target, command,
                TimeSpan.FromSeconds(request.PatchTimeoutSeconds), cancellationToken);
            if (patch.ExitCode != 0 || patch.TimedOut)
            {
                await CallbackAsync(request.Callback, "patch", patch.ExitCode, patch.TimedOut, cancellationToken);
                return TypedResults.Ok(new AwdpVerificationRunnerResult("patch", patch.ExitCode, patch.TimedOut));
            }

            var environment = new Dictionary<string, string>(request.Checker.Environment ?? new Dictionary<string, string>(), StringComparer.Ordinal)
            {
                ["TARGET_HOST"] = target.InternalHost ?? target.ResourceId,
                ["TARGET_PORT"] = request.TargetPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["TARGET_READY_TIMEOUT_SECONDS"] = request.ReadyTimeoutSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
            };
            OneShotResult checker;
            using var checkerTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            checkerTimeout.CancelAfter(TimeSpan.FromSeconds(request.Checker.TimeoutSeconds));
            try
            {
                checker = await oneShotProviders.OneShot(request.Checker.Provider).RunAsync(new(Guid.NewGuid(),
                    request.Checker.Provider, request.Checker.Image, request.Checker.Command ?? [], environment,
                    Labels(request, expiresAt), new Dictionary<int, int>(), new(268_435_456, 500_000_000, 128),
                    new(true, true, true, ["ALL"], []), null, NetworkName: networkId), checkerTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await CallbackAsync(request.Callback, "checker", -1, true, cancellationToken);
                return TypedResults.Ok(new AwdpVerificationRunnerResult("checker", -1, true));
            }
            await CallbackAsync(request.Callback, "checker", checker.ExitCode, false, cancellationToken);
            return TypedResults.Ok(new AwdpVerificationRunnerResult("checker", checker.ExitCode, false));
        }
        catch (InvalidDataException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Fix archive is invalid.", detail: exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: "AWDP verification failed.");
        }
        finally
        {
            if (target is not null)
            {
                try { await providers.Containers(target.Provider).DestroyAsync(target, CancellationToken.None); }
                catch { /* expiry labels allow the Runner reaper to remove a failed cleanup later */ }
            }
            if (networkId is not null)
            {
                try
                {
                    var sandbox = providers.Containers(request.Runtime.Provider) as IContainerSandboxLifecycle;
                    if (sandbox is not null) await sandbox.DeleteIsolatedNetworkAsync(networkId, CancellationToken.None);
                }
                catch { /* expiry-labelled resources are handled by Runner cleanup */ }
            }
            try
            {
                if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
            }
            catch
            {
                // The sandbox result is already recorded; stale temp files are safe to remove on the next Runner cleanup.
            }
        }
    }

    private async Task<(HttpResponseMessage Response, Stream Stream, string FileName)> DownloadArchiveAsync(
        RunAwdpVerificationRequest request, CancellationToken cancellationToken)
    {
        var baseText = configuration["RunnerScoring:CallbackBaseUrl"]
            ?? throw new InvalidOperationException("RunnerScoring:CallbackBaseUrl is required.");
        var url = new Uri(new Uri(baseText.EndsWith('/') ? baseText : baseText + "/"),
            $"internal/fix-archives/{request.UploadId:D}");
        var delays = new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15) };
        for (var index = 0; index < delays.Length; index++)
        {
            if (delays[index] > TimeSpan.Zero) await Task.Delay(delays[index], cancellationToken);
            using var message = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                tokens.IssueFixArchiveRead(request.Callback.RunnerId, request.UploadId, request.SubmissionId, DateTimeOffset.UtcNow));
            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            var response = await clients.CreateClient(nameof(RunAwdpVerificationEndpoint))
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, attemptTimeout.Token);
            if (response.IsSuccessStatusCode)
            {
                var name = response.Content.Headers.ContentDisposition?.FileNameStar
                    ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "fix.tar";
                return (response, await response.Content.ReadAsStreamAsync(cancellationToken), name);
            }
            response.Dispose();
            if (index == delays.Length - 1) throw new HttpRequestException("Fix archive download failed.");
        }
        throw new UnreachableException();
    }

    private async Task CallbackAsync(RunnerScoringCallback callback, string phase, int exitCode, bool timedOut,
        CancellationToken cancellationToken)
    {
        var baseUrl = new Uri(configuration["RunnerScoring:CallbackBaseUrl"]!);
        if (!baseUrl.IsBaseOf(callback.Url)) throw new InvalidOperationException("Runner callback URL is not allowed.");
        var payload = callback.Context.ToDictionary(item => item.Key, item => (object?)item.Value);
        payload["phase"] = phase; payload["exitCode"] = exitCode; payload["timedOut"] = timedOut;
        payload["occurredAt"] = DateTimeOffset.UtcNow;
        var delays = new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15) };
        for (var index = 0; index < delays.Length; index++)
        {
            if (delays[index] > TimeSpan.Zero) await Task.Delay(delays[index], cancellationToken);
            try
            {
                using var message = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, callback.Url)
                {
                    Content = JsonContent.Create(payload)
                };
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                    tokens.Issue(callback.RunnerId, DateTimeOffset.UtcNow));
                using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await clients.CreateClient(nameof(RunAwdpVerificationEndpoint))
                    .SendAsync(message, attemptTimeout.Token);
                if (response.IsSuccessStatusCode) return;
                if (index == delays.Length - 1) response.EnsureSuccessStatusCode();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch when (index < delays.Length - 1)
            {
                // Retry transient transport and non-success responses with a fresh scoring token.
            }
        }
        throw new UnreachableException();
    }

    private static Dictionary<string, string> Labels(
        RunAwdpVerificationRequest request, DateTimeOffset expiresAt) => new(StringComparer.Ordinal)
    {
        ["noctf.io/job-kind"] = "awdp-verification", ["noctf.io/submission-id"] = request.SubmissionId.ToString("N"),
        ["noctf.io/competition-id"] = request.CompetitionId.ToString("N"),
        ["noctf.io/competition-challenge-id"] = request.CompetitionChallengeId.ToString("N"),
        ["noctf.io/expires-at"] = expiresAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
}
