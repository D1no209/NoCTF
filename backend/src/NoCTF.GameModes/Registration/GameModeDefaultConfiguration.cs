using System.Text.Json;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Flags;
using NoCTF.GameModes.Scoring;

namespace NoCTF.GameModes.Registration;

public static class GameModeDefaultConfiguration
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string GetCompetitionJson(GameMode mode) => mode switch
    {
        GameMode.Ctf => JsonSerializer.Serialize(new Ctf.Configuration.CtfConfiguration(
            Ctf.Configuration.CtfConfiguration.CurrentSchemaVersion,
            ScoreCurveConfiguration.Default,
            []), Options),
        GameMode.Awd => JsonSerializer.Serialize(Awd.Configuration.AwdConfiguration.Default, Options),
        GameMode.Awdp => JsonSerializer.Serialize(new Awdp.Configuration.AwdpConfiguration(
            Awdp.Configuration.AwdpConfiguration.CurrentSchemaVersion,
            RoundDurationSeconds: 300,
            Break: ScoreCurveConfiguration.Default,
            Fix: ScoreCurveConfiguration.Default,
            ViolationPenalty: 100,
            ServiceDownPenalty: 50,
            RequireBreakBeforeFix: false,
            BreakWrongPenalty: 0,
            FixFailurePenalty: 0,
            MaxBreakSubmissions: 10,
            MaxFixSubmissions: 10,
            EvaluationDispatchMode: EvaluationDispatchMode.Automatic,
            FlagTemplate: PerTeamFlagTemplate.Default), Options),
        GameMode.Koh => JsonSerializer.Serialize(new Koh.Configuration.KohConfiguration(1, 5, 10), Options),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported game mode.")
    };
}
