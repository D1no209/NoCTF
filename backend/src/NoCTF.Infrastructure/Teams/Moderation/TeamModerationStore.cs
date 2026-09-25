using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Observability;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Teams.Moderation;

public sealed class TeamModerationStore(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    ICompetitionEventRecorder? eventRecorder = null) : ITeamModerationStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken cancellationToken) =>
        db.Competitions.AsNoTracking().Where(item => item.Id == competitionId && item.DeletedAt == null)
            .Select(item => (CompetitionStatus?)item.Status).SingleOrDefaultAsync(cancellationToken);

    public async Task<TeamModerationStoreResult> ApplyAsync(
        TeamModerationCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, cancellationToken);
        var competition = await CompetitionTeamMutationCriticalSection.AcquireAsync(db, command.CompetitionId, cancellationToken);
        if (competition is null)
            return new(TeamModerationFailure.CompetitionNotFound);
        var team = await db.Teams.SingleOrDefaultAsync(
            item => item.CompetitionId == command.CompetitionId && item.Id == command.TeamId && item.DeletedAt == null,
            cancellationToken);
        if (team is null)
            return new(TeamModerationFailure.TeamNotFound);
        if (team.IsBanned == command.Ban)
            return new();

        var ban = command.Ban
            ? null
            : await db.CompetitionEvents.AsNoTracking()
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
        var safeReason = command.Ban
            ? PlatformLogRedactor.Redact(command.Reason!, [])
            : null;

        team.IsBanned = command.Ban;
        team.BannedAt = command.Ban ? command.OccurredAt : null;
        team.BannedById = command.Ban ? command.ActorId : null;
        team.BanReason = safeReason;
        await events.RecordAsync(new(
            command.CompetitionId,
            command.Ban
                ? CompetitionEventKind.TeamBanned
                : CompetitionEventKind.TeamUnbanned,
            command.Ban
                ? CompetitionEventLevel.Warning
                : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            command.OccurredAt,
            ActorUserId: command.ActorId,
            TeamId: team.Id,
            ParentEventId: ban?.Id,
            Reason: safeReason), cancellationToken);
        if (command.Ban)
        {
            // A post-contest ban must also revoke direct access to active practice environments.
            var practiceRuntimes = await db.RuntimeInstances.Where(runtime =>
                runtime.CompetitionId == command.CompetitionId && runtime.TeamId == team.Id
                && runtime.Purpose == RuntimePurpose.Practice
                && (runtime.State == RuntimeState.Queued || runtime.State == RuntimeState.Provisioning || runtime.State == RuntimeState.Running))
                .ToListAsync(cancellationToken);
            foreach (var runtime in practiceRuntimes)
            {
                if (runtime.State == RuntimeState.Queued && runtime.RunnerId is null && runtime.ProviderReceipt is null)
                {
                    runtime.State = RuntimeState.Stopped;
                    runtime.StoppedAt = command.OccurredAt;
                }
                else
                {
                    runtime.State = RuntimeState.Stopping;
                    await outbox.PublishAsync(new StopRuntime(runtime.Id));
                }
            }
            await outbox.PublishAsync(new TeamBanned(
                command.CompetitionId,
                team.Id,
                team.Name,
                command.OccurredAt,
                command.AnnouncePublicly
                    ? TeamBanAnnouncementKind.RuleViolation
                    : null));
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await transaction.FlushMessagesAsync(outbox);
        return new();
    }
}
