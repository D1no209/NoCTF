using System.Globalization;
using System.Security.Claims;
using NoCTF.Domain.Identity.Mfa;

namespace NoCTF.Infrastructure.Authentication.Mfa;

public static class AuthenticationContextClaims
{
    public static IEnumerable<Claim> Write(AuthenticationContext context)
    {
        yield return new("auth_method", context.Method.ToString());
        yield return new("auth_time", context.AuthenticatedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64);
        yield return new("mfa_source", context.MfaSource.ToString());
        if (context.MfaAuthenticatedAt is { } time) yield return new("mfa_time", time.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64);
        if (context.PrimaryCredentialId is { } primary) yield return new("auth_credential", primary.ToString("N"));
        if (context.CredentialId is { } credential) yield return new("mfa_credential", credential.ToString("N"));
        if (context.ProviderId is { } provider) yield return new("mfa_provider", provider.ToString("N"));
        if (context.TrustPolicyId is { } policy) yield return new("mfa_trust_policy", policy.ToString("N"));
    }

    public static AuthenticationContext? Read(ClaimsPrincipal principal)
    {
        if (!Enum.TryParse<AuthenticationMethod>(principal.FindFirstValue("auth_method"), out var method) || !Enum.IsDefined(method)
            || !Enum.TryParse<MfaSource>(principal.FindFirstValue("mfa_source"), out var source) || !Enum.IsDefined(source)
            || !long.TryParse(principal.FindFirstValue("auth_time"), out var authenticated)) return null;
        try
        {
            var mfaTime = principal.FindFirstValue("mfa_time");
            if (mfaTime is not null && !long.TryParse(mfaTime, out _)) return null;
            var context = new AuthenticationContext(method, DateTimeOffset.FromUnixTimeSeconds(authenticated), source,
                mfaTime is null ? null : DateTimeOffset.FromUnixTimeSeconds(long.Parse(mfaTime, CultureInfo.InvariantCulture)),
                GuidValue(principal, "mfa_credential"), GuidValue(principal, "mfa_provider"), GuidValue(principal, "mfa_trust_policy"), GuidValue(principal, "auth_credential"));
            return context.IsWellFormed ? context : null;
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static Guid? GuidValue(ClaimsPrincipal principal, string key) => Guid.TryParse(principal.FindFirstValue(key), out var value) ? value : null;
}
