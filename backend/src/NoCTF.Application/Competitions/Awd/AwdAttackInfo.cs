using NoCTF.Application.Challenges.Management;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Awd;

public sealed record AwdAttackInfoItem(Guid FlagId, int GenerationRound, Guid StoreId, string StoreName,
    int Ordinal, string PublicLocator, DateTimeOffset IssuedAt, int? ExclusiveExpiryRound, double? RemainingActiveSeconds);
public sealed record AwdAttackInfoPage(IReadOnlyList<AwdAttackInfoItem> Items, int Total);

public interface IAwdAttackInfoReader
{
    Task<AwdAttackInfoPage?> ReadAsync(Guid competitionId, Guid challengeId, Guid ownTeamId,
        Guid targetTeamId, DateTimeOffset now, int offset, int limit, CancellationToken ct);
}

public sealed class ListAwdAttackInfo(ICompetitionChallengeReadAccess access, IAwdAttackInfoReader reader)
{
    public async Task<AwdAttackInfoPage?> ExecuteAsync(Guid userId, Guid competitionId, Guid challengeId,
        Guid targetTeamId, DateTimeOffset now, int offset, int limit, CancellationToken ct)
    {
        var decision = await access.ResolveAsync(userId, competitionId, now, ct);
        if (decision?.TeamId is not Guid ownTeamId || decision.Visibility.GameMode != GameMode.Awd
            || !ParticipantChallengeVisibilityPolicy.CanView(decision.Visibility.CompetitionStatus)) return null;
        return await reader.ReadAsync(competitionId, challengeId, ownTeamId, targetTeamId, now, offset, limit, ct);
    }
}
