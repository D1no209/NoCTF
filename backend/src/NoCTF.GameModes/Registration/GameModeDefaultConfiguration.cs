using System.Text.Json;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Registration;

public static class GameModeDefaultConfiguration
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string GetCompetitionJson(GameMode mode) => mode switch
    {
        GameMode.Ctf => JsonSerializer.Serialize(new Ctf.Configuration.CtfConfiguration(1,
            new(500, 100, 10), []), Options),
        GameMode.Awd => JsonSerializer.Serialize(new Awd.Configuration.AwdConfiguration(1,
            300, 10, 2, 50, 100, 50, 50), Options),
        GameMode.Awdp => JsonSerializer.Serialize(new Awdp.Configuration.AwdpConfiguration(1,
            300, new(Awdp.Configuration.AchievementSettlement.PerRound, 50),
            new(Awdp.Configuration.AchievementSettlement.PerRound, 50), 100, 50), Options),
        GameMode.Koh => JsonSerializer.Serialize(new Koh.Configuration.KohConfiguration(1, 5, 10), Options),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported game mode.")
    };
}
