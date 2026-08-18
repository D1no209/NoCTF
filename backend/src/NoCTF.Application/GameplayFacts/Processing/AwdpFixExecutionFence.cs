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
    int Generation,
    long RuntimeProcessingVersion,
    DateTimeOffset Deadline,
    string RunnerPool,
    string RunnerId);

public sealed record AwdpFixExecutionFenceResult(
    AwdpFixExecutionFenceDisposition Disposition,
    Guid RuntimeInstanceId,
    int Generation,
    long RuntimeProcessingVersion,
    RuntimeProvider Provider,
    string? ProviderReceiptJson,
    string RunnerPool,
    string RunnerId)
{
    public static AwdpFixExecutionFenceResult Superseded(
        AwdpFixExecutionFenceRequest request) =>
        new(
            AwdpFixExecutionFenceDisposition.Superseded,
            request.RuntimeInstanceId,
            request.Generation,
            request.RuntimeProcessingVersion,
            default,
            null,
            request.RunnerPool,
            request.RunnerId);
}

public sealed record AwdpFixStageTransitionRequest(
    Guid GameplayFactId,
    Guid RuntimeInstanceId,
    int Generation,
    long RuntimeProcessingVersion,
    AwdpFixStage ExpectedStage,
    AwdpFixStage NextStage);

public interface IAwdpFixExecutionFence
{
    Task<AwdpFixExecutionFenceResult> AcquireAsync(
        AwdpFixExecutionFenceRequest request,
        CancellationToken cancellationToken);

    Task<bool> TryAdvanceStageAsync(
        AwdpFixStageTransitionRequest request,
        CancellationToken cancellationToken);
}
