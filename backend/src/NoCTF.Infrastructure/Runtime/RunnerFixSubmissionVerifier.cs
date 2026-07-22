using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime;

public sealed class RunnerFixSubmissionVerifier(
    NoCtfDbContext db,
    IHttpClientFactory clients,
    IAwdpCheckExitCodeMapper exitCodes,
    IConfiguration configuration) : IRunnerFixSubmissionVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<FixVerificationResult> VerifyAsync(FixSubmissionReceived submission, CancellationToken cancellationToken)
    {
        var target = await db.CompetitionChallenges.AsNoTracking()
            .Join(db.Competitions.AsNoTracking(), item => item.CompetitionId, competition => competition.Id,
                (item, competition) => new { Challenge = item, Competition = competition })
            .Where(item => item.Challenge.Id == submission.CompetitionChallengeId
                           && item.Challenge.CompetitionId == submission.CompetitionId
                           && item.Competition.Mode == GameMode.Awdp
                           && item.Competition.Status == CompetitionStatus.Running)
            .Select(item => new
            {
                item.Challenge.ConfigurationJson, item.Challenge.Revision, item.Competition.StartTime,
                CompetitionConfigurationJson = item.Competition.ConfigurationJson
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null) return Platform(ScoringFailureCode.ProducerUnavailable);
        var settings = JsonSerializer.Deserialize<AwdpChallengeConfiguration>(target.ConfigurationJson, JsonOptions);
        var competition = JsonSerializer.Deserialize<AwdpConfiguration>(target.CompetitionConfigurationJson, JsonOptions);
        if (settings?.Runtime is null || settings.Checker is null || competition is null)
            return Platform(ScoringFailureCode.ProducerUnavailable);
        var runnerBase = configuration["Runtime:Runner:BaseUrl"];
        var callbackBase = configuration["RunnerScoring:CallbackBaseUrl"];
        if (!Uri.TryCreate(runnerBase, UriKind.Absolute, out var runnerUri)
            || !Uri.TryCreate(callbackBase, UriKind.Absolute, out var callbackUri))
            return Platform(ScoringFailureCode.ProducerUnavailable);

        var round = Math.Max(1, (int)((submission.ReceivedAt - target.StartTime).TotalSeconds
            / competition.RoundDurationSeconds) + 1);
        var sourceKey = $"awdp:{submission.CompetitionId:N}:{submission.CompetitionChallengeId:N}:{submission.TeamId:N}:round:{round}:fix:{submission.SubmissionId:N}:revision:{target.Revision}:verification";
        var callback = new RunnerScoringCallback(
            new Uri(new Uri(callbackUri.ToString().TrimEnd('/') + "/"),
                $"internal/competitions/{submission.CompetitionId:D}/awdp-verification-results"),
            configuration["RunnerScoring:RunnerId"] ?? "noctf-runner",
            new Dictionary<string, string>
            {
                ["teamId"] = submission.TeamId.ToString("D"),
                ["competitionChallengeId"] = submission.CompetitionChallengeId.ToString("D"),
                ["submissionId"] = submission.SubmissionId.ToString("D"),
                ["sourceKey"] = sourceKey
            });
        var request = new RunnerRequest(submission.SubmissionId, submission.UploadId, submission.SubmissionId,
            submission.CompetitionId, submission.TeamId, submission.CompetitionChallengeId, settings.Runtime,
            settings.PatchEntrypoint, settings.PatchCommand ?? ["/bin/sh", "{entrypoint}"],
            settings.PatchTimeoutSeconds, settings.TargetPort, settings.ReadyTimeoutSeconds, settings.Checker, callback);
        using var message = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(runnerUri.ToString().TrimEnd('/') + "/"), "jobs/awdp-verification"))
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-Runner-Key", configuration["Runtime:Runner:ApiKey"]);
        using var runnerTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runnerTimeout.CancelAfter(TimeSpan.FromSeconds(
            (long)settings.PatchTimeoutSeconds + settings.ReadyTimeoutSeconds
            + settings.Checker.TimeoutSeconds + 180L));
        try
        {
            using var response = await clients.CreateClient(nameof(RunnerFixSubmissionVerifier))
                .SendAsync(message, runnerTimeout.Token);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<RunnerResult>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Runner returned an empty verification result.");
            if (result.Phase == "patch")
                return result.TimedOut
                    ? Team(ScoringFailureCode.AwdpPatchTimeout)
                    : Team(ScoringFailureCode.AwdpPatchFailed);
            var decision = exitCodes.Map(result.ExitCode, result.TimedOut);
            return decision.Result switch
            {
                ScoringResult.Correct => new(FixVerificationDecision.Valid),
                ScoringResult.PlatformFailed => Platform(decision.FailureCode ?? ScoringFailureCode.CheckerPlatformError),
                _ => Team(decision.FailureCode ?? ScoringFailureCode.AwdpFixFailed)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (runnerTimeout.IsCancellationRequested)
        {
            return Platform(ScoringFailureCode.ProducerTimeout);
        }
        catch { return Platform(ScoringFailureCode.ProducerUnavailable); }
    }

    private static FixVerificationResult Team(ScoringFailureCode code) => new(FixVerificationDecision.TeamFailure, code);
    private static FixVerificationResult Platform(ScoringFailureCode code) => new(FixVerificationDecision.PlatformFailed, code);

    private sealed record RunnerRequest(Guid OperationId, Guid UploadId, Guid SubmissionId, Guid CompetitionId,
        Guid TeamId, Guid CompetitionChallengeId, ChallengeRuntimeTemplate Runtime, string PatchEntrypoint,
        IReadOnlyList<string> PatchCommand, int PatchTimeoutSeconds, int TargetPort, int ReadyTimeoutSeconds,
        RunnerJobConfiguration Checker, RunnerScoringCallback Callback);
    private sealed record RunnerResult(string Phase, int ExitCode, bool TimedOut);
}
