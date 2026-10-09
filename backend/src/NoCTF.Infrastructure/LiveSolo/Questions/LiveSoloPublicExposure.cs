using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Questions;

/// <summary>Publication eligibility is sufficient exposure; a viewer request is not required.</summary>
internal static class LiveSoloPublicExposure
{
    private sealed record Candidate(Guid CompetitionId, Guid CanonicalChallengeId, Guid MatchId, DateTimeOffset PublicAt);
    public static async Task RememberAsync(NoCtfDbContext db, Guid competitionId, DateTimeOffset now, CancellationToken ct)
    {
        var candidates = await db.LiveSoloProgramSegments.AsNoTracking().Where(x => x.PublicAt <= now)
            .Join(db.LiveSoloMediaSessions, s => s.MediaSessionId, m => m.Id, (s, m) => new { Segment = s, m.MatchId })
            .Join(db.LiveSoloMatches, x => x.MatchId, m => m.Id, (x, m) => new { x.Segment, Match = m })
            .Where(x => x.Match.CompetitionId == competitionId && x.Match.StartedAt != null)
            .Join(db.Competitions, x => x.Match.CompetitionId, c => c.Id, (x, c) => new { x.Segment, x.Match, Competition = c })
            .Where(x => x.Competition.Mode == GameMode.LiveSolo && x.Competition.AccessMode == CompetitionAccessMode.Public
                && (x.Competition.Status == CompetitionStatus.Running || x.Competition.Status == CompetitionStatus.Paused || x.Competition.Status == CompetitionStatus.Finished)
                && db.Set<LiveSoloCompetitionModeConfiguration>().Any(c => c.CompetitionId == competitionId && c.Enabled))
            .Join(db.Set<LiveSoloProgramFrameQuestion>(), x => x.Segment.FrameId, q => q.FrameId, (x, q) => new { x.Segment, x.Match, Question = q })
            .Where(x => x.Question.HasStaticAnswer && x.Question.CanonicalChallengeId != Guid.Empty)
            .Select(x => new Candidate(x.Match.CompetitionId,
                x.Question.CanonicalChallengeId,
                x.Match.Id, x.Segment.PublicAt)).ToArrayAsync(ct);
        var canonicalIds = candidates.Select(x => x.CanonicalChallengeId).Distinct().ToArray();
        var recorded = await db.LiveSoloQuestionExposures.Where(x => x.CompetitionId == competitionId && canonicalIds.Contains(x.CanonicalChallengeId))
            .Select(x => x.CanonicalChallengeId).ToArrayAsync(ct);
        foreach (var candidate in candidates.GroupBy(x => x.CanonicalChallengeId).Select(x => x.OrderBy(y => y.PublicAt).ThenBy(y => y.MatchId).First()))
            if (!recorded.Contains(candidate.CanonicalChallengeId)) db.LiveSoloQuestionExposures.Add(new() {
                CompetitionId = competitionId, CanonicalChallengeId = candidate.CanonicalChallengeId, MatchId = candidate.MatchId, PublicAt = candidate.PublicAt });
        // Expired but not-yet-pruned rows are intentionally remembered too: a Worker outage cannot erase a completed publication window.
        if (db.ChangeTracker.Entries<LiveSoloQuestionExposure>().Any(x => x.State == EntityState.Added)) await db.SaveChangesAsync(ct);
    }
}
