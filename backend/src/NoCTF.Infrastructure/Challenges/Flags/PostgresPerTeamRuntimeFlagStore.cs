using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Domain.Challenges;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Flags;

namespace NoCTF.Infrastructure.Challenges.Flags;

public sealed class PostgresPerTeamRuntimeFlagStore(
    NoCtfDbContext db,
    TeamChallengeCriticalSection criticalSection)
    : IPerTeamRuntimeFlagStore
{
    public PostgresPerTeamRuntimeFlagStore(NoCtfDbContext db)
        : this(db, new TeamChallengeCriticalSection(new LocalCriticalSectionRegistry())) { }

    public async Task<string> EnsureAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid teamId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var generationLease = await criticalSection.AcquireAsync(
            db,
            teamId,
            competitionChallengeId,
            cancellationToken);

        var existing = await db.ChallengeFlags.SingleOrDefaultAsync(
            flag => flag.CompetitionChallengeId == competitionChallengeId
                && flag.TeamId == teamId
                && flag.SpecificationKind == SpecificationKind.RuntimeDefinition
                && flag.SpecificationId == competitionChallengeId,
            cancellationToken);
        if (existing is not null)
            return existing.Flag;

        var scope = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == competitionChallengeId
                && challenge.CompetitionId == competitionId)
            .Join(
                db.Competitions.AsNoTracking(),
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { Challenge = challenge, Competition = competition })
            .Join(
                db.Challenges.AsNoTracking(),
                target => target.Challenge.ChallengeId,
                challenge => challenge.Id,
                (target, challenge) => new
                {
                    target.Challenge.ChallengeId,
                    target.Challenge.RulesJson,
                    target.Competition.FlagDerivationSecret,
                    target.Competition.ConfigurationJson
                })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The competition challenge does not belong to the competition.");
        var template = CtfFlagTemplateResolver.Resolve(
            scope.ConfigurationJson,
            scope.RulesJson);
        var flag = PerTeamFlagGenerator.Generate(
            template,
            new(
                scope.FlagDerivationSecret,
                competitionId,
                scope.ChallengeId,
                competitionChallengeId,
                teamId));
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
