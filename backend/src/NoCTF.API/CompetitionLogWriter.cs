using System.Text.Json;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API;

public static class CompetitionLogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Add(
        ApplicationDbContext db,
        Guid competitionId,
        string eventType,
        string message,
        string level = "info",
        Guid? teamId = null,
        Guid? userId = null,
        Guid? challengeId = null,
        object? metadata = null)
    {
        db.CompetitionLogs.Add(new CompetitionLog
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            Level = string.IsNullOrWhiteSpace(level) ? "info" : level.Trim().ToLowerInvariant(),
            EventType = eventType,
            Message = message,
            TeamId = teamId,
            UserId = userId,
            ChallengeId = challengeId,
            MetadataJson = metadata is null ? "{}" : JsonSerializer.Serialize(metadata, JsonOptions),
            CreatedAt = DateTime.UtcNow,
        });
    }
}
