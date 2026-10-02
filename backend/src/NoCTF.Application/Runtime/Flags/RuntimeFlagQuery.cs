using NoCTF.Application.Challenges.Flags;

namespace NoCTF.Application.Runtime.Flags;

public enum RuntimeFlagSource { Instance, Team, Static, AwdRound }
public enum RuntimeFlagState { Active, Scheduled, Expired, Deleted }

public sealed record RuntimeFlagScope(Guid? CompetitionId, Guid? ChallengeId, bool CanManageTemplate);
public sealed record RuntimeFlagView(ChallengeFlagView Flag, RuntimeFlagSource Source, RuntimeFlagState State);
public sealed record RuntimeFlagPage(IReadOnlyList<RuntimeFlagView> Items, int Total);
public sealed record RuntimeFlagQuery(Guid RuntimeInstanceId, bool IncludeHistory, int Offset, int Limit, DateTimeOffset Now, bool Desc = true);

public interface IRuntimeFlagReader
{
    Task<RuntimeFlagScope?> FindScopeAsync(Guid runtimeInstanceId, Guid actorId, bool isAdministrator, CancellationToken ct);
    Task<RuntimeFlagPage> ReadAsync(RuntimeFlagQuery query, CancellationToken ct);
}

public sealed class QueryRuntimeFlags(IRuntimeFlagReader reader)
{
    public Task<RuntimeFlagScope?> FindScopeAsync(Guid runtimeInstanceId, Guid actorId, bool isAdministrator, CancellationToken ct) =>
        reader.FindScopeAsync(runtimeInstanceId, actorId, isAdministrator, ct);

    public Task<RuntimeFlagPage> ExecuteAsync(RuntimeFlagQuery query, CancellationToken ct) => reader.ReadAsync(query, ct);
}
