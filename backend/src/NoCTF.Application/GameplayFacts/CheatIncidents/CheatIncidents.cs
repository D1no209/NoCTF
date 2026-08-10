using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.GameplayFacts.CheatIncidents;

public sealed record CheatIncidentQuery(
    Guid CompetitionId,
    Guid? SourceTeamId,
    Guid? OwnerTeamId,
    Guid? ActorUserId,
    Guid? CompetitionChallengeId,
    CheatIncidentStatus? Status,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset? BeforeDetectedAt,
    Guid? BeforeGameplayFactId,
    int Limit);

public sealed record CheatIncidentListItem(
    Guid GameplayFactId,
    Guid SourceTeamId,
    string SourceTeamName,
    Guid OwnerTeamId,
    string OwnerTeamName,
    Guid ActorUserId,
    string SubmittedByUserName,
    Guid CompetitionChallengeId,
    string ChallengeTitle,
    GameplayFactKind GameplayFactKind,
    GameplayFactResult Result,
    GameplayFactFailureCode FailureCode,
    CheatIncidentStatus Status,
    Guid? ResolvedByUserId,
    string? ResolvedByUserName,
    DateTimeOffset? ResolvedAt,
    string? ResolutionReason,
    DateTimeOffset SubmittedAt,
    DateTimeOffset DetectedAt,
    bool SourceTeamIsBanned);

public sealed record CheatIncidentPage(
    IReadOnlyList<CheatIncidentListItem> Items,
    int PendingCount);

public sealed record CheatIncidentDetail(
    Guid GameplayFactId,
    string Value,
    Guid SourceTeamId,
    string SourceTeamName,
    Guid OwnerTeamId,
    string OwnerTeamName,
    Guid ActorUserId,
    string SubmittedByUserName,
    Guid CompetitionChallengeId,
    string ChallengeTitle,
    GameplayFactKind GameplayFactKind,
    GameplayFactResult Result,
    GameplayFactFailureCode FailureCode,
    CheatIncidentStatus Status,
    Guid? ResolvedByUserId,
    string? ResolvedByUserName,
    DateTimeOffset? ResolvedAt,
    string? ResolutionReason,
    DateTimeOffset SubmittedAt,
    DateTimeOffset DetectedAt,
    bool SourceTeamIsBanned,
    DateTimeOffset? SourceTeamBannedAt,
    Guid? SourceTeamBannedByUserId,
    string? SourceTeamBanReason);

public sealed record CheatIncidentResolutionCommand(
    Guid CompetitionId,
    Guid GameplayFactId,
    Guid ActorUserId,
    string Reason,
    DateTimeOffset OccurredAt);

public enum CheatIncidentResolutionFailure : short
{
    InvalidReason,
    NotFound,
    CompetitionFinished,
    NotPending,
    AlreadyBanned,
    NotConfirmed,
    TeamNotBanned
}

public sealed record CheatIncidentResolutionResult(
    CheatIncidentResolutionFailure? Failure = null)
{
    public bool Succeeded => Failure is null;
}

public interface ICheatIncidentStore
{
    Task<CheatIncidentPage?> ListAsync(
        CheatIncidentQuery query,
        CancellationToken cancellationToken);

    Task<CheatIncidentDetail?> GetDetailAsync(
        Guid competitionId,
        Guid gameplayFactId,
        Guid actorUserId,
        DateTimeOffset accessedAt,
        CancellationToken cancellationToken);

    Task<CheatIncidentResolutionResult> DismissAsync(
        CheatIncidentResolutionCommand command,
        CancellationToken cancellationToken);

    Task<CheatIncidentResolutionResult> ConfirmAndBanAsync(
        CheatIncidentResolutionCommand command,
        CancellationToken cancellationToken);

    Task<CheatIncidentResolutionResult> CorrectAndUnbanAsync(
        CheatIncidentResolutionCommand command,
        CancellationToken cancellationToken);
}

public sealed class ListCheatIncidents(ICheatIncidentStore store)
{
    public Task<CheatIncidentPage?> ExecuteAsync(
        CheatIncidentQuery query,
        CancellationToken cancellationToken = default) =>
        query.Limit is >= 1 and <= 200
        && query.From <= query.To
        && query.To - query.From <= TimeSpan.FromDays(31)
        && ((query.BeforeDetectedAt is null) == (query.BeforeGameplayFactId is null))
            ? store.ListAsync(query, cancellationToken)
            : Task.FromResult<CheatIncidentPage?>(null);
}

public sealed class AccessCheatIncident(ICheatIncidentStore store)
{
    public Task<CheatIncidentDetail?> ExecuteAsync(
        Guid competitionId,
        Guid gameplayFactId,
        Guid actorUserId,
        DateTimeOffset accessedAt,
        CancellationToken cancellationToken = default) =>
        store.GetDetailAsync(
            competitionId,
            gameplayFactId,
            actorUserId,
            accessedAt,
            cancellationToken);
}

public sealed class ResolveCheatIncident(ICheatIncidentStore store)
{
    public Task<CheatIncidentResolutionResult> DismissAsync(
        CheatIncidentResolutionCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(command, store.DismissAsync, cancellationToken);

    public Task<CheatIncidentResolutionResult> ConfirmAndBanAsync(
        CheatIncidentResolutionCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(command, store.ConfirmAndBanAsync, cancellationToken);

    public Task<CheatIncidentResolutionResult> CorrectAndUnbanAsync(
        CheatIncidentResolutionCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(command, store.CorrectAndUnbanAsync, cancellationToken);

    private static Task<CheatIncidentResolutionResult> ExecuteAsync(
        CheatIncidentResolutionCommand command,
        Func<CheatIncidentResolutionCommand, CancellationToken,
            Task<CheatIncidentResolutionResult>> operation,
        CancellationToken cancellationToken)
    {
        var reason = command.Reason.Trim();
        return reason.Length is >= 8 and <= 512
            ? operation(command with { Reason = reason }, cancellationToken)
            : Task.FromResult(new CheatIncidentResolutionResult(
                CheatIncidentResolutionFailure.InvalidReason));
    }
}
