using System.Text.Json;
using System.Text.Json.Serialization;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdStartGateTests
{
    [Test]
    public async Task Published_challenge_count_must_fit_the_positive_team_runtime_quota()
    {
        var competitionId = Guid.CreateVersion7();
        var firstChallengeId = Guid.CreateVersion7();
        var secondChallengeId = Guid.CreateVersion7();
        var configurations = new GameModeChallengeConfigurationCatalog();
        var definition = CompleteDefinition();
        var rules = configurations.GetDefaultJson(GameMode.Awd);
        var gate = new CompetitionStartGate(
            new Store(new(
                competitionId,
                GameMode.Awd,
                CompetitionStatus.Published,
                GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Awd),
                [
                    new(firstChallengeId, GameMode.Awd, rules, definition, true, []),
                    new(secondChallengeId, GameMode.Awd, rules, definition, true, [])
                ],
                ApprovedTeamCount: 1,
                MaxConcurrentRuntimeInstancesPerTeam: 1)),
            new GameModeCompetitionConfigurationValidator(),
            configurations);

        var errors = await gate.ValidateAsync(competitionId);

        await Assert.That(errors).IsNotNull();
        await Assert.That(errors!.Count(error => error.Code == StartGateFailureCode.RuntimeQuotaInsufficient))
            .IsEqualTo(1);
        await Assert.That(errors.Any(error => error.Code == StartGateFailureCode.RuntimeDefinitionInvalid))
            .IsFalse();
    }

    private static string CompleteDefinition() =>
        JsonSerializer.Serialize(
            new AwdChallengeConfiguration(
                AwdChallengeConfiguration.CurrentSchemaVersion,
                Runtime: new ChallengeRuntimeTemplate(
                    RuntimeAllocation.PerTeam,
                    new ContainerRuntimeDefinition(
                        "registry.example/awd:v1",
                        PortMappings: new Dictionary<int, int> { [8080] = 0 }),
                    new RuntimeResourceLimits(67_108_864, 100_000_000, 64),
                    UrlBindings:
                    [
                        new(
                            "nc {HOST} {PORT}",
                            RuntimeExposure.Participants,
                            ContainerPort: 8080)
                    ],
                    FlagSource: RuntimeFlagSource.AwdRotation),
                FlagInjection: new AwdFlagInjectionConfiguration(
                    "printf '%s' '${FLAG}' > /dev/shm/flag")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

    private sealed class Store(CompetitionStartGateSnapshot snapshot)
        : ICompetitionStartGateStore
    {
        public Task<CompetitionStartGateSnapshot?> LoadAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionStartGateSnapshot?>(
                competitionId == snapshot.CompetitionId ? snapshot : null);
    }
}
