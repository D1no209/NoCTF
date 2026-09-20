using System.Net;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;

namespace NoCTF.Tests.Unit.Authentication;

public sealed class CasSsoProtocolAdapterTests
{
    [Test]
    public async Task Uses_the_original_service_and_reads_the_authenticated_subject(
        CancellationToken cancellationToken)
    {
        Uri? requestUri = null;
        var backchannel = new FakeBackchannel(request =>
        {
            requestUri = request.RequestUri;
            return Response("""
                <cas:serviceResponse xmlns:cas="http://www.yale.edu/tp/cas">
                  <cas:authenticationSuccess>
                    <cas:user>student-1</cas:user>
                    <cas:attributes><cas:displayName>Student One</cas:displayName></cas:attributes>
                  </cas:authenticationSuccess>
                </cas:serviceResponse>
                """);
        });
        var provider = Provider();
        const string service = "https://noctf.example/api/v1/auth/sso/callback/cas/00000000-0000-0000-0000-000000000001?flow=abc";

        var result = await new CasSsoProtocolAdapter(backchannel).AuthenticateAsync(
            provider, Flow(provider, service), "ST-valid-ticket", cancellationToken);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.Subject).IsEqualTo("student-1");
        await Assert.That(result.Value.DisplayName).IsEqualTo("Student One");
        await Assert.That(requestUri!.Query).Contains(Uri.EscapeDataString(service));
    }

    [Test]
    public async Task Rejects_dtd_and_external_entity_payloads(
        CancellationToken cancellationToken)
    {
        var backchannel = new FakeBackchannel(_ => Response("""
            <!DOCTYPE serviceResponse [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <cas:serviceResponse xmlns:cas="http://www.yale.edu/tp/cas">
              <cas:authenticationSuccess><cas:user>&xxe;</cas:user></cas:authenticationSuccess>
            </cas:serviceResponse>
            """));
        var provider = Provider();
        var result = await new CasSsoProtocolAdapter(backchannel).AuthenticateAsync(
            provider,
            Flow(provider, "https://noctf.example/callback?flow=abc"),
            "ST-valid-ticket",
            cancellationToken);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(SsoFailureCode.AuthenticationFailed);
    }

    private static SsoProviderRuntimeConfiguration Provider() => new(
        GlobalEnabled: true,
        "https://noctf.example",
        Guid.Parse("00000000-0000-0000-0000-000000000001"),
        "Example CAS",
        SsoProtocol.Cas,
        Enabled: true,
        AllowLogin: true,
        AllowBinding: true,
        TimeoutSeconds: 10,
        AllowedHosts: ["cas.example.test"],
        Fingerprint: "fingerprint",
        Oidc: null,
        Cas: new(
            "example-cas",
            "https://cas.example.test/login",
            "https://cas.example.test/serviceValidate",
            "displayName"));

    private static SsoFlowRecord Flow(
        SsoProviderRuntimeConfiguration provider,
        string serviceUrl)
    {
        var now = DateTimeOffset.UtcNow;
        return new(
            Guid.CreateVersion7(now),
            provider.ProviderId,
            SsoProtocol.Cas,
            SsoFlowIntent.Login,
            "browser",
            SsoFlowState.Processing,
            "state",
            provider.Fingerprint,
            "/",
            now,
            now.AddMinutes(10),
            ServiceUrl: serviceUrl,
            ProcessingToken: "processing");
    }

    private static SsoBackchannelResponse Response(string content) =>
        new(HttpStatusCode.OK, content, null);

    private sealed class FakeBackchannel(
        Func<HttpRequestMessage, SsoBackchannelResponse> responder) : ISsoBackchannel
    {
        public Task<SsoBackchannelResponse> SendAsync(
            SsoBackchannelPolicy policy,
            HttpRequestMessage request,
            int maximumResponseBytes,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));

        public void ValidateUri(SsoBackchannelPolicy policy, Uri uri)
        {
            if (!policy.AllowedHosts.Contains(uri.DnsSafeHost, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Host rejected by test backchannel.");
        }
    }
}
