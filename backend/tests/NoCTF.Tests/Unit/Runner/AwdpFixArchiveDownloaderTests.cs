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

            await Assert.That(downloaded).IsTrue();
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

            await Assert.That(downloaded).IsFalse();
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

        await Assert.That(downloaded).IsFalse();
        await Assert.That(File.Exists(destination)).IsFalse();
    }

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
}
