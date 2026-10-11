using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Resources;

public sealed class LiveSoloReviewStore(NoCtfDbContext db, ICompetitionModerationAuthorizer authorizer) : ILiveSoloReviewStore
{
    private sealed record Access(Guid? TeamId, LiveSoloMatch Match);
    private async Task<Access?> AuthorizeAsync(LiveSoloReviewRequest request, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == request.ActorId && x.AccountStatus == UserAccountStatus.Active, ct)) return null;
        var match = await db.LiveSoloMatches.AsNoTracking().Include(x => x.Roster).Include(x => x.Slots)
            .SingleOrDefaultAsync(x => x.Id == request.MatchId && x.CompetitionId == request.CompetitionId, ct);
        var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.CompetitionId && x.Mode == GameMode.LiveSolo, ct);
        if (match is null || competition?.ModeConfiguration is not LiveSoloCompetitionModeConfiguration { Enabled: true }) return null;
        if (request.Staff) return await authorizer.CanObserveAsync(request.ActorId, request.CompetitionId, ct) ? new(null, match) : null;
        if (competition.Status is not (CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished)) return null;
        var teamId = await db.Teams.AsNoTracking().Where(x => x.CompetitionId == request.CompetitionId && !x.IsBanned
            && x.RegistrationStatus == TeamRegistrationStatus.Approved && x.Members.Any(m => m.UserId == request.ActorId))
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (teamId is not Guid team) return null;
        if (competition.Status != CompetitionStatus.Finished && (!match.Roster.Any(x => x.UserId == request.ActorId && x.TeamId == team)
            || !match.Slots.Any(x => x.TeamId == team && x.RosterLockedAt != null))) return null;
        return new(team, match);
    }

    public async Task<IReadOnlyList<LiveSoloReviewQuestion>?> ListAsync(LiveSoloReviewRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        if (await AuthorizeAsync(request, ct) is null) return null;
        var rows = await db.LiveSoloRoundQuestions.AsNoTracking().Where(x => request.Staff || x.OpenedAt != null)
            .Join(db.LiveSoloRounds.AsNoTracking().Where(x => x.MatchId == request.MatchId), q => q.RoundId, r => r.Id,
                (q, r) => new { Question = q, Round = r })
            .OrderBy(x => x.Round.Number).ThenBy(x => x.Round.Replay).ThenBy(x => x.Question.Position)
            .Select(x => new { x.Question.Id, x.Question.RoundId, x.Question.CompetitionChallengeId, x.Round.Number, x.Round.Replay,
                x.Question.Position, x.Question.OpenedAt, Content = x.Question.OpenedContent }).ToArrayAsync(ct);
        var futureIds = request.Staff ? rows.Where(x => x.OpenedAt == null).Select(x => x.CompetitionChallengeId).ToArray() : [];
        var future = await db.CompetitionChallenges.AsNoTracking().Where(x => futureIds.Contains(x.Id))
            .Join(db.Challenges.AsNoTracking(), x => x.ChallengeId, x => x.Id, (e, t) => new { e.Id, Title = e.CustomTitle ?? t.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, ct);
        return rows.Select(x => new LiveSoloReviewQuestion(x.Id, x.RoundId, x.CompetitionChallengeId, x.Number, x.Replay, x.Position,
            x.Content?.Title ?? (x.OpenedAt is null ? future.GetValueOrDefault(x.CompetitionChallengeId) : null), x.OpenedAt,
            x.Content != null || request.Staff && x.OpenedAt == null)).ToArray();
    }

    public async Task<LiveSoloReviewContent?> ReadAsync(LiveSoloReviewRequest request, Guid questionId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var access = await AuthorizeAsync(request, ct); if (access is null) return null;
        var question = await db.LiveSoloRoundQuestions.AsNoTracking().Include(x => x.OpenedContent)
            .SingleOrDefaultAsync(x => x.Id == questionId && db.LiveSoloRounds.Any(r => r.Id == x.RoundId && r.MatchId == request.MatchId), ct);
        if (question is null || !request.Staff && question.OpenedAt is null) return null;
        LiveSoloQuestionView body;
        IReadOnlyList<LiveSoloHintView> hints;
        IReadOnlyList<LiveSoloAttachmentView> attachments;
        if (question.OpenedContent is { } content)
        {
            body = new(question.Id, question.CompetitionChallengeId, question.Position, content.Title, content.Description, content.Direction,
                content.Tags.OrderBy(x => x.Position).Select(x => x.Name).ToArray(), question.OpenedAt!.Value, null);
            hints = content.Hints.OrderBy(x => x.Position).Select(x => new LiveSoloHintView(x.HintId, x.Content, x.PublishedAt)).ToArray();
            var allowed = content.Attachments.AsEnumerable();
            if (content.RandomAttachment && !request.Staff)
            {
                var assigned = await db.LiveSoloAttachmentAssignments.AsNoTracking()
                    .Where(x => x.RoundQuestionId == question.Id && x.TeamId == access.TeamId).Select(x => x.AttachmentId).ToArrayAsync(ct);
                allowed = allowed.Where(x => assigned.Contains(x.AttachmentId));
            }
            var refs = allowed.ToDictionary(x => x.AttachmentId, x => x.FileId);
            var files = await db.Files.AsNoTracking().Where(x => refs.Values.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            attachments = refs.Select(x => new LiveSoloAttachmentView(x.Key, files[x.Value].FileName, files[x.Value].ContentType, files[x.Value].ByteLength)).ToArray();
        }
        else if (request.Staff && question.OpenedAt is null)
        {
            var material = await db.CompetitionChallenges.AsNoTracking().Where(x => x.Id == question.CompetitionChallengeId)
                .Join(db.Challenges.AsNoTracking(), x => x.ChallengeId, x => x.Id, (e, t) => new { Entry = e, Template = t }).SingleOrDefaultAsync(ct);
            if (material is null) return null;
            body = new(question.Id, question.CompetitionChallengeId, question.Position, material.Entry.CustomTitle ?? material.Template.Title,
                material.Template.Description, material.Template.Direction, material.Entry.Tags.OrderBy(x => x.Position).Select(x => x.Name).ToArray(), default, null);
            hints = material.Entry.Hints.Where(x => x.Cost == 0).Select(x => new LiveSoloHintView(x.Id, x.Content, x.PublishedAt ?? default)).ToArray();
            attachments = await db.Set<ChallengeAttachment>().Where(x => x.ChallengeId == material.Template.Id && x.DeletedAt == null)
                .Select(x => new LiveSoloAttachmentView(x.Id, x.File.FileName, x.File.ContentType, x.File.ByteLength)).ToArrayAsync(ct);
        }
        else return null; // Legacy disclosed content was not recorded; never substitute a newer template.
        return new(body, hints, attachments, question.OpenedAt != null);
    }

    public async Task<LiveSoloReviewFile?> FileAsync(LiveSoloReviewRequest request, Guid questionId, Guid attachmentId, CancellationToken ct)
    {
        var content = await ReadAsync(request, questionId, ct);
        var metadata = content?.Attachments.SingleOrDefault(x => x.Id == attachmentId); if (metadata is null) return null;
        var fileId = content!.Historical
            ? await db.Set<LiveSoloOpenedAttachment>().Where(x => x.RoundQuestionId == questionId && x.AttachmentId == attachmentId).Select(x => (Guid?)x.FileId).SingleOrDefaultAsync(ct)
            : await db.Set<ChallengeAttachment>().Where(x => x.Id == attachmentId && x.DeletedAt == null).Select(x => (Guid?)x.FileId).SingleOrDefaultAsync(ct);
        return await db.Files.AsNoTracking().Where(x => x.Id == fileId)
            .Select(x => new LiveSoloReviewFile(x.Id, x.ObjectKey, metadata)).SingleOrDefaultAsync(ct);
    }
}
