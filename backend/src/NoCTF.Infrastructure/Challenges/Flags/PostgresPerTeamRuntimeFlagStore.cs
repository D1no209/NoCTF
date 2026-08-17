using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Domain.Challenges;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Flags;
using NoCTF.GameModes.Awdp.Configuration;

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

    public async Task<string> EnsureGenerationAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid teamId,
        Guid runtimeInstanceId,
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
                && flag.SpecificationKind == SpecificationKind.RuntimeGeneration
                && flag.SpecificationId == runtimeInstanceId,
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
                    challenge.DefinitionJson,
                    target.Competition.FlagDerivationSecret,
                    target.Competition.ConfigurationJson
                })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The competition challenge does not belong to the competition.");
        var configuration = AwdpConfigurationResolver.Resolve(
            scope.ConfigurationJson,
            scope.RulesJson,
            scope.DefinitionJson);
        if (!configuration.UsesContinuousRoundScoring
            || configuration.Runtime is not
            {
                Allocation: NoCTF.Application.Runtime.Provisioning.RuntimeAllocation.PerTeam,
                FlagSource: NoCTF.Application.Runtime.Provisioning.RuntimeFlagSource.PerTeam,
                Definition: NoCTF.Application.Runtime.Provisioning.ContainerRuntimeDefinition
                {
                    FlagEnvironmentVariableName.Length: > 0
                }
            })
        {
            throw new InvalidOperationException(
                "AWDP generation flags require a PerTeam Container runtime flag environment variable.");
        }
        var flag = PerTeamFlagGenerator.Generate(
            configuration.FlagTemplate,
            new(
                scope.FlagDerivationSecret,
                competitionId,
                scope.ChallengeId,
                competitionChallengeId,
                teamId,
                runtimeInstanceId));
        db.ChallengeFlags.Add(new ChallengeFlag
        {
            Id = Guid.CreateVersion7(now),
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            Flag = flag,
            FlagSha256 = ManageChallengeFlags.Hash(flag),
            MatchKind = ChallengeFlagMatchKind.Exact,
            SpecificationKind = SpecificationKind.RuntimeGeneration,
            SpecificationId = runtimeInstanceId,
            CreatedAt = now
        });
        return flag;
    }

    public async Task InvalidateGenerationAsync(
        Guid runtimeInstanceId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var flag = await db.ChallengeFlags.SingleOrDefaultAsync(
            candidate => candidate.SpecificationKind == SpecificationKind.RuntimeGeneration
                && candidate.SpecificationId == runtimeInstanceId,
            cancellationToken);
        if (flag is not null && (flag.ValidUntil is null || flag.ValidUntil > now))
            flag.ValidUntil = now;
    }
}
