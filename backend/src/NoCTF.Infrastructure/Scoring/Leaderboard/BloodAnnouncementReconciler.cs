using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Blood;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed class BloodAnnouncementReconciler(NoCtfDbContext db, ICompetitionEventRecorder events,
    IPostCommitMessagePublisher messages, TimeProvider clock)
{
    public async Task ReconcileAsync(ScoreboardProjection projection, CancellationToken ct)
    {
        if (projection.Schema.Mode != GameMode.Ctf) return;
        var id = projection.Snapshot.CompetitionId;
        var awards = projection.BloodAwards.ToDictionary(item => (item.CompetitionChallengeId, Position: (BloodPosition)item.Rank));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var settings = await db.Competitions.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new { item.FrozenStartAt, item.HiddenStartAt }).SingleAsync(ct);
        var now = clock.GetUtcNow();
        if (!CompetitionLeaderboardVisibilityPolicy.CanAnnounceBlood(settings.FrozenStartAt, settings.HiddenStartAt, now)) return;
        var published = (await db.CompetitionChallenges.AsNoTracking()
            .Where(item => item.CompetitionId == id && item.IsPublished)
            .Select(item => item.Id).ToArrayAsync(ct)).ToHashSet();
        var eligibleTeams = (await db.Teams.AsNoTracking()
            .Where(item => item.CompetitionId == id && !item.IsBanned
                && item.RegistrationStatus == NoCTF.Domain.Teams.TeamRegistrationStatus.Approved)
            .Select(item => item.Id).ToArrayAsync(ct)).ToHashSet();
        var existing = await db.BloodAnnouncements.Where(item => item.CompetitionId == id)
            .ToDictionaryAsync(item => (item.CompetitionChallengeId, item.Position), ct);
        var challenges = awards.Keys.Select(item => item.CompetitionChallengeId)
            .Concat(existing.Keys.Select(item => item.CompetitionChallengeId)).Where(published.Contains).Distinct();
        foreach (var challengeId in challenges)
        foreach (var position in Enum.GetValues<BloodPosition>())
        {
            var candidate = awards.GetValueOrDefault((challengeId, position));
            if (candidate is not null && !eligibleTeams.Contains(candidate.TeamId)) candidate = null;
            var row = existing.GetValueOrDefault((challengeId, position));
            if (row is not null && row.PublishedVersion >= projection.Snapshot.Version) continue;
            if (row is null && candidate is null) continue;
            if (row is null)
            {
                row = new BloodAnnouncement { Id = Guid.NewGuid(), CompetitionId = id,
                    CompetitionChallengeId = challengeId, Position = position };
                db.BloodAnnouncements.Add(row);
            }
            row.PublishedVersion = projection.Snapshot.Version;
            if (row.GameplayFactId == candidate?.GameplayFactId && row.TeamId == candidate?.TeamId) continue;
            var hadAward = row.GameplayFactId is not null;
            var kind = Kind(position, candidate is null, hadAward || row.LastEventId is not null);
            var eventId = await events.RecordAsync(new(id, kind, CompetitionEventLevel.Information,
                CompetitionEventVisibility.Public, now, TeamId: candidate?.TeamId ?? row.TeamId,
                CompetitionChallengeId: challengeId, GameplayFactId: candidate?.GameplayFactId ?? row.GameplayFactId,
                ParentEventId: row.LastEventId, LeaderboardVisibility: CompetitionLeaderboardVisibility.Normal,
                FrozenStartAt: settings.FrozenStartAt), ct);
            row.GameplayFactId = candidate?.GameplayFactId;
            row.TeamId = candidate?.TeamId;
            row.LastEventId = eventId;
            if (!hadAward && candidate is not null && kind is CompetitionEventKind.FirstBloodAwarded
                    or CompetitionEventKind.SecondBloodAwarded or CompetitionEventKind.ThirdBloodAwarded)
            {
                var title = projection.ChallengeCatalog.Challenges.Single(item => item.CompetitionChallengeId == challengeId).Title;
                var team = projection.Snapshot.Teams.Single(item => item.TeamId == candidate.TeamId);
                await messages.PublishAsync(new BloodAwarded(id, challengeId, title, candidate.Rank,
                    candidate.TeamId, team.TeamName, candidate.OccurredAt));
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await transaction.DisposeAsync();
        await messages.FlushCommittedMessagesAsync();
    }

    private static CompetitionEventKind Kind(BloodPosition rank, bool withdrawn, bool corrected) => (rank, withdrawn, corrected) switch
    {
        (BloodPosition.First, true, _) => CompetitionEventKind.FirstBloodWithdrawn,
        (BloodPosition.Second, true, _) => CompetitionEventKind.SecondBloodWithdrawn,
        (BloodPosition.Third, true, _) => CompetitionEventKind.ThirdBloodWithdrawn,
        (BloodPosition.First, false, true) => CompetitionEventKind.FirstBloodCorrected,
        (BloodPosition.Second, false, true) => CompetitionEventKind.SecondBloodCorrected,
        (BloodPosition.Third, false, true) => CompetitionEventKind.ThirdBloodCorrected,
        (BloodPosition.First, false, false) => CompetitionEventKind.FirstBloodAwarded,
        (BloodPosition.Second, false, false) => CompetitionEventKind.SecondBloodAwarded,
        (BloodPosition.Third, false, false) => CompetitionEventKind.ThirdBloodAwarded,
        _ => throw new ArgumentOutOfRangeException(nameof(rank))
    };
}
