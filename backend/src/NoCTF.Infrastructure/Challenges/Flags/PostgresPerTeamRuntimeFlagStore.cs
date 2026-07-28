using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Domain.Challenges;

namespace NoCTF.Infrastructure.Challenges.Flags;

public sealed class PostgresPerTeamRuntimeFlagStore(NoCtfDbContext db)
    : IPerTeamRuntimeFlagStore
{
    public async Task<string> EnsureAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid teamId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var lockKey = $"runtime-flag:{competitionChallengeId:D}:{teamId:D}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);

        var existing = await db.ChallengeFlags.SingleOrDefaultAsync(
            flag => flag.CompetitionChallengeId == competitionChallengeId
                && flag.TeamId == teamId
                && flag.SpecificationKind == SpecificationKind.RuntimeDefinition
                && flag.SpecificationId == competitionChallengeId,
            cancellationToken);
        if (existing is not null)
            return existing.Flag;

        var secret = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == competitionChallengeId
                && challenge.CompetitionId == competitionId)
            .Join(
                db.Competitions.AsNoTracking(),
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (_, competition) => competition.FlagDerivationSecret)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The competition challenge does not belong to the competition.");
        var flag = PerTeamRuntimeFlagDerivation.Derive(
            secret,
            competitionId,
            competitionChallengeId,
            teamId);
        db.ChallengeFlags.Add(new ChallengeFlag
        {
            Id = Guid.CreateVersion7(now),
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            Flag = flag,
            FlagSha256 = ManageChallengeFlags.Hash(flag),
            SpecificationKind = SpecificationKind.RuntimeDefinition,
            SpecificationId = competitionChallengeId,
            CreatedAt = now
        });
        return flag;
    }
}
