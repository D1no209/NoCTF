using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Challenges.Images;
using NoCTF.Infrastructure.Challenges.Images;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class OciRegistryManifestResolverTests
{
    [Test]
    public async Task Anonymous_manifest_is_parsed_and_pinned_to_its_content_digest()
    {
        var manifest = Manifest(1);
        var handler = new RecordingHandler((_, _) => Task.FromResult(Response(manifest)));
        var resolver = Resolver(handler);

        var result = await resolver.ResolveAsync("registry.example/acme/app:v1", default);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.PinnedImage).IsEqualTo(
            $"registry.example/acme/app@{Digest(manifest)}");
        await Assert.That(handler.Requests).Count().IsEqualTo(1);
        await Assert.That(handler.Requests[0].Uri.AbsolutePath)
            .IsEqualTo("/v2/acme/app/manifests/v1");
    }

    [Test]
    public async Task Bearer_challenge_is_followed_without_exposing_or_reusing_the_token_elsewhere()
    {
        var manifest = IndexManifest();
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.Host == "auth.example")
                return Task.FromResult(Response("""{"token":"secret-access-token"}"""));
            if (request.Headers.Authorization is null)
            {
                var unauthorized = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                unauthorized.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue(
                    "Bearer",
                    "realm=\"https://auth.example/token\",service=\"registry.example\",scope=\"repository:acme/app:pull\""));
                return Task.FromResult(unauthorized);
            }
            return Task.FromResult(Response(manifest));
        });

        var result = await Resolver(handler).ResolveAsync(
            "registry.example/acme/app:stable",
            default);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.PinnedImage).IsEqualTo(
            $"registry.example/acme/app@{Digest(manifest)}");
        await Assert.That(handler.Requests).Count().IsEqualTo(3);
        await Assert.That(handler.Requests[1].Uri.Query)
            .Contains("scope=repository%3Aacme%2Fapp%3Apull");
        await Assert.That(handler.Requests[2].AuthorizationScheme).IsEqualTo("Bearer");
        await Assert.That(handler.Requests[2].AuthorizationParameter)
            .IsEqualTo("secret-access-token");
        await Assert.That(handler.Requests[0].AuthorizationParameter).IsNull();
        await Assert.That(handler.Requests[1].AuthorizationParameter).IsNull();
    }

    [Test]
    public async Task Unsupported_registry_authentication_returns_a_stable_failure()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Basic", "realm=\"private\""));
            return Task.FromResult(response);
        });

        var result = await Resolver(handler).ResolveAsync(
            "registry.example/acme/private:v1",
            default);

        await Assert.That(result.Failure)
            .IsEqualTo(RegistryManifestFailureCode.AuthenticationRequired);
    }

    [Test]
    public async Task Manifest_digest_mismatch_is_rejected()
    {
        var response = Response(Manifest(1));
        response.Headers.TryAddWithoutValidation(
            "Docker-Content-Digest",
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var handler = new RecordingHandler((_, _) => Task.FromResult(response));

        var result = await Resolver(handler).ResolveAsync(
            "registry.example/acme/app:v1",
            default);

        await Assert.That(result.Failure)
            .IsEqualTo(RegistryManifestFailureCode.ManifestInvalid);
    }

    [Test]
    public async Task Tag_drift_resolves_to_the_current_manifest_each_time()
    {
        var manifests = new Queue<string>(
        [
            Manifest(1),
            Manifest(2)
        ]);
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(Response(manifests.Dequeue())));
        var resolver = Resolver(handler);

        var first = await resolver.ResolveAsync("registry.example/acme/app:latest", default);
        var second = await resolver.ResolveAsync("registry.example/acme/app:latest", default);

        await Assert.That(first.PinnedImage).IsNotEqualTo(second.PinnedImage);
        await Assert.That(handler.Requests).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Already_pinned_image_does_not_contact_the_registry()
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException());
        var resolver = Resolver(handler);
        const string image =
            "registry.example/acme/app@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        var result = await resolver.ResolveAsync(image, default);

        await Assert.That(result.PinnedImage).IsEqualTo(image);
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task Incomplete_json_document_is_not_accepted_as_a_manifest()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Response(
            """{"schemaVersion":2,"mediaType":"application/vnd.oci.image.manifest.v1+json"}""")));

        var result = await Resolver(handler).ResolveAsync(
            "registry.example/acme/app:v1",
            default);

        await Assert.That(result.Failure)
            .IsEqualTo(RegistryManifestFailureCode.ManifestInvalid);
    }

    [Test]
    public async Task Oversized_authentication_response_returns_a_stable_failure()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.Host == "auth.example")
                return Task.FromResult(Response($$"""{"token":"{{new string('a', 70_000)}}"}"""));
            var unauthorized = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            unauthorized.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue(
                "Bearer",
                "realm=\"https://auth.example/token\""));
            return Task.FromResult(unauthorized);
        });

        var result = await Resolver(handler).ResolveAsync(
            "registry.example/acme/app:v1",
            default);

        await Assert.That(result.Failure)
            .IsEqualTo(RegistryManifestFailureCode.AuthenticationFailed);
    }

    [Test]
    public async Task Chunked_oversized_authentication_response_is_stopped_at_the_read_limit()
    {
        var stream = new CountingNonSeekableStream(
            Encoding.UTF8.GetBytes($$"""{"token":"{{new string('a', 1_000_000)}}"}"""));
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.Host == "auth.example")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(stream)
                });
            }
            var unauthorized = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            unauthorized.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue(
                "Bearer",
                "realm=\"https://auth.example/token\""));
            return Task.FromResult(unauthorized);
        });

        var result = await Resolver(handler).ResolveAsync(
            "registry.example/acme/app:v1",
            default);

        await Assert.That(result.Failure)
            .IsEqualTo(RegistryManifestFailureCode.AuthenticationFailed);
        await Assert.That(stream.BytesRead).IsLessThan(100_000L);
    }

    [Test]
    [Arguments("""{"schemaVersion":2,"mediaType":"application/vnd.oci.image.manifest.v1+json","config":{"digest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":2},"layers":[]}""")]
    [Arguments("""{"schemaVersion":2,"mediaType":"application/vnd.oci.image.manifest.v1+json","config":{"mediaType":"application/vnd.oci.image.config.v1+json","digest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":0},"layers":[]}""")]
    public async Task Invalid_manifest_descriptor_is_rejected(string manifest)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Response(manifest)));

        var result = await Resolver(handler).ResolveAsync(
            "registry.example/acme/app:v1",
            default);

        await Assert.That(result.Failure)
            .IsEqualTo(RegistryManifestFailureCode.ManifestInvalid);
    }

    private static OciRegistryManifestResolver Resolver(HttpMessageHandler handler) =>
        new(new HttpClientFactory(new HttpClient(handler)));

    private static HttpResponseMessage Response(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string Manifest(int revision) => $$"""
        {
          "schemaVersion": 2,
          "mediaType": "application/vnd.oci.image.manifest.v1+json",
          "config": {
            "mediaType": "application/vnd.oci.image.config.v1+json",
            "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "size": 2
          },
          "layers": [{
            "mediaType": "application/vnd.oci.image.layer.v1.tar+gzip",
            "digest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "size": {{revision}}
          }]
        }
        """;

    private static string IndexManifest() => """
        {
          "schemaVersion": 2,
          "mediaType": "application/vnd.oci.image.index.v1+json",
          "manifests": [{
            "mediaType": "application/vnd.oci.image.manifest.v1+json",
            "digest": "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
            "size": 2
          }]
        }
        """;

    private static string Digest(string text) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()}";

    private sealed class HttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
        : HttpMessageHandler
    {
        public List<RequestRecord> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new(
                request.RequestUri!,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            return handle(request, cancellationToken);
        }
    }

    private sealed record RequestRecord(
        Uri Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter);

    private sealed class CountingNonSeekableStream(byte[] bytes) : Stream
    {
        private int position;

        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = Math.Min(count, bytes.Length - position);
            if (read <= 0)
                return 0;
            bytes.AsSpan(position, read).CopyTo(buffer.AsSpan(offset, read));
            position += read;
            BytesRead += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = Math.Min(buffer.Length, bytes.Length - position);
            if (read <= 0)
                return ValueTask.FromResult(0);
            bytes.AsMemory(position, read).CopyTo(buffer);
            position += read;
            BytesRead += read;
            return ValueTask.FromResult(read);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
