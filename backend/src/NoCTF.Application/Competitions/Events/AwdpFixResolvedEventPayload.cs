using System.Text.Json;
using System.Text.Json.Serialization;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.Competitions.Events;

public sealed record AwdpFixResolvedEventPayload(
    int SchemaVersion,
    Guid GameplayFactId,
    Guid PatchUploadId,
    Guid RuntimeInstanceId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    AwdpFixOutcome Outcome,
    GameplayFactFailureCode? FailureCode,
    DateTimeOffset ResolvedAt)
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static AwdpFixResolvedEventPayload Create(
        Guid gameplayFactId,
        Guid patchUploadId,
        Guid runtimeInstanceId,
        Guid teamId,
        Guid competitionChallengeId,
        AwdpFixOutcome outcome,
        GameplayFactFailureCode? failureCode,
        DateTimeOffset resolvedAt) => new(
            CurrentSchemaVersion,
            gameplayFactId,
            patchUploadId,
            runtimeInstanceId,
            teamId,
            competitionChallengeId,
            outcome,
            failureCode,
            resolvedAt);

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public static AwdpFixResolvedEventPayload? Deserialize(string json) =>
        JsonSerializer.Deserialize<AwdpFixResolvedEventPayload>(json, JsonOptions);
}
