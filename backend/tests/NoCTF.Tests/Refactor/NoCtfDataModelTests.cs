using NoCTF.Application.Submissions.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Tests.Refactor;

public sealed class NoCtfDataModelTests
{
    [Test]
    public async Task Manual_adjustment_is_canonical_signed_int32_and_queued()
    {
        var store = new RecordingIntakeStore();
        var useCase = new CreateManualAdjustment(store);
        var result = await useCase.ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), -25, Guid.NewGuid(),
            DateTimeOffset.UtcNow));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.Received!.Delta).IsEqualTo(-25);
        await Assert.That(store.Received.Delta.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .IsEqualTo("-25");
    }

    [Test]
    public async Task Manual_adjustment_changes_ctf_projection_score()
    {
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;
        var projection = new CtfLeaderboardProjector().Project(new(
            Guid.NewGuid(),
            GameMode.Ctf,
            [new(teamId, "red", false, false, at)],
            [new(Guid.NewGuid(), teamId, challengeId, SubmissionKind.ManualAdjust, at,
                new ScoringEvent
                {
                    Id = eventId,
                    TeamId = teamId,
                    CompetitionChallengeId = challengeId,
                    Kind = ScoringEventKind.ManualAdjust,
                    Result = ScoringResult.Correct,
                    OccurredAt = at,
                    CreatedAt = at
                },
                SubmittedFlag: "-25")],
            [],
            [new(challengeId, "web", "Web", false)]));

        await Assert.That(projection.Entries.Single().Score).IsEqualTo(-25);
    }

    private sealed class RecordingIntakeStore : ISubmissionIntakeStore
    {
        public ManualAdjustmentSubmissionReceived? Received { get; private set; }

        public Task<SubmissionAdmissionSnapshot?> LoadAdmissionAsync(Guid competitionId, Guid competitionChallengeId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<SubmissionAdmissionSnapshot?>(null);
        public Task<SubmissionAcceptanceResult> TryAcceptFlagAsync(FlagSubmissionReceived received, SubmissionAdmissionSnapshot snapshot, int? maxAttempts, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<SubmissionAcceptanceResult>> TryAcceptFlagsAsync(IReadOnlyList<FlagSubmissionReceived> received, SubmissionAdmissionSnapshot snapshot, int? maxAttempts, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SubmissionAcceptanceResult> TryAcceptFixAsync(FixSubmissionReceived received, SubmissionAdmissionSnapshot snapshot, int? maxAttempts, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SubmissionAcceptanceResult> TryAcceptHintUnlockAsync(HintUnlockSubmissionReceived received, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SubmissionAcceptanceResult> TryAcceptManualAdjustmentAsync(ManualAdjustmentSubmissionReceived received, CancellationToken cancellationToken)
        {
            Received = received;
            return Task.FromResult(new SubmissionAcceptanceResult(SubmissionAcceptanceState.Created, received.SubmissionId, received.ReceivedAt));
        }
    }
}
