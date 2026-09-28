using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;

namespace NoCTF.Application.Teams.Registration;

public sealed record CreateTeamCommand(
    Guid CompetitionId,
    Guid UserId,
    string Name,
    DateTimeOffset RegisteredAt,
    string? TrackKey,
    string? TrackInvitationCode = null);
public sealed record TeamView(
    Guid Id,
    Guid CompetitionId,
    string Name,
    Guid? AvatarFileId,
    Guid CaptainId,
    IReadOnlyList<Guid> MemberIds,
    TeamRegistrationStatus RegistrationStatus,
    bool IsLocked,
    bool IsBanned,
    DateTimeOffset RegisteredAt,
    string TrackKey = CompetitionTrackConfiguration.DefaultTrackKey,
    string TrackName = "Default");

public sealed record TeamListQuery(
    Guid CompetitionId,
    bool IncludePending,
    bool IncludeInternal,
    string? Keyword,
    int Offset,
    int Limit,
    bool Desc);

public sealed record TeamListPage(
    IReadOnlyList<TeamView> Items,
    int Total);
public sealed record TeamRegistrationPolicy(
    CompetitionStatus Status,
    bool AutoApprove,
    bool CompetitionDeleted,
    bool AllowWhileRunning = false,
    GameMode Mode = GameMode.Ctf,
    IReadOnlyList<CompetitionTrackDefinition>? Tracks = null,
    bool TracksEnabled = false,
    bool PracticeModeEnabled = false)
{
    public bool PracticeOpen => Mode == GameMode.Ctf && Status == CompetitionStatus.Finished && PracticeModeEnabled && !CompetitionDeleted;
}
public enum TeamRegistrationFailure
{
    InvalidTeamName,
    CompetitionNotFound,
    RegistrationClosed,
    UserAlreadyRegistered,
    TeamNameOrMembershipConflict,
    TeamNotFound,
    CompetitionFinished,
    TeamLocked,
    TeamBanned,
    TeamConflict,
    TeamReviewConflict,
    CompetitionActive,
    TrackNotFound,
    TrackNotPublicSelectable,
    TrackInvitationRequired,
    TrackInvitationInvalid,
    TrackSsoIdentityRequired
}
public sealed record TeamCreateStoreResult(TeamView? Team, TeamRegistrationFailure? Failure = null);
public sealed record TeamReviewStoreResult(bool Changed, TeamRegistrationFailure? Failure = null);
public sealed record TeamUpdateStoreResult(TeamView? Team, TeamRegistrationFailure? Failure = null);
public sealed record UpdateTeamCommand(
    Guid CompetitionId,
    Guid TeamId,
    string Name,
    string? TrackKey = null,
    string? TrackInvitationCode = null,
    Guid? ActorUserId = null,
    DateTimeOffset? UpdatedAt = null);

public interface ITeamRegistrationStore
{
    Task<TeamRegistrationPolicy?> GetPolicyAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<TeamCreateStoreResult> TryCreateAsync(CreateTeamCommand command, TeamRegistrationStatus status, CancellationToken cancellationToken);
    Task<IReadOnlyList<TeamView>> ListAsync(Guid competitionId, bool includePending, CancellationToken cancellationToken);
    async Task<TeamListPage> ListPageAsync(TeamListQuery query, CancellationToken cancellationToken)
    {
        var items = await ListAsync(query.CompetitionId, query.IncludePending, cancellationToken);
        if (!query.IncludeInternal)
            items = await ListPublicAsync(query.CompetitionId, query.IncludePending, cancellationToken);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            items = items.Where(team => team.Name.Contains(query.Keyword, StringComparison.OrdinalIgnoreCase)).ToArray();
        var ordered = query.Desc
            ? items.OrderByDescending(team => team.Name).ThenByDescending(team => team.Id)
            : items.OrderBy(team => team.Name).ThenBy(team => team.Id);
        return new TeamListPage(ordered.Skip(query.Offset).Take(query.Limit).ToArray(), items.Count);
    }
    Task<IReadOnlyList<TeamView>> ListPublicAsync(
        Guid competitionId,
        bool includePending,
        CancellationToken cancellationToken) =>
        ListAsync(competitionId, includePending, cancellationToken);
    Task<TeamReviewStoreResult> SetStatusAsync(Guid competitionId, Guid teamId, TeamRegistrationStatus status, CancellationToken cancellationToken);
    Task<TeamReviewStoreResult> SubmitAsync(
        Guid competitionId,
        Guid teamId,
        Guid userId,
        string? trackInvitationCode,
        CancellationToken cancellationToken) =>
        Task.FromResult(new TeamReviewStoreResult(
            false,
            TeamRegistrationFailure.TeamReviewConflict));
    Task<TeamView?> FindAsync(Guid competitionId, Guid teamId, bool includePending, CancellationToken cancellationToken);
    Task<TeamView?> FindPublicAsync(
        Guid competitionId,
        Guid teamId,
        bool includePending,
        CancellationToken cancellationToken) =>
        FindAsync(competitionId, teamId, includePending, cancellationToken);
    Task<TeamView?> FindForUserAsync(Guid competitionId, Guid userId, bool includePending, CancellationToken cancellationToken) =>
        Task.FromResult<TeamView?>(null);
    Task<bool> CanManageAsync(Guid actorId, Guid competitionId, Guid teamId, CancellationToken cancellationToken);
    Task<TeamUpdateStoreResult> UpdateAsync(UpdateTeamCommand command, CancellationToken cancellationToken);
    Task<TeamRegistrationFailure?> SoftDeleteAsync(Guid competitionId, Guid teamId, Guid actorId, DateTimeOffset deletedAt, CancellationToken cancellationToken);
}

public sealed class CreateTeam(ITeamRegistrationStore store)
{
    public async Task<OperationResult<TeamView, TeamRegistrationFailure>> ExecuteAsync(CreateTeamCommand command, CancellationToken ct = default)
    {
        var name = command.Name.Trim();
        if (name.Length is < 1 or > 128)
            return OperationResult<TeamView, TeamRegistrationFailure>.Failure(TeamRegistrationFailure.InvalidTeamName, "Team name is required and must be at most 128 characters.");
        var policy = await store.GetPolicyAsync(command.CompetitionId, ct);
        if (policy is null || policy.CompetitionDeleted)
            return OperationResult<TeamView, TeamRegistrationFailure>.Failure(TeamRegistrationFailure.CompetitionNotFound, "Competition was not found.");
        if (RegistrationIsClosed(policy))
            return OperationResult<TeamView, TeamRegistrationFailure>.Failure(TeamRegistrationFailure.RegistrationClosed, "Team registration is closed.");
        var tracks = CompetitionTrackConfiguration.FromPersisted(
            policy.Mode,
            policy.Tracks);
        var requestedTrack = tracks.DefaultTrack;
        if (policy.TracksEnabled)
        {
            var requestedKey = CompetitionTrackConfiguration.NormalizeKey(command.TrackKey);
            if (requestedKey is null)
                return OperationResult<TeamView, TeamRegistrationFailure>.Failure(
                    TeamRegistrationFailure.TrackNotFound,
                    "A competition track must be selected when tracks are enabled.");
            requestedTrack = tracks.Find(requestedKey);
            if (requestedTrack is null)
                return OperationResult<TeamView, TeamRegistrationFailure>.Failure(
                    TeamRegistrationFailure.TrackNotFound,
                    "The selected competition track was not found.");
            if (requestedTrack.IsInternal || !requestedTrack.IsPublicSelectable)
                return OperationResult<TeamView, TeamRegistrationFailure>.Failure(
                    TeamRegistrationFailure.TrackNotPublicSelectable,
                    "The selected competition track cannot be selected by participants.");
        }
        var created = await store.TryCreateAsync(command with
        {
            Name = name,
            TrackKey = policy.TracksEnabled ? requestedTrack.Key : command.TrackKey
        },
            policy.PracticeOpen ? TeamRegistrationStatus.Approved : TeamRegistrationStatus.Unregistered, ct);
        return created.Team is not null
            ? OperationResult<TeamView, TeamRegistrationFailure>.Success(created.Team)
            : OperationResult<TeamView, TeamRegistrationFailure>.Failure(created.Failure ?? TeamRegistrationFailure.TeamConflict, "The team could not be created.");
    }

    private static bool RegistrationIsClosed(TeamRegistrationPolicy policy) =>
        !policy.PracticeOpen && (policy.Status is CompetitionStatus.Paused or CompetitionStatus.Finished
        || policy.Status == CompetitionStatus.Running && !policy.AllowWhileRunning);
}

public sealed class ListCompetitionTeams(ITeamRegistrationStore store)
{
    public Task<TeamListPage> ExecutePageAsync(
        TeamListQuery query,
        CancellationToken ct = default) =>
        store.ListPageAsync(query, ct);

    public Task<IReadOnlyList<TeamView>> ExecuteAsync(
        Guid competitionId,
        bool includePending,
        bool includeInternal,
        CancellationToken ct = default) =>
        includeInternal
            ? store.ListAsync(competitionId, includePending, ct)
            : store.ListPublicAsync(competitionId, includePending, ct);
}

public sealed class GetTeam(ITeamRegistrationStore store)
{
    public Task<TeamView?> ExecuteAsync(
        Guid competitionId,
        Guid teamId,
        bool includePending,
        bool includeInternal,
        CancellationToken ct = default) =>
        includeInternal
            ? store.FindAsync(competitionId, teamId, includePending, ct)
            : store.FindPublicAsync(competitionId, teamId, includePending, ct);
}

public sealed class GetMyTeam(ITeamRegistrationStore store)
{
    public Task<TeamView?> ExecuteAsync(Guid competitionId, Guid userId, CancellationToken ct = default) =>
        store.FindForUserAsync(competitionId, userId, includePending: true, ct);
}

public sealed class UpdateTeam(ITeamRegistrationStore store)
{
    public async Task<OperationResult<TeamView, TeamRegistrationFailure>> ExecuteAsync(UpdateTeamCommand command, CancellationToken ct = default)
    {
        var name = command.Name.Trim();
        if (name.Length is < 1 or > 128)
            return OperationResult<TeamView, TeamRegistrationFailure>.Failure(TeamRegistrationFailure.InvalidTeamName, "Team name is required and must be at most 128 characters.");
        var result = await store.UpdateAsync(command with { Name = name }, ct);
        if (result.Team is null)
            return OperationResult<TeamView, TeamRegistrationFailure>.Failure(result.Failure ?? TeamRegistrationFailure.TeamConflict, "Team was not found or can no longer be changed.");
        return OperationResult<TeamView, TeamRegistrationFailure>.Success(result.Team);
    }
}

public static class ParticipantTeamMutationPolicy
{
    public static bool CanChangeOrganization(
        CompetitionStatus status,
        bool allowTeamRegistrationWhileRunning) =>
        status is CompetitionStatus.Visible or CompetitionStatus.Published
        || status == CompetitionStatus.Running && allowTeamRegistrationWhileRunning;

    public static TeamRegistrationStatus RegistrationStatusAfterChange() =>
        TeamRegistrationStatus.Unregistered;
}

public sealed class DeleteTeam(ITeamRegistrationStore store)
{
    public async Task<OperationResult<TeamRegistrationFailure>> ExecuteAsync(Guid competitionId, Guid teamId, Guid actorId, DateTimeOffset now, CancellationToken ct = default)
    {
        var failure = await store.SoftDeleteAsync(competitionId, teamId, actorId, now, ct);
        if (failure is not null) return OperationResult<TeamRegistrationFailure>.Failure(failure.Value, "Team was not deleted.");
        return OperationResult<TeamRegistrationFailure>.Success();
    }
}

public sealed class ReviewTeamRegistration(ITeamRegistrationStore store)
{
    public async Task<OperationResult<TeamRegistrationFailure>> ExecuteAsync(
        Guid competitionId,
        Guid teamId,
        TeamRegistrationStatus status,
        CancellationToken ct = default)
    {
        var policy = await store.GetPolicyAsync(competitionId, ct);
        if (policy is null || policy.CompetitionDeleted)
            return OperationResult<TeamRegistrationFailure>.Failure(TeamRegistrationFailure.CompetitionNotFound, "Competition was not found.");
        if (policy.Status == CompetitionStatus.Finished)
            return OperationResult<TeamRegistrationFailure>.Failure(TeamRegistrationFailure.CompetitionFinished, "Finished competitions are read-only.");
        var result = await store.SetStatusAsync(competitionId, teamId, status, ct);
        return result.Changed
            ? OperationResult<TeamRegistrationFailure>.Success()
            : OperationResult<TeamRegistrationFailure>.Failure(result.Failure ?? TeamRegistrationFailure.TeamReviewConflict, "Team registration was not reviewed.");
    }
}

public sealed class SubmitTeamRegistration(ITeamRegistrationStore store)
{
    public async Task<OperationResult<TeamRegistrationFailure>> ExecuteAsync(
        Guid competitionId,
        Guid teamId,
        Guid userId,
        string? trackInvitationCode,
        CancellationToken ct = default)
    {
        var policy = await store.GetPolicyAsync(competitionId, ct);
        if (policy is null || policy.CompetitionDeleted)
            return OperationResult<TeamRegistrationFailure>.Failure(TeamRegistrationFailure.CompetitionNotFound, "Competition was not found.");
        if (policy.Status is CompetitionStatus.Paused or CompetitionStatus.Finished
            || policy.Status == CompetitionStatus.Running && !policy.AllowWhileRunning)
            return OperationResult<TeamRegistrationFailure>.Failure(TeamRegistrationFailure.RegistrationClosed, "Team registration is closed.");
        var result = await store.SubmitAsync(
            competitionId,
            teamId,
            userId,
            trackInvitationCode,
            ct);
        return result.Changed
            ? OperationResult<TeamRegistrationFailure>.Success()
            : OperationResult<TeamRegistrationFailure>.Failure(
                result.Failure ?? TeamRegistrationFailure.TeamReviewConflict,
                "Team registration was not submitted.");
    }
}
