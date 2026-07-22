using System.Net.Http.Json;
using NoCTF.Application.Authentication;
using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Runner.Composition;

public sealed class RunnerScoringCallbackDispatcher(
    IRunnerScoringTokenIssuer tokens,
    IHttpClientFactory clients,
    IConfiguration configuration) : IRunnerScoringCallbackDispatcher
{
    public async Task DispatchAsync(
        RunnerScoringCallback? callback,
        OneShotResult result,
        bool timedOut,
        CancellationToken cancellationToken)
    {
        if (callback is null) return;
        var configuredBaseUrl = configuration["RunnerScoring:CallbackBaseUrl"];
        if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var baseUrl)
            || !baseUrl.IsBaseOf(callback.Url))
            throw new InvalidOperationException("Runner scoring callback URL is not allowed.");

        var payload = callback.Context.ToDictionary(item => item.Key, item => (object?)item.Value);
        payload["exitCode"] = result.ExitCode;
        payload["timedOut"] = timedOut;
        payload["occurredAt"] = result.FinishedAt;
        for (var index = 0; index < RunnerScoringCallbackDeliveryPolicy.RetryDelays.Count; index++)
        {
            var delay = RunnerScoringCallbackDeliveryPolicy.RetryDelays[index];
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, callback.Url)
                {
                    Content = JsonContent.Create(payload)
                };
                request.Headers.Authorization = new("Bearer", tokens.Issue(callback.RunnerId, DateTimeOffset.UtcNow));
                using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptTimeout.CancelAfter(RunnerScoringCallbackDeliveryPolicy.AttemptTimeout);
                using var response = await clients.CreateClient(nameof(RunnerScoringCallbackDispatcher))
                    .SendAsync(request, attemptTimeout.Token);
                if (response.IsSuccessStatusCode) return;
                if (index == RunnerScoringCallbackDeliveryPolicy.RetryDelays.Count - 1)
                    response.EnsureSuccessStatusCode();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch when (index < RunnerScoringCallbackDeliveryPolicy.RetryDelays.Count - 1)
            {
                // Retry transient transport and non-success responses with a fresh scoring token.
            }
        }
        throw new InvalidOperationException("Runner scoring callback retry loop completed without a result.");
    }
}
