using NoCTF.Application.Storage;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class CreateFixUploadTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ExecuteAsync_ApprovedTeam_ReturnsUploadGrant()
    {
        var admission = AllowedAdmission();
        var grant = new FixUploadGrant(Guid.NewGuid(), "fix/archive.zip", new("https://storage/upload"), Now.AddMinutes(15));
        var useCase = new CreateFixUpload(new AdmissionStore(admission), new UploadStore(grant), new Policy(), () => Now);

        var result = await useCase.ExecuteAsync(Command());

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.Grant).IsEqualTo(grant);
    }

    [Test]
    public async Task ExecuteAsync_BannedTeam_RejectsBeforeCreatingUpload()
    {
        var admission = AllowedAdmission() with { TeamBanned = true };
        var store = new UploadStore(new(Guid.NewGuid(), "fix/archive.zip", new("https://storage/upload"), Now.AddMinutes(15)));
        var useCase = new CreateFixUpload(new AdmissionStore(admission), store, new Policy(), () => Now);

        var result = await useCase.ExecuteAsync(Command());

        await Assert.That(result.ErrorCode).IsEqualTo("team_banned");
        await Assert.That(store.CreateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_InvalidSha256_RejectsRequest()
    {
        var store = new UploadStore(new(Guid.NewGuid(), "fix/archive.zip", new("https://storage/upload"), Now.AddMinutes(15)));
        var useCase = new CreateFixUpload(new AdmissionStore(AllowedAdmission()), store, new Policy(), () => Now);

        var result = await useCase.ExecuteAsync(Command() with { Sha256 = "invalid" });

        await Assert.That(result.ErrorCode).IsEqualTo("sha256_required");
        await Assert.That(store.CreateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_NonHexSha256_RejectsRequest()
    {
        var store = new UploadStore(new(Guid.NewGuid(), "fix/archive.zip", new("https://storage/upload"), Now.AddMinutes(15)));
        var useCase = new CreateFixUpload(new AdmissionStore(AllowedAdmission()), store, new Policy(), () => Now);

        var result = await useCase.ExecuteAsync(Command() with { Sha256 = new string('z', 64) });

        await Assert.That(result.ErrorCode).IsEqualTo("sha256_required");
        await Assert.That(store.CreateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_AdmissionChangesBeforeReservation_ReturnsConcurrencyFailure()
    {
        var useCase = new CreateFixUpload(
            new AdmissionStore(AllowedAdmission()),
            new ChangedUploadStore(),
            new Policy(),
            () => Now);

        var result = await useCase.ExecuteAsync(Command());

        await Assert.That(result.ErrorCode).IsEqualTo("upload_admission_changed");
    }

    private static CreateFixUploadCommand Command() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "fix.zip",
        "application/zip", 128, new string('a', 64), Now);

    private static SubmissionAdmissionSnapshot AllowedAdmission() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), GameMode.Awdp, 0, 0, "{}", "{}", 0, 0, CompetitionStatus.Running,
        Now.AddHours(-1), Now.AddHours(1), false, false, true, false, false, true, true);

    private sealed class AdmissionStore(SubmissionAdmissionSnapshot snapshot) : ISubmissionIntakeStore
    {
        public Task<SubmissionAcceptanceResult?> FindAcceptedAsync(Guid competitionId, string idempotencyKey,
            Guid teamId, Guid challengeId, Guid userId, NoCTF.Domain.Submissions.SubmissionKind kind,
            AwdAttackTarget? attackTarget,
            CancellationToken cancellationToken) => Task.FromResult<SubmissionAcceptanceResult?>(null);
        public Task<SubmissionAdmissionSnapshot?> LoadAdmissionAsync(Guid competitionId, Guid teamId, Guid challengeId,
            Guid userId, CancellationToken cancellationToken) => Task.FromResult<SubmissionAdmissionSnapshot?>(snapshot);
        public Task<SubmissionAcceptanceResult> TryAcceptFlagAsync(FlagSubmissionReceived received,
            SubmissionAdmissionSnapshot admission, int? maxAttempts,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SubmissionAcceptanceResult> TryAcceptFixAsync(FixSubmissionReceived received,
            SubmissionAdmissionSnapshot admission, int? maxAttempts,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Policy : ISubmissionAdmissionModePolicy
    {
        public SubmissionAdmissionRules GetRules(GameMode mode, string competitionConfigurationJson,
            string challengeConfigurationJson) => new(false, true, null, 10);
    }

    private sealed class UploadStore(FixUploadGrant grant) : IFixUploadSessionStore
    {
        public int CreateCalls { get; private set; }
        public Task<FixUploadCreationResult> CreateAsync(
            CreateFixUploadCommand command,
            SubmissionAdmissionSnapshot expectedAdmission,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult(FixUploadCreationResult.Created(grant));
        }
        public Task<FixUploadMetadata?> GetAuthorizedMetadataAsync(Guid uploadId, Guid competitionId, Guid teamId,
            Guid challengeId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ChangedUploadStore : IFixUploadSessionStore
    {
        public Task<FixUploadCreationResult> CreateAsync(
            CreateFixUploadCommand command,
            SubmissionAdmissionSnapshot expectedAdmission,
            CancellationToken cancellationToken) =>
            Task.FromResult(FixUploadCreationResult.Rejected(FixUploadCreationFailure.AdmissionChanged));

        public Task<FixUploadMetadata?> GetAuthorizedMetadataAsync(
            Guid uploadId,
            Guid competitionId,
            Guid teamId,
            Guid challengeId,
            Guid userId,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
