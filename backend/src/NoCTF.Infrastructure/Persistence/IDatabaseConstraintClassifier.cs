namespace NoCTF.Infrastructure.Persistence;

/// <summary>Provider-specific recognition of the successful AWD capture business identity.</summary>
public interface IDatabaseConstraintClassifier
{
    bool IsAwdCaptureIdentityConflict(Exception exception);
}
