using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Leaderboard;

public sealed class LeaderboardProjectorCatalog : ILeaderboardProjectorCatalog
{
    private readonly IReadOnlyDictionary<GameMode, IGameModeLeaderboardProjector> _projectors =
        new Dictionary<GameMode, IGameModeLeaderboardProjector>
        {
            [GameMode.Ctf] = new CtfLeaderboardProjector(),
            [GameMode.Awd] = new AwdLeaderboardProjector(),
            [GameMode.Awdp] = new AwdpLeaderboardProjector(),
            [GameMode.Koh] = new KohLeaderboardProjector()
        };

    public IGameModeLeaderboardProjector Get(GameMode mode) => _projectors[mode];
}
