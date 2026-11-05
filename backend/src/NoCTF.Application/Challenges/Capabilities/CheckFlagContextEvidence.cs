using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Application.Challenges.Capabilities;

public sealed record CheckFlagContextEvidence(Guid FlagId, string? PrivateReadContext, string? PublicLocator)
{
    public override string ToString() => $"CheckFlagContextEvidence {{ FlagId = {FlagId}, Material = [REDACTED] }}";
}

public static class CheckFlagContextValidation
{
    public static CapabilityExecutionFailure? Validate(IReadOnlySet<Guid> permittedFlags, IReadOnlyList<CheckFlagContextEvidence> contexts) =>
        contexts.Select(value => value.FlagId).Distinct().Count() != contexts.Count
        || contexts.Any(value => value.FlagId == Guid.Empty || !permittedFlags.Contains(value.FlagId)
            || value.PrivateReadContext?.Length > 8192 || value.PublicLocator?.Length > 2048
            || value.PrivateReadContext is null && value.PublicLocator is null)
            ? CapabilityExecutionFailure.InvalidEvidence : null;
}
