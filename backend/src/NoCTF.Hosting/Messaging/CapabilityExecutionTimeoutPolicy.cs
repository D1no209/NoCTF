using JasperFx;
using JasperFx.CodeGeneration;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Messaging;
using Wolverine.Configuration;
using Wolverine.Runtime.Handlers;

namespace NoCTF.Hosting.Messaging;

internal sealed class CapabilityExecutionTimeoutPolicy : IHandlerPolicy
{
    public void Apply(
        IReadOnlyList<HandlerChain> chains,
        GenerationRules rules,
        IServiceContainer container)
    {
        foreach (var chain in chains.Where(chain => TimeoutFor(chain.MessageType) is not null))
        {
            chain.ExecutionTimeoutInSeconds =
                TimeoutFor(chain.MessageType)!.Value;
        }
    }

    internal static int? TimeoutFor(Type messageType) =>
        messageType == typeof(NoCTF.Application.Competitions.Awd.Fixes.RunPersistentFix)
            ? NoCTF.Application.Competitions.Awd.Fixes.PersistentFixExecutionBudget.HandlerSeconds
            : messageType == typeof(NoCTF.Application.Challenges.Testing.RunTemplateCapabilityTest)
            ? NoCTF.Application.Challenges.Testing.TemplateCapabilityTestBudget.HandlerSeconds
            : messageType == typeof(RunFixExecution)
            ? FixExecutionBudget.HandlerExecutionTimeoutSeconds
            : null;
}
