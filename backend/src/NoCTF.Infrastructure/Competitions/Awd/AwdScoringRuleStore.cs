using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Competitions.Events;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Awd;

public sealed class AwdScoringRuleStore(NoCtfDbContext db, ILeaderboardSnapshotFactory projections,
    PlatformSecretProtector secrets, ICompetitionEventRecorder events, IPostCommitMessagePublisher publisher)
    : IAwdScoringRuleStore
{
    public async Task<AwdScoringRulesView?> ReadAsync(Guid competitionId, Guid challengeId, int? roundNumber, CancellationToken ct)
    {
        var round = await db.AwdRounds.AsNoTracking().Where(item => item.CompetitionId == competitionId
                && (roundNumber == null || item.Number == roundNumber))
            .OrderByDescending(item => item.Number).FirstOrDefaultAsync(ct);
        if (round is null) return null;
        var rules = await db.AwdRoundRules.AsNoTracking().Where(item => item.CompetitionId == competitionId
                && item.CompetitionChallengeId == challengeId && item.RoundId == round.Id)
            .OrderBy(item => item.Revision).ToArrayAsync(ct);
        if (rules.Length == 0) return null;
        var latest = await db.Set<AwdScoringRuleChange>().AsNoTracking().Where(item => item.CompetitionId == competitionId
                && item.CompetitionChallengeId == challengeId && item.FirstRound <= round.Number && item.LastRound >= round.Number)
            .OrderByDescending(item => item.Sequence).FirstOrDefaultAsync(ct);
        var changes = await db.Set<AwdScoringRuleChange>().AsNoTracking().Where(item => item.CompetitionId == competitionId
                && item.CompetitionChallengeId == challengeId)
            .OrderByDescending(item => item.Sequence).Take(50)
            .Join(db.CompetitionEvents.AsNoTracking(), change => change.EventId, entry => entry.Id,
                (change, entry) => new AwdScoringRuleAuditView(change.EventId, entry.ActorUserId, entry.Reason,
                    change.FirstRound, change.LastRound, change.Timing, change.AppliedAt, change.Policy)).ToArrayAsync(ct);
        return new(round.Number, round.Id, rules.Select(rule => new AwdRoundScoringPolicyView(rule.Id, rule.Revision,
            rule.EffectiveAt, AwdScoringPolicy.From(rule), latest?.Policy ?? AwdScoringPolicy.From(rule), latest?.EventId)).ToArray(), changes);
    }

    public async Task<AwdScoringRuleResult> PreviewAsync(AwdScoringRuleCandidate candidate, CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, IsolationLevel.RepeatableRead, ct);
        var failure = await ValidateAsync(candidate, ct);
        if (failure is not null) return new(failure);
        var before = await projections.CreateScoreboardAsync(candidate.CompetitionId, candidate.Now, ct);
        var after = await projections.CreateAwdScoringPreviewAsync(candidate.CompetitionId, candidate.Now,
            new(candidate.CompetitionChallengeId, candidate.FirstRound, candidate.LastRound, candidate.Policy), ct);
        if (before is null || after is null) return new(AwdScoringRuleFailure.NotFound);
        var old = before.Snapshot.Teams.ToDictionary(team => team.TeamId);
        var impact = after.Snapshot.Teams.Select(team => new AwdScoringTeamImpact(team.TeamId, team.TeamName,
            old[team.TeamId].TotalScore, team.TotalScore)).OrderBy(team => team.TeamId).ToArray();
        var rules = await Rules(candidate).Select(rule => rule.Id).ToArrayAsync(ct);
        var flagCount = await db.AwdFlagIssues.CountAsync(issue => rules.Contains(issue.RulesId), ct);
        var stamp = await StampAsync(candidate, ct);
        var at = candidate.Now.UtcTicks.ToString(CultureInfo.InvariantCulture);
        var token = Convert.ToBase64String(secrets.Protect(at + ":" + stamp,
            PlatformSecretPurpose.AwdScoringPreview, candidate.ActorUserId));
        await transaction.CommitAsync(ct);
        return new(Preview: new(token, candidate.Now, candidate.Now.AddMinutes(2), flagCount, rules.Length, impact));
    }

    public async Task<AwdScoringRuleResult> ApplyAsync(AwdScoringRuleCandidate candidate, string token, CancellationToken ct)
    {
        // The digest binds all mutable score inputs. Serializable isolation closes the validation/commit window.
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, IsolationLevel.Serializable, ct);
        var failure = await ValidateAsync(candidate, ct);
        if (failure is not null) return new(failure);
        if (!await TokenMatchesAsync(candidate, token, ct)) return new(AwdScoringRuleFailure.PreviewExpired);
        var sequence = checked((await db.Set<AwdScoringRuleChange>().Where(item => item.CompetitionChallengeId == candidate.CompetitionChallengeId)
            .MaxAsync(item => (int?)item.Sequence, ct) ?? 0) + 1);
        var eventId = await events.RecordAsync(new(candidate.CompetitionId, CompetitionEventKind.AwdScoringRulesChanged,
            CompetitionEventLevel.Warning, CompetitionEventVisibility.Staff, candidate.Now,
            ActorUserId: candidate.ActorUserId, CompetitionChallengeId: candidate.CompetitionChallengeId,
            Reason: candidate.Reason, AwdRuleTiming: candidate.Timing,
            AwdEffectiveFromRound: candidate.FirstRound, AwdEffectiveThroughRound: candidate.LastRound), ct);
        db.Set<AwdScoringRuleChange>().Add(new()
        {
            EventId = eventId, CompetitionId = candidate.CompetitionId,
            CompetitionChallengeId = candidate.CompetitionChallengeId, FirstRound = candidate.FirstRound,
            LastRound = candidate.LastRound, Timing = candidate.Timing, AppliedAt = candidate.Now, Policy = candidate.Policy,
            Sequence = sequence
        });
        // Competing configuration writers fence against this application as well.
        var competition = await db.Competitions.SingleAsync(item => item.Id == candidate.CompetitionId, ct);
        competition.ConcurrencyStamp = Guid.NewGuid();
        competition.UpdatedAt = candidate.Now;
        await publisher.PublishAsync(new ProjectLeaderboard(candidate.CompetitionId));
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException || TransactionFailureClassifier.IsRetryable(exception))
        {
            publisher.DiscardPendingMessages();
            db.ChangeTracker.Clear();
            return new(AwdScoringRuleFailure.Conflict);
        }
        await transaction.FlushMessagesAsync(publisher);
        return new(EventId: eventId);
    }

    private async Task<AwdScoringRuleFailure?> ValidateAsync(AwdScoringRuleCandidate candidate, CancellationToken ct)
    {
        if (!await db.CompetitionChallenges.AnyAsync(challenge => challenge.Id == candidate.CompetitionChallengeId
                && challenge.CompetitionId == candidate.CompetitionId && challenge.Mode == GameMode.Awd
                && challenge.DeletedAt == null, ct)) return AwdScoringRuleFailure.NotFound;
        var latest = await db.AwdRounds.AsNoTracking().Where(round => round.CompetitionId == candidate.CompetitionId)
            .OrderByDescending(round => round.Number).FirstOrDefaultAsync(ct);
        if (latest is null || candidate.LastRound > latest.Number) return AwdScoringRuleFailure.InvalidRequest;
        if (candidate.Timing == AwdRuleChangeTiming.Immediately)
        {
            var status = await CompetitionStateReader.ReadAsync(db, candidate.CompetitionId, ct);
            if (candidate.FirstRound != latest.Number || candidate.LastRound != latest.Number
                || status is not (CompetitionStatus.Running or CompetitionStatus.Paused)
                || status == CompetitionStatus.Running && candidate.Now >= latest.EndsAt)
                return AwdScoringRuleFailure.InvalidRequest;
        }
        var rules = await Rules(candidate).AsNoTracking().ToArrayAsync(ct);
        return rules.Length == 0 || rules.Any(rule => AwdEffectiveRules.FromSnapshot(candidate.Policy.Apply(rule)).Validate().Count > 0)
            ? AwdScoringRuleFailure.InvalidRequest : null;
    }

    private IQueryable<AwdRoundRules> Rules(AwdScoringRuleCandidate candidate) => db.AwdRoundRules
        .Where(rule => rule.CompetitionId == candidate.CompetitionId && rule.CompetitionChallengeId == candidate.CompetitionChallengeId
            && db.AwdRounds.Any(round => round.Id == rule.RoundId && round.Number >= candidate.FirstRound
                && round.Number <= candidate.LastRound));

    private async Task<bool> TokenMatchesAsync(AwdScoringRuleCandidate candidate, string token, CancellationToken ct)
    {
        try
        {
            var parts = secrets.Unprotect(Convert.FromBase64String(token), PlatformSecretPurpose.AwdScoringPreview,
                candidate.ActorUserId).Split(':', 2);
            if (parts.Length != 2 || !long.TryParse(parts[0], CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks) return false;
            var at = new DateTimeOffset(ticks, TimeSpan.Zero);
            if (at > candidate.Now || candidate.Now - at >= TimeSpan.FromMinutes(2)) return false;
            var expected = await StampAsync(candidate, ct);
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(parts[1]), Encoding.UTF8.GetBytes(expected));
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException) { return false; }
    }

    private async Task<string> StampAsync(AwdScoringRuleCandidate candidate, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Append<T>(T value) => hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(value));
        Append(new { candidate.CompetitionId, candidate.CompetitionChallengeId, candidate.ActorUserId,
            candidate.FirstRound, candidate.LastRound, candidate.Timing, candidate.Policy, candidate.Reason });
        Append(await db.Competitions.AsNoTracking().Where(item => item.Id == candidate.CompetitionId)
            .Select(item => item.ConcurrencyStamp).SingleAsync(ct));
        Append(await db.CompetitionEvents.CountAsync(item => item.CompetitionId == candidate.CompetitionId, ct));
        var sources = db.GameplayFacts.AsNoTracking().Where(item => item.CompetitionId == candidate.CompetitionId)
            .Select(item => new { item.Id, item.ConcurrencyStamp })
            .Concat(db.CompetitionChallenges.AsNoTracking().Where(item => item.CompetitionId == candidate.CompetitionId)
                .Select(item => new { item.Id, item.ConcurrencyStamp }))
            .Concat(db.Teams.AsNoTracking().Where(item => item.CompetitionId == candidate.CompetitionId)
                .Select(item => new { item.Id, item.ConcurrencyStamp }))
            .Concat(db.AwdRounds.AsNoTracking().Where(item => item.CompetitionId == candidate.CompetitionId)
                .Select(item => new { item.Id, item.ConcurrencyStamp }))
            .Concat(db.ChallengeFlags.AsNoTracking().Where(item => item.AwdIssue != null && item.AwdIssue.CompetitionId == candidate.CompetitionId)
                .Select(item => new { item.Id, item.ConcurrencyStamp }));
        await foreach (var source in sources.OrderBy(item => item.Id).AsAsyncEnumerable().WithCancellation(ct)) Append(source);
        // Issuance metadata can change independently of the protected flag value. No secret is hashed or exported.
        Append(await db.AwdFlagIssues.AsNoTracking().Where(item => item.CompetitionId == candidate.CompetitionId)
            .OrderBy(item => item.FlagId).Select(item => new { item.FlagId, item.RulesId, item.State, item.Obligation,
                item.IssuedAt, item.ExpiryRound, item.ActiveExpiryTicks, item.HistoricalExpiryRound,
                item.HistoricalActiveExpiryTicks }).ToArrayAsync(ct));
        Append(await db.AwdRoundRules.AsNoTracking().Where(item => item.CompetitionId == candidate.CompetitionId)
            .OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync(ct));
        Append(await db.Set<AwdScoringRuleChange>().AsNoTracking().Where(item => item.CompetitionId == candidate.CompetitionId)
            .OrderBy(item => item.EventId).Select(item => item.EventId).ToArrayAsync(ct));
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
