using NoCTF.Infrastructure.Persistence;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;

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
            .Where(item => item.Id == competitionId && item.Mode == GameMode.Awd)
            .Select(item => new
            {
                item.Status,
                item.ConfigurationJson,
                item.AccumulatedRunningSeconds,
                item.RunningSince
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null || competition.Status != CompetitionStatus.Running)
            return null;
        if (!await db.CompetitionChallenges.AsNoTracking().AnyAsync(
                item => item.Id == competitionChallengeId && item.CompetitionId == competitionId,
                ct))
            return null;

        var ownTeamId = await db.Teams.AsNoTracking()
            .Where(team =>
                team.CompetitionId == competitionId &&
                team.MemberIds.Contains(userId) &&
                team.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !team.IsBanned)
            .Select(team => (Guid?)team.Id)
            .SingleOrDefaultAsync(ct);
        if (ownTeamId is null)
            return null;

        var effectiveSeconds = competition.AccumulatedRunningSeconds +
            (competition.RunningSince is null
                ? 0
                : Math.Max(0, (long)(now - competition.RunningSince.Value).TotalSeconds));
        var hardeningSeconds = ReadHardeningSeconds(competition.ConfigurationJson);
        var exposeAll = effectiveSeconds >= hardeningSeconds;

        var teams = await db.Teams.AsNoTracking()
            .Where(team =>
                team.CompetitionId == competitionId &&
                team.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !team.IsBanned &&
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
                TeamId = instance.TeamId!.Value,
                instance.Urls,
                instance.ParticipantUrlIndexes
            })
            .ToListAsync(ct);
        var byTeam = runtimes.ToDictionary(runtime => runtime.TeamId);
        return teams.Select(team =>
        {
            if (!byTeam.TryGetValue(team.Id, out var runtime))
                return new RuntimeTargetView(team.Id, team.Name, []);
            var urls = runtime.ParticipantUrlIndexes
                .Where(index => index >= 0 && index < runtime.Urls.Length)
                .Select(index => runtime.Urls[index])
                .ToArray();
            return new RuntimeTargetView(team.Id, team.Name, urls);
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
