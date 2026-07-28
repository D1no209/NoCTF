namespace NoCTF.Application.Competitions.Awd;

public enum AwdRuntimeProvisioningOutcome
{
    NotApplicable,
    Applied,
    Idempotent,
    RejectedBusiness
}

public interface IAwdRuntimeProvisioner
{
    Task<AwdRuntimeProvisioningOutcome> EnsureAsync(
        Guid competitionId,
        CancellationToken cancellationToken);
}
