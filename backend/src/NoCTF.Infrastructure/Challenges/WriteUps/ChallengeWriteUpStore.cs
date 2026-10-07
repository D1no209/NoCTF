using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.WriteUps;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Progression;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges.WriteUps;

public sealed class ChallengeWriteUpStore(NoCtfDbContext db, ICompetitionModerationAuthorizer authorizer,
    ICompetitionChallengeReadAccess readAccess, ICompetitionEventRecorder events,
    IPostCommitMessagePublisher messages) : IChallengeWriteUpStore
{
    private sealed record Context(Competition Competition, CompetitionChallenge Challenge,
        Guid? TeamId, bool CanObserve, bool CanJudge, bool CanManage);

    private async Task<Context?> ContextAsync(Guid competitionId, Guid challengeId, Guid actorId,
        bool staff, DateTimeOffset now, CancellationToken ct)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == actorId && x.AccountStatus == UserAccountStatus.Active, ct)) return null;
        var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == competitionId, ct);
        var challenge = await db.CompetitionChallenges.AsNoTracking().IgnoreAutoIncludes()
            .SingleOrDefaultAsync(x => x.Id == challengeId && x.CompetitionId == competitionId, ct);
        if (competition is null || challenge is null) return null;
        var observe = await authorizer.CanObserveAsync(actorId, competitionId, ct);
        var manage = observe && await authorizer.CanModerateAsync(actorId, competitionId, ct);
        var judge = observe && await authorizer.CanJudgeAsync(actorId, competitionId, ct);
        if (staff) return observe ? new(competition, challenge, null, observe, judge, manage) : null;
        if (!competition.SingleWriteUpsEnabled || !challenge.IsPublished || challenge.DeletedAt is not null
            || !ParticipantChallengeVisibilityPolicy.CanView(competition.Status)) return null;
        var decision = await readAccess.ResolveAsync(actorId, competitionId, now, ct);
        if (decision?.TeamId is not Guid teamId || !await db.Teams.AsNoTracking().AnyAsync(x => x.Id == teamId
                && x.CompetitionId == competitionId && !x.IsBanned && x.RegistrationStatus == TeamRegistrationStatus.Approved, ct)
            || !await new ProgressionChallengeAccess(db).IsActiveAsync(competitionId, challengeId, teamId, ct)) return null;
        return new(competition, challenge, teamId, observe, judge, manage);
    }

    public async Task<WriteUpListView?> ListAsync(Guid competitionId, Guid challengeId, Guid actorId,
        bool staff, DateTimeOffset now, CancellationToken ct)
    {
        var context = await ContextAsync(competitionId, challengeId, actorId, staff, now, ct);
        if (context is null) return null;
        var roots = await db.ChallengeWriteUps.AsNoTracking().Include(x => x.Versions)
            .Where(x => x.CompetitionId == competitionId && x.CompetitionChallengeId == challengeId
                && (staff || x.PublishedVersionId != null || x.TeamId == context.TeamId))
            .OrderByDescending(x => x.Source).ThenByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).ToArrayAsync(ct);
        var receipt = context.TeamId is Guid teamId ? await db.WriteUpUnlockReceipts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TeamId == teamId && x.CompetitionChallengeId == challengeId, ct) : null;
        var access = Access(context, receipt, now);
        var items = new List<ChallengeWriteUpView>();
        var names = await db.Teams.AsNoTracking().Where(x => roots.Select(r => r.TeamId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var title = context.Challenge.CustomTitle ?? await db.Challenges.AsNoTracking()
            .Where(x => x.Id == context.Challenge.ChallengeId).Select(x => x.Title).SingleAsync(ct);
        foreach (var root in roots)
            items.Add(Map(root, title, root.TeamId is Guid id ? names.GetValueOrDefault(id, string.Empty) : string.Empty,
                staff || root.TeamId == context.TeamId));
        return new(access, items);
    }

    private static WriteUpAccessView Access(Context context, WriteUpUnlockReceipt? receipt, DateTimeOffset now)
    {
        var competition = context.Competition;
        var percent = context.Challenge.WriteUpDeductionPercent ?? competition.SingleWriteUpDeductionPercent;
        var stampBytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{competition.ConcurrencyStamp:N}:{context.Challenge.ConcurrencyStamp:N}:{percent}"));
        var settings = new WriteUpSettingsView(competition.SingleWriteUpsEnabled, percent,
            competition.SingleWriteUpDeadlineHours, CompetitionWriteUpPolicy.DeadlineAt(competition.EndAt,
                competition.SingleWriteUpDeadlineHours), context.Challenge.WriteUpDeductionPercent, new Guid(stampBytes.AsSpan(0, 16)));
        return new(context.CanManage, context.CanJudge,
            context.TeamId is not null && ChallengeWriteUpPolicy.CanSubmit(competition.SingleWriteUpsEnabled,
                competition.EndAt, competition.SingleWriteUpDeadlineHours, now),
            competition.Status == CompetitionStatus.Finished, receipt is not null,
            receipt?.DeductionPercent ?? percent, receipt?.UnlockedAt,
            context.TeamId is not null && competition.Status == CompetitionStatus.Running, settings, context.TeamId);
    }

    public async Task<WriteUpMutationResult> SaveDraftAsync(SaveWriteUpDraft command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var context = await ContextAsync(command.CompetitionId, command.CompetitionChallengeId, command.ActorId,
            command.Official, command.Now, ct);
        if (context is null || (command.Official ? !context.CanManage : context.TeamId is null)) return new(Failure: ChallengeWriteUpFailure.Forbidden);
        if (!context.Competition.SingleWriteUpsEnabled) return new(Failure: ChallengeWriteUpFailure.Disabled);
        if (!command.Official && !Access(context, null, command.Now).CanSubmit) return new(Failure: ChallengeWriteUpFailure.DeadlinePassed);
        if (!ChallengeWriteUpPolicy.ValidContent(command.Format, command.Markdown, command.FileId)) return new(Failure: ChallengeWriteUpFailure.InvalidContent);
        if (command.FileId is Guid fileId && !await db.Files.AnyAsync(x => x.Id == fileId && x.ContentType == "application/pdf", ct)) return new(Failure: ChallengeWriteUpFailure.InvalidContent);
        var scopeId = command.Official ? command.CompetitionId : context.TeamId!.Value;
        var source = command.Official ? WriteUpSource.Official : WriteUpSource.Team;
        var root = await db.ChallengeWriteUps.Include(x => x.Versions).SingleOrDefaultAsync(x =>
            x.CompetitionChallengeId == command.CompetitionChallengeId && x.Source == source && x.AuthorScopeId == scopeId, ct);
        if (root is not null && root.ConcurrencyStamp != command.ExpectedStamp) return new(Failure: ChallengeWriteUpFailure.Conflict);
        if (root is null)
        {
            if (command.ExpectedStamp is not null) return new(Failure: ChallengeWriteUpFailure.Conflict);
            root = new() { Id = Guid.CreateVersion7(command.Now), CompetitionId = command.CompetitionId,
                CompetitionChallengeId = command.CompetitionChallengeId, Source = source,
                AuthorScopeId = scopeId, TeamId = command.Official ? null : context.TeamId };
            db.ChallengeWriteUps.Add(root);
        }
        var draft = root.Versions.SingleOrDefault(x => x.Id == root.DraftVersionId);
        if (draft is null)
        {
            draft = new() { Id = Guid.CreateVersion7(command.Now), WriteUpId = root.Id,
                Number = root.NextVersionNumber++, State = WriteUpVersionState.Draft };
            root.Versions.Add(draft);
            db.ChallengeWriteUpVersions.Add(draft);
            root.DraftVersionId = draft.Id;
        }
        var oldFileId = draft.FileId;
        draft.Format = command.Format; draft.Markdown = command.Markdown; draft.FileId = command.FileId;
        draft.ActorUserId = command.ActorId; draft.UpdatedAt = command.Now; root.UpdatedAt = command.Now;
        if (oldFileId is Guid old && old != command.FileId) await messages.PublishAsync(new CleanupFile(old));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await messages.FlushCommittedMessagesAsync();
        return await MutationViewAsync(root, context, ct);
    }

    public async Task<WriteUpMutationResult> SubmitAsync(SubmitWriteUp command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var context = await ContextAsync(command.CompetitionId, command.CompetitionChallengeId, command.ActorId, command.Official, command.Now, ct);
        if (context is null || (command.Official ? !context.CanManage : context.TeamId is null)) return new(Failure: ChallengeWriteUpFailure.Forbidden);
        if (!context.Competition.SingleWriteUpsEnabled) return new(Failure: ChallengeWriteUpFailure.Disabled);
        if (!command.Official && !Access(context, null, command.Now).CanSubmit) return new(Failure: ChallengeWriteUpFailure.DeadlinePassed);
        var source = command.Official ? WriteUpSource.Official : WriteUpSource.Team;
        var scopeId = command.Official ? command.CompetitionId : context.TeamId!.Value;
        var root = await db.ChallengeWriteUps.Include(x => x.Versions).SingleOrDefaultAsync(x =>
            x.CompetitionChallengeId == command.CompetitionChallengeId && x.Source == source && x.AuthorScopeId == scopeId, ct);
        if (root is null) return new(Failure: ChallengeWriteUpFailure.NotFound);
        if (root.ConcurrencyStamp != command.ExpectedStamp) return new(Failure: ChallengeWriteUpFailure.Conflict);
        var draft = root.Versions.SingleOrDefault(x => x.Id == root.DraftVersionId);
        if (draft is null || !ChallengeWriteUpPolicy.ValidContent(draft.Format, draft.Markdown, draft.FileId)) return new(Failure: ChallengeWriteUpFailure.InvalidContent);
        draft.State = WriteUpVersionState.Submitted; draft.SubmittedAt = command.Now; draft.UpdatedAt = command.Now;
        root.SubmittedVersionId = draft.Id; root.DraftVersionId = null; root.UpdatedAt = command.Now;
        await RecordAsync(context, root, command.ActorId, CompetitionEventKind.ChallengeWriteUpSubmitted, command.Now, ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); await messages.FlushCommittedMessagesAsync();
        return await MutationViewAsync(root, context, ct);
    }

    public async Task<WriteUpMutationResult> ReviewAsync(ReviewWriteUp command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var context = await ContextAsync(command.CompetitionId, command.CompetitionChallengeId, command.ActorId, true, command.Now, ct);
        if (context is null || (command.Action == WriteUpReviewAction.Reject ? !context.CanJudge : !context.CanManage)) return new(Failure: ChallengeWriteUpFailure.Forbidden);
        if (!context.Competition.SingleWriteUpsEnabled && command.Action != WriteUpReviewAction.Withdraw) return new(Failure: ChallengeWriteUpFailure.Disabled);
        var root = await db.ChallengeWriteUps.Include(x => x.Versions).SingleOrDefaultAsync(x => x.Id == command.WriteUpId
            && x.CompetitionId == command.CompetitionId && x.CompetitionChallengeId == command.CompetitionChallengeId, ct);
        if (root is null) return new(Failure: ChallengeWriteUpFailure.NotFound);
        if (root.ConcurrencyStamp != command.ExpectedStamp) return new(Failure: ChallengeWriteUpFailure.Conflict);
        var version = root.Versions.SingleOrDefault(x => x.Id == command.VersionId);
        if (version is null) return new(Failure: ChallengeWriteUpFailure.NotFound);
        if (command.Action == WriteUpReviewAction.Withdraw)
        {
            if (root.PublishedVersionId != version.Id) return new(Failure: ChallengeWriteUpFailure.Conflict);
            root.PublishedVersionId = null;
        }
        else
        {
            if (root.SubmittedVersionId != version.Id || version.State is not (WriteUpVersionState.Submitted or WriteUpVersionState.Approved))
                return new(Failure: ChallengeWriteUpFailure.Conflict);
            if (command.Action == WriteUpReviewAction.Reject && (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 4000))
                return new(Failure: ChallengeWriteUpFailure.InvalidContent);
            if (command.Action == WriteUpReviewAction.Reject && version.State != WriteUpVersionState.Submitted)
                return new(Failure: ChallengeWriteUpFailure.Conflict);
            version.State = command.Action == WriteUpReviewAction.Publish ? WriteUpVersionState.Approved : WriteUpVersionState.Rejected;
            version.ReviewedByUserId = command.ActorId; version.ReviewedAt = command.Now; version.ReviewReason = command.Reason?.Trim();
            if (command.Action == WriteUpReviewAction.Publish) root.PublishedVersionId = version.Id;
        }
        root.UpdatedAt = command.Now;
        var kind = command.Action switch { WriteUpReviewAction.Publish => CompetitionEventKind.ChallengeWriteUpPublished,
            WriteUpReviewAction.Reject => CompetitionEventKind.ChallengeWriteUpRejected, _ => CompetitionEventKind.ChallengeWriteUpWithdrawn };
        await RecordAsync(context, root, command.ActorId, kind, command.Now, ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); await messages.FlushCommittedMessagesAsync();
        return await MutationViewAsync(root, context, ct);
    }

    public async Task<WriteUpContentView> ReadContentAsync(Guid competitionId, Guid challengeId, Guid versionId,
        Guid actorId, bool staff, DateTimeOffset now, CancellationToken ct)
    {
        var context = await ContextAsync(competitionId, challengeId, actorId, staff, now, ct);
        if (context is null) return new(versionId, default, null, null, ChallengeWriteUpFailure.Forbidden);
        var root = await db.ChallengeWriteUps.AsNoTracking().Include(x => x.Versions).SingleOrDefaultAsync(x =>
            x.CompetitionId == competitionId && x.CompetitionChallengeId == challengeId && x.Versions.Any(v => v.Id == versionId), ct);
        if (root is null) return new(versionId, default, null, null, ChallengeWriteUpFailure.NotFound);
        var own = context.TeamId is not null && root.TeamId == context.TeamId;
        if (!staff && !own)
        {
            if (root.PublishedVersionId != versionId) return new(versionId, default, null, null, ChallengeWriteUpFailure.NotPublished);
            if (context.Competition.Status != CompetitionStatus.Finished && !await db.WriteUpUnlockReceipts.AsNoTracking()
                .AnyAsync(x => x.TeamId == context.TeamId && x.CompetitionChallengeId == challengeId, ct))
                return new(versionId, default, null, null, ChallengeWriteUpFailure.Forbidden);
        }
        var version = root.Versions.Single(x => x.Id == versionId);
        var file = version.FileId is Guid fileId ? await db.Files.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fileId, ct) : null;
        return new(versionId, version.Format, version.Markdown, file is null ? null : new(versionId, file.ObjectKey, file.FileName, file.ContentType));
    }

    private ValueTask<Guid> RecordAsync(Context context, ChallengeWriteUp root, Guid actorId, CompetitionEventKind kind,
        DateTimeOffset now, CancellationToken ct) => events.RecordAsync(new(context.Competition.Id, kind,
            CompetitionEventLevel.Information, CompetitionEventVisibility.Staff, now, ActorUserId: actorId,
            TeamId: root.TeamId, SubjectType: EntityReferenceKind.CompetitionChallenge,
            SubjectId: root.CompetitionChallengeId), ct);

    private async Task<WriteUpMutationResult> MutationViewAsync(ChallengeWriteUp root, Context context, CancellationToken ct)
    {
        var title = context.Challenge.CustomTitle ?? await db.Challenges.AsNoTracking().Where(x => x.Id == context.Challenge.ChallengeId)
            .Select(x => x.Title).SingleAsync(ct);
        var name = root.TeamId is Guid teamId ? await db.Teams.AsNoTracking().Where(x => x.Id == teamId).Select(x => x.Name).SingleAsync(ct) : string.Empty;
        return new(Map(root, title, name, true));
    }

    private static ChallengeWriteUpView Map(ChallengeWriteUp root, string title, string authorName, bool privateVersions)
    {
        WriteUpVersionView Metadata(ChallengeWriteUpVersion v) => new(v.Id, v.Number, v.Format,
            v.State, v.ConcurrencyStamp, v.ActorUserId, v.UpdatedAt, v.SubmittedAt, privateVersions ? v.ReviewReason : null);
        WriteUpVersionView? Version(Guid? id) => root.Versions.SingleOrDefault(x => x.Id == id) is { } v ? Metadata(v) : null;
        return new(root.Id, root.CompetitionChallengeId, title, root.Source, root.TeamId, authorName, root.ConcurrencyStamp,
            root.PublishedVersionId, root.UpdatedAt, privateVersions ? Version(root.DraftVersionId) : null,
            privateVersions ? Version(root.SubmittedVersionId) : null, Version(root.PublishedVersionId),
            privateVersions ? root.Versions.OrderByDescending(x => x.Number).Select(Metadata).ToArray() : []);
    }
}
