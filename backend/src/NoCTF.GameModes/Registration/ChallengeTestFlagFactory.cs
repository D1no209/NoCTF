using System.Security.Cryptography;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Flags;

namespace NoCTF.GameModes.Registration;

public sealed record ChallengeTestFlagPlan(
    string? Flag,
    RuntimeTestFlagDelivery Delivery)
{
    public RuntimeTestFlagState InitialState => Delivery == RuntimeTestFlagDelivery.NotRequired
        ? RuntimeTestFlagState.NotRequired
        : RuntimeTestFlagState.Pending;
}

public static class ChallengeTestFlagFactory
{
    public static ChallengeTestFlagPlan Create(
        GameMode mode,
        ChallengeRuntimeTemplate runtime,
        Guid challengeId,
        Guid runtimeInstanceId,
        FlagTemplateValue? flagTemplate = null)
    {
        var delivery = runtime.FlagSource switch
        {
            RuntimeFlagSource.PerTeam => RuntimeTestFlagDelivery.Environment,
            RuntimeFlagSource.AwdRotation when mode == GameMode.Awd =>
                RuntimeTestFlagDelivery.Command,
            _ => RuntimeTestFlagDelivery.NotRequired
        };
        if (delivery == RuntimeTestFlagDelivery.NotRequired)
            return new(null, delivery);

        var flag = PerTeamFlagGenerator.Generate(
            flagTemplate is null
                ? PerTeamFlagTemplate.Default
                : new PerTeamFlagTemplate(
                    flagTemplate.Header,
                    flagTemplate.BodyTemplate,
                    flagTemplate.LeetLiteralText),
            new(
                RandomNumberGenerator.GetBytes(32),
                runtimeInstanceId,
                challengeId,
                runtimeInstanceId,
                runtimeInstanceId,
                runtimeInstanceId));
        return new(flag, delivery);
    }

}
