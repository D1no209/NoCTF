using System.Net;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Runner;

public sealed class AwdpFixArchiveDownloaderTests
{
    [Test]
    public async Task Download_uses_the_claim_bound_bearer_token_and_verifies_the_body()
    {
        var body = Encoding.UTF8.GetBytes("archive-body");
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        });
        var downloader = new AwdpFixArchiveDownloader(new StubHttpClientFactory(handler));
        var destination = Path.Combine(
            Path.GetTempPath(),
            $"noctf-fix-archive-{Guid.NewGuid():N}.tar.gz");
        var archive = new AwdpFixArchive(
            new Uri("https://api.example/api/internal/v1/awdp/fix-archives/"
                + "11111111-1111-1111-1111-111111111111"),
            "claim-bound-token",
            "fix.tar.gz",
            body.LongLength,
            SHA256.HashData(body));

        try
        {
            var downloaded = await downloader.DownloadAsync(
                archive,
                destination,
                CancellationToken.None);

            await Assert.That(downloaded)
                .IsEqualTo(AwdpFixArchiveDownloadOutcome.Downloaded);
            await Assert.That(await File.ReadAllBytesAsync(destination)).IsEquivalentTo(body);
            await Assert.That(handler.Method).IsEqualTo(HttpMethod.Get);
            await Assert.That(handler.RequestUri).IsEqualTo(archive.DownloadUrl);
            await Assert.That(handler.AuthorizationScheme).IsEqualTo("Bearer");
            await Assert.That(handler.AuthorizationParameter).IsEqualTo("claim-bound-token");
        }
        finally
        {
            if (File.Exists(destination))
                File.Delete(destination);
        }
    }

    [Test]
    public async Task Download_rejects_a_body_that_does_not_match_the_database_metadata()
    {
        var body = Encoding.UTF8.GetBytes("tampered-archive");
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        });
        var downloader = new AwdpFixArchiveDownloader(new StubHttpClientFactory(handler));
        var destination = Path.Combine(
            Path.GetTempPath(),
            $"noctf-fix-archive-{Guid.NewGuid():N}.tar.gz");
        var archive = new AwdpFixArchive(
            new Uri("https://api.example/api/internal/v1/awdp/fix-archives/"
                + "11111111-1111-1111-1111-111111111111"),
            "claim-bound-token",
            "fix.tar.gz",
            body.LongLength,
            SHA256.HashData(Encoding.UTF8.GetBytes("expected-archive")));

        try
        {
            var downloaded = await downloader.DownloadAsync(
                archive,
                destination,
                CancellationToken.None);

            await Assert.That(downloaded)
                .IsEqualTo(AwdpFixArchiveDownloadOutcome.IntegrityMismatch);
        }
        finally
        {
            if (File.Exists(destination))
                File.Delete(destination);
        }
    }

    [Test]
    public async Task Download_treats_a_missing_claim_bound_archive_as_unavailable()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        var downloader = new AwdpFixArchiveDownloader(new StubHttpClientFactory(handler));
        var destination = Path.Combine(
            Path.GetTempPath(),
            $"noctf-fix-archive-{Guid.NewGuid():N}.tar.gz");
        var archive = new AwdpFixArchive(
            new Uri("https://api.example/api/internal/v1/awdp/fix-archives/"
                + "11111111-1111-1111-1111-111111111111"),
            "claim-bound-token",
            "fix.tar.gz",
            1,
            new byte[SHA256.HashSizeInBytes]);

        var downloaded = await downloader.DownloadAsync(
            archive,
            destination,
            CancellationToken.None);

        await Assert.That(downloaded)
            .IsEqualTo(AwdpFixArchiveDownloadOutcome.HttpStatusRejected);
        await Assert.That(File.Exists(destination)).IsFalse();
    }

    [Test]
    public async Task Download_treats_transport_failures_as_a_stable_unavailable_outcome()
    {
        var downloader = new AwdpFixArchiveDownloader(
            new StubHttpClientFactory(new TransportFailureHandler()));
        var destination = Path.Combine(
            Path.GetTempPath(),
            $"noctf-fix-archive-{Guid.NewGuid():N}.tar.gz");
        var archive = new AwdpFixArchive(
            new Uri("https://api.example/api/internal/v1/awdp/fix-archives/"
                + "11111111-1111-1111-1111-111111111111"),
            "claim-bound-token",
            "fix.tar.gz",
            1,
            new byte[SHA256.HashSizeInBytes]);

        var downloaded = await downloader.DownloadAsync(
            archive,
            destination,
            CancellationToken.None);

        await Assert.That(downloaded)
            .IsEqualTo(AwdpFixArchiveDownloadOutcome.ConnectionFailed);
        await Assert.That(File.Exists(destination)).IsFalse();
    }

    [Test]
    [Arguments(HttpStatusCode.TemporaryRedirect)]
    [Arguments(HttpStatusCode.Unauthorized)]
    [Arguments(HttpStatusCode.Forbidden)]
    [Arguments(HttpStatusCode.NotFound)]
    public async Task Download_rejects_non_success_responses_without_following_them(
        HttpStatusCode statusCode)
    {
        var downloader = new AwdpFixArchiveDownloader(
            new StubHttpClientFactory(new RecordingHandler(
                new HttpResponseMessage(statusCode))));

        var downloaded = await downloader.DownloadAsync(
            Archive(),
            Destination(),
            CancellationToken.None);

        await Assert.That(downloaded)
            .IsEqualTo(AwdpFixArchiveDownloadOutcome.HttpStatusRejected);
    }

    [Test]
    public async Task Download_timeout_is_bounded_and_terminal()
    {
        var downloader = new AwdpFixArchiveDownloader(
            new StubHttpClientFactory(new BlockingHandler()),
            TimeSpan.FromMilliseconds(25));

        var downloaded = await downloader.DownloadAsync(
            Archive(),
            Destination(),
            CancellationToken.None);

        await Assert.That(downloaded)
            .IsEqualTo(AwdpFixArchiveDownloadOutcome.TimedOut);
    }

    [Test]
    public async Task Download_interruption_removes_the_partial_file_and_is_terminal()
    {
        var destination = Destination();
        var downloader = new AwdpFixArchiveDownloader(
            new StubHttpClientFactory(new RecordingHandler(new HttpResponseMessage(
                HttpStatusCode.OK)
            {
                Content = new StreamContent(new InterruptedStream())
            })));

        var downloaded = await downloader.DownloadAsync(
            Archive(),
            destination,
            CancellationToken.None);

        await Assert.That(downloaded)
            .IsEqualTo(AwdpFixArchiveDownloadOutcome.TransferInterrupted);
        await Assert.That(File.Exists(destination)).IsFalse();
    }

    [Test]
    public async Task Caller_cancellation_is_not_converted_into_a_retryable_download_failure()
    {
        var downloader = new AwdpFixArchiveDownloader(
            new StubHttpClientFactory(new BlockingHandler()),
            TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(25));
        Func<Task> action = async () => _ = await downloader.DownloadAsync(
            Archive(),
            Destination(),
            cancellation.Token);

        await Assert.That(action).Throws<OperationCanceledException>();
    }

    private static string Destination() => Path.Combine(
        Path.GetTempPath(),
        $"noctf-fix-archive-{Guid.NewGuid():N}.tar.gz");

    private static AwdpFixArchive Archive() => new(
        new Uri("https://api.example/api/internal/v1/awdp/fix-archives/"
            + "11111111-1111-1111-1111-111111111111"),
        "claim-bound-token",
        "fix.tar.gz",
        1,
        new byte[SHA256.HashSizeInBytes]);

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return Task.FromResult(response);
        }
    }

    private sealed class TransportFailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("The internal archive endpoint is unavailable.");
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The delay must be cancelled.");
        }
    }

    private sealed class InterruptedStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("The response stream was interrupted.");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(
                new IOException("The response stream was interrupted."));

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
