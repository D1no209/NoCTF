using Marten;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Evaluation;
using NoCTF.Application.Scoring.Events;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Scoring.Ports;
using NoCTF.Infrastructure.Eventing.ProjectionCheckpoints;
using NoCTF.Infrastructure.Eventing.Projections;
using NoCTF.Infrastructure.Persistence;
using NoCTF.GameModes.Leaderboard;
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
        ScoringContext context,
        long caughtUpSequence,
        CancellationToken cancellationToken)
    {
        var checkpoint = await session.LoadAsync<ScoringProjectionCheckpoint>(lease.CompetitionId, cancellationToken)
            ?? throw new InvalidOperationException("Scoring rebuild checkpoint was not found.");
        if (checkpoint.StagingScoringStreamId != lease.StagingStreamId)
            throw new InvalidOperationException("Scoring rebuild lease is no longer active.");
        var currentSubmissionVersion = (await session.Events.FetchStreamStateAsync(
            StreamIds.Submission(lease.CompetitionId), cancellationToken))?.Version ?? 0;
        if (currentSubmissionVersion != caughtUpSequence)
            throw new InvalidOperationException("Submission stream changed during scoring activation; rebuild must retry.");

        var oldStream = checkpoint.ActiveScoringStreamId;
        var scoringEvents = await session.Events.FetchStreamAsync(lease.StagingStreamId, token: cancellationToken);
        var leaderboard = GameModeLeaderboardProjectorCatalog.Get(context.Mode).Project(
            context,
            checkpoint.ProjectionVersion + 1,
            scoringEvents.Select(item => item.Data)
                .OfType<NoCTF.Application.Submissions.Events.IScoringStreamEvent>()
                .ToList());

        checkpoint.ActiveScoringStreamId = lease.StagingStreamId;
        checkpoint.StagingScoringStreamId = null;
        checkpoint.SubmissionHighWaterMark = caughtUpSequence;
        checkpoint.ProjectionVersion++;
        checkpoint.IsDirty = true;
        checkpoint.UpdatedAt = DateTimeOffset.UtcNow;
        session.Store(checkpoint);
        session.Store(leaderboard);
        await session.SaveChangesAsync(cancellationToken);

        // Old scoring streams are retained until a maintenance sweep confirms the
        // checkpoint switch. Activation itself stays short and atomic.
    }

}
