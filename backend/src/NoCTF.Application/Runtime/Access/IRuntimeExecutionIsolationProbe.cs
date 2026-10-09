using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Access;

public interface IRuntimeExecutionIsolationProbe
{
    RuntimeProvider Provider { get; }
    Task<RuntimeIsolationState> CheckAsync(CancellationToken ct);
}
