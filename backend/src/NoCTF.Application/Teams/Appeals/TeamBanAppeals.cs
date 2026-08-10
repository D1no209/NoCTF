using NoCTF.Domain.Teams;

namespace NoCTF.Application.Teams.Appeals;

public sealed record TeamBanAppealView(
    Guid Id,
    Guid ActorUserId,
    string SubmittedByUserName,
    string Statement,
    DateTimeOffset SubmittedAt,
    TeamBanAppealStatus Status,
    Guid? ResolvedByUserId,
    string? ResolvedByUserName,
    string? ResolutionReason,
    DateTimeOffset? ResolvedAt);

public sealed record TeamBanCaseView(
    Guid BanEventId,
    Guid CompetitionId,
    Guid TeamId,
    string TeamName,
    TeamBanSource Source,
    DateTimeOffset BannedAt,
    bool IsCurrentlyBanned,
    bool CanAppeal,
    TeamBanAppealView? Appeal);

public sealed record SubmitTeamBanAppealCommand(
    Guid CompetitionId,
    Guid ActorUserId,
    string Statement,
    DateTimeOffset SubmittedAt);

public enum TeamBanAppealResolution : short
{
    Uphold,
    Accept
}

public sealed record ResolveTeamBanAppealCommand(
    Guid CompetitionId,
    Guid AppealId,
    Guid ActorUserId,
    TeamBanAppealResolution Resolution,
    string Reason,
    DateTimeOffset ResolvedAt);

public sealed record CorrectTeamBanCommand(
    Guid CompetitionId,
    Guid TeamId,
    Guid ActorUserId,
    string Reason,
    DateTimeOffset CorrectedAt);

public enum TeamBanAppealFailure : short
{
    InvalidStatement,
    InvalidReason,
    CompetitionNotFound,
    TeamNotFound,
    BanNotFound,
    CaptainRequired,
    AppealAlreadySubmitted,
    AppealNotFound,
    AppealAlreadyResolved,
    BanNoLongerCurrent
}

public sealed record TeamBanAppealMutationResult(
    TeamBanCaseView? BanCase = null,
    TeamBanAppealFailure? Failure = null)
{
    public bool Succeeded => Failure is null;
}

public interface ITeamBanAppealStore
{
    Task<TeamBanCaseView?> GetForMemberAsync(
        Guid competitionId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TeamBanCaseView>?> ListForStaffAsync(
        Guid competitionId,
        CancellationToken cancellationToken);

    Task<TeamBanAppealMutationResult> SubmitAsync(
        SubmitTeamBanAppealCommand command,
        CancellationToken cancellationToken);

    Task<TeamBanAppealMutationResult> ResolveAsync(
        ResolveTeamBanAppealCommand command,
        CancellationToken cancellationToken);

    Task<TeamBanAppealMutationResult> CorrectAsync(
        CorrectTeamBanCommand command,
        CancellationToken cancellationToken);
}

public sealed class GetMyTeamBanCase(ITeamBanAppealStore store)
{
    public Task<TeamBanCaseView?> ExecuteAsync(
        Guid competitionId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        store.GetForMemberAsync(competitionId, userId, cancellationToken);
}

public sealed class ListTeamBanAppeals(ITeamBanAppealStore store)
{
    public Task<IReadOnlyList<TeamBanCaseView>?> ExecuteAsync(
        Guid competitionId,
        CancellationToken cancellationToken = default) =>
        store.ListForStaffAsync(competitionId, cancellationToken);
}

public sealed class SubmitTeamBanAppeal(ITeamBanAppealStore store)
{
    public Task<TeamBanAppealMutationResult> ExecuteAsync(
        SubmitTeamBanAppealCommand command,
        CancellationToken cancellationToken = default)
    {
        var statement = command.Statement.Trim();
        return statement.Length is >= 16 and <= 512
            ? store.SubmitAsync(command with { Statement = statement }, cancellationToken)
            : Task.FromResult(new TeamBanAppealMutationResult(
                Failure: TeamBanAppealFailure.InvalidStatement));
    }
}

public sealed class ResolveTeamBanAppeal(ITeamBanAppealStore store)
{
    public Task<TeamBanAppealMutationResult> ExecuteAsync(
        ResolveTeamBanAppealCommand command,
        CancellationToken cancellationToken = default)
    {
        var reason = command.Reason.Trim();
        return reason.Length is >= 8 and <= 512
            ? store.ResolveAsync(command with { Reason = reason }, cancellationToken)
            : Task.FromResult(new TeamBanAppealMutationResult(
                Failure: TeamBanAppealFailure.InvalidReason));
    }
}

public sealed class CorrectTeamBan(ITeamBanAppealStore store)
{
    public Task<TeamBanAppealMutationResult> ExecuteAsync(
        CorrectTeamBanCommand command,
        CancellationToken cancellationToken = default)
    {
        var reason = command.Reason.Trim();
        return reason.Length is >= 8 and <= 512
            ? store.CorrectAsync(command with { Reason = reason }, cancellationToken)
            : Task.FromResult(new TeamBanAppealMutationResult(
                Failure: TeamBanAppealFailure.InvalidReason));
    }
}
