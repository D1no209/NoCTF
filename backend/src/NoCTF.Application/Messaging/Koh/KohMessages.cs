using System.Buffers.Binary;
using System.Security.Cryptography;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.Messaging;

public sealed record PollKohChallenge(
    Guid GameplayFactId,
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    DateTimeOffset RunningSince,
    DateTimeOffset DueAt)
{
    public static PollKohChallenge Create(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset runningSince,
        DateTimeOffset dueAt) => new(
            CreateGameplayFactId(competitionChallengeId, dueAt),
            competitionId,
            competitionChallengeId,
            runningSince,
            dueAt);

    public PollKohChallenge At(DateTimeOffset dueAt) => this with
    {
        GameplayFactId = CreateGameplayFactId(CompetitionChallengeId, dueAt),
        DueAt = dueAt
    };

    private static Guid CreateGameplayFactId(
        Guid competitionChallengeId,
        DateTimeOffset dueAt)
    {
        Span<byte> input = stackalloc byte[24];
        competitionChallengeId.TryWriteBytes(input[..16]);
        BinaryPrimitives.WriteInt64BigEndian(input[16..], dueAt.UtcTicks);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }
}

public sealed record RecordKohObservation(
    Guid GameplayFactId,
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid? TeamId,
    GameplayFactResult? Result,
    GameplayFactFailureCode? FailureCode,
    DateTimeOffset RunningSince,
    DateTimeOffset DueAt,
    DateTimeOffset ObservedAt);
