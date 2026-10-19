using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Worker;

public sealed class LeaderboardMessageHandler(ILeaderboardCache leaderboard, NoCtfDbContext db,
    IPostCommitMessagePublisher? messages = null)
{
    public async Task Handle(
        ProjectLeaderboard message,
        CancellationToken cancellationToken)
    {
        if (message.Mode == LeaderboardRefreshMode.Force
            || message.Mode == LeaderboardRefreshMode.IfMissing && await leaderboard.NeedsRefreshAsync(message.CompetitionId, cancellationToken))
            await leaderboard.RefreshAsync(message.CompetitionId, cancellationToken);
        else if (message.Mode != LeaderboardRefreshMode.IfMissing)
            throw new ArgumentOutOfRangeException(nameof(message));
        var projection = await leaderboard.GetScoreboardAsync(message.CompetitionId, cancellationToken);
        if (projection is null)
        {
            if (message.Mode == LeaderboardRefreshMode.IfMissing
                && await db.Competitions.AnyAsync(item => item.Id == message.CompetitionId
                    && item.Mode != NoCTF.Domain.Competitions.GameMode.LiveSolo, cancellationToken))
                throw new TimeoutException("The invalidated leaderboard is not published yet.");
            return;
        }
        if (messages is not null)
        {
            await messages.PublishAsync(new ReconcileBloodAnnouncements(message.CompetitionId, projection.Snapshot.Version));
            await messages.FlushCommittedMessagesAsync();
        }
    }
}

public sealed class BloodAnnouncementMessageHandler(
    NoCTF.Infrastructure.Scoring.Leaderboard.BloodAnnouncementReconciler reconciler,
    ILeaderboardCache leaderboard)
{
    public async Task Handle(ReconcileBloodAnnouncements message, CancellationToken ct)
    {
        var projection = await leaderboard.GetScoreboardAsync(message.CompetitionId, ct);
        if (projection is not null
            && (message.ProjectionVersion == 0 || projection.Snapshot.Version >= message.ProjectionVersion))
            await reconciler.ReconcileAsync(projection, ct);
    }
}
