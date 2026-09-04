using System.Security.Cryptography;
using System.Text.Json;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ChallengeTestFlagPlan Create(
        GameMode mode,
        string definitionJson,
        ChallengeRuntimeTemplate runtime,
        Guid challengeId,
        Guid runtimeInstanceId)
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

        var template = ResolveTemplate(mode, definitionJson) ?? PerTeamFlagTemplate.Default;
        var flag = PerTeamFlagGenerator.Generate(
            template,
            new(
                RandomNumberGenerator.GetBytes(32),
                runtimeInstanceId,
                challengeId,
                runtimeInstanceId,
                runtimeInstanceId,
                runtimeInstanceId));
        return new(flag, delivery);
    }

    private static PerTeamFlagTemplate? ResolveTemplate(
        GameMode mode,
        string definitionJson) => mode switch
    {
        GameMode.Ctf => Deserialize<CtfChallengeConfiguration>(definitionJson).FlagTemplate,
        GameMode.Awd => Deserialize<AwdChallengeConfiguration>(definitionJson).FlagTemplate,
        GameMode.Awdp => Deserialize<AwdpChallengeConfiguration>(definitionJson).FlagTemplate,
        GameMode.Koh => null,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported game mode.")
    };

    private static T Deserialize<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidOperationException($"{typeof(T).Name} is required.");
}
