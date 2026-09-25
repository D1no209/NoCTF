using System.Text.Json;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Tests.Unit.Application;

public sealed class ReplayFingerprintJsonContextTests
{
    [Test]
    public async Task Typed_fingerprints_preserve_existing_receipt_hash_inputs()
    {
        var action = RuntimeAction.Extend;
        var extension = TimeSpan.FromMinutes(5);
        var teamId = (Guid?)Guid.NewGuid();
        var flags = new[] { "flag{one}", "flag{two}" };
        var competitionChallengeId = Guid.NewGuid();
        const string fileName = "patch.tar.gz";
        const string contentType = "application/gzip";
        const long length = 42;
        const string sha256 = "ABCDEF";

        await Assert.That(JsonSerializer.Serialize(new EmptyReplayFingerprint(),
                ReplayFingerprintJsonContext.Default.EmptyReplayFingerprint))
            .IsEqualTo(JsonSerializer.Serialize(new { }));
        await Assert.That(JsonSerializer.Serialize(new RuntimeReplayFingerprint(action, extension),
                ReplayFingerprintJsonContext.Default.RuntimeReplayFingerprint))
            .IsEqualTo(JsonSerializer.Serialize(new { Action = action, Extension = extension }));
        await Assert.That(JsonSerializer.Serialize(new FlagReplayFingerprint(flags),
                ReplayFingerprintJsonContext.Default.FlagReplayFingerprint))
            .IsEqualTo(JsonSerializer.Serialize(new { Flags = flags }));
        await Assert.That(JsonSerializer.Serialize(new ManualAdjustmentReplayFingerprint(
                    teamId.Value, 25),
                ReplayFingerprintJsonContext.Default.ManualAdjustmentReplayFingerprint))
            .IsEqualTo(JsonSerializer.Serialize(new { TeamId = teamId.Value, Delta = 25 }));
        await Assert.That(JsonSerializer.Serialize(new AdminRuntimeReplayFingerprint(
                    teamId, action, extension),
                ReplayFingerprintJsonContext.Default.AdminRuntimeReplayFingerprint))
            .IsEqualTo(JsonSerializer.Serialize(new { teamId, action, extension }));
        await Assert.That(JsonSerializer.Serialize(new PatchUploadReplayFingerprint(
                    competitionChallengeId, fileName, contentType, length, sha256),
                ReplayFingerprintJsonContext.Default.PatchUploadReplayFingerprint))
            .IsEqualTo(JsonSerializer.Serialize(new
            {
                competitionChallengeId, fileName, contentType,
                Length = length, Sha256 = sha256
            }));
    }
}
