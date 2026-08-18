using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.GameplayFacts.Management;

public sealed record GameplayFactListFilter(
    Guid CompetitionId,
    Guid? CompetitionChallengeId,
    Guid? TeamId,
    Guid? VictimTeamId,
    Guid? ActorUserId,
    GameplayFactKind? Kind,
    GameplayFactState? State,
    GameplayFactResult? Result,
    GameplayFactFailureCode? FailureCode,
    DateTimeOffset? OccurredFrom,
    DateTimeOffset? OccurredTo,
    string? Value,
    GameplayFactReferenceKind? ReferenceKind,
    Guid? ReferenceId);

public sealed record GameplayFactListItem(
    Guid Id,
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid? TeamId,
    Guid? VictimTeamId,
    Guid? ActorUserId,
    GameplayFactKind Kind,
    GameplayFactState State,
    GameplayFactResult? Result,
    GameplayFactFailureCode? FailureCode,
    GameplayFactReferenceKind? ReferenceKind,
    Guid? ReferenceId,
    string? Value,
    DateTimeOffset OccurredAt,
    DateTimeOffset UpdatedAt);

public interface IGameplayFactManagementStore
{
    Task<IReadOnlyList<GameplayFactListItem>> ListAdminAsync(
        GameplayFactListFilter filter,
        DateTimeOffset? beforeOccurredAt,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<GameplayFactListItem>?> ListPlayerAsync(
        Guid competitionId,
        Guid userId,
        Guid? competitionChallengeId,
        GameplayFactKind? kind,
        DateTimeOffset? beforeOccurredAt,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken);
    Task QueueDrainAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset cutoff,
        bool rejudge,
        Guid? gameplayFactId,
        CancellationToken cancellationToken);
}

public sealed class ListGameplayFacts(IGameplayFactManagementStore store)
{
    public Task<IReadOnlyList<GameplayFactListItem>> AdminAsync(
        GameplayFactListFilter filter,
        DateTimeOffset? beforeOccurredAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct = default) =>
        store.ListAdminAsync(filter, beforeOccurredAt, beforeId, limit, ct);

    public Task<IReadOnlyList<GameplayFactListItem>?> PlayerAsync(
        Guid competitionId,
        Guid userId,
        Guid? competitionChallengeId,
        GameplayFactKind? kind,
        DateTimeOffset? beforeOccurredAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct = default) =>
        store.ListPlayerAsync(
            competitionId,
            userId,
            competitionChallengeId,
            kind,
            beforeOccurredAt,
            beforeId,
            limit,
            ct);
}

public sealed class QueueGameplayFactWork(IGameplayFactManagementStore store)
{
    public Task QueueAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset cutoff,
        bool rejudge,
        Guid? gameplayFactId,
        CancellationToken ct = default) =>
        store.QueueDrainAsync(
            competitionId, competitionChallengeId, cutoff, rejudge, gameplayFactId, ct);
}
