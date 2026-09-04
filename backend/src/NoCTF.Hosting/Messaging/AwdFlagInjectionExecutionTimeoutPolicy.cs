using JasperFx;
using JasperFx.CodeGeneration;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Messaging;
using Wolverine.Configuration;
using Wolverine.Runtime.Handlers;

namespace NoCTF.Hosting.Messaging;

internal sealed class AwdFlagInjectionExecutionTimeoutPolicy : IHandlerPolicy
{
    public void Apply(
        IReadOnlyList<HandlerChain> chains,
        GenerationRules rules,
        IServiceContainer container)
    {
        foreach (var chain in chains.Where(chain => TimeoutFor(chain.MessageType) is not null))
        {
            chain.ExecutionTimeoutInSeconds =
                AwdFlagInjectionExecutionBudget.HandlerExecutionTimeoutSeconds;
        }
    }

    internal static int? TimeoutFor(Type messageType) =>
        messageType == typeof(InjectAwdFlag)
            || messageType == typeof(InjectChallengeTestFlag)
            ? AwdFlagInjectionExecutionBudget.HandlerExecutionTimeoutSeconds
            : null;
}
