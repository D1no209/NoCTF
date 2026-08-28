namespace NoCTF.API.Endpoints.Authentication;

public static class RefreshRequestGuard
{
    public static bool IsAllowed(HttpRequest request, RefreshHttpOptions options)
    {
        var suppliedOrigin = request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(suppliedOrigin))
            suppliedOrigin = request.Headers.Referer.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(suppliedOrigin))
            return false;
        if (!Uri.TryCreate(suppliedOrigin, UriKind.Absolute, out var supplied)) return false;

        var apiOrigin = new Uri($"{request.Scheme}://{request.Host}");
        var allowedOrigins = options.RefreshAllowedOrigins
            .Select(NormalizeOrigin)
            .Where(origin => origin is not null)
            .Cast<Uri>()
            .Append(apiOrigin);

        return allowedOrigins.Any(origin => Uri.Compare(
            supplied,
            origin,
            UriComponents.SchemeAndServer,
            UriFormat.Unescaped,
            StringComparison.OrdinalIgnoreCase) == 0);
    }

    private static Uri? NormalizeOrigin(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var origin) ? origin : null;
}
