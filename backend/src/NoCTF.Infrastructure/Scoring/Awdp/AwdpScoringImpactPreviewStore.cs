using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Awdp;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Scoring.Awdp;

public sealed class AwdpScoringImpactPreviewStore(
    NoCtfDbContext db,
    ILeaderboardSnapshotFactory snapshots) : IAwdpScoringImpactPreviewStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AwdpScoringImpactCompetition>> PreviewAsync(
        int limit,
        DateTimeOffset projectedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var candidates = await db.Competitions.IgnoreQueryFilters().AsNoTracking()
            .Where(competition => competition.Mode == GameMode.Awdp)
            .OrderBy(competition => competition.CreatedAt)
            .ThenBy(competition => competition.Id)
            .Take(500)
            .Select(competition => new
            {
                competition.Id,
                competition.Title,
                competition.ConfigurationJson
            })
            .ToArrayAsync(cancellationToken);
        var results = new List<AwdpScoringImpactCompetition>(Math.Min(limit, candidates.Length));
        foreach (var candidate in candidates)
        {
            var currentConfiguration = AwdpConfigurationParser.ParseCompetition(
                candidate.ConfigurationJson);
            if (currentConfiguration.UsesContinuousRoundScoring)
                continue;

            var current = await snapshots.CreateAsync(candidate.Id, projectedAt, cancellationToken);
            var continuousConfiguration = currentConfiguration with
            {
                SchemaVersion = AwdpConfiguration.CurrentSchemaVersion,
                Break = currentConfiguration.Break with { Settlement = AchievementSettlement.PerRound },
                Fix = currentConfiguration.Fix with { Settlement = AchievementSettlement.PerRound },
                RequireBreakBeforeFix = false
            };
            var continuous = await snapshots.CreateWithConfigurationAsync(
                candidate.Id,
                JsonSerializer.Serialize(continuousConfiguration, JsonOptions),
                projectedAt,
                cancellationToken);
            if (current is null || continuous is null)
                continue;

            var currentTeams = current.Entries.ToDictionary(entry => entry.TeamId);
            var continuousTeams = continuous.Entries.ToDictionary(entry => entry.TeamId);
            var teams = currentTeams.Keys.Concat(continuousTeams.Keys)
                .Distinct()
                .Select(teamId =>
                {
                    var currentEntry = currentTeams.GetValueOrDefault(teamId);
                    var continuousEntry = continuousTeams.GetValueOrDefault(teamId);
                    var currentScore = currentEntry?.Score ?? 0;
                    var continuousScore = continuousEntry?.Score ?? 0;
                    return new AwdpScoringImpactTeam(
                        teamId,
                        currentEntry?.TeamName ?? continuousEntry?.TeamName ?? teamId.ToString(),
                        currentScore,
                        continuousScore,
                        checked(continuousScore - currentScore));
                })
                .Where(team => team.Delta != 0)
                .OrderByDescending(team => Math.Abs((decimal)team.Delta))
                .ThenBy(team => team.TeamId)
                .ToArray();
            if (teams.Length == 0)
                continue;
            results.Add(new(
                candidate.Id,
                candidate.Title,
                currentConfiguration.SchemaVersion,
                teams));
            if (results.Count == limit)
                break;
        }
        await transaction.CommitAsync(cancellationToken);
        return results;
    }
}
