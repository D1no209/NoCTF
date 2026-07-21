namespace NoCTF.API.Endpoints.Authentication;

public static class RefreshRequestGuard
{
    public static bool IsAllowed(HttpRequest request, IConfiguration configuration)
    {
        var suppliedOrigin = request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(suppliedOrigin))
            suppliedOrigin = request.Headers.Referer.FirstOrDefault();

        // Non-browser clients do not consistently send Origin or Referer. The refresh cookie is
        // still HttpOnly and SameSite=Strict, so retain that supported client contract.
        if (string.IsNullOrWhiteSpace(suppliedOrigin)) return true;
        if (!Uri.TryCreate(suppliedOrigin, UriKind.Absolute, out var supplied)) return false;

        var apiOrigin = new Uri($"{request.Scheme}://{request.Host}");
        var allowedOrigins = configuration
            .GetSection("Authentication:RefreshAllowedOrigins")
            .Get<string[]>()
            ?.Select(NormalizeOrigin)
            .Where(origin => origin is not null)
            .Cast<Uri>()
            .Append(apiOrigin)
            ?? [apiOrigin];

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
