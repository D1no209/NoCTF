using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Competitions.Koh;

public sealed record KohChallengeAccessView(
    string ControlFlag,
    Guid RuntimeInstanceId,
    RuntimeAccessMode AccessMode,
    IReadOnlyList<RuntimeAccessEndpointView> AccessEndpoints);

public interface IKohChallengeAccessReader
{
    Task<KohChallengeAccessView?> FindAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken);
}
