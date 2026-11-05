namespace NoCTF.Infrastructure.Persistence;

/// <summary>Provider-specific recognition of current result and capture identity conflicts.</summary>
public interface IDatabaseConstraintClassifier
{
    bool IsAwdCaptureIdentityConflict(Exception exception);
    bool IsCapabilityOutcomeIdentityConflict(Exception exception);
}
