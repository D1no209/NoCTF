using Marten;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Evaluation;
using NoCTF.Application.Scoring.Events;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Scoring.Ports;
using NoCTF.Infrastructure.Eventing.ProjectionCheckpoints;
using NoCTF.Infrastructure.Eventing.Projections;
using NoCTF.Infrastructure.Persistence;
using EF = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions;

namespace NoCTF.Infrastructure.Eventing.ScoringStreams;

public sealed class MartenScoringRebuildStore(
    NoCtfDbContext db,
    IDocumentSession session,
    IDocumentStore store) : IScoringRebuildStore
{
    public async Task<ScoringRebuildLease> BeginAsync(Guid competitionId, CancellationToken cancellationToken)
    {
        var submissionState = await session.Events.FetchStreamStateAsync(StreamIds.Submission(competitionId), cancellationToken);
        var checkpoint = await session.LoadAsync<ScoringProjectionCheckpoint>(competitionId, cancellationToken)
            ?? new ScoringProjectionCheckpoint
            {
                Id = competitionId,
                CompetitionId = competitionId,
                ActiveScoringStreamId = StreamIds.Scoring(competitionId)
            };
        checkpoint.StagingScoringStreamId = Guid.NewGuid();
        checkpoint.UpdatedAt = DateTimeOffset.UtcNow;
        session.Store(checkpoint);
        await session.SaveChangesAsync(cancellationToken);
        return new(competitionId, checkpoint.StagingScoringStreamId.Value, submissionState?.Version ?? 0);
    }

    public async Task<ScoringContext> LoadContextAsync(Guid competitionId, CancellationToken cancellationToken)
    {
        var competition = await EF.SingleAsync(
            db.Competitions.AsNoTracking(),
            item => item.Id == competitionId,
            cancellationToken);
        var configuration = await EF.SingleAsync(
            db.CompetitionConfigurations.AsNoTracking(),
            item => item.CompetitionId == competitionId,
            cancellationToken);
        var teams = await EF.ToDictionaryAsync(
            db.Teams.AsNoTracking().Where(item => item.CompetitionId == competitionId),
            item => item.Id,
                item => new ScoringTeam(item.Id, item.Name, item.Ban.IsBanned, item.Deletion.IsDeleted),
                cancellationToken);
        var challengeConfigurations = await EF.ToDictionaryAsync(
            db.ChallengeConfigurations.AsNoTracking(),
            item => item.ChallengeId,
            item => item.Json,
            cancellationToken);
        var challenges = await EF.ToDictionaryAsync(
            db.Challenges.AsNoTracking().Where(item => item.CompetitionId == competitionId),
            item => item.Id,
                item => new ScoringChallenge(item.Id, item.Direction, item.Deletion.IsDeleted,
                    challengeConfigurations.GetValueOrDefault(item.Id, """{"schemaVersion":1}""")),
                cancellationToken);
        return new(competitionId, competition.Mode, configuration.Json, teams, challenges);
    }

    public async Task<IReadOnlyList<SubmissionEventEnvelope>> ReadSubmissionEventsAsync(
        Guid competitionId,
        long afterSequence,
        long throughSequence,
        CancellationToken cancellationToken)
    {
        var events = await session.Events.FetchStreamAsync(StreamIds.Submission(competitionId), token: cancellationToken);
        return events.Where(item => item.Version > afterSequence && item.Version <= throughSequence)
            .Select(item => item.Data as NoCTF.Application.Submissions.Events.ISubmissionStreamEvent is { } submissionEvent
                ? new SubmissionEventEnvelope(item.Version, submissionEvent, item.Timestamp)
                : null)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    public async Task ReplaceStagingEventsAsync(
        ScoringRebuildLease lease,
        IReadOnlyList<DerivedScoringEvent> events,
        CancellationToken cancellationToken)
    {
        await store.Advanced.Clean.DeleteSingleEventStreamAsync(lease.StagingStreamId, null, cancellationToken);
        session.Events.StartStream(lease.StagingStreamId, events.Select(item => item.Event).ToArray());
        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task<long> GetSubmissionHighWaterMarkAsync(Guid competitionId, CancellationToken cancellationToken) =>
        (await session.Events.FetchStreamStateAsync(StreamIds.Submission(competitionId), cancellationToken))?.Version ?? 0;

    public async Task ActivateAsync(
        ScoringRebuildLease lease,
        long caughtUpSequence,
        CancellationToken cancellationToken)
    {
        var checkpoint = await session.LoadAsync<ScoringProjectionCheckpoint>(lease.CompetitionId, cancellationToken)
            ?? throw new InvalidOperationException("Scoring rebuild checkpoint was not found.");
        if (checkpoint.StagingScoringStreamId != lease.StagingStreamId)
            throw new InvalidOperationException("Scoring rebuild lease is no longer active.");

        var oldStream = checkpoint.ActiveScoringStreamId;
        var scoringEvents = await session.Events.FetchStreamAsync(lease.StagingStreamId, token: cancellationToken);
        var teamNames = await EF.ToDictionaryAsync(
            db.Teams.AsNoTracking().Where(team => team.CompetitionId == lease.CompetitionId),
            team => team.Id,
            team => team.Name,
            cancellationToken);
        var leaderboard = BuildLeaderboard(
            lease.CompetitionId,
            checkpoint.ProjectionVersion + 1,
            teamNames,
            scoringEvents.Select(item => item.Data).OfType<NoCTF.Application.Submissions.Events.IScoringStreamEvent>());

        checkpoint.ActiveScoringStreamId = lease.StagingStreamId;
        checkpoint.StagingScoringStreamId = null;
        checkpoint.SubmissionHighWaterMark = caughtUpSequence;
        checkpoint.ProjectionVersion++;
        checkpoint.IsDirty = true;
        checkpoint.UpdatedAt = DateTimeOffset.UtcNow;
        session.Store(checkpoint);
        session.Store(leaderboard);
        await session.SaveChangesAsync(cancellationToken);

        if (oldStream != Guid.Empty && oldStream != lease.StagingStreamId)
            await store.Advanced.Clean.DeleteSingleEventStreamAsync(oldStream, null, cancellationToken);
    }

    private static LeaderboardDocument BuildLeaderboard(
        Guid competitionId,
        long version,
        IReadOnlyDictionary<Guid, string> teamNames,
        IEnumerable<NoCTF.Application.Submissions.Events.IScoringStreamEvent> events)
    {
        var scores = new Dictionary<Guid, long>();
        var solves = new Dictionary<Guid, List<SolveRecorded>>();
        foreach (var @event in events)
        {
            if (@event is ScoreAwarded awarded)
                scores[awarded.TeamId] = scores.GetValueOrDefault(awarded.TeamId) + awarded.Points;
            else if (@event is ScoreDeducted deducted)
                scores[deducted.TeamId] = scores.GetValueOrDefault(deducted.TeamId) - deducted.Points;
            else if (@event is SolveRecorded solve)
                solves.GetOrAdd(solve.TeamId).Add(solve);
        }

        var ranked = teamNames.Keys.Select(teamId =>
            {
                var teamSolves = solves.GetValueOrDefault(teamId) ?? [];
                return new
                {
                    TeamId = teamId,
                    Name = teamNames[teamId],
                    Score = scores.GetValueOrDefault(teamId),
                    Solves = teamSolves
                };
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Solves.Count)
            .ThenBy(item => item.Solves.Count == 0 ? DateTimeOffset.MaxValue : item.Solves.Max(solve => solve.SolvedAt))
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();

        var entries = ranked.Select((item, index) => new LeaderboardEntry(
            index + 1,
            item.TeamId,
            item.Name,
            item.Score,
            item.Solves.Count,
            item.Solves.Count == 0 ? null : item.Solves.Max(solve => solve.SolvedAt),
            item.Solves.GroupBy(solve => solve.ChallengeId)
                .Select(group => new LeaderboardChallengeSummary(group.Key, string.Empty, group.Count()))
                .ToList())).ToList();
        return new()
        {
            Id = competitionId,
            CompetitionId = competitionId,
            ProjectionVersion = version,
            GeneratedAt = DateTimeOffset.UtcNow,
            Entries = entries
        };
    }
}

internal static class DictionaryListExtensions
{
    public static List<TValue> GetOrAdd<TKey, TValue>(this Dictionary<TKey, List<TValue>> dictionary, TKey key)
        where TKey : notnull
    {
        if (!dictionary.TryGetValue(key, out var values))
        {
            values = [];
            dictionary[key] = values;
        }
        return values;
    }
}
