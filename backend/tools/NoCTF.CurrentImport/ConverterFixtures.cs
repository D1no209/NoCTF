using System.Text.Json;
using System.Text.Json.Nodes;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
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
        var ctf = LegacyConverters.Competition(id, GameMode.Ctf, WithVersion(
            new CtfConfiguration(ScoreCurveConfiguration.Default, [], ScoreSettlementMode: CtfScoreSettlementMode.AtSolve), 2));
        var ctfRules = LegacyConverters.Rules(id, GameMode.Ctf, WithVersion(
            new CtfChallengeConfiguration(null, null, ScoreSettlementMode: CtfScoreSettlementMode.AtSolve), 2));
        if (((CtfCompetitionModeConfiguration)ctf).ScoreSettlementMode != CtfScoreSettlementMode.AtSolve
            || ((CtfCompetitionChallengeRules)ctfRules).ScoreSettlementMode != CtfScoreSettlementMode.AtSolve)
            throw new InvalidOperationException("CTF settlement mode was not preserved by the converter.");
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
        var runtime = new ChallengeRuntimeTemplate(RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition([
                new RuntimeServiceDefinition("web", "example/web:1", 0.25m, 128,
                    ["serve"], ["--port", "8080"], new Dictionary<string, string> { ["MODE"] = "test" }, [8080], "FLAG"),
                new RuntimeServiceDefinition("db", "example/db:1", 0.5m, 256)
            ]));
        var runtimeDefinition = LegacyConverters.Definition(id, GameMode.Koh,
            WithVersion(new KohChallengeConfiguration(Runtime: runtime), 1));
        if (runtimeDefinition.Runtime is not ContainerChallengeRuntimeTemplate { Services.Count: 2 } container
            || container.Services[0] is not { Name: "web", CpuCores: 0.25m, MemoryMiB: 128, FlagEnvironmentVariableName: "FLAG" }
            || container.Services[1] is not { Name: "db", Position: 1 }
            || container.Services[0].Commands.Count != 3
            || container.Services[0].Environment.Single().Value != "test"
            || container.Services[0].InternalPorts.Single().Port != 8080
            || container.Services.Any(service => service.ChallengeId != id)
            || container.Services[0].Commands.Any(command => command.ChallengeId != id || command.ServiceName != "web"))
            throw new InvalidOperationException("Named Runtime services were not preserved by the converter.");
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
