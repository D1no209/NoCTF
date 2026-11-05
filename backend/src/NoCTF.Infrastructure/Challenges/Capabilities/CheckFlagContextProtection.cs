using NoCTF.Infrastructure.Authentication;

namespace NoCTF.Infrastructure.Challenges.Capabilities;

public static class CheckFlagContextProtection
{
    public static byte[]? Protect(string? value, Guid ownerId, Guid flagId, PlatformSecretProtector? secrets) => value is null ? null
        : (secrets ?? throw new InvalidOperationException("Check context protection is unavailable."))
            .Protect(value, PlatformSecretPurpose.CheckFlagContextEvidence, ownerId, flagId);

    public static string? Reveal(byte[]? value, Guid ownerId, Guid flagId, PlatformSecretProtector? secrets) => value is null ? null
        : (secrets ?? throw new InvalidOperationException("Check context protection is unavailable."))
            .Unprotect(value, PlatformSecretPurpose.CheckFlagContextEvidence, ownerId, flagId);
}
