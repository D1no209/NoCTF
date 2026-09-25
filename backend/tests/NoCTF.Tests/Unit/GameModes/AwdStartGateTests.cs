using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Domain.Challenges;

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
        var rules = configurations.CreateDefaultRulesForTest(GameMode.Awd);
        var gate = new CompetitionStartGate(
            new Store(new(
                competitionId,
                GameMode.Awd,
                CompetitionStatus.Published,
                CompetitionModeConfigurationDefaults.Create(GameMode.Awd, competitionId),
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

    private static ChallengeDefinition CompleteDefinition() =>
        new AwdChallengeDefinition
        {
            FlagInjectionCommand = "printf '%s' '${FLAG}' > /dev/shm/flag",
            FlagInjectionTimeoutSeconds = 30,
            Runtime = new ContainerChallengeRuntimeTemplate
            {
                Allocation = PersistedRuntimeAllocation.PerTeam,
                Limits = new()
                {
                    MemoryBytes = 67_108_864,
                    NanoCpus = 100_000_000,
                    PidsLimit = 64
                },
                HasExplicitLimits = true,
                FlagSource = PersistedRuntimeFlagSource.AwdRotation,
                Image = "registry.example/awd:v1",
                Capabilities = [new() { Name = "ALL" }],
                PortMappings = [new() { ContainerPort = 8080, HostPort = 0 }],
                UrlBindings =
                [
                    new()
                    {
                        UrlTemplate = "nc {HOST} {PORT}",
                        Exposure = PersistedRuntimeExposure.Participants,
                        ContainerPort = 8080
                    }
                ]
            }
        };

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
