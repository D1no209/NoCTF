using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Tests.Refactor;

public sealed class NoCtfDataModelTests
{
    [Test]
    public async Task Manual_adjustment_is_created_with_a_canonical_signed_int32()
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
        var at = DateTimeOffset.UtcNow;
        var projection = new CtfLeaderboardProjector().Project(new(
            Guid.NewGuid(),
            GameMode.Ctf,
            [new(teamId, "red", false, false, at)],
            [new(Guid.NewGuid(), teamId, challengeId, GameplayFactKind.ManualAdjustment, at,
                GameplayFactState.Completed, GameplayFactResult.Applied, null, Value: "-25")],
            [new(challengeId, "web", "Web", false)]));

        await Assert.That(projection.Entries.Single().Score).IsEqualTo(-25);
        var cell = projection.Cells.Single();
        await Assert.That(cell.TeamId).IsEqualTo(teamId);
        await Assert.That(cell.CompetitionChallengeId).IsEqualTo(challengeId);
        await Assert.That(cell.Score).IsEqualTo(-25);
        await Assert.That(cell.SolvedAt).IsNull();
    }

    private sealed class RecordingIntakeStore : IGameplayFactIntakeStore
    {
        public ManualAdjustmentGameplayFactReceived? Received { get; private set; }

        public Task<GameplayFactAdmissionSnapshot?> LoadAdmissionAsync(Guid competitionId, Guid competitionChallengeId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<GameplayFactAdmissionSnapshot?>(null);
        public Task<GameplayFactAcceptanceResult> TryAcceptFlagAsync(FlagGameplayFactReceived received, GameplayFactAdmissionSnapshot snapshot, int? maxAttempts, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<GameplayFactAcceptanceResult>> TryAcceptFlagsAsync(IReadOnlyList<FlagGameplayFactReceived> received, GameplayFactAdmissionSnapshot snapshot, int? maxAttempts, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GameplayFactAcceptanceResult> TryAcceptFixAsync(FixGameplayFactReceived received, GameplayFactAdmissionSnapshot snapshot, int? maxAttempts, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GameplayFactAcceptanceResult> TryAcceptHintUnlockAsync(HintUnlockGameplayFactReceived received, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GameplayFactAcceptanceResult> TryAcceptManualAdjustmentAsync(ManualAdjustmentGameplayFactReceived received, CancellationToken cancellationToken)
        {
            Received = received;
            return Task.FromResult(new GameplayFactAcceptanceResult(GameplayFactAcceptanceState.Created, received.GameplayFactId, received.OccurredAt));
        }
    }
}
