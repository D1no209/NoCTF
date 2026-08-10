using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Teams.Appeals;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Teams.Appeals;

public sealed class TeamBanAppealStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    ICompetitionEventRecorder events) : ITeamBanAppealStore
{
    private static readonly CompetitionEventKind[] ResolutionKinds =
    [
        CompetitionEventKind.TeamBanAppealUpheld,
        CompetitionEventKind.TeamBanAppealAccepted
    ];

    public async Task<TeamBanCaseView?> GetForMemberAsync(
        Guid competitionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var team = await db.Teams.AsNoTracking()
            .Where(candidate =>
                candidate.CompetitionId == competitionId
                && candidate.MemberIds.Contains(userId))
            .Select(candidate => new TeamFact(
                candidate.Id,
                candidate.CompetitionId,
                candidate.Name,
                candidate.CaptainId,
                candidate.IsBanned))
            .SingleOrDefaultAsync(cancellationToken);
        if (team is null)
            return null;

        var ban = await LatestBan(team.Id, competitionId)
            .SingleOrDefaultAsync(cancellationToken);
        return ban is null
            ? null
            : await BuildCaseAsync(ban, team, userId, cancellationToken);
    }

    public async Task<IReadOnlyList<TeamBanCaseView>?> ListForStaffAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var exists = await db.Competitions.AsNoTracking().AnyAsync(
            competition => competition.Id == competitionId
                && competition.DeletedAt == null,
            cancellationToken);
        if (!exists)
            return null;

        var appeals = await db.CompetitionEvents.AsNoTracking()
            .Where(@event =>
                @event.CompetitionId == competitionId
                && @event.Kind == CompetitionEventKind.TeamBanAppealSubmitted
                && @event.ParentEventId != null
                && (@event.SubjectType == EntityReferenceKind.Team
                    || @event.RelatedType == EntityReferenceKind.Team))
            .OrderByDescending(@event => @event.OccurredAt)
            .ThenByDescending(@event => @event.Id)
            .ToArrayAsync(cancellationToken);
        if (appeals.Length == 0)
            return [];

        var banIds = appeals.Select(appeal => appeal.ParentEventId!.Value).ToArray();
        var bans = await db.CompetitionEvents.AsNoTracking()
            .Where(@event =>
                banIds.Contains(@event.Id)
                && @event.Kind == CompetitionEventKind.TeamBanned)
            .ToDictionaryAsync(@event => @event.Id, cancellationToken);
        var teamIds = appeals.Select(appeal => appeal.TeamId!.Value).Distinct().ToArray();
        var teams = await db.Teams.IgnoreQueryFilters().AsNoTracking()
            .Where(team => teamIds.Contains(team.Id))
            .Select(team => new TeamFact(
                team.Id,
                team.CompetitionId,
                team.Name,
                team.CaptainId,
                team.IsBanned))
            .ToDictionaryAsync(team => team.Id, cancellationToken);
        var latestBanIds = await LoadLatestBanIdsAsync(
            competitionId,
            teamIds,
            cancellationToken);
        var resolutions = await LoadResolutionsAsync(
            appeals.Select(appeal => appeal.Id).ToArray(),
            cancellationToken);
        var names = await LoadUserNamesAsync(
            appeals,
            resolutions.Values,
            cancellationToken);

        return appeals
            .Where(appeal =>
                bans.ContainsKey(appeal.ParentEventId!.Value)
                && teams.ContainsKey(appeal.TeamId!.Value))
            .Select(appeal =>
            {
                var ban = bans[appeal.ParentEventId!.Value];
                var team = teams[appeal.TeamId!.Value];
                resolutions.TryGetValue(appeal.Id, out var resolution);
                var isCurrent = team.IsBanned
                    && latestBanIds.TryGetValue(team.Id, out var latestBanId)
                    && latestBanId == ban.Id;
                return MapCase(
                    ban,
                    team,
                    isCurrent,
                    canAppeal: false,
                    appeal,
                    resolution,
                    names);
            })
            .ToArray();
    }

    public async Task<TeamBanAppealMutationResult> SubmitAsync(
        SubmitTeamBanAppealCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        var competitionStatus = await CompetitionStateReader.ReadAsync(
            db,
            command.CompetitionId,
            cancellationToken);
        if (competitionStatus is null)
            return new(Failure: TeamBanAppealFailure.CompetitionNotFound);

        var team = await db.Teams.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item =>
                item.CompetitionId == command.CompetitionId
                && item.MemberIds.Contains(command.ActorUserId)
                && item.DeletedAt == null,
                cancellationToken);
        if (team is null)
            return new(Failure: TeamBanAppealFailure.TeamNotFound);
        if (team.CaptainId != command.ActorUserId)
            return new(Failure: TeamBanAppealFailure.CaptainRequired);
        if (!team.IsBanned)
            return new(Failure: TeamBanAppealFailure.BanNotFound);

        var ban = await LatestBan(team.Id, command.CompetitionId)
            .SingleOrDefaultAsync(cancellationToken);
        if (ban is null)
            return new(Failure: TeamBanAppealFailure.BanNotFound);
        var alreadySubmitted = await db.CompetitionEvents.AsNoTracking().AnyAsync(
            @event =>
                @event.CompetitionId == command.CompetitionId
                && @event.Kind == CompetitionEventKind.TeamBanAppealSubmitted
                && @event.ParentEventId == ban.Id,
            cancellationToken);
        if (alreadySubmitted)
            return new(Failure: TeamBanAppealFailure.AppealAlreadySubmitted);

        await events.RecordAsync(new(
            command.CompetitionId,
            CompetitionEventKind.TeamBanAppealSubmitted,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            command.SubmittedAt,
            ActorUserId: command.ActorUserId,
            TeamId: team.Id,
            ParentEventId: ban.Id,
            Reason: command.Statement), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return new();
    }

    public async Task<TeamBanAppealMutationResult> ResolveAsync(
        ResolveTeamBanAppealCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var competitionStatus = await CompetitionStateReader.ReadAsync(
            db,
            command.CompetitionId,
            cancellationToken);
        if (competitionStatus is null)
            return new(Failure: TeamBanAppealFailure.CompetitionNotFound);

        var appeal = await db.CompetitionEvents.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == command.AppealId,
                cancellationToken);
        if (appeal is null
            || appeal.CompetitionId != command.CompetitionId
            || appeal.Kind != CompetitionEventKind.TeamBanAppealSubmitted
            || appeal.TeamId is null
            || appeal.ParentEventId is null)
        {
            return new(Failure: TeamBanAppealFailure.AppealNotFound);
        }

        var resolved = await db.CompetitionEvents.AsNoTracking().AnyAsync(
            @event =>
                @event.CompetitionId == command.CompetitionId
                && @event.ParentEventId == appeal.Id
                && ResolutionKinds.Contains(@event.Kind),
            cancellationToken);
        if (resolved)
            return new(Failure: TeamBanAppealFailure.AppealAlreadyResolved);

        var ban = await db.CompetitionEvents.AsNoTracking().SingleOrDefaultAsync(
            @event =>
                @event.Id == appeal.ParentEventId.Value
                && @event.CompetitionId == command.CompetitionId
                && @event.Kind == CompetitionEventKind.TeamBanned
                && (@event.SubjectType == EntityReferenceKind.Team
                    && @event.SubjectId == appeal.TeamId
                    || @event.RelatedType == EntityReferenceKind.Team
                    && @event.RelatedId == appeal.TeamId),
            cancellationToken);
        if (ban is null)
            return new(Failure: TeamBanAppealFailure.BanNotFound);

        var team = await db.Teams.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item =>
                item.Id == appeal.TeamId.Value
                && item.CompetitionId == command.CompetitionId
                && item.DeletedAt == null,
                cancellationToken);
        if (team is null)
            return new(Failure: TeamBanAppealFailure.TeamNotFound);
        if (!await IsCurrentBanAsync(team, ban.Id, cancellationToken))
            return new(Failure: TeamBanAppealFailure.BanNoLongerCurrent);

        var eventKind = command.Resolution switch
        {
            TeamBanAppealResolution.Uphold =>
                CompetitionEventKind.TeamBanAppealUpheld,
            TeamBanAppealResolution.Accept =>
                CompetitionEventKind.TeamBanAppealAccepted,
            _ => throw new ArgumentOutOfRangeException(
                nameof(command),
                command.Resolution,
                null)
        };
        await events.RecordAsync(new(
            command.CompetitionId,
            eventKind,
            command.Resolution == TeamBanAppealResolution.Accept
                ? CompetitionEventLevel.Warning
                : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            command.ResolvedAt,
            ActorUserId: command.ActorUserId,
            TeamId: team.Id,
            ParentEventId: appeal.Id,
            Reason: command.Reason), cancellationToken);

        if (command.Resolution == TeamBanAppealResolution.Accept)
        {
            await ApplyCorrectionAsync(
                team,
                ban,
                command.ActorUserId,
                command.Reason,
                command.ResolvedAt,
                cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return new();
    }

    public async Task<TeamBanAppealMutationResult> CorrectAsync(
        CorrectTeamBanCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var competitionStatus = await CompetitionStateReader.ReadAsync(
            db,
            command.CompetitionId,
            cancellationToken);
        if (competitionStatus is null)
            return new(Failure: TeamBanAppealFailure.CompetitionNotFound);

        var team = await db.Teams.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item =>
                item.Id == command.TeamId
                && item.CompetitionId == command.CompetitionId
                && item.DeletedAt == null,
                cancellationToken);
        if (team is null)
            return new(Failure: TeamBanAppealFailure.TeamNotFound);
        if (!team.IsBanned)
            return new(Failure: TeamBanAppealFailure.BanNoLongerCurrent);

        var ban = await LatestBan(team.Id, command.CompetitionId)
            .SingleOrDefaultAsync(cancellationToken);
        if (ban is null)
            return new(Failure: TeamBanAppealFailure.BanNotFound);

        var pendingAppeal = await db.CompetitionEvents.AsNoTracking()
            .Where(@event =>
                @event.CompetitionId == command.CompetitionId
                && @event.Kind == CompetitionEventKind.TeamBanAppealSubmitted
                && @event.ParentEventId == ban.Id
                && !db.CompetitionEvents.Any(resolution =>
                    resolution.ParentEventId == @event.Id
                    && ResolutionKinds.Contains(resolution.Kind)))
            .OrderBy(@event => @event.OccurredAt)
            .ThenBy(@event => @event.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (pendingAppeal is not null)
        {
            await events.RecordAsync(new(
                command.CompetitionId,
                CompetitionEventKind.TeamBanAppealAccepted,
                CompetitionEventLevel.Warning,
                CompetitionEventVisibility.Staff,
                command.CorrectedAt,
                ActorUserId: command.ActorUserId,
                TeamId: team.Id,
                ParentEventId: pendingAppeal.Id,
                Reason: command.Reason), cancellationToken);
        }

        await ApplyCorrectionAsync(
            team,
            ban,
            command.ActorUserId,
            command.Reason,
            command.CorrectedAt,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return new();
    }

    private async Task ApplyCorrectionAsync(
        Team team,
        CompetitionEvent ban,
        Guid actorUserId,
        string reason,
        DateTimeOffset correctedAt,
        CancellationToken cancellationToken)
    {
        team.IsBanned = false;
        team.BannedAt = null;
        team.BannedById = null;
        team.BanReason = null;
        await events.RecordAsync(new(
            team.CompetitionId,
            CompetitionEventKind.TeamUnbanned,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            correctedAt,
            ActorUserId: actorUserId,
            TeamId: team.Id,
            ParentEventId: ban.Id,
            Reason: reason), cancellationToken);
        await events.RecordAsync(new(
            team.CompetitionId,
            CompetitionEventKind.TeamBanCorrectionPublished,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Public,
            correctedAt,
            TeamId: team.Id,
            ParentEventId: ban.Id), cancellationToken);
        await LeaderboardDirty.MarkAsync(
            db,
            team.CompetitionId,
            cancellationToken);
        await outbox.PublishAsync(new TeamBanCorrected(
            team.CompetitionId,
            team.Id,
            team.Name,
            ban.Id,
            correctedAt));
    }

    private IQueryable<CompetitionEvent> LatestBan(Guid teamId, Guid competitionId) =>
        db.CompetitionEvents.AsNoTracking()
            .Where(@event =>
                @event.CompetitionId == competitionId
                && (@event.SubjectType == EntityReferenceKind.Team
                    && @event.SubjectId == teamId
                    || @event.RelatedType == EntityReferenceKind.Team
                    && @event.RelatedId == teamId)
                && @event.Kind == CompetitionEventKind.TeamBanned)
            .OrderByDescending(@event => @event.OccurredAt)
            .ThenByDescending(@event => @event.Id)
            .Take(1);

    private async Task<bool> IsCurrentBanAsync(
        Team team,
        Guid banEventId,
        CancellationToken cancellationToken)
    {
        if (!team.IsBanned)
            return false;
        var currentBanId = await LatestBan(team.Id, team.CompetitionId)
            .Select(@event => (Guid?)@event.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return currentBanId == banEventId;
    }

    private async Task<TeamBanCaseView> BuildCaseAsync(
        CompetitionEvent ban,
        TeamFact team,
        Guid viewerUserId,
        CancellationToken cancellationToken)
    {
        var appeal = await db.CompetitionEvents.AsNoTracking()
            .Where(@event =>
                @event.CompetitionId == ban.CompetitionId
                && @event.Kind == CompetitionEventKind.TeamBanAppealSubmitted
                && @event.ParentEventId == ban.Id)
            .OrderBy(@event => @event.OccurredAt)
            .ThenBy(@event => @event.Id)
            .FirstOrDefaultAsync(cancellationToken);
        CompetitionEvent? resolution = null;
        if (appeal is not null)
        {
            resolution = await db.CompetitionEvents.AsNoTracking()
                .Where(@event =>
                    @event.CompetitionId == ban.CompetitionId
                    && @event.ParentEventId == appeal.Id
                    && ResolutionKinds.Contains(@event.Kind))
                .OrderByDescending(@event => @event.OccurredAt)
                .ThenByDescending(@event => @event.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        var names = await LoadUserNamesAsync(
            appeal is null ? [] : [appeal],
            resolution is null ? [] : [resolution],
            cancellationToken);
        return MapCase(
            ban,
            team,
            team.IsBanned,
            team.IsBanned && team.CaptainId == viewerUserId && appeal is null,
            appeal,
            resolution,
            names);
    }

    private async Task<IReadOnlyDictionary<Guid, CompetitionEvent>> LoadResolutionsAsync(
        Guid[] appealIds,
        CancellationToken cancellationToken)
    {
        var resolutions = await db.CompetitionEvents.AsNoTracking()
            .Where(@event =>
                @event.ParentEventId != null
                && appealIds.Contains(@event.ParentEventId.Value)
                && ResolutionKinds.Contains(@event.Kind))
            .OrderByDescending(@event => @event.OccurredAt)
            .ThenByDescending(@event => @event.Id)
            .ToArrayAsync(cancellationToken);
        return resolutions
            .GroupBy(@event => @event.ParentEventId!.Value)
            .ToDictionary(group => group.Key, group => group.First());
    }

    private async Task<IReadOnlyDictionary<Guid, Guid>> LoadLatestBanIdsAsync(
        Guid competitionId,
        Guid[] teamIds,
        CancellationToken cancellationToken)
    {
        var bans = await db.CompetitionEvents.AsNoTracking()
            .Where(@event =>
                @event.CompetitionId == competitionId
                && (@event.SubjectType == EntityReferenceKind.Team
                    && teamIds.Contains(@event.SubjectId)
                    || @event.RelatedType == EntityReferenceKind.Team
                    && @event.RelatedId != null
                    && teamIds.Contains(@event.RelatedId.Value))
                && @event.Kind == CompetitionEventKind.TeamBanned)
            .OrderByDescending(@event => @event.OccurredAt)
            .ThenByDescending(@event => @event.Id)
            .Select(@event => new
            {
                TeamId = @event.SubjectType == EntityReferenceKind.Team
                    ? @event.SubjectId
                    : @event.RelatedId!.Value,
                @event.Id
            })
            .ToArrayAsync(cancellationToken);
        return bans
            .GroupBy(ban => ban.TeamId)
            .ToDictionary(group => group.Key, group => group.First().Id);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadUserNamesAsync(
        IEnumerable<CompetitionEvent> appeals,
        IEnumerable<CompetitionEvent> resolutions,
        CancellationToken cancellationToken)
    {
        var userIds = appeals.Concat(resolutions)
            .Where(@event => @event.ActorUserId != null)
            .Select(@event => @event.ActorUserId!.Value)
            .Distinct()
            .ToArray();
        return await db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.UserName, cancellationToken);
    }

    private static TeamBanCaseView MapCase(
        CompetitionEvent ban,
        TeamFact team,
        bool isCurrent,
        bool canAppeal,
        CompetitionEvent? appeal,
        CompetitionEvent? resolution,
        IReadOnlyDictionary<Guid, string> names) =>
        new(
            ban.Id,
            ban.CompetitionId,
            team.Id,
            team.Name,
            ban.GameplayFactId is null
                ? TeamBanSource.ManualModeration
                : TeamBanSource.CheatIncident,
            ban.OccurredAt,
            isCurrent,
            canAppeal,
            appeal is null
                ? null
                : new TeamBanAppealView(
                    appeal.Id,
                    appeal.ActorUserId!.Value,
                    ResolveName(appeal.ActorUserId.Value, names),
                    appeal.Reason ?? string.Empty,
                    appeal.OccurredAt,
                    StatusOf(resolution),
                    resolution?.ActorUserId,
                    resolution?.ActorUserId is Guid resolverId
                        ? ResolveName(resolverId, names)
                        : null,
                    resolution?.Reason,
                    resolution?.OccurredAt));

    private static TeamBanAppealStatus StatusOf(CompetitionEvent? resolution) =>
        resolution?.Kind switch
        {
            null => TeamBanAppealStatus.Submitted,
            CompetitionEventKind.TeamBanAppealUpheld => TeamBanAppealStatus.Upheld,
            CompetitionEventKind.TeamBanAppealAccepted => TeamBanAppealStatus.Accepted,
            _ => throw new InvalidOperationException("Unsupported team ban appeal resolution event.")
        };

    private static string ResolveName(
        Guid userId,
        IReadOnlyDictionary<Guid, string> names) =>
        names.TryGetValue(userId, out var name) ? name : "anonymous";

    private sealed record TeamFact(
        Guid Id,
        Guid CompetitionId,
        string Name,
        Guid CaptainId,
        bool IsBanned);
}
