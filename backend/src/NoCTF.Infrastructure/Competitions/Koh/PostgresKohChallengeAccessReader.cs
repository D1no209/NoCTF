using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Targets;

namespace NoCTF.Infrastructure.Competitions.Koh;

public sealed class PostgresKohChallengeAccessReader(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog runtimeTemplates)
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

        var challenge = await db.Competitions.AsNoTracking()
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
                (competition, challenge) => new { competition.Mode, Challenge = challenge })
            .Join(
                db.Challenges.AsNoTracking().Where(template => template.DeletedAt == null),
                item => item.Challenge.ChallengeId,
                template => template.Id,
                (item, template) => new
                {
                    item.Mode,
                    CompetitionChallengeId = item.Challenge.Id,
                    template.DefinitionJson
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (challenge is null)
            return null;

        var flag = await db.ChallengeFlags.AsNoTracking()
            .Where(candidate => candidate.TeamId == teamId
                && candidate.SpecificationKind == SpecificationKind.RuntimeDefinition
                && candidate.SpecificationId == competitionChallengeId
                && candidate.DeletedAt == null)
            .Where(candidate => candidate.CompetitionChallengeId == challenge.CompetitionChallengeId)
            .Select(candidate => candidate.Flag)
            .SingleOrDefaultAsync(cancellationToken);
        if (flag is null)
            return null;

        var runtime = await db.RuntimeInstances.AsNoTracking()
            .Where(instance => instance.CompetitionId == competitionId
                && instance.CompetitionChallengeId == competitionChallengeId
                && instance.TeamId == null
                && instance.State == RuntimeState.Running)
            .OrderByDescending(instance => instance.CreatedAt)
            .ThenByDescending(instance => instance.Id)
            .Select(instance => new
            {
                instance.Id,
                instance.AccessMode,
                AccessEndpoints = instance.AccessEndpoints.OrderBy(endpoint => endpoint.BindingIndex)
                    .Select(endpoint => new NoCTF.Application.Runtime.Instances.RuntimeAccessEndpointView(
                        endpoint.BindingIndex,
                        endpoint.DirectAddress,
                        endpoint.TargetHost,
                        endpoint.TargetPort)).ToArray()
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (runtime is null)
            return null;
        var accessEndpoints = RuntimeParticipantUrlProjection.Filter(
            runtimeTemplates,
            challenge.Mode,
            challenge.DefinitionJson,
            runtime.AccessEndpoints);
        return new(
            flag,
            runtime.Id,
            runtime.AccessMode,
            accessEndpoints);
    }
}
