using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Infrastructure.Persistence;
using EF = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions;

namespace NoCTF.Infrastructure.Eventing.SubmissionStreams;

/// <summary>Reads a team-scoped runtime flag and falls back to shared challenge configuration.</summary>
public sealed class ConfiguredChallengeInstanceFlagReader(NoCtfDbContext db) : IChallengeInstanceFlagReader
{
    public async Task<string?> ReadAsync(
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        if (teamId == Guid.Empty)
            return null;

        var instanceReceipt = await EF.FirstOrDefaultAsync(db.ChallengeInstances.AsNoTracking()
                .Where(item => item.CompetitionId == competitionId
                    && item.TeamId == teamId
                    && item.ChallengeId == challengeId)
                .OrderByDescending(item => item.UpdatedAt)
                .Select(item => item.Receipt),
            cancellationToken);
        var instanceFlag = ReadFlag(instanceReceipt);
        if (instanceFlag is not null)
            return instanceFlag;

        var configurationQuery =
            from challenge in db.Challenges.AsNoTracking()
            join configuration in db.ChallengeConfigurations.AsNoTracking()
                on challenge.Id equals configuration.ChallengeId
            where challenge.Id == challengeId && challenge.CompetitionId == competitionId
            select configuration.Json;
        var configurationJson = await EF.SingleOrDefaultAsync(configurationQuery, cancellationToken);
        return ReadFlag(configurationJson);
    }

    private static string? ReadFlag(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("flag", out var flag) ? flag.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
