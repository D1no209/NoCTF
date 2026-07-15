using NoCTF.PluginBase;

namespace NoCTF.Plugins.AWDP;

public static class AwdpBackgroundTaskTypes
{
    public const string PatchValidation = "awdp.patch.validation";
    public const string ContainerCleanup = "awdp.container.cleanup";
}

public record AwdpPatchValidationPayload(Guid SubmissionId);
public record AwdpContainerCleanupPayload(ContainerInstance Container);

public interface IAwdpPatchService
{
    Task<AwdpPatchSubmitResult> SubmitPatchAsync(
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        Stream patchArchive,
        string fileName,
        CancellationToken ct = default);

    Task ValidatePatchAsync(Guid submissionId, CancellationToken ct = default);
}

public sealed record AwdpPatchSubmitResult(
    bool Success,
    string Code,
    Guid? SubmissionId = null,
    int DefenseAttempts = 0,
    int MaxDefenseAttempts = 0);
