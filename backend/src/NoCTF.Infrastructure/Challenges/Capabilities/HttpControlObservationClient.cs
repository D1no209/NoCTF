using NoCTF.Application.Challenges.Capabilities;

namespace NoCTF.Infrastructure.Challenges.Capabilities;

public sealed class HttpControlObservationClient(IHttpClientFactory clients) : IControlObservationClient
{
    public const string ClientName = "ChallengeControl";
    private const int MaximumBodyBytes = 4096;

    public async Task<ControlReadResponse> ReadAsync(
        Uri controlUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        using var client = clients.CreateClient(ClientName);
        try
        {
            using var response = await client.GetAsync(
                controlUrl,
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token);
            if (!response.IsSuccessStatusCode)
                return new(ControlReadState.Unavailable, ReadOnlyMemory<byte>.Empty);
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
            var body = new byte[MaximumBodyBytes + 1];
            var length = 0;
            while (length < body.Length)
            {
                var read = await stream.ReadAsync(body.AsMemory(length), linked.Token);
                if (read == 0)
                    break;
                length += read;
            }
            return new(ControlReadState.Completed, body.AsMemory(0, length).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            return new(ControlReadState.TimedOut, ReadOnlyMemory<byte>.Empty);
        }
        catch (HttpRequestException)
        {
            return new(ControlReadState.Unavailable, ReadOnlyMemory<byte>.Empty);
        }
    }
}
