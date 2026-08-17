using System.Text.Json;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Scoring;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.Application;

public sealed class SubmitFixTests
{
    [Test]
    public async Task Current_awdp_fix_is_accepted_when_break_is_not_required()
    {
        var store = new Store(requireBreakBeforeFix: false);
        var useCase = new SubmitFix(store, new GameModeGameplayFactAdmissionPolicy());

        var result = await useCase.ExecuteAsync(new(
            store.CompetitionId,
            store.CompetitionChallengeId,
            store.UserId,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.FixWrites).IsEqualTo(1);
    }

    [Test]
    public async Task Current_awdp_fix_requires_a_break_when_configured()
    {
        var store = new Store(requireBreakBeforeFix: true);
        var useCase = new SubmitFix(store, new GameModeGameplayFactAdmissionPolicy());

        var result = await useCase.ExecuteAsync(new(
            store.CompetitionId,
            store.CompetitionChallengeId,
            store.UserId,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(GameplayFactAdmissionFailureCode.BreakRequired);
        await Assert.That(store.FixWrites).IsEqualTo(0);
    }

    private sealed class Store(bool requireBreakBeforeFix) : IGameplayFactIntakeStore
    {
        public Guid CompetitionId { get; } = Guid.NewGuid();
        public Guid CompetitionChallengeId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public int FixWrites { get; private set; }

        public Task<GameplayFactAdmissionSnapshot?> LoadAdmissionAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<GameplayFactAdmissionSnapshot?>(new(
                competitionId,
                Guid.NewGuid(),
                competitionChallengeId,
                GameMode.Awdp,
                1,
                1,
                GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Awdp),
                JsonSerializer.Serialize(
                    new AwdpChallengeConfiguration(
                        AwdpChallengeConfiguration.CurrentSchemaVersion,
                        null,
                        new ScoreCurveConfiguration(20, 20, 2, ScoreDecayMode.Fixed),
                        requireBreakBeforeFix,
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

        public Task<GameplayFactAcceptanceResult> TryAcceptFlagAsync(
            FlagGameplayFactReceived received,
            GameplayFactAdmissionSnapshot snapshot,
            int? maxAttempts,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<IReadOnlyList<GameplayFactAcceptanceResult>> TryAcceptFlagsAsync(
            IReadOnlyList<FlagGameplayFactReceived> received,
            GameplayFactAdmissionSnapshot snapshot,
            int? maxAttempts,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<GameplayFactAcceptanceResult> TryAcceptFixAsync(
            FixGameplayFactReceived received,
            GameplayFactAdmissionSnapshot snapshot,
            int? maxAttempts,
            CancellationToken cancellationToken)
        {
            FixWrites++;
            return Task.FromResult(new GameplayFactAcceptanceResult(
                GameplayFactAcceptanceState.Created,
                received.GameplayFactId,
                received.OccurredAt));
        }
    }
}
