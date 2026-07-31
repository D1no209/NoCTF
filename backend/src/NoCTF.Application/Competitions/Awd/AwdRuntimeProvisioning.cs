namespace NoCTF.Application.Competitions.Awd;

public enum AwdRuntimeProvisioningOutcome
{
    NotApplicable,
    Applied,
    Idempotent,
    DeferredCleanup,
    CapacityExceeded,
    RejectedBusiness
}

public interface IAwdRuntimeProvisioner
{
    Task<AwdRuntimeProvisioningOutcome> EnsureAsync(
        Guid competitionId,
        CancellationToken cancellationToken);
}
