using Marten;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using EF = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions;

namespace NoCTF.Infrastructure.Eventing.SubmissionStreams;

/// <summary>Combines relational admission state with optimistic appends to the permanent Marten stream.</summary>
public sealed class MartenSubmissionIntakeStore(NoCtfDbContext db, IDocumentSession session) : ISubmissionIntakeStore
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
        CancellationToken cancellationToken) =>
        TryAppendAsync(received.CompetitionId, received, expectedRevision, cancellationToken);

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
        var existing = await session.Events.FetchStreamAsync(StreamIds.Submission(received.CompetitionId), token: cancellationToken);
        if (existing.Select(item => item.Data).OfType<FixSubmissionReceived>()
            .Any(item => item.UploadId == received.UploadId))
            return false;
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
            session.Events.Append(StreamIds.Submission(competitionId), expectedRevision, @event);
            await session.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (
            exception.GetType().Name.Contains("Concurrency", StringComparison.OrdinalIgnoreCase)
            || exception.GetType().Name.Contains("Unexpected", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
    }
}
