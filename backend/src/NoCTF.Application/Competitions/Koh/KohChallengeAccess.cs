namespace NoCTF.Application.Competitions.Koh;

public sealed record KohChallengeAccessView(
    string ControlFlag,
    IReadOnlyList<string> Urls);

public interface IKohChallengeAccessReader
{
    Task<KohChallengeAccessView?> FindAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken);
}
