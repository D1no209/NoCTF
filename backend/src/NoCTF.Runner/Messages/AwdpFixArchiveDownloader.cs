using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace NoCTF.Runner.Messages;

public sealed record AwdpFixArchive(
    Uri DownloadUrl,
    string DownloadToken,
    string OriginalFileName,
    long ByteLength,
    byte[] Sha256);

public enum AwdpFixArchiveDownloadOutcome
{
    Downloaded,
    Unavailable,
    IntegrityMismatch
}

public sealed class AwdpFixArchiveDownloader(IHttpClientFactory httpClients)
{
    public async Task<AwdpFixArchiveDownloadOutcome> DownloadAsync(
        AwdpFixArchive archive,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, archive.DownloadUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            archive.DownloadToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        HttpResponseMessage response;
        try
        {
            response = await httpClients.CreateClient().SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
        }
        catch (HttpRequestException)
        {
            return AwdpFixArchiveDownloadOutcome.Unavailable;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return AwdpFixArchiveDownloadOutcome.Unavailable;
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return AwdpFixArchiveDownloadOutcome.Unavailable;
            try
            {
                return await CopyAndVerifyAsync(
                    response,
                    archive,
                    destinationPath,
                    timeout.Token);
            }
            catch (HttpRequestException)
            {
                DeletePartialFile(destinationPath);
                return AwdpFixArchiveDownloadOutcome.Unavailable;
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                DeletePartialFile(destinationPath);
                return AwdpFixArchiveDownloadOutcome.Unavailable;
            }
        }
    }

    private static async Task<AwdpFixArchiveDownloadOutcome> CopyAndVerifyAsync(
        HttpResponseMessage response,
        AwdpFixArchive archive,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        var accepted = false;
        try
        {
            long length = 0;
            await using (var destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                buffer.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    var read = await source.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        cancellationToken);
                    if (read == 0)
                        break;
                    length = checked(length + read);
                    if (length > archive.ByteLength)
                        break;
                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(
                        buffer.AsMemory(0, read),
                        cancellationToken);
                }
            }
            accepted = length == archive.ByteLength
                && CryptographicOperations.FixedTimeEquals(
                    hash.GetHashAndReset(),
                    archive.Sha256);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        if (!accepted)
            DeletePartialFile(destinationPath);
        return accepted
            ? AwdpFixArchiveDownloadOutcome.Downloaded
            : AwdpFixArchiveDownloadOutcome.IntegrityMismatch;
    }

    private static void DeletePartialFile(string destinationPath)
    {
        if (File.Exists(destinationPath))
            File.Delete(destinationPath);
    }
}
