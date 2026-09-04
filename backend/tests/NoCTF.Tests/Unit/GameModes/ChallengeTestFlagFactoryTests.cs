using System.Text.Json;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Flags;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class ChallengeTestFlagFactoryTests
{
    [Test]
    public async Task Create_PerTeamRuntime_UsesChallengeTemplateAndEnvironmentDelivery()
    {
        var challengeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var runtimeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var definitionJson = JsonSerializer.Serialize(
            new CtfChallengeConfiguration(
                CtfChallengeConfiguration.CurrentSchemaVersion,
                null,
                null,
                Runtime: null,
                FlagTemplate: new PerTeamFlagTemplate(
                    "test",
                    "[CHALLENGEID:N]-[TEAMHASH:8]",
                    false)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var runtime = Runtime(RuntimeFlagSource.PerTeam);

        var plan = ChallengeTestFlagFactory.Create(
            GameMode.Ctf,
            definitionJson,
            runtime,
            challengeId,
            runtimeId);

        await Assert.That(plan.Delivery).IsEqualTo(RuntimeTestFlagDelivery.Environment);
        await Assert.That(plan.InitialState).IsEqualTo(RuntimeTestFlagState.Pending);
        await Assert.That(plan.Flag).StartsWith($"test{{{challengeId:N}-");
    }

    [Test]
    public async Task Create_AwdRotation_UsesCommandDelivery()
    {
        var definitionJson = JsonSerializer.Serialize(
            new AwdChallengeConfiguration(
                AwdChallengeConfiguration.CurrentSchemaVersion,
                FlagTemplate: new PerTeamFlagTemplate("awdtest", "[GUID:N]", false)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var plan = ChallengeTestFlagFactory.Create(
            GameMode.Awd,
            definitionJson,
            Runtime(RuntimeFlagSource.AwdRotation),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());

        await Assert.That(plan.Delivery).IsEqualTo(RuntimeTestFlagDelivery.Command);
        await Assert.That(plan.Flag).StartsWith("awdtest{");
    }

    [Test]
    public async Task Create_StaticRuntime_DoesNotInventAnInjectedFlag()
    {
        var definitionJson = JsonSerializer.Serialize(
            new CtfChallengeConfiguration(
                CtfChallengeConfiguration.CurrentSchemaVersion,
                null,
                null),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var plan = ChallengeTestFlagFactory.Create(
            GameMode.Ctf,
            definitionJson,
            Runtime(RuntimeFlagSource.Static),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());

        await Assert.That(plan.Delivery).IsEqualTo(RuntimeTestFlagDelivery.NotRequired);
        await Assert.That(plan.InitialState).IsEqualTo(RuntimeTestFlagState.NotRequired);
        await Assert.That(plan.Flag).IsNull();
    }

    private static ChallengeRuntimeTemplate Runtime(RuntimeFlagSource flagSource) =>
        new(
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "challenge:test",
                FlagEnvironmentVariableName: flagSource == RuntimeFlagSource.PerTeam
                    ? "CHALLENGE_FLAG"
                    : null),
            new RuntimeResourceLimits(67_108_864, 100_000_000, 64),
            FlagSource: flagSource);
}
