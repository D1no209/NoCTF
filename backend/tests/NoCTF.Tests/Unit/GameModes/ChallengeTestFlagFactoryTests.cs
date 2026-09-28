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
        var runtime = Runtime(RuntimeFlagSource.PerTeam);

        var plan = ChallengeTestFlagFactory.Create(
            GameMode.Ctf,
            runtime,
            challengeId,
            runtimeId);

        await Assert.That(plan.Delivery).IsEqualTo(RuntimeTestFlagDelivery.Environment);
        await Assert.That(plan.InitialState).IsEqualTo(RuntimeTestFlagState.Pending);
        await Assert.That(plan.Flag).StartsWith("flag{");
    }

    [Test]
    public async Task Create_AwdRotation_UsesCommandDelivery()
    {
        var plan = ChallengeTestFlagFactory.Create(
            GameMode.Awd,
            Runtime(RuntimeFlagSource.AwdRotation),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());

        await Assert.That(plan.Delivery).IsEqualTo(RuntimeTestFlagDelivery.Command);
        await Assert.That(plan.Flag).StartsWith("flag{");
    }

    [Test]
    public async Task Create_StaticRuntime_DoesNotInventAnInjectedFlag()
    {
        var plan = ChallengeTestFlagFactory.Create(
            GameMode.Ctf,
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
                Security: ContainerSecurityPolicy.Default,
                FlagEnvironmentVariableName: flagSource == RuntimeFlagSource.PerTeam
                    ? "CHALLENGE_FLAG"
                    : null),
            new RuntimeResourceLimits(67_108_864, 100_000_000, 64),
            FlagSource: flagSource);
}
