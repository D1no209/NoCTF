using NoCTF.Infrastructure.Persistence;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Lifecycle;

namespace NoCTF.Infrastructure.Runtime.Targets;

public sealed class RuntimeTargetReader(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog runtimeTemplates) : IRuntimeTargetReader
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
            .Select(item => new
            {
                item.Status,
                item.ConfigurationJson,
                item.StartAt,
                item.EndAt
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null || competition.Status != CompetitionStatus.Running)
            return null;
        var challenge = await db.CompetitionChallenges.AsNoTracking()
                .Where(item =>
                    item.Id == competitionChallengeId &&
                    item.CompetitionId == competitionId &&
                    item.IsPublished &&
                    item.DeletedAt == null)
                .Join(
                    db.Challenges.AsNoTracking()
                        .Where(template => template.DeletedAt == null),
                    challenge => challenge.ChallengeId,
                    template => template.Id,
                    (challenge, template) => new { template.DefinitionJson })
                .SingleOrDefaultAsync(ct);
        if (challenge is null)
            return null;

        var ownTeamId = await db.Teams.AsNoTracking()
            .Where(team =>
                team.CompetitionId == competitionId &&
                team.MemberIds.Contains(userId) &&
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
        var hardeningSeconds = ReadHardeningSeconds(competition.ConfigurationJson);
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
        var runtimes = await db.RuntimeInstances.AsNoTracking()
            .Where(instance =>
                instance.CompetitionChallengeId == competitionChallengeId &&
                instance.State == RuntimeState.Running &&
                instance.TeamId != null &&
                teamIds.Contains(instance.TeamId.Value))
            .Select(instance => new
            {
                instance.Id,
                TeamId = instance.TeamId!.Value,
                instance.Urls,
                instance.AccessMode,
                AccessEndpoints = instance.AccessEndpoints
                    .OrderBy(endpoint => endpoint.BindingIndex)
                    .Select(endpoint => new RuntimeAccessEndpointView(
                        endpoint.BindingIndex,
                        endpoint.DirectAddress,
                        endpoint.TargetHost,
                        endpoint.TargetPort))
                    .ToArray()
            })
            .ToListAsync(ct);
        var byTeam = runtimes.ToDictionary(runtime => runtime.TeamId);
        return teams.Select(team =>
        {
            if (!byTeam.TryGetValue(team.Id, out var runtime))
                return new RuntimeTargetView(team.Id, team.Name, []);
            var urls = RuntimeParticipantUrlProjection.Filter(
                runtimeTemplates,
                GameMode.Awd,
                challenge.DefinitionJson,
                runtime.Urls);
            var endpoints = RuntimeParticipantUrlProjection.Filter(
                runtimeTemplates,
                GameMode.Awd,
                challenge.DefinitionJson,
                runtime.AccessEndpoints);
            return new RuntimeTargetView(
                team.Id,
                team.Name,
                urls,
                runtime.Id,
                endpoints,
                runtime.AccessMode);
        }).ToArray();
    }

    private static long ReadHardeningSeconds(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("hardeningDurationSeconds", out var value)
            ? value.GetInt64()
            : document.RootElement.TryGetProperty("HardeningDurationSeconds", out value)
                ? value.GetInt64()
                : 0;
    }
}
