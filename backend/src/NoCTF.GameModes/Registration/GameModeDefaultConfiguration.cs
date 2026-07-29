using System.Text.Json;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.GameModes.Registration;

public static class GameModeDefaultConfiguration
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string GetCompetitionJson(GameMode mode) => mode switch
    {
        GameMode.Ctf => JsonSerializer.Serialize(new Ctf.Configuration.CtfConfiguration(1,
            new(500, 100, 10), []), Options),
        GameMode.Awd => JsonSerializer.Serialize(Awd.Configuration.AwdConfiguration.Default, Options),
        GameMode.Awdp => JsonSerializer.Serialize(new Awdp.Configuration.AwdpConfiguration(
            Awdp.Configuration.AwdpConfiguration.CurrentSchemaVersion,
            RoundDurationSeconds: 300,
            Break: new(Awdp.Configuration.AchievementSettlement.PerRound, 50),
            Fix: new(Awdp.Configuration.AchievementSettlement.PerRound, 50),
            ViolationPenalty: 100,
            ServiceDownPenalty: 50,
            RequireBreakBeforeFix: true,
            BreakWrongPenalty: 0,
            FixFailurePenalty: 0,
            MaxBreakSubmissions: 10,
            MaxFixSubmissions: 10,
            EvaluationDispatchMode: EvaluationDispatchMode.Automatic,
            Runtime: null,
            PatchEntrypoint: "fix.sh",
            PatchCommand: null,
            PatchTimeoutSeconds: 60,
            Checker: null,
            ReadyTimeoutSeconds: 30), Options),
        GameMode.Koh => JsonSerializer.Serialize(new Koh.Configuration.KohConfiguration(1, 5, 10), Options),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported game mode.")
    };
}
