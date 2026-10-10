using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Timing;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Competitions.Progression;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges.Timing;

public sealed class ChallengeTimingPreviewReader(NoCtfDbContext db, ILeaderboardSnapshotFactory projections, PlatformSecretProtector secrets)
    : IChallengeTimingPreviewReader
{
    public async Task<ChallengeTimingFailure?> ValidateCandidateAsync(ChangeChallengeTiming candidate, CancellationToken ct)
    {
        if (!candidate.Timing.IsValid) return ChallengeTimingFailure.InvalidOrder;
        var challenge = await db.CompetitionChallenges.AsNoTracking().IgnoreAutoIncludes()
            .Where(x => x.Id == candidate.CompetitionChallengeId && x.CompetitionId == candidate.CompetitionId && x.DeletedAt == null)
            .Select(x => new { x.Mode, x.IsPublished }).SingleOrDefaultAsync(ct);
        return challenge is null ? ChallengeTimingFailure.NotFound
            : challenge.Mode == NoCTF.Domain.Competitions.GameMode.LiveSolo ? ChallengeTimingFailure.UnsupportedMode
            : challenge.IsPublished && candidate.Timing.AutoOpenAt > candidate.Now ? ChallengeTimingFailure.PublishedFutureOpening
            : null;
    }
    public async Task<ChallengeTimingPreview?> PreviewAsync(ChangeChallengeTiming candidate, Guid actorId, CancellationToken ct)
    {
        if (!candidate.Timing.IsValid) return null;
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, IsolationLevel.RepeatableRead, ct);
        var challenge = await db.CompetitionChallenges.AsNoTracking().IgnoreAutoIncludes().SingleOrDefaultAsync(x => x.Id == candidate.CompetitionChallengeId
            && x.CompetitionId == candidate.CompetitionId, ct);
        if (challenge is null || challenge.Mode == NoCTF.Domain.Competitions.GameMode.LiveSolo) return null;
        var before = await projections.CreateScoreboardAsync(candidate.CompetitionId, candidate.Now, ct);
        var after = await projections.CreateTimingPreviewAsync(candidate.CompetitionId, candidate.Now, new(challenge.Id, candidate.Timing), ct);
        if (before is null || after is null) return null;
        var beforeRows = before.Snapshot.Teams.ToDictionary(x => x.TeamId);
        var current = ChallengeTiming.From(challenge);
        var competition = await db.Competitions.AsNoTracking().Where(x => x.Id == candidate.CompetitionId)
            .Select(x => new { x.StartAt, x.EndAt }).SingleAsync(ct);
        var official = await NoCTF.Infrastructure.Competitions.Lifecycle.CompetitionOfficialWindowReader.ReadAsync(
            db, candidate.CompetitionId, competition.StartAt, competition.EndAt, ct);
        var beforeOpen = current.AutoOpenAt; var beforeClose = current.SubmissionDeadlineAt; var beforeScore = current.ScoringEndsAt;
        var afterOpen = candidate.Timing.AutoOpenAt; var afterClose = candidate.Timing.SubmissionDeadlineAt; var afterScore = candidate.Timing.ScoringEndsAt;
        var facts = db.GameplayFacts.AsNoTracking().Where(x => x.CompetitionChallengeId == challenge.Id
            && (x.Kind == GameplayFactKind.FlagAttempt || x.Kind == GameplayFactKind.BreakAttempt || x.Kind == GameplayFactKind.FixAttempt))
            .Select(x => new { x.TeamId, x.Result,
                BeforeValid = (beforeOpen == null || x.OccurredAt >= beforeOpen)
                    && (challenge.Mode == NoCTF.Domain.Competitions.GameMode.Ctf && x.Kind == GameplayFactKind.FlagAttempt && x.OccurredAt >= official.EndAt
                        || beforeClose == null || x.OccurredAt < beforeClose),
                AfterValid = (afterOpen == null || x.OccurredAt >= afterOpen)
                    && (challenge.Mode == NoCTF.Domain.Competitions.GameMode.Ctf && x.Kind == GameplayFactKind.FlagAttempt && x.OccurredAt >= official.EndAt
                        || afterClose == null || x.OccurredAt < afterClose),
                BeforeScoring = !(challenge.Mode == NoCTF.Domain.Competitions.GameMode.Ctf && x.Kind == GameplayFactKind.FlagAttempt && x.OccurredAt >= official.EndAt)
                    && (beforeScore == null || x.OccurredAt < beforeScore),
                AfterScoring = !(challenge.Mode == NoCTF.Domain.Competitions.GameMode.Ctf && x.Kind == GameplayFactKind.FlagAttempt && x.OccurredAt >= official.EndAt)
                    && (afterScore == null || x.OccurredAt < afterScore) });
        var completions = await facts.GroupBy(x => x.TeamId).Select(group => new { Team = group.Key,
            Before = group.Count(x => x.BeforeValid && (x.Result == GameplayFactResult.Correct || x.Result == GameplayFactResult.RightButDue)),
            After = group.Count(x => x.AfterValid && (x.Result == GameplayFactResult.Correct || x.Result == GameplayFactResult.RightButDue)) }).ToArrayAsync(ct);
        int Completion(Guid team, bool proposed) => completions.FirstOrDefault(x => x.Team == team) is { } row ? (proposed ? row.After : row.Before) : 0;
        var graph = await db.CompetitionProgressions.AsNoTracking().Include(x => x.Nodes).Include(x => x.Edges).AsSplitQuery()
            .SingleOrDefaultAsync(x => x.CompetitionId == candidate.CompetitionId, ct);
        var existingCompletion = graph is { Enabled: true } ? await db.GameplayFacts.AsNoTracking()
            .Where(x => x.CompetitionId == candidate.CompetitionId && x.TeamId != null && x.TimeEligibility == GameplayFactTimeEligibility.Valid
                && (x.Kind == GameplayFactKind.FlagAttempt || x.Kind == GameplayFactKind.FixAttempt)
                && (x.Result == GameplayFactResult.Correct || x.Result == GameplayFactResult.RightButDue))
            .Select(x => new { Team = x.TeamId!.Value, x.CompetitionChallengeId }).Distinct().ToArrayAsync(ct) : [];
        int Progression(Guid team)
        {
            if (graph is not { Enabled: true }) return 0;
            var oldSet = existingCompletion.Where(x => x.Team == team).Select(x => x.CompetitionChallengeId).ToHashSet();
            if (Completion(team, false) > 0) oldSet.Add(challenge.Id); else oldSet.Remove(challenge.Id);
            var newSet = new HashSet<Guid>(oldSet);
            if (Completion(team, true) > 0) newSet.Add(challenge.Id); else newSet.Remove(challenge.Id);
            var oldGraph = ProgressionGraphRules.Evaluate(graph.Nodes, graph.Edges, oldSet);
            var newGraph = ProgressionGraphRules.Evaluate(graph.Nodes, graph.Edges, newSet);
            return oldGraph.Nodes.Count(x => x.Value != newGraph.Nodes[x.Key]);
        }
        static string Blood(ScoreboardProjection projection, Guid team) => string.Join(',', projection.EntryAllocations
            .Where(x => x.TeamId == team && x.Entry.Award is not null)
            .Select(x => $"{x.Entry.Id:N}:{x.Entry.Award}").Order());
        var teams = after.Snapshot.Teams.Select(row =>
        {
            var old = beforeRows[row.TeamId];
            return new ChallengeTimingImpact(row.TeamId, row.TeamName, old.TotalScore, row.TotalScore,
                Blood(before, row.TeamId) != Blood(after, row.TeamId), Completion(row.TeamId, false), Completion(row.TeamId, true), Progression(row.TeamId));
        }).Where(x => x.ScoreBefore != x.ScoreAfter || x.BloodChanged || x.CompletionBefore != x.CompletionAfter || x.ProgressionNodesChanged > 0).ToArray();
        var affected = await facts.CountAsync(x => x.BeforeValid != x.AfterValid
            || (x.BeforeValid && x.BeforeScoring) != (x.AfterValid && x.AfterScoring), ct);
        var expires = candidate.Now.AddMinutes(2);
        var stamp = await StampAsync(candidate, actorId, ct);
        var payload = expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) + ":" + stamp;
        var token = Convert.ToBase64String(secrets.Protect(payload, PlatformSecretPurpose.ChallengeTimingPreview, actorId));
        await transaction.CommitAsync(ct);
        return new(token, expires, affected, teams);
    }

    public async Task<bool> ValidateAsync(ChangeChallengeTiming candidate, Guid actorId, string? token, CancellationToken ct)
    {
        var current = await db.CompetitionChallenges.AsNoTracking().IgnoreAutoIncludes().SingleOrDefaultAsync(x => x.Id == candidate.CompetitionChallengeId
            && x.CompetitionId == candidate.CompetitionId, ct);
        if (current is null) return true;
        if (ChallengeTiming.From(current) == candidate.Timing || !await db.GameplayFacts.AnyAsync(x => x.CompetitionChallengeId == current.Id, ct)) return true;
        if (string.IsNullOrEmpty(token)) return false;
        try
        {
            var payload = secrets.Unprotect(Convert.FromBase64String(token), PlatformSecretPurpose.ChallengeTimingPreview, actorId).Split(':', 2);
            if (payload.Length != 2 || !long.TryParse(payload[0], CultureInfo.InvariantCulture, out var expires)
                || candidate.Now.ToUnixTimeSeconds() >= expires) return false;
            var expected = await StampAsync(candidate, actorId, ct);
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(payload[1]), Encoding.UTF8.GetBytes(expected));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException) { return false; }
    }

    private async Task<string> StampAsync(ChangeChallengeTiming candidate, Guid actor, CancellationToken ct)
    {
        var challenge = await db.CompetitionChallenges.AsNoTracking().Where(x => x.Id == candidate.CompetitionChallengeId)
            .Select(x => x.ConcurrencyStamp).SingleAsync(ct);
        var competition = await db.Competitions.AsNoTracking().Where(x => x.Id == candidate.CompetitionId).Select(x => x.ConcurrencyStamp).SingleAsync(ct);
        var events = await db.CompetitionEvents.CountAsync(x => x.CompetitionId == candidate.CompetitionId, ct);
        var text = string.Join('|', actor.ToString("N"), challenge.ToString("N"), competition.ToString("N"),
            candidate.Timing.AutoOpenAt?.ToUniversalTime().ToString("O"), candidate.Timing.ScoringEndsAt?.ToUniversalTime().ToString("O"),
            candidate.Timing.SubmissionDeadlineAt?.ToUniversalTime().ToString("O"), events.ToString(CultureInfo.InvariantCulture));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(text));
        // Bind every mutable projection source, including changes whose timestamp
        // is equal to another row's timestamp. No Flag/content data enters this digest.
        var sources = db.GameplayFacts.AsNoTracking().Where(x => x.CompetitionId == candidate.CompetitionId)
            .Select(x => new { x.Id, x.ConcurrencyStamp })
            .Concat(db.CompetitionChallenges.AsNoTracking().Where(x => x.CompetitionId == candidate.CompetitionId)
                .Select(x => new { x.Id, x.ConcurrencyStamp }))
            .Concat(db.Teams.AsNoTracking().Where(x => x.CompetitionId == candidate.CompetitionId)
                .Select(x => new { x.Id, x.ConcurrencyStamp }));
        await foreach (var source in sources.OrderBy(x => x.Id).AsAsyncEnumerable().WithCancellation(ct))
        {
            hash.AppendData(source.Id.ToByteArray());
            hash.AppendData(source.ConcurrencyStamp.ToByteArray());
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
