using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Awdp.Runtime;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdpTargetRuntimeFactoryTests
{
    [Test]
    public async Task Fix_target_is_submission_owned_and_not_a_player_runtime()
    {
        var gameplayFactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var template = new ChallengeRuntimeTemplate(
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition("target:latest"));

        var target = AwdpTargetRuntimeFactory.Create(
            gameplayFactId,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            template,
            new RuntimePlacement(RuntimeProvider.Docker, "awdp"),
            generation: 2,
            competitionConfigurationRevision: 3,
            competitionChallengeRevision: 5,
            challengeDefinitionRevision: 7,
            now: DateTimeOffset.Parse("2026-07-24T00:00:00Z"));

        await Assert.That(target.Purpose).IsEqualTo(RuntimePurpose.AwdpTarget);
        await Assert.That(target.GameplayFactId).IsEqualTo(gameplayFactId);
        await Assert.That(target.TeamId).IsNull();
        await Assert.That(target.Generation).IsEqualTo(2);
        await Assert.That(target.SourceCompetitionConfigurationRevision).IsEqualTo(3);
        await Assert.That(target.SourceCompetitionChallengeRevision).IsEqualTo(5);
        await Assert.That(target.SourceChallengeDefinitionRevision).IsEqualTo(7);
        await Assert.That(target.RunnerPool).IsEqualTo("awdp");
        await Assert.That(target.State).IsEqualTo(RuntimeState.Queued);
        await Assert.That(target.ExpiresAt).IsNull();
    }
}
