using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Submissions.CheatIncidents;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Submissions;
using NoCTF.Infrastructure.Observability;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Submissions.CheatIncidents;

public sealed class CheatIncidentStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    ICompetitionEventRecorder events) : ICheatIncidentStore
{
    private static readonly CompetitionEventKind[] ResolutionKinds =
    [
        CompetitionEventKind.CheatIncidentConfirmed,
        CompetitionEventKind.CheatIncidentDismissed,
        CompetitionEventKind.CheatIncidentSuperseded,
        CompetitionEventKind.CheatIncidentCorrected
    ];

    public async Task<CheatIncidentPage?> ListAsync(
        CheatIncidentQuery query,
        CancellationToken cancellationToken)
    {
        var exists = await db.Competitions.AsNoTracking().AnyAsync(
            competition => competition.Id == query.CompetitionId
                && competition.DeletedAt == null,
            cancellationToken);
        if (!exists)
            return null;

        var incidents = IncidentScoringEvents(query.CompetitionId)
            .Where(item => item.OccurredAt >= query.From && item.OccurredAt <= query.To);
        if (query.SourceTeamId is Guid sourceTeamId)
            incidents = incidents.Where(item => item.TeamId == sourceTeamId);
        if (query.OwnerTeamId is Guid ownerTeamId)
            incidents = incidents.Where(item => item.VictimTeamId == ownerTeamId);
        if (query.SubmittedByUserId is Guid userId)
        {
            incidents = incidents.Where(item => db.Submissions.Any(submission =>
                submission.Id == item.SubmissionId
                && submission.SubmittedByUserId == userId));
        }
        if (query.CompetitionChallengeId is Guid challengeId)
            incidents = incidents.Where(item => item.CompetitionChallengeId == challengeId);
        if (query.Status is CheatIncidentStatus status)
            incidents = FilterStatus(incidents, status);
        if (query.BeforeDetectedAt is DateTimeOffset before
            && query.BeforeScoringEventId is Guid beforeId)
        {
            incidents = incidents.Where(item =>
                item.OccurredAt < before
                || item.OccurredAt == before && item.Id.CompareTo(beforeId) < 0);
        }

        var page = incidents
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id)
            .Take(query.Limit);
        var facts = (await ProjectFacts(page).ToArrayAsync(cancellationToken))
            .OrderByDescending(item => item.DetectedAt)
            .ThenByDescending(item => item.ScoringEventId)
            .ToArray();
        var resolutions = await LoadResolutionsAsync(
            facts.Select(item => item.ScoringEventId).ToArray(),
            cancellationToken);
        var resolverNames = await LoadResolverNamesAsync(resolutions, cancellationToken);
        var pendingCount = await IncidentScoringEvents(query.CompetitionId)
            .CountAsync(item => !db.CompetitionEvents.Any(@event =>
                    (@event.SubjectType == EntityReferenceKind.ScoringEvent
                        && @event.SubjectId == item.Id
                        || @event.RelatedType == EntityReferenceKind.ScoringEvent
                        && @event.RelatedId == item.Id)
                    && ResolutionKinds.Contains(@event.Kind)),
                cancellationToken);
        return new(
            facts.Select(item => MapListItem(
                item,
                Resolve(item.ScoringEventId, resolutions),
                resolverNames)).ToArray(),
            pendingCount);
    }

    public async Task<CheatIncidentDetail?> GetDetailAsync(
        Guid competitionId,
        Guid scoringEventId,
        Guid actorUserId,
        DateTimeOffset accessedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var fact = await ProjectFacts(IncidentScoringEvents(competitionId)
                .Where(item => item.Id == scoringEventId))
            .SingleOrDefaultAsync(cancellationToken);
        if (fact is null)
            return null;
        var resolutions = await LoadResolutionsAsync([scoringEventId], cancellationToken);
        var resolution = Resolve(scoringEventId, resolutions);
        var resolverNames = await LoadResolverNamesAsync(resolutions, cancellationToken);
        await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.ProtectedSubmissionFlagAccessed,
            CompetitionEventLevel.Warning,
            CompetitionEventVisibility.Staff,
            accessedAt,
            ActorUserId: actorUserId,
            TeamId: fact.SourceTeamId,
            CompetitionChallengeId: fact.CompetitionChallengeId,
            SubmissionId: fact.SubmissionId,
            ScoringEventId: scoringEventId,
            SubmissionKind: fact.SubmissionKind,
            Reason: "Anti-cheat incident detail viewed."), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return MapDetail(fact, resolution, resolverNames);
    }

    public Task<CheatIncidentResolutionResult> DismissAsync(
        CheatIncidentResolutionCommand command,
        CancellationToken cancellationToken) =>
        ResolveAsync(command, ResolutionOperation.Dismiss, cancellationToken);

    public Task<CheatIncidentResolutionResult> ConfirmAndBanAsync(
        CheatIncidentResolutionCommand command,
        CancellationToken cancellationToken) =>
        ResolveAsync(command, ResolutionOperation.ConfirmAndBan, cancellationToken);

    public Task<CheatIncidentResolutionResult> CorrectAndUnbanAsync(
        CheatIncidentResolutionCommand command,
        CancellationToken cancellationToken) =>
        ResolveAsync(command, ResolutionOperation.CorrectAndUnban, cancellationToken);

    private async Task<CheatIncidentResolutionResult> ResolveAsync(
        CheatIncidentResolutionCommand command,
        ResolutionOperation operation,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var competitionStatus = await CompetitionStateReader.ReadAsync(
            db,
            command.CompetitionId,
            cancellationToken);
        if (competitionStatus is null)
            return new(CheatIncidentResolutionFailure.NotFound);
        if (competitionStatus == CompetitionStatus.Finished
            && operation != ResolutionOperation.CorrectAndUnban)
            return new(CheatIncidentResolutionFailure.CompetitionFinished);

        var scoringEvent = await db.ScoringEvents
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item => item.Id == command.ScoringEventId,
                cancellationToken);
        if (scoringEvent is null
            || scoringEvent.CompetitionId != command.CompetitionId
            || scoringEvent.FailureCode != ScoringFailureCode.ForeignTeamFlagDetected
            || scoringEvent.SubmissionId is null
            || scoringEvent.TeamId is null
            || scoringEvent.VictimTeamId is null)
        {
            return new(CheatIncidentResolutionFailure.NotFound);
        }

        var resolution = await db.CompetitionEvents.AsNoTracking()
            .Where(@event =>
                (@event.SubjectType == EntityReferenceKind.ScoringEvent
                    && @event.SubjectId == scoringEvent.Id
                    || @event.RelatedType == EntityReferenceKind.ScoringEvent
                    && @event.RelatedId == scoringEvent.Id)
                && ResolutionKinds.Contains(@event.Kind))
            .OrderByDescending(@event => @event.OccurredAt)
            .ThenByDescending(@event => @event.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var currentStatus = StatusOf(resolution);
        if (operation is ResolutionOperation.Dismiss or ResolutionOperation.ConfirmAndBan
            && currentStatus != CheatIncidentStatus.Pending)
        {
            return new(CheatIncidentResolutionFailure.NotPending);
        }
        if (operation == ResolutionOperation.CorrectAndUnban
            && currentStatus != CheatIncidentStatus.Confirmed)
        {
            return new(CheatIncidentResolutionFailure.NotConfirmed);
        }

        var team = await db.Teams.SingleAsync(
            item => item.Id == scoringEvent.TeamId.Value
                && item.CompetitionId == command.CompetitionId,
            cancellationToken);
        if (operation == ResolutionOperation.ConfirmAndBan && team.IsBanned)
            return new(CheatIncidentResolutionFailure.AlreadyBanned);
        if (operation == ResolutionOperation.CorrectAndUnban && !team.IsBanned)
            return new(CheatIncidentResolutionFailure.TeamNotBanned);

        CompetitionEvent? ban = null;
        if (operation == ResolutionOperation.CorrectAndUnban)
        {
            ban = await db.CompetitionEvents.AsNoTracking()
                .Where(@event =>
                    @event.CompetitionId == command.CompetitionId
                    && (@event.SubjectType == EntityReferenceKind.Team
                        && @event.SubjectId == team.Id
                        || @event.RelatedType == EntityReferenceKind.Team
                        && @event.RelatedId == team.Id)
                    && @event.Kind == CompetitionEventKind.TeamBanned)
                .OrderByDescending(@event => @event.OccurredAt)
                .ThenByDescending(@event => @event.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (ban?.ScoringEventId != scoringEvent.Id)
                return new(CheatIncidentResolutionFailure.TeamNotBanned);
        }

        var submittedFlag = await db.Submissions.AsNoTracking()
            .Where(item => item.Id == scoringEvent.SubmissionId.Value)
            .Select(item => item.SubmittedFlag)
            .SingleAsync(cancellationToken);
        var safeReason = SanitizeReason(command.Reason, submittedFlag);
        var eventKind = operation switch
        {
            ResolutionOperation.Dismiss => CompetitionEventKind.CheatIncidentDismissed,
            ResolutionOperation.ConfirmAndBan => CompetitionEventKind.CheatIncidentConfirmed,
            ResolutionOperation.CorrectAndUnban => CompetitionEventKind.CheatIncidentCorrected,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
        await events.RecordAsync(new(
            command.CompetitionId,
            eventKind,
            operation == ResolutionOperation.Dismiss
                ? CompetitionEventLevel.Information
                : CompetitionEventLevel.Warning,
            CompetitionEventVisibility.Staff,
            command.OccurredAt,
            ActorUserId: command.ActorUserId,
            TeamId: team.Id,
            CompetitionChallengeId: scoringEvent.CompetitionChallengeId,
            SubmissionId: scoringEvent.SubmissionId,
            ScoringEventId: scoringEvent.Id,
            ScoringEventKind: scoringEvent.Kind,
            ScoringResult: scoringEvent.Result,
            Reason: safeReason), cancellationToken);

        if (operation == ResolutionOperation.ConfirmAndBan)
        {
            team.IsBanned = true;
            team.BannedAt = command.OccurredAt;
            team.BannedById = command.ActorUserId;
            team.BanReason = safeReason;
            await events.RecordAsync(new(
                command.CompetitionId,
                CompetitionEventKind.TeamBanned,
                CompetitionEventLevel.Warning,
                CompetitionEventVisibility.Staff,
                command.OccurredAt,
                ActorUserId: command.ActorUserId,
                TeamId: team.Id,
                SubmissionId: scoringEvent.SubmissionId,
                ScoringEventId: scoringEvent.Id), cancellationToken);
            await LeaderboardRevision.IncrementAsync(db, command.CompetitionId, cancellationToken);
            await outbox.PublishAsync(new InvalidateLeaderboard(command.CompetitionId));
            await outbox.PublishAsync(new TeamBanned(
                command.CompetitionId,
                team.Id,
                team.Name,
                command.OccurredAt,
                TeamBanAnnouncementKind.ConfirmedCheating));
        }
        else if (operation == ResolutionOperation.CorrectAndUnban)
        {
            var pendingAppeal = await db.CompetitionEvents.AsNoTracking()
                .Where(@event =>
                    @event.CompetitionId == command.CompetitionId
                    && @event.Kind == CompetitionEventKind.TeamBanAppealSubmitted
                    && @event.ParentEventId == ban!.Id
                    && !db.CompetitionEvents.Any(appealResolution =>
                        appealResolution.ParentEventId == @event.Id
                        && (appealResolution.Kind == CompetitionEventKind.TeamBanAppealUpheld
                            || appealResolution.Kind == CompetitionEventKind.TeamBanAppealAccepted)))
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
                    command.OccurredAt,
                    ActorUserId: command.ActorUserId,
                    TeamId: team.Id,
                    ParentEventId: pendingAppeal.Id,
                    Reason: safeReason), cancellationToken);
            }
            team.IsBanned = false;
            team.BannedAt = null;
            team.BannedById = null;
            team.BanReason = null;
            await events.RecordAsync(new(
                command.CompetitionId,
                CompetitionEventKind.TeamUnbanned,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Staff,
                command.OccurredAt,
                ActorUserId: command.ActorUserId,
                TeamId: team.Id,
                SubmissionId: scoringEvent.SubmissionId,
                ScoringEventId: scoringEvent.Id,
                ParentEventId: ban!.Id,
                Reason: safeReason), cancellationToken);
            await events.RecordAsync(new(
                command.CompetitionId,
                CompetitionEventKind.TeamBanCorrectionPublished,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Public,
                command.OccurredAt,
                TeamId: team.Id,
                ParentEventId: ban.Id), cancellationToken);
            await LeaderboardRevision.IncrementAsync(db, command.CompetitionId, cancellationToken);
            await outbox.PublishAsync(new InvalidateLeaderboard(command.CompetitionId));
            await outbox.PublishAsync(new TeamBanCorrected(
                command.CompetitionId,
                team.Id,
                team.Name,
                ban.Id,
                command.OccurredAt));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return new();
    }

    private IQueryable<ScoringEvent> IncidentScoringEvents(Guid competitionId) =>
        db.ScoringEvents.IgnoreQueryFilters()
            .Where(scoringEvent =>
                scoringEvent.CompetitionId == competitionId
                && scoringEvent.Kind == ScoringEventKind.SubmissionEvaluation
                && scoringEvent.Result == ScoringResult.Rejected
                && scoringEvent.FailureCode == ScoringFailureCode.ForeignTeamFlagDetected
                && scoringEvent.SubmissionId != null
                && scoringEvent.TeamId != null
                && scoringEvent.VictimTeamId != null);

    private IQueryable<IncidentFact> ProjectFacts(IQueryable<ScoringEvent> scoringEvents) =>
        scoringEvents
            .Join(
                db.Submissions.AsNoTracking(),
                scoringEvent => scoringEvent.SubmissionId,
                submission => (Guid?)submission.Id,
                (scoringEvent, submission) => new { scoringEvent, submission })
            .Join(
                db.Teams.IgnoreQueryFilters().AsNoTracking(),
                item => item.scoringEvent.TeamId,
                team => (Guid?)team.Id,
                (item, sourceTeam) => new { item.scoringEvent, item.submission, sourceTeam })
            .Join(
                db.Teams.IgnoreQueryFilters().AsNoTracking(),
                item => item.scoringEvent.VictimTeamId,
                team => (Guid?)team.Id,
                (item, ownerTeam) => new
                {
                    item.scoringEvent,
                    item.submission,
                    item.sourceTeam,
                    ownerTeam
                })
            .Join(
                db.Users.AsNoTracking(),
                item => item.submission.SubmittedByUserId,
                user => user.Id,
                (item, user) => new
                {
                    item.scoringEvent,
                    item.submission,
                    item.sourceTeam,
                    item.ownerTeam,
                    user
                })
            .Join(
                db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking(),
                item => item.submission.CompetitionChallengeId,
                challenge => challenge.Id,
                (item, competitionChallenge) => new
                {
                    item.scoringEvent,
                    item.submission,
                    item.sourceTeam,
                    item.ownerTeam,
                    item.user,
                    competitionChallenge
                })
            .Join(
                db.Challenges.IgnoreQueryFilters().AsNoTracking(),
                item => item.competitionChallenge.ChallengeId,
                challenge => challenge.Id,
                (item, challenge) => new IncidentFact(
                    item.scoringEvent.Id,
                    item.submission.Id,
                    item.submission.SubmittedFlag!,
                    item.sourceTeam.Id,
                    item.sourceTeam.Name,
                    item.ownerTeam.Id,
                    item.ownerTeam.Name,
                    item.user.Id,
                    item.user.UserName,
                    item.competitionChallenge.Id,
                    challenge.Title,
                    item.submission.Kind,
                    item.scoringEvent.Result,
                    item.scoringEvent.FailureCode!.Value,
                    item.submission.ReceivedAt,
                    item.scoringEvent.OccurredAt,
                    item.sourceTeam.IsBanned,
                    item.sourceTeam.BannedAt,
                    item.sourceTeam.BannedById,
                    item.sourceTeam.BanReason));

    private IQueryable<ScoringEvent> FilterStatus(
        IQueryable<ScoringEvent> incidents,
        CheatIncidentStatus status)
    {
        if (status == CheatIncidentStatus.Pending)
        {
            return incidents.Where(item => !db.CompetitionEvents.Any(@event =>
                (@event.SubjectType == EntityReferenceKind.ScoringEvent
                    && @event.SubjectId == item.Id
                    || @event.RelatedType == EntityReferenceKind.ScoringEvent
                    && @event.RelatedId == item.Id)
                && ResolutionKinds.Contains(@event.Kind)));
        }
        var expectedKind = KindOf(status);
        return incidents.Where(item => db.CompetitionEvents
            .Where(@event =>
                (@event.SubjectType == EntityReferenceKind.ScoringEvent
                    && @event.SubjectId == item.Id
                    || @event.RelatedType == EntityReferenceKind.ScoringEvent
                    && @event.RelatedId == item.Id)
                && ResolutionKinds.Contains(@event.Kind))
            .OrderByDescending(@event => @event.OccurredAt)
            .ThenByDescending(@event => @event.Id)
            .Select(@event => (CompetitionEventKind?)@event.Kind)
            .FirstOrDefault() == expectedKind);
    }

    private async Task<IReadOnlyDictionary<Guid, CompetitionEvent>> LoadResolutionsAsync(
        Guid[] scoringEventIds,
        CancellationToken cancellationToken)
    {
        if (scoringEventIds.Length == 0)
            return new Dictionary<Guid, CompetitionEvent>();
        var resolutions = await db.CompetitionEvents.AsNoTracking()
            .Where(@event =>
                (@event.SubjectType == EntityReferenceKind.ScoringEvent
                    && scoringEventIds.Contains(@event.SubjectId)
                    || @event.RelatedType == EntityReferenceKind.ScoringEvent
                    && @event.RelatedId != null
                    && scoringEventIds.Contains(@event.RelatedId.Value))
                && ResolutionKinds.Contains(@event.Kind))
            .OrderByDescending(@event => @event.OccurredAt)
            .ThenByDescending(@event => @event.Id)
            .ToArrayAsync(cancellationToken);
        return resolutions
            .GroupBy(@event => @event.ScoringEventId!.Value)
            .ToDictionary(group => group.Key, group => group.First());
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadResolverNamesAsync(
        IReadOnlyDictionary<Guid, CompetitionEvent> resolutions,
        CancellationToken cancellationToken)
    {
        var userIds = resolutions.Values
            .Where(@event => @event.ActorUserId != null)
            .Select(@event => @event.ActorUserId!.Value)
            .Distinct()
            .ToArray();
        return await db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(
                user => user.Id,
                user => user.UserName,
                cancellationToken);
    }

    private static CheatIncidentListItem MapListItem(
        IncidentFact fact,
        CompetitionEvent? resolution,
        IReadOnlyDictionary<Guid, string> resolverNames) =>
        new(
            fact.ScoringEventId,
            fact.SubmissionId,
            fact.SourceTeamId,
            fact.SourceTeamName,
            fact.OwnerTeamId,
            fact.OwnerTeamName,
            fact.SubmittedByUserId,
            fact.SubmittedByUserName,
            fact.CompetitionChallengeId,
            fact.ChallengeTitle,
            fact.SubmissionKind,
            fact.Result,
            fact.FailureCode,
            StatusOf(resolution),
            resolution?.ActorUserId,
            Resolve(resolution?.ActorUserId, resolverNames),
            resolution?.OccurredAt,
            resolution?.Reason,
            fact.SubmittedAt,
            fact.DetectedAt,
            fact.SourceTeamIsBanned);

    private static CheatIncidentDetail MapDetail(
        IncidentFact fact,
        CompetitionEvent? resolution,
        IReadOnlyDictionary<Guid, string> resolverNames) =>
        new(
            fact.ScoringEventId,
            fact.SubmissionId,
            fact.SubmittedFlag,
            fact.SourceTeamId,
            fact.SourceTeamName,
            fact.OwnerTeamId,
            fact.OwnerTeamName,
            fact.SubmittedByUserId,
            fact.SubmittedByUserName,
            fact.CompetitionChallengeId,
            fact.ChallengeTitle,
            fact.SubmissionKind,
            fact.Result,
            fact.FailureCode,
            StatusOf(resolution),
            resolution?.ActorUserId,
            Resolve(resolution?.ActorUserId, resolverNames),
            resolution?.OccurredAt,
            resolution?.Reason,
            fact.SubmittedAt,
            fact.DetectedAt,
            fact.SourceTeamIsBanned,
            fact.SourceTeamBannedAt,
            fact.SourceTeamBannedByUserId,
            fact.SourceTeamBanReason);

    private static CompetitionEvent? Resolve(
        Guid scoringEventId,
        IReadOnlyDictionary<Guid, CompetitionEvent> resolutions) =>
        resolutions.TryGetValue(scoringEventId, out var resolution)
            ? resolution
            : null;

    private static string? Resolve(
        Guid? userId,
        IReadOnlyDictionary<Guid, string> names) =>
        userId is Guid id && names.TryGetValue(id, out var name)
            ? name
            : null;

    private static CheatIncidentStatus StatusOf(CompetitionEvent? resolution) =>
        resolution?.Kind switch
        {
            null => CheatIncidentStatus.Pending,
            CompetitionEventKind.CheatIncidentConfirmed => CheatIncidentStatus.Confirmed,
            CompetitionEventKind.CheatIncidentDismissed => CheatIncidentStatus.Dismissed,
            CompetitionEventKind.CheatIncidentSuperseded => CheatIncidentStatus.Superseded,
            CompetitionEventKind.CheatIncidentCorrected => CheatIncidentStatus.Corrected,
            _ => throw new InvalidOperationException("Unsupported cheat incident resolution event.")
        };

    private static CompetitionEventKind KindOf(CheatIncidentStatus status) => status switch
    {
        CheatIncidentStatus.Confirmed => CompetitionEventKind.CheatIncidentConfirmed,
        CheatIncidentStatus.Dismissed => CompetitionEventKind.CheatIncidentDismissed,
        CheatIncidentStatus.Superseded => CompetitionEventKind.CheatIncidentSuperseded,
        CheatIncidentStatus.Corrected => CompetitionEventKind.CheatIncidentCorrected,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static string SanitizeReason(string reason, string? submittedFlag)
    {
        var sanitized = string.IsNullOrEmpty(submittedFlag)
            ? reason
            : reason.Replace(submittedFlag, "[REDACTED]", StringComparison.Ordinal);
        return PlatformLogRedactor.Redact(sanitized, []);
    }

    private enum ResolutionOperation : short
    {
        Dismiss,
        ConfirmAndBan,
        CorrectAndUnban
    }

    private sealed record IncidentFact(
        Guid ScoringEventId,
        Guid SubmissionId,
        string SubmittedFlag,
        Guid SourceTeamId,
        string SourceTeamName,
        Guid OwnerTeamId,
        string OwnerTeamName,
        Guid SubmittedByUserId,
        string SubmittedByUserName,
        Guid CompetitionChallengeId,
        string ChallengeTitle,
        SubmissionKind SubmissionKind,
        ScoringResult Result,
        ScoringFailureCode FailureCode,
        DateTimeOffset SubmittedAt,
        DateTimeOffset DetectedAt,
        bool SourceTeamIsBanned,
        DateTimeOffset? SourceTeamBannedAt,
        Guid? SourceTeamBannedByUserId,
        string? SourceTeamBanReason);
}
