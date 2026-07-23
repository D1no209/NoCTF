namespace NoCTF.Domain.Submissions;

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
