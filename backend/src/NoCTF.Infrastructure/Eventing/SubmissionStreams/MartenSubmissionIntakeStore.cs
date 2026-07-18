using Marten;
using Marten.Exceptions;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Persistence;
using Wolverine.Marten;
using EF = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions;

namespace NoCTF.Infrastructure.Eventing.SubmissionStreams;

/// <summary>Combines relational admission state with optimistic appends to the permanent Marten stream.</summary>
public sealed class MartenSubmissionIntakeStore(
    NoCtfDbContext db,
    IDocumentSession session,
    IMartenOutbox outbox,
    IChallengeInstanceFlagReader flagReader) : ISubmissionIntakeStore
{
    public async Task<SubmissionAdmissionSnapshot?> LoadAdmissionAsync(
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var competition = await EF.SingleOrDefaultAsync(
            db.Competitions.AsNoTracking(),
            item => item.Id == competitionId,
            cancellationToken);
        var team = await EF.SingleOrDefaultAsync(
            db.Teams.AsNoTracking(),
            item => item.Id == teamId && item.CompetitionId == competitionId,
            cancellationToken);
        var challenge = await EF.SingleOrDefaultAsync(
            db.Challenges.AsNoTracking(),
            item => item.Id == challengeId && item.CompetitionId == competitionId,
            cancellationToken);
        if (competition is null || team is null || challenge is null)
            return null;

        var belongs = await EF.AnyAsync(
            db.TeamMembers.AsNoTracking(),
            member => member.CompetitionId == competitionId
                && member.TeamId == teamId
                && member.UserId == userId,
            cancellationToken);
        var stream = await session.Events.FetchStreamStateAsync(StreamIds.Submission(competitionId), cancellationToken);

        return new(
            competitionId,
            teamId,
            challengeId,
            stream?.Version ?? 0,
            competition.Status,
            competition.StartTime,
            competition.EndTime,
            competition.Deletion.IsDeleted,
            challenge.Deletion.IsDeleted,
            challenge.IsPublished,
            team.Deletion.IsDeleted,
            team.Ban.IsBanned,
            team.RegistrationStatus == TeamRegistrationStatus.Approved,
            belongs);
    }

    public Task<bool> TryAcceptFlagAsync(
        FlagSubmissionReceived received,
        long expectedRevision,
        CancellationToken cancellationToken) => TryAcceptFlagWithSnapshotAsync(received, expectedRevision, cancellationToken);

    private async Task<bool> TryAcceptFlagWithSnapshotAsync(
        FlagSubmissionReceived received,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        var expectedFlag = await flagReader.ReadAsync(received.CompetitionId, received.TeamId,
            received.ChallengeId, received.ReceivedAt, cancellationToken);
        return await TryAppendAsync(received.CompetitionId,
            received with { ExpectedFlagAtReceipt = expectedFlag }, expectedRevision, cancellationToken);
    }

    public Task<bool> TryAcceptFixAsync(
        FixSubmissionReceived received,
        long expectedRevision,
        CancellationToken cancellationToken) =>
        TryAppendFixAsync(received, expectedRevision, cancellationToken);

    private async Task<bool> TryAppendFixAsync(
        FixSubmissionReceived received,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        var upload = await session.LoadAsync<FixUploadSession>(received.UploadId, cancellationToken);
        if (upload is null
            || upload.Consumed
            || upload.ExpiresAt <= received.ReceivedAt
            || upload.CompetitionId != received.CompetitionId
            || upload.TeamId != received.TeamId
            || upload.ChallengeId != received.ChallengeId
            || upload.UserId != received.UserId)
            return false;

        upload.Consumed = true;
        upload.ConsumedBySubmissionId = received.SubmissionId;
        upload.ConsumedAt = received.ReceivedAt;
        session.Store(upload);
        return await TryAppendAsync(received.CompetitionId, received, expectedRevision, cancellationToken);
    }

    private async Task<bool> TryAppendAsync(
        Guid competitionId,
        ISubmissionStreamEvent @event,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        try
        {
            outbox.Enroll(session);
            session.Events.Append(StreamIds.Submission(competitionId), expectedRevision, @event);
            switch (@event)
            {
                case FlagSubmissionReceived flag:
                    await outbox.SendAsync(new ProcessFlagSubmission(
                        flag.CompetitionId, flag.TeamId, flag.UserId, flag.SubmissionId));
                    break;
                case FixSubmissionReceived fix:
                    await outbox.SendAsync(new ProcessFixSubmission(
                        fix.CompetitionId, fix.TeamId, fix.UserId, fix.SubmissionId));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(@event));
            }
            await session.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (MartenCommandException)
        {
            return false;
        }
    }
}
