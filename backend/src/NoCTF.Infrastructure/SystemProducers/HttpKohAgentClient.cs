using System.Net.Http.Json;
using NoCTF.Application.SystemProducers;

namespace NoCTF.Infrastructure.SystemProducers;

public sealed class HttpKohAgentClient(HttpClient client) : IKohAgentClient
{
    public async Task<KohAgentObservation> ObserveAsync(
        Uri agentUri,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        try
        {
            using var response = await client.GetAsync(agentUri, linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<KohAgentObservation>(cancellationToken: linked.Token)
                ?? throw new InvalidOperationException("KoH agent returned an empty observation.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCancellation.IsCancellationRequested)
        {
            throw new TimeoutException("KoH observation timed out.");
        }
    }
}
