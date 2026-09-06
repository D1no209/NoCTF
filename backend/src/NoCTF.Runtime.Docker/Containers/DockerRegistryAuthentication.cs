using System.Text;
using System.Text.Json;
using Docker.DotNet.Models;

namespace NoCTF.Runtime.Docker.Containers;

/// <summary>Reads the same per-registry Docker login file used by the Compose CLI.</summary>
public static class DockerRegistryAuthentication
{
    public static AuthConfig Read(string image, string? configDirectory = null)
    {
        var slash = image.IndexOf('/');
        var first = slash < 0 ? string.Empty : image[..slash];
        var registry = first.Contains('.') || first.Contains(':') || first == "localhost"
            ? first : "docker.io";
        configDirectory ??= Environment.GetEnvironmentVariable("DOCKER_CONFIG");
        if (string.IsNullOrWhiteSpace(configDirectory))
            configDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker");
        var path = Path.Combine(configDirectory, "config.json");
        if (!File.Exists(path)) return new AuthConfig();
        if (new FileInfo(path).Length > 1_048_576)
            throw new InvalidOperationException("Docker login configuration exceeds the supported size.");
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            if (json.RootElement.TryGetProperty("auths", out var auths))
            {
                foreach (var entry in auths.EnumerateObject())
                {
                    if (!string.Equals(NormalizeRegistry(entry.Name), NormalizeRegistry(registry), StringComparison.OrdinalIgnoreCase))
                        continue;
                    var value = entry.Value;
                    if (value.TryGetProperty("identitytoken", out var identity) && !string.IsNullOrEmpty(identity.GetString()))
                        return new AuthConfig { ServerAddress = registry, IdentityToken = identity.GetString() };
                    if (value.TryGetProperty("auth", out var encoded) && !string.IsNullOrEmpty(encoded.GetString()))
                    {
                        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded.GetString()!));
                        var separator = decoded.IndexOf(':');
                        if (separator < 1) throw new FormatException();
                        return new AuthConfig { ServerAddress = registry, Username = decoded[..separator], Password = decoded[(separator + 1)..] };
                    }
                }
            }
            if (json.RootElement.TryGetProperty("credsStore", out _)
                || json.RootElement.TryGetProperty("credHelpers", out var helpers)
                    && helpers.EnumerateObject().Any(item =>
                        string.Equals(NormalizeRegistry(item.Name), NormalizeRegistry(registry), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Use a dedicated Docker config directory with inline auths for NoCTF; desktop credential helpers are not available inside the Host image.");
            return new AuthConfig();
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            throw new InvalidOperationException("Docker login configuration is invalid or requires an unavailable credential helper; use a dedicated config directory with inline auths created by docker login.");
        }
    }

    private static string NormalizeRegistry(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            value = uri.Authority;
        value = value.TrimEnd('/');
        return value is "index.docker.io" or "registry-1.docker.io" ? "docker.io" : value;
    }
}
