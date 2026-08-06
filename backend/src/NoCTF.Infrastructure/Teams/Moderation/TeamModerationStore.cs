using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Observability;

namespace NoCTF.Infrastructure.Teams.Moderation;

public sealed class TeamModerationStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
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
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var status = await CompetitionStateReader.ReadAsync(db, command.CompetitionId, cancellationToken);
        if (status is null)
            return new(TeamModerationFailure.CompetitionNotFound);
        if (status == CompetitionStatus.Finished)
            return new(TeamModerationFailure.CompetitionFinished);
        var team = await db.Teams.SingleOrDefaultAsync(
            item => item.CompetitionId == command.CompetitionId && item.Id == command.TeamId,
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
                    && @event.TeamId == team.Id
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
        await db.SaveChangesAsync(cancellationToken);
        await LeaderboardRevision.IncrementAsync(db, command.CompetitionId, cancellationToken);
        await outbox.PublishAsync(new InvalidateLeaderboard(command.CompetitionId));
        if (command.Ban)
        {
            await outbox.PublishAsync(new TeamBanned(
                command.CompetitionId,
                team.Id,
                team.Name,
                command.OccurredAt));
        }
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
        return new();
    }
}
