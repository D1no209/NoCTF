using NoCTF.Application.Competitions.Koh;

namespace NoCTF.Infrastructure.Competitions.Koh;

public sealed class HttpKohControlClient(HttpClient client) : IKohControlClient
{
    private const int MaximumBodyBytes = 4096;

    public async Task<KohControlResponse> ObserveAsync(
        Uri controlUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        try
        {
            using var response = await client.GetAsync(
                controlUrl,
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token);
            if (!response.IsSuccessStatusCode)
                return KohControlResponse.Unavailable();
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
            return KohControlResponse.Success(body.AsMemory(0, length).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            return KohControlResponse.Timeout();
        }
        catch (HttpRequestException)
        {
            return KohControlResponse.Unavailable();
        }
    }
}
