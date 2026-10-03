using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace NoCTF.E2E;

internal static class E2EHttpClient
{
    internal static string UniqueIdentity(string value)
    {
        var suffix = Environment.GetEnvironmentVariable("NOCTF_E2E_RUN_SUFFIX");
        if (string.IsNullOrWhiteSpace(suffix)) return value;
        var at = value.IndexOf('@');
        return at < 0 ? $"{value}-{suffix}" : $"{value[..at]}-{suffix}{value[at..]}";
    }
    internal static string AdminUserName(string defaultName) =>
        Environment.GetEnvironmentVariable("NOCTF_E2E_ADMIN_USERNAME") ?? defaultName;

    internal static HttpClient Create(string baseUrl, string? token = null)
    {
        var client = new HttpClient(CreateHandler()) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    internal static SocketsHttpHandler CreateHandler()
    {
        var handler = new SocketsHttpHandler();
        var address = Environment.GetEnvironmentVariable("NOCTF_E2E_CONNECT_ADDRESS");
        if (!string.IsNullOrWhiteSpace(address))
        {
            handler.UseProxy = false;
            var parsed = IPAddress.Parse(address);
            handler.ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(parsed, context.DnsEndPoint.Port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            };
        }
        var certificateHash = Environment.GetEnvironmentVariable("NOCTF_E2E_TLS_SHA256");
        if (!string.IsNullOrWhiteSpace(certificateHash))
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                errors == SslPolicyErrors.None || certificate is not null
                    && string.Equals(certificate.GetCertHashString(HashAlgorithmName.SHA256), certificateHash, StringComparison.OrdinalIgnoreCase);
        return handler;
    }
}
