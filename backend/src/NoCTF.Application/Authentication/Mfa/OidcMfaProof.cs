namespace NoCTF.Application.Authentication.Mfa;

public sealed record OidcMfaProof(Guid ProviderId, Guid TrustPolicyId, DateTimeOffset AuthenticatedAt);

public sealed record OidcMfaTrust(
    bool Enabled,
    Guid PolicyId,
    int MaxAgeSeconds,
    IReadOnlyList<string> AcrValues,
    IReadOnlyList<IReadOnlyList<string>> AmrCombinations)
{
    public OidcMfaProof? Verify(Guid providerId, string? acr, IReadOnlyList<string> amr, DateTimeOffset? authenticatedAt, DateTimeOffset now)
    {
        if (!Enabled || PolicyId == Guid.Empty || MaxAgeSeconds is < 1 or > 300 || authenticatedAt is null
            || authenticatedAt.Value > now.AddSeconds(30) || now - authenticatedAt.Value > TimeSpan.FromSeconds(MaxAgeSeconds)) return null;
        var accepted = acr is not null && AcrValues.Contains(acr, StringComparer.Ordinal)
            || AmrCombinations.Any(combination => combination.Count > 0 && combination.All(value => amr.Contains(value, StringComparer.Ordinal)));
        return accepted ? new(providerId, PolicyId, authenticatedAt.Value) : null;
    }
}
