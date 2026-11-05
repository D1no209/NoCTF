using System.Security.Cryptography;
using System.Text.Json;
using NoCTF.Application.Common;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.Storage;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Testing;

namespace NoCTF.Application.Challenges.Testing;

public sealed class StartTemplateCapabilityTest(ITemplateCapabilityTestStore store, ManagedFileUploads uploads, TimeProvider clock)
{
    public Task<OperationResult<TemplateCapabilityTestAccepted, TemplateCapabilityTestFailure>> CheckAsync(
        ChallengeTestAccess access, Guid requestId, CancellationToken ct) =>
        store.StartAsync(new(access, requestId, TemplateCapabilityTestKind.Check, null, string.Empty, clock.GetUtcNow()), ct);

    public async Task<OperationResult<TemplateCapabilityTestAccepted, TemplateCapabilityTestFailure>> FixAsync(
        ChallengeTestAccess access, Guid requestId, bool checkAfterFix, string fileName, string contentType, Stream archive, CancellationToken ct)
    {
        var material = await store.ReadAsync(access, ChallengeCapabilityKind.Fix, ct);
        if (!material.Succeeded)
            return OperationResult<TemplateCapabilityTestAccepted, TemplateCapabilityTestFailure>.Failure(material.FailureCode!.Value, material.ErrorMessage!);
        if (requestId == Guid.Empty || !archive.CanRead || !archive.CanSeek)
            return Failure(TemplateCapabilityTestFailure.InvalidInput);
        if (!fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            return Failure(TemplateCapabilityTestFailure.ArchiveInvalid);
        if (checkAfterFix && material.Value!.Challenge.Definition!.Check is null)
            return Failure(TemplateCapabilityTestFailure.CapabilityMissing);
        var maximum = material.Value!.Challenge.Definition!.Fix!.MaximumUploadBytes;
        if (archive.Length > maximum || archive.Length > PatchUploadRules.HardMaximumArchiveBytes)
            return Failure(TemplateCapabilityTestFailure.ArchiveTooLarge);
        var validation = NoCTF.Application.Challenges.Capabilities.FixArchiveValidation.Validate(archive);
        archive.Position = 0;
        if (validation is not null)
            return OperationResult<TemplateCapabilityTestAccepted, TemplateCapabilityTestFailure>.Failure(TemplateCapabilityTestFailure.ArchiveInvalid, validation);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(archive, ct));
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new TemplateFixFingerprint(hash, fileName, contentType))));
        archive.Position = 0;
        var fileId = Guid.CreateVersion7();
        await uploads.CreateAsync(fileId, $"template-fix-tests/{fileId:N}", Path.GetFileName(fileName),
            string.IsNullOrWhiteSpace(contentType) ? "application/gzip" : contentType, archive, clock.GetUtcNow(), ct);
        var attached = false;
        try
        {
            var result = await store.StartAsync(new(access, requestId,
                checkAfterFix ? TemplateCapabilityTestKind.FixAndCheck : TemplateCapabilityTestKind.Fix,
                fileId, fingerprint, clock.GetUtcNow()), ct);
            attached = result.Succeeded;
            return result;
        }
        finally
        {
            if (!attached) await uploads.AbandonAsync(fileId);
        }
    }

    private static OperationResult<TemplateCapabilityTestAccepted, TemplateCapabilityTestFailure> Failure(TemplateCapabilityTestFailure failure) =>
        OperationResult<TemplateCapabilityTestAccepted, TemplateCapabilityTestFailure>.Failure(failure, $"The capability test was rejected: {failure}.");

    private sealed record TemplateFixFingerprint(string Sha256, string FileName, string ContentType);
}
