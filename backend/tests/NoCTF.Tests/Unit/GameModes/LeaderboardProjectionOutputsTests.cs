using System.Text.Json;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Leaderboard;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class LeaderboardProjectionOutputsTests
{
    [Test]
    public async Task Combined_projection_invokes_mode_projector_once()
    {
        var projector = new CountingProjector();
        var engine = new LeaderboardProjectionEngine(new SingleProjectorCatalog(projector));

        _ = engine.ProjectOutputs(EmptyInput());

        await Assert.That(projector.CallCount).IsEqualTo(1);
    }

    [Test]
    [Arguments(GameMode.Ctf)]
    [Arguments(GameMode.Awd)]
    [Arguments(GameMode.Awdp)]
    [Arguments(GameMode.Koh)]
    public async Task Combined_projection_is_deterministic_for_every_mode(GameMode mode)
    {
        var engine = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog());
        var input = EmptyInput(mode);

        var first = engine.ProjectOutputs(input);
        var replay = engine.ProjectOutputs(input);

        await Assert.That(JsonSerializer.Serialize(first))
            .IsEqualTo(JsonSerializer.Serialize(replay));
    }

    private static LeaderboardProjectionInput EmptyInput(GameMode mode = GameMode.Ctf) => new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        mode,
        [],
        [],
        [],
        ProjectedAt: DateTimeOffset.Parse("2026-08-28T00:00:00Z"),
        CompetitionStatus: CompetitionStatus.Running);

    private sealed class SingleProjectorCatalog(IGameModeLeaderboardProjector projector)
        : ILeaderboardProjectorCatalog
    {
        public IGameModeLeaderboardProjector Get(GameMode mode) => projector;
    }

    private sealed class CountingProjector : IGameModeLeaderboardProjector
    {
        public GameMode Mode => GameMode.Ctf;

        public int CallCount { get; private set; }

        public GameModeLeaderboardProjection Project(LeaderboardProjectionInput input)
        {
            CallCount++;
            return new([], []);
        }
    }
}
