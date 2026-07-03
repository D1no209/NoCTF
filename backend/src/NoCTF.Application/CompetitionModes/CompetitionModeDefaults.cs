using NoCTF.Application.Scoring;
using NoCTF.PluginBase;

namespace NoCTF.Application.CompetitionModes;

public static class CompetitionModeDefaults
{
    public static string GetModeKey(GameModeType mode)
        => mode.ToString().ToLowerInvariant();

    public static string[] GetScoringProfile(GameModeType mode)
        => mode switch
        {
            GameModeType.Ctf => [ScoringKeys.DecaySolve, ScoringKeys.BloodBonus],
            GameModeType.Awd => [ScoringKeys.RoundAccumulation],
            GameModeType.Awdp => [ScoringKeys.RoundAccumulation, ScoringKeys.OneShotVerification],
            GameModeType.Koh => [ScoringKeys.ControlInterval],
            _ => []
        };
}
