namespace NoCTF.API.Endpoints.Authentication;

internal static class RefreshRequestGuard
{
    public static bool IsSameOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin))
            origin = request.Headers.Referer.ToString();
        if (string.IsNullOrWhiteSpace(origin)) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var supplied)) return false;
        var current = new Uri($"{request.Scheme}://{request.Host}");
        return Uri.Compare(supplied, current, UriComponents.SchemeAndServer, UriFormat.Unescaped,
            StringComparison.OrdinalIgnoreCase) == 0;
    }
}
