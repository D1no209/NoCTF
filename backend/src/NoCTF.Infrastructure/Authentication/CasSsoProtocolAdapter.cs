using System.Text;
using System.Xml;
using System.Xml.Linq;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Application.Common;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Authentication;

public sealed class CasSsoProtocolAdapter(ISsoBackchannel backchannel) : ISsoProtocolAdapter
{
    private const int MaximumResponseBytes = 64 * 1024;
    private static readonly XNamespace CasNamespace = "http://www.yale.edu/tp/cas";

    public SsoProtocol Protocol => SsoProtocol.Cas;

    public Task<SsoAuthorizationRequest> CreateAuthorizationAsync(
        SsoProviderRuntimeConfiguration provider,
        string correlationToken,
        CancellationToken ct)
    {
        var cas = Required(provider);
        var login = Endpoint(cas.LoginUrl, "login");
        backchannel.ValidateUri(Policy(provider), login);
        var service = CallbackUri(provider, correlationToken);
        var authorization = AppendQuery(login, new Dictionary<string, string>
        {
            ["service"] = service.AbsoluteUri
        });
        return Task.FromResult(new SsoAuthorizationRequest(
            authorization,
            ServiceUrl: service.AbsoluteUri));
    }

    public async Task<OperationResult<SsoExternalIdentity, SsoFailureCode>> AuthenticateAsync(
        SsoProviderRuntimeConfiguration provider,
        SsoFlowRecord flow,
        string credential,
        CancellationToken ct)
    {
        var cas = Required(provider);
        if (string.IsNullOrWhiteSpace(flow.ServiceUrl)
            || string.IsNullOrWhiteSpace(credential)
            || !credential.StartsWith("ST-", StringComparison.Ordinal)
            || credential.Length > 4096)
            return Failure("The CAS callback is incomplete.");
        try
        {
            var validation = Endpoint(cas.ServiceValidateUrl, "serviceValidate");
            var uri = AppendQuery(validation, new Dictionary<string, string>
            {
                ["service"] = flow.ServiceUrl,
                ["ticket"] = credential
            });
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await backchannel.SendAsync(
                Policy(provider), request, MaximumResponseBytes, ct);
            if (!response.IsSuccessStatusCode)
                return Failure("The CAS ticket validation endpoint rejected the request.");
            var document = Parse(response.Content);
            var root = document.Root;
            if (root?.Name != CasNamespace + "serviceResponse")
                return Failure("The CAS validation response root is invalid.");
            if (root.Elements(CasNamespace + "authenticationFailure").Any())
                return Failure("The CAS ticket was rejected.");
            var successes = root.Elements(CasNamespace + "authenticationSuccess").ToArray();
            if (successes.Length != 1)
                return Failure("The CAS validation response is ambiguous.");
            var users = successes[0].Elements(CasNamespace + "user").ToArray();
            if (users.Length != 1 || !ValidSubject(users[0].Value))
                return Failure("The CAS authentication subject is invalid.");
            var attributes = successes[0].Element(CasNamespace + "attributes");
            var displayName = attributes?.Elements()
                .SingleOrDefault(element => string.Equals(
                    element.Name.LocalName,
                    cas.DisplayNameAttribute,
                    StringComparison.Ordinal))?.Value;
            return OperationResult<SsoExternalIdentity, SsoFailureCode>.Success(new(
                provider.ProviderId,
                SsoProtocol.Cas,
                cas.IdentityNamespace,
                users[0].Value,
                NormalizeDisplayName(displayName)));
        }
        catch (Exception exception) when (exception is XmlException
            or InvalidOperationException)
        {
            return Failure("CAS validation failed.");
        }
    }

    private static XDocument Parse(string content)
    {
        using var reader = XmlReader.Create(
            new StringReader(content),
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumResponseBytes,
                IgnoreComments = true
            });
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static Uri CallbackUri(
        SsoProviderRuntimeConfiguration provider,
        string correlationToken)
    {
        var callback = new Uri(
            new Uri(provider.PublicBaseUrl.TrimEnd('/') + "/", UriKind.Absolute),
            $"api/v1/auth/sso/callback/cas/{provider.ProviderId:D}");
        return AppendQuery(callback, new Dictionary<string, string>
        {
            ["flow"] = correlationToken
        });
    }

    private static Uri AppendQuery(Uri uri, IReadOnlyDictionary<string, string> values)
    {
        var builder = new UriBuilder(uri);
        var existing = builder.Query.TrimStart('?');
        var query = string.Join('&', values.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        builder.Query = string.IsNullOrEmpty(existing) ? query : $"{existing}&{query}";
        return builder.Uri;
    }

    private static Uri Endpoint(string value, string name) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && uri.UserInfo.Length == 0
            ? uri
            : throw new InvalidOperationException($"The CAS {name} endpoint is invalid.");

    private static CasSsoRuntimeConfiguration Required(SsoProviderRuntimeConfiguration provider) =>
        provider.Cas ?? throw new InvalidOperationException("CAS provider settings are missing.");

    private static SsoBackchannelPolicy Policy(SsoProviderRuntimeConfiguration provider) =>
        new(provider.TimeoutSeconds, provider.AllowedHosts);

    private static bool ValidSubject(string value) => value.Length is >= 1 and <= 255
        && !value.Any(char.IsControl);

    private static string? NormalizeDisplayName(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized)
            ? null
            : normalized.Length <= 256 ? normalized : normalized[..256];
    }

    private static OperationResult<SsoExternalIdentity, SsoFailureCode> Failure(string message) =>
        OperationResult<SsoExternalIdentity, SsoFailureCode>.Failure(
            SsoFailureCode.AuthenticationFailed,
            message);
}
