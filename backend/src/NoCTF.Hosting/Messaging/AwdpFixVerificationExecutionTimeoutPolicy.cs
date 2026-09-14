using JasperFx;
using JasperFx.CodeGeneration;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Application.Messaging;
using Wolverine.Configuration;
using Wolverine.Runtime.Handlers;

namespace NoCTF.Hosting.Messaging;

internal sealed class AwdpFixVerificationExecutionTimeoutPolicy : IHandlerPolicy
{
    public void Apply(
        IReadOnlyList<HandlerChain> chains,
        GenerationRules rules,
        IServiceContainer container)
    {
        foreach (var chain in chains.Where(chain =>
                     chain.MessageType == typeof(RunAwdpFixVerification)
                     || chain.MessageType == typeof(RunPatchVerification)))
        {
            chain.ExecutionTimeoutInSeconds =
                AwdpFixExecutionBudget.HandlerExecutionTimeoutSeconds;
        }
    }

    internal static int? TimeoutFor(Type messageType) =>
        messageType == typeof(RunAwdpFixVerification)
            || messageType == typeof(RunPatchVerification)
            ? AwdpFixExecutionBudget.HandlerExecutionTimeoutSeconds
            : null;
}
