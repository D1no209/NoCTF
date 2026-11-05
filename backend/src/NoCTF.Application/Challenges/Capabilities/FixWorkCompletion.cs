using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Application.Challenges.Capabilities;

public enum FixWorkCompletionKind : short { Completed, Rejected, PlatformFailed }

public sealed record FixWorkCompletion(FixWorkCompletionKind Kind, CapabilityExecutionFailure? Failure = null,
    FixExecutionPhase? FailedPhase = null);
