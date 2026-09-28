using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Lifecycle;

namespace NoCTF.Infrastructure.Runtime.Targets;

public sealed class RuntimeTargetReader(NoCtfDbContext db) : IRuntimeTargetReader
{
    public async Task<IReadOnlyList<RuntimeTargetView>?> ListAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(item =>
                item.Id == competitionId &&
                item.Mode == GameMode.Awd &&
                item.DeletedAt == null)
            .SingleOrDefaultAsync(ct);
        if (competition is null || competition.Status != CompetitionStatus.Running)
            return null;
        var challengeId = await db.CompetitionChallenges.AsNoTracking()
            .Where(item =>
                item.Id == competitionChallengeId &&
                item.CompetitionId == competitionId &&
                item.IsPublished &&
                item.DeletedAt == null)
            .Select(item => (Guid?)item.ChallengeId)
            .SingleOrDefaultAsync(ct);
        if (challengeId is null)
            return null;
        var template = await db.Challenges.AsNoTracking()
            .Include(item => item.Definition)
                .ThenInclude(definition => definition!.Runtime)
                    .ThenInclude(runtime => runtime!.UrlBindings)
            .SingleOrDefaultAsync(item => item.Id == challengeId.Value
                && item.DeletedAt == null, ct);
        if (template?.Definition is null)
            return null;
        var participantBindingIndexes = await db.Set<ChallengeRuntimeUrlBinding>()
            .AsNoTracking()
            .Where(binding => binding.ChallengeId == challengeId.Value
                && !binding.IsControlCheck
                && binding.Exposure == PersistedRuntimeExposure.Participants)
            .Select(binding => binding.Position)
            .ToArrayAsync(ct);

        var ownTeamId = await db.Teams.AsNoTracking()
            .Where(team =>
                team.CompetitionId == competitionId &&
                team.Members.Any(member => member.UserId == userId) &&
                team.RegistrationStatus == TeamRegistrationStatus.Approved &&
                team.DeletedAt == null &&
                !team.IsBanned)
            .Select(team => (Guid?)team.Id)
            .SingleOrDefaultAsync(ct);
        if (ownTeamId is null)
            return null;

        var effectiveRuntime = await CompetitionEffectiveRuntimeReader.ReadAsync(
            db,
            competitionId,
            competition.StartAt,
            competition.EndAt,
            now,
            ct);
        var effectiveSeconds = Math.Max(0, (long)effectiveRuntime.Elapsed.TotalSeconds);
        var hardeningSeconds = (competition.ModeConfiguration as AwdCompetitionModeConfiguration)
            ?.HardeningDurationSeconds ?? 0;
        var exposeAll = effectiveSeconds >= hardeningSeconds;

        var teams = await db.Teams.AsNoTracking()
            .Where(team =>
                team.CompetitionId == competitionId &&
                team.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !team.IsBanned &&
                team.DeletedAt == null &&
                (exposeAll || team.Id == ownTeamId.Value))
            .OrderBy(team => team.RegisteredAt)
            .ThenBy(team => team.Id)
            .Select(team => new { team.Id, team.Name })
            .ToListAsync(ct);
        var teamIds = teams.Select(team => team.Id).ToArray();
        var runtimeEntities = await db.RuntimeInstances.AsNoTracking()
            .Include(instance => instance.AccessEndpoints)
            .Where(instance =>
                instance.CompetitionChallengeId == competitionChallengeId &&
                instance.State == RuntimeState.Running &&
                instance.TeamId != null &&
                teamIds.Contains(instance.TeamId.Value))
            .ToListAsync(ct);
        var byTeam = runtimeEntities.ToDictionary(runtime => runtime.TeamId!.Value);
        return teams.Select(team =>
        {
            if (!byTeam.TryGetValue(team.Id, out var runtime))
                return new RuntimeTargetView(team.Id, team.Name);
            var endpoints = runtime.AccessEndpoints
                .Where(endpoint => participantBindingIndexes.Contains(endpoint.BindingIndex))
                .OrderBy(endpoint => endpoint.BindingIndex)
                .Select(endpoint => new RuntimeAccessEndpointView(
                    endpoint.BindingIndex,
                    endpoint.DirectAddress,
                    endpoint.TargetHost,
                    endpoint.TargetPort))
                .ToArray();
            return new RuntimeTargetView(
                team.Id,
                team.Name,
                runtime.Id,
                endpoints,
                runtime.AccessMode);
        }).ToArray();
    }

}
