using NoCTF.Domain.Runtime;

namespace NoCTF.Application.GameplayFacts.Processing;

public enum AwdpFixExecutionFenceDisposition
{
    Execute,
    Recover,
    Superseded
}

public sealed record AwdpFixExecutionFenceRequest(
    Guid GameplayFactId,
    Guid CompetitionChallengeId,
    Guid PatchUploadId,
    Guid RuntimeInstanceId,
    DateTimeOffset Deadline,
    string RunnerId);

public sealed record AwdpFixExecutionFenceResult(
    AwdpFixExecutionFenceDisposition Disposition,
    Guid RuntimeInstanceId,
    RuntimeProvider Provider,
    string? ProviderReceiptJson,
    string RunnerId)
{
    public static AwdpFixExecutionFenceResult Superseded(
        AwdpFixExecutionFenceRequest request) =>
        new(
            AwdpFixExecutionFenceDisposition.Superseded,
            request.RuntimeInstanceId,
            default,
            null,
            request.RunnerId);
}

public interface IAwdpFixExecutionFence
{
    Task<AwdpFixExecutionFenceResult> AcquireAsync(
        AwdpFixExecutionFenceRequest request,
        CancellationToken cancellationToken);

}
