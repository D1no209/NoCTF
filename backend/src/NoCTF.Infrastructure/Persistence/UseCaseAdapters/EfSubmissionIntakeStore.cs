using Microsoft.EntityFrameworkCore;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

/// <summary>EF transaction seam for accepting immutable submission facts.</summary>
public sealed class EfSubmissionIntakeStore(NoCtfDbContext db, IBackgroundWorkScheduler scheduler) : ISubmissionIntakeStore
{
    public async Task<SubmissionAdmissionSnapshot?> LoadAdmissionAsync(Guid competitionId, Guid teamId, Guid challengeId, Guid userId, CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == competitionId, ct);
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(x => x.Id == teamId && x.CompetitionId == competitionId, ct);
        var challenge = await db.Challenges.AsNoTracking().SingleOrDefaultAsync(x => x.Id == challengeId && x.CompetitionId == competitionId, ct);
        if (competition is null || team is null || challenge is null) return null;
        var belongs = await db.TeamMembers.AsNoTracking().AnyAsync(x => x.CompetitionId == competitionId && x.TeamId == teamId && x.UserId == userId, ct);
        return new(competitionId, teamId, challengeId, 0, competition.Status, competition.StartTime, competition.EndTime,
            competition.Deletion.IsDeleted, challenge.Deletion.IsDeleted, challenge.IsPublished, team.Deletion.IsDeleted,
            team.Ban.IsBanned, team.RegistrationStatus == NoCTF.Domain.Teams.TeamRegistrationStatus.Approved, belongs);
    }

    public async Task<bool> TryAcceptFlagAsync(FlagSubmissionReceived received, long _, CancellationToken ct)
    {
        var entity = new Submission
        {
            Id = received.SubmissionId, CompetitionId = received.CompetitionId, TeamId = received.TeamId,
            ChallengeId = received.ChallengeId, UserId = received.UserId, Kind = NoCTF.Domain.Submissions.SubmissionKind.Flag,
            Flag = received.Flag, ReceivedAt = received.ReceivedAt, CreatedAt = received.ReceivedAt, UpdatedAt = received.ReceivedAt
        };
        db.Submissions.Add(entity);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return false; }
        await scheduler.EnqueueSubmissionAsync(entity.Id, ct);
        return true;
    }

    public async Task<bool> TryAcceptFixAsync(FixSubmissionReceived received, long _, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var record = await db.FixSubmissionRecords.SingleOrDefaultAsync(x => x.UploadId == received.UploadId, ct);
        if (record is null || record.SubmissionId is not null || record.ExpiresAt <= received.ReceivedAt
            || record.CompetitionId != received.CompetitionId || record.TeamId != received.TeamId || record.ChallengeId != received.ChallengeId)
            return false;
        var entity = new Submission
        {
            Id = received.SubmissionId, CompetitionId = received.CompetitionId, TeamId = received.TeamId,
            ChallengeId = received.ChallengeId, UserId = received.UserId, Kind = NoCTF.Domain.Submissions.SubmissionKind.Fix,
            ReceivedAt = received.ReceivedAt, CreatedAt = received.ReceivedAt, UpdatedAt = received.ReceivedAt
        };
        record.SubmissionId = entity.Id;
        record.ClaimedAt = received.ReceivedAt;
        record.VerificationStatus = FixVerificationStatus.Claimed;
        record.UpdatedAt = received.ReceivedAt;
        db.Submissions.Add(entity);
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException) { await transaction.RollbackAsync(ct); return false; }
        await scheduler.EnqueueSubmissionAsync(entity.Id, ct);
        return true;
    }
}
