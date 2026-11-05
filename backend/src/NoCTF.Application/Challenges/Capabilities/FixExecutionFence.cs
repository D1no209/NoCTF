using NoCTF.Domain.Runtime;
using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Application.Challenges.Capabilities;

public enum FixExecutionFenceDisposition
{
    Execute,
    Recover,
    Superseded
}

public sealed record FixExecutionFenceRequest(
    Guid GameplayFactId,
    Guid CompetitionChallengeId,
    Guid PatchUploadId,
    Guid RuntimeInstanceId,
    DateTimeOffset Deadline,
    string RunnerId);

public sealed record FixExecutionFenceResult(
    FixExecutionFenceDisposition Disposition,
    Guid RuntimeInstanceId,
    RuntimeProvider Provider,
    RuntimeReceiptData? ProviderReceipt,
    string RunnerId)
{
    public static FixExecutionFenceResult Superseded(
        FixExecutionFenceRequest request) =>
        new(
            FixExecutionFenceDisposition.Superseded,
            request.RuntimeInstanceId,
            default,
            null,
            request.RunnerId);
}

public interface IFixExecutionFence
{
    Task<FixExecutionFenceResult> AcquireAsync(
        FixExecutionFenceRequest request,
        CancellationToken cancellationToken);

}
