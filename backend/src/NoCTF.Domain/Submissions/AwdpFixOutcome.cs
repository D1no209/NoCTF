namespace NoCTF.Domain.Gameplay;

public enum AwdpFixOutcome
{
    Fixed,
    StillVulnerable,
    RuleViolation,
    ServiceUnavailable,
    PatchFailed,
    PatchTimeout,
    PlatformFailed
}
