using System.Text.Json;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.Application;

public sealed class SubmitFixTests
{
    [Test]
    public async Task Required_break_is_rejected_before_patch_upload_consumption()
    {
        var store = new Store();
        var useCase = new SubmitFix(store, new GameModeSubmissionAdmissionPolicy());

        var result = await useCase.ExecuteAsync(new(
            store.CompetitionId,
            store.CompetitionChallengeId,
            store.UserId,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow));

        await Assert.That(result.ErrorCode).IsEqualTo("break_required");
        await Assert.That(store.FixWrites).IsEqualTo(0);
    }

    private sealed class Store : ISubmissionIntakeStore
    {
        public Guid CompetitionId { get; } = Guid.NewGuid();
        public Guid CompetitionChallengeId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public int FixWrites { get; private set; }

        public Task<SubmissionAdmissionSnapshot?> LoadAdmissionAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<SubmissionAdmissionSnapshot?>(new(
                competitionId,
                Guid.NewGuid(),
                competitionChallengeId,
                GameMode.Awdp,
                1,
                1,
                "{}",
                JsonSerializer.Serialize(
                    new AwdpChallengeConfiguration(
                        AwdpChallengeConfiguration.CurrentSchemaVersion,
                        null,
                        new AwdpAchievementConfiguration(AchievementSettlement.Milestone, 20),
                        true,
                        10,
                        10),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                0,
                0,
                CompetitionStatus.Running,
                DateTimeOffset.UtcNow.AddHours(-1),
                DateTimeOffset.UtcNow.AddHours(1),
                false,
                false,
                true,
                false,
                false,
                true,
                true));

        public Task<SubmissionAcceptanceResult> TryAcceptFlagAsync(
            FlagSubmissionReceived received,
            SubmissionAdmissionSnapshot snapshot,
            int? maxAttempts,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<IReadOnlyList<SubmissionAcceptanceResult>> TryAcceptFlagsAsync(
            IReadOnlyList<FlagSubmissionReceived> received,
            SubmissionAdmissionSnapshot snapshot,
            int? maxAttempts,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<SubmissionAcceptanceResult> TryAcceptFixAsync(
            FixSubmissionReceived received,
            SubmissionAdmissionSnapshot snapshot,
            int? maxAttempts,
            CancellationToken cancellationToken)
        {
            FixWrites++;
            return Task.FromResult(new SubmissionAcceptanceResult(
                SubmissionAcceptanceState.Created,
                received.SubmissionId,
                received.ReceivedAt));
        }
    }
}
