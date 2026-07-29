using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests;

public sealed class FixedRuntimePlacementPolicy(
    RuntimeProvider provider = RuntimeProvider.Docker,
    string runnerPool = "tests") : IRuntimePlacementPolicy
{
    public RuntimePlacement Resolve(RuntimeKind runtimeKind) => new(provider, runnerPool);
}
