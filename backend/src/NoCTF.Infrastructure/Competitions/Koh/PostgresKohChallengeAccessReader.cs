using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Koh;

public sealed class PostgresKohChallengeAccessReader(NoCtfDbContext db)
    : IKohChallengeAccessReader
{
    public async Task<KohChallengeAccessView?> FindAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
            return null;
        var teamId = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                && team.MemberIds.Contains(userId)
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && !team.IsBanned
                && team.DeletedAt == null)
            .Select(team => (Guid?)team.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (teamId is null)
            return null;

        var flag = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId
                && competition.Mode == GameMode.Koh
                && competition.Status == CompetitionStatus.Running
                && competition.DeletedAt == null)
            .Join(
                db.CompetitionChallenges.AsNoTracking()
                    .Where(challenge => challenge.Id == competitionChallengeId
                        && challenge.IsPublished
                        && challenge.DeletedAt == null),
                competition => competition.Id,
                challenge => challenge.CompetitionId,
                (competition, challenge) => challenge.Id)
            .Join(
                db.ChallengeFlags.AsNoTracking()
                    .Where(candidate => candidate.TeamId == teamId
                        && candidate.SpecificationKind == SpecificationKind.RuntimeDefinition
                        && candidate.SpecificationId == competitionChallengeId
                        && candidate.DeletedAt == null),
                challengeId => (Guid?)challengeId,
                candidate => candidate.CompetitionChallengeId,
                (challengeId, candidate) => candidate.Flag)
            .SingleOrDefaultAsync(cancellationToken);
        if (flag is null)
            return null;

        var runtime = await db.RuntimeInstances.AsNoTracking()
            .Where(instance => instance.CompetitionId == competitionId
                && instance.CompetitionChallengeId == competitionChallengeId
                && instance.TeamId == null
                && instance.State == RuntimeState.Running)
            .OrderByDescending(instance => instance.Generation)
            .Select(instance => new
            {
                instance.Urls,
                instance.ParticipantUrlIndexes
            })
            .FirstOrDefaultAsync(cancellationToken);
        var urls = runtime is null
            ? []
            : runtime.ParticipantUrlIndexes
                .Where(index => index >= 0 && index < runtime.Urls.Length)
                .Select(index => runtime.Urls[index])
                .ToArray();
        return new(flag, urls);
    }
}
