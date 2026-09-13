using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Teams.WriteUps;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Infrastructure.Teams.WriteUps;

public sealed class TeamWriteUpStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    ICompetitionEventRecorder events,
    FileReferenceLock fileLock) : ITeamWriteUpStore
{
    public Task<TeamWriteUpSubmissionContext?> FindSubmissionContextAsync(
        Guid competitionId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        return db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                && team.MemberIds.Contains(actorUserId)
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && !team.IsBanned)
            .Join(
                db.Competitions.AsNoTracking(),
                team => team.CompetitionId,
                competition => competition.Id,
                (team, competition) => new TeamWriteUpSubmissionContext(
                    team.Id,
                    competition.EndAt,
                    competition.WriteUpSubmissionDeadlineHours))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<TeamWriteUpSubmissionResult> ReplaceAsync(
        Guid competitionId,
        Guid teamId,
        Guid actorUserId,
        Guid fileId,
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var team = await db.Teams.SingleOrDefaultAsync(candidate =>
            candidate.Id == teamId
            && candidate.CompetitionId == competitionId,
            cancellationToken);
        if (team is null)
            return new(TeamWriteUpSubmissionState.NotFound);
        if (!team.MemberIds.Contains(actorUserId)
            || team.RegistrationStatus != TeamRegistrationStatus.Approved
            || team.IsBanned)
        {
            return new(TeamWriteUpSubmissionState.Forbidden);
        }
        var competition = await db.Competitions.AsNoTracking()
            .Where(candidate => candidate.Id == competitionId)
            .Select(candidate => new
            {
                candidate.EndAt,
                candidate.WriteUpSubmissionDeadlineHours
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (competition is null)
            return new(TeamWriteUpSubmissionState.NotFound);
        if (!CompetitionWriteUpPolicy.CanSubmit(
                competition.EndAt,
                competition.WriteUpSubmissionDeadlineHours,
                submittedAt))
        {
            return new(TeamWriteUpSubmissionState.SubmissionDeadlinePassed);
        }
        if (!await fileLock.AcquireAsync(db, fileId, cancellationToken))
            return new(TeamWriteUpSubmissionState.NotFound);

        var file = await db.Files.SingleAsync(candidate => candidate.Id == fileId, cancellationToken);
        var submittedBy = await FindUserNameAsync(actorUserId, cancellationToken);
        var previousFileId = team.WriteUpFileId;
        team.WriteUpFileId = file.Id;
        team.WriteUpSubmittedByUserId = actorUserId;
        team.WriteUpSubmittedAt = submittedAt;
        await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.TeamWriteUpSubmitted,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            submittedAt,
            ActorUserId: actorUserId,
            TeamId: team.Id,
            SubjectType: EntityReferenceKind.Team,
            SubjectId: team.Id,
            RelatedType: EntityReferenceKind.File,
            RelatedId: file.Id), cancellationToken);
        if (previousFileId is { } previous && previous != file.Id)
            await outbox.PublishAsync(new CleanupFile(previous));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushCommittedMessagesAsync();
        return new(
            TeamWriteUpSubmissionState.Updated,
            Map(team.Id, team.Name, file, actorUserId, submittedBy, submittedAt));
    }

    public async Task<TeamWriteUpReference?> FindMineAsync(
        Guid competitionId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var row = await References(competitionId, actorUserId)
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : Map(row, await FindUserNameAsync(row.SubmittedByUserId, cancellationToken));
    }

    public async Task<TeamWriteUpReference?> FindAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        var row = await References(competitionId, teamId: teamId)
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : Map(row, await FindUserNameAsync(row.SubmittedByUserId, cancellationToken));
    }

    public async Task<IReadOnlyList<TeamWriteUpReference>> ListAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var rows = (await References(competitionId)
                .ToArrayAsync(cancellationToken))
            .OrderByDescending(item => item.SubmittedAt)
            .ThenBy(item => item.TeamId)
            .ToArray();
        if (rows.Length == 0)
            return [];

        var userIds = rows.Select(item => item.SubmittedByUserId).Distinct().ToArray();
        var userNames = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.UserName, cancellationToken);
        return rows.Select(row => Map(row, userNames.GetValueOrDefault(row.SubmittedByUserId)))
            .ToArray();
    }

    public async Task<IReadOnlyList<TeamWriteUpManualAdjustment>> ReadManualAdjustmentsAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var rows = await db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == competitionId
                && fact.TeamId != null
                && fact.Kind == GameplayFactKind.ManualAdjustment
                && fact.Result == GameplayFactResult.Applied)
            .Select(fact => new
            {
                TeamId = fact.TeamId!.Value,
                fact.CompetitionChallengeId,
                fact.Value
            })
            .ToArrayAsync(cancellationToken);
        return rows.GroupBy(row => new
            {
                row.TeamId,
                row.CompetitionChallengeId
            })
            .Select(group => new TeamWriteUpManualAdjustment(
                group.Key.TeamId,
                group.Key.CompetitionChallengeId,
                group.Aggregate(
                    0L,
                    (total, row) => checked(total + int.Parse(
                        row.Value!,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture)))))
            .ToArray();
    }

    private IQueryable<ReferenceRow> References(
        Guid competitionId,
        Guid? memberId = null,
        Guid? teamId = null)
    {
        var teams = db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                && team.WriteUpFileId != null
                && team.WriteUpSubmittedByUserId != null
                && team.WriteUpSubmittedAt != null);
        if (memberId is { } userId)
            teams = teams.Where(team => team.MemberIds.Contains(userId));
        if (teamId is { } selectedTeamId)
            teams = teams.Where(team => team.Id == selectedTeamId);
        return teams.Join(
                db.Files.AsNoTracking(),
                team => team.WriteUpFileId,
                file => file.Id,
                (team, file) => new ReferenceRow(
                    team.Id,
                    team.Name,
                    team.MemberIds,
                    file.Id,
                    file.ObjectKey,
                    file.FileName,
                    file.ContentType,
                    file.ByteLength,
                    file.Sha256,
                    team.WriteUpSubmittedByUserId!.Value,
                    team.WriteUpSubmittedAt!.Value));
    }

    private Task<string?> FindUserNameAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.UserName)
            .SingleOrDefaultAsync(cancellationToken);

    private static TeamWriteUpReference Map(ReferenceRow row, string? submittedBy) =>
        new(
            row.TeamId,
            row.TeamName,
            row.FileId,
            row.ObjectKey,
            row.FileName,
            row.ContentType,
            row.ByteLength,
            Convert.ToHexString(row.Sha256),
            row.SubmittedByUserId,
            submittedBy,
            row.SubmittedAt);

    private static TeamWriteUpReference Map(
        Guid teamId,
        string teamName,
        NoCTF.Domain.Storage.StoredFile file,
        Guid submittedByUserId,
        string? submittedBy,
        DateTimeOffset submittedAt) =>
        new(
            teamId,
            teamName,
            file.Id,
            file.ObjectKey,
            file.FileName,
            file.ContentType,
            file.ByteLength,
            Convert.ToHexString(file.Sha256),
            submittedByUserId,
            submittedBy,
            submittedAt);

    private sealed record ReferenceRow(
        Guid TeamId,
        string TeamName,
        Guid[] MemberIds,
        Guid FileId,
        string ObjectKey,
        string FileName,
        string ContentType,
        long ByteLength,
        byte[] Sha256,
        Guid SubmittedByUserId,
        DateTimeOffset SubmittedAt);
}
