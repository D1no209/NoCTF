namespace NoCTF.Application.Competitions.Koh;

public enum KohRuntimeProvisioningOutcome
{
    NotApplicable,
    Applied,
    Idempotent,
    RejectedBusiness
}

public interface IKohRuntimeProvisioner
{
    Task<KohRuntimeProvisioningOutcome> EnsureAsync(
        Guid competitionId,
        CancellationToken cancellationToken);
}
