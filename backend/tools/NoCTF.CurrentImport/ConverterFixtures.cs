using System.Text.Json;
using System.Text.Json.Nodes;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Scoring;

namespace NoCTF.CurrentImport;

internal static class ConverterFixtures
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static void Validate()
    {
        const string unsupported = "{\"schemaVersion\":0}";
        var id = Guid.CreateVersion7();
        ValidateMode(
            GameMode.Awd,
            WithVersion(AwdConfiguration.Default, 2),
            WithVersion(new AwdChallengeConfiguration(), 2),
            id);
        ValidateMode(
            GameMode.Awdp,
            WithVersion(new AwdpConfiguration(300,
                ScoreCurveConfiguration.Default, ScoreCurveConfiguration.Default), 4),
            WithVersion(AwdpChallengeConfiguration.Empty, 4),
            id);
        ValidateMode(
            GameMode.Koh,
            WithVersion(new KohConfiguration(30, 25), 1),
            WithVersion(new KohChallengeConfiguration(), 1),
            id);
        foreach (var mode in new[] { GameMode.Awd, GameMode.Awdp, GameMode.Koh })
        {
            try
            {
                _ = LegacyConverters.Competition(id, mode, unsupported);
                throw new InvalidOperationException($"Mode {mode} accepted an old schema.");
            }
            catch (InvalidOperationException exception) when (
                exception.Message.Contains("requires schemaVersion", StringComparison.Ordinal))
            {
            }
        }
    }

    private static void ValidateMode(
        GameMode mode,
        string competition,
        string challenge,
        Guid id)
    {
        var configuration = LegacyConverters.Competition(id, mode, competition);
        var definition = LegacyConverters.Definition(id, mode, challenge);
        var rules = LegacyConverters.Rules(id, mode, challenge);
        if (configuration.Mode != mode || definition.Mode != mode || rules.Mode != mode
            || configuration.CompetitionId != id || definition.ChallengeId != id
            || rules.CompetitionChallengeId != id)
            throw new InvalidOperationException($"Mode {mode} converter fixture did not preserve scope.");
    }

    private static string WithVersion<T>(T value, int version)
    {
        var root = JsonSerializer.SerializeToNode(value, Options)?.AsObject()
            ?? throw new InvalidOperationException("Fixture must be a JSON object.");
        root["schemaVersion"] = JsonValue.Create(version);
        return root.ToJsonString(Options);
    }
}
