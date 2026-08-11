namespace NoCTF.Domain.Runtime;

public enum RuntimeAccessScheme
{
    Http,
    Https,
    Tcp,
    Udp,
    Ssh
}

public readonly record struct RuntimeAccessUrl
{
    private RuntimeAccessUrl(string value, RuntimeAccessScheme scheme)
    {
        Value = value;
        Scheme = scheme;
    }

    public string Value { get; }
    public RuntimeAccessScheme Scheme { get; }

    public static bool TryCreate(string? value, out RuntimeAccessUrl accessUrl)
    {
        accessUrl = default;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !TryReadScheme(uri.Scheme, out var scheme))
            return false;

        accessUrl = new(uri.AbsoluteUri, scheme);
        return true;
    }

    private static bool TryReadScheme(
        string scheme,
        out RuntimeAccessScheme accessScheme)
    {
        var parsedScheme = scheme.ToLowerInvariant() switch
        {
            "http" => (RuntimeAccessScheme?)RuntimeAccessScheme.Http,
            "https" => RuntimeAccessScheme.Https,
            "tcp" => RuntimeAccessScheme.Tcp,
            "udp" => RuntimeAccessScheme.Udp,
            "ssh" => RuntimeAccessScheme.Ssh,
            _ => null
        };
        accessScheme = parsedScheme.GetValueOrDefault();
        return parsedScheme.HasValue;
    }
}
