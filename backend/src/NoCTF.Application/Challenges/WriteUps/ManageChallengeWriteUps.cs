using NoCTF.Application.Storage;
using NoCTF.Application.Teams.WriteUps;
using FluentStorage.Storage;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges.WriteUps;

namespace NoCTF.Application.Challenges.WriteUps;

public enum ChallengeWriteUpFailure : short
{
    NotFound, Forbidden, Disabled, DeadlinePassed, InvalidContent, Conflict,
    NotPublished, ConfirmationChanged, CompetitionNotRunning, ContentUnavailable
}
public enum WriteUpReviewFilter : short { All, Submitted, Published, Rejected, Draft }
public sealed record WriteUpReviewQuery(Guid CompetitionId, Guid ActorId, Guid? ChallengeId,
    string? Search, WriteUpReviewFilter Filter, int Offset, int Limit, DateTimeOffset Now, WriteUpSource? Source = null);
public sealed record WriteUpReviewPage(IReadOnlyList<ChallengeWriteUpView> Items, int TotalCount, bool CanManage, bool CanJudge);

public sealed record WriteUpVersionView(Guid Id, int Number, WriteUpFormat Format,
    WriteUpVersionState State, Guid ConcurrencyStamp, Guid ActorUserId,
    DateTimeOffset UpdatedAt, DateTimeOffset? SubmittedAt, string? ReviewReason,
    string? Markdown = null, Guid? FileId = null, string? FileName = null, string? ActorDisplayName = null,
    DateTimeOffset? PublishedAt = null);
public sealed record ChallengeWriteUpView(Guid Id, Guid CompetitionChallengeId,
    string ChallengeTitle, WriteUpSource Source, Guid? TeamId, string AuthorName,
    Guid ConcurrencyStamp, Guid? PublishedVersionId, DateTimeOffset UpdatedAt,
    WriteUpVersionView? Draft, WriteUpVersionView? Submitted, WriteUpVersionView? Published,
    IReadOnlyList<WriteUpVersionView> Versions)
{
    public int ViewedTeamCount { get; init; }
}
public sealed record WriteUpSettingsView(bool Enabled, int DeductionPercent, int DeadlineHours,
    DateTimeOffset DeadlineAt, int? ChallengeDeductionPercent, Guid PolicyStamp);
public sealed record WriteUpAccessView(bool CanManage, bool CanJudge, bool CanSubmit,
    bool IsFree, bool IsUnlocked, int DeductionPercent, DateTimeOffset? UnlockedAt,
    bool CanUnlock, WriteUpSettingsView Settings, Guid? TeamId, bool CanShowCurrentScore = true);
public sealed record WriteUpListView(WriteUpAccessView Access, IReadOnlyList<ChallengeWriteUpView> Items);
public sealed record WriteUpMutationResult(ChallengeWriteUpView? WriteUp = null,
    ChallengeWriteUpFailure? Failure = null);
public sealed record SaveWriteUpDraft(Guid CompetitionId, Guid CompetitionChallengeId, Guid ActorId,
    bool Official, WriteUpFormat Format, string? Markdown, Guid? FileId,
    Guid? ExpectedStamp, DateTimeOffset Now, Guid? ExecutionScopeId = null);
public sealed record SubmitWriteUp(Guid CompetitionId, Guid CompetitionChallengeId, Guid ActorId,
    bool Official, Guid ExpectedStamp, DateTimeOffset Now, Guid? ExecutionScopeId = null);
public sealed record ReviewWriteUp(Guid CompetitionId, Guid CompetitionChallengeId, Guid WriteUpId,
    Guid VersionId, Guid ActorId, Guid ExpectedStamp, WriteUpReviewAction Action,
    string? Reason, DateTimeOffset Now, Guid? ExecutionScopeId = null);
public sealed record WriteUpFileReference(Guid VersionId, string ObjectKey, string FileName, string ContentType)
{
    public Guid FileId { get; init; }
}
public sealed record WriteUpContentView(Guid VersionId, WriteUpFormat Format, string? Markdown,
    WriteUpFileReference? File, ChallengeWriteUpFailure? Failure = null);
public sealed record UnlockWriteUp(Guid CompetitionId, Guid CompetitionChallengeId, Guid VersionId,
    Guid ActorId, Guid PolicyStamp, DateTimeOffset Now);
public sealed record WriteUpUnlockResult(bool Created, int DeductionPercent, ChallengeWriteUpFailure? Failure = null);
public sealed record UpdateWriteUpSettings(Guid CompetitionId, Guid? ChallengeId, Guid ActorId,
    Guid ExpectedStamp, bool? Enabled, int? DeductionPercent, int? DeadlineHours,
    bool DeductionSpecified, DateTimeOffset Now);
public sealed record WriteUpSettingsResult(WriteUpSettingsView? Settings, Guid? ConcurrencyStamp,
    ChallengeWriteUpFailure? Failure = null);
public sealed record WriteUpPdfContent(string FileName, Stream Content, Guid FileId);
public sealed record ChallengeWriteUpQuote(Guid VersionId, Guid PolicyStamp, bool IsFree, bool IsUnlocked,
    bool CanUnlock, int DeductionPercent, long? GrossPoints, long? EstimatedDeductionPoints);

public interface IChallengeWriteUpStore
{
    Task<WriteUpListView?> ListAsync(Guid competitionId, Guid challengeId, Guid actorId, bool staff, DateTimeOffset now, CancellationToken ct, Guid? executionScopeId = null);
    Task<WriteUpMutationResult> SaveDraftAsync(SaveWriteUpDraft command, CancellationToken ct);
    Task<WriteUpMutationResult> SubmitAsync(SubmitWriteUp command, CancellationToken ct);
    Task<WriteUpMutationResult> ReviewAsync(ReviewWriteUp command, CancellationToken ct);
    Task<WriteUpContentView> ReadContentAsync(Guid competitionId, Guid challengeId, Guid versionId, Guid actorId, bool staff, DateTimeOffset now, CancellationToken ct, Guid? executionScopeId = null);
    Task<WriteUpContentView> ReadUnlockCandidateAsync(UnlockWriteUp command, CancellationToken ct);
    Task<WriteUpUnlockResult> UnlockAsync(UnlockWriteUp command, CancellationToken ct);
    Task<WriteUpSettingsResult> GetSettingsAsync(Guid competitionId, Guid? challengeId, Guid actorId, DateTimeOffset now, CancellationToken ct);
    Task<WriteUpSettingsResult> UpdateSettingsAsync(UpdateWriteUpSettings command, CancellationToken ct);
    Task<WriteUpReviewPage?> ListReviewAsync(WriteUpReviewQuery query, CancellationToken ct);
    Task<IReadOnlyList<ChallengeWriteUpView>?> ListMineAsync(Guid competitionId, Guid actorId, DateTimeOffset now, CancellationToken ct);
}

public sealed class ManageChallengeWriteUps(IChallengeWriteUpStore store, ManagedFileUploads uploads, IStore objects,
    ILeaderboardSnapshotFactory? snapshots = null)
{
    public Task<WriteUpListView?> ListAsync(Guid competitionId, Guid challengeId, Guid actorId,
        bool staff, DateTimeOffset now, CancellationToken ct, Guid? executionScopeId = null) => store.ListAsync(competitionId, challengeId, actorId, staff, now, ct, executionScopeId);

    public Task<WriteUpMutationResult> SaveMarkdownAsync(SaveWriteUpDraft command, CancellationToken ct) =>
        ChallengeWriteUpPolicy.ValidContent(command.Format, command.Markdown, command.FileId)
            ? store.SaveDraftAsync(command, ct)
            : Task.FromResult(new WriteUpMutationResult(Failure: ChallengeWriteUpFailure.InvalidContent));

    public async Task<WriteUpMutationResult> SavePdfAsync(SaveWriteUpDraft command, string fileName,
        string contentType, long byteLength, Stream content, CancellationToken ct)
    {
        var access = await store.ListAsync(command.CompetitionId, command.CompetitionChallengeId,
            command.ActorId, command.Official, command.Now, ct, command.ExecutionScopeId);
        if (access is null || (command.Official ? !access.Access.CanManage : !access.Access.CanSubmit))
            return new(Failure: ChallengeWriteUpFailure.Forbidden);
        if (byteLength is <= 0 or > TeamWriteUpRules.MaximumFileBytes
            || !TeamWriteUpRules.HasValidFileName(fileName)
            || !TeamWriteUpRules.HasValidContentType(contentType)
            || !await TeamWriteUpRules.HasValidHeaderAsync(content, ct))
            return new(Failure: ChallengeWriteUpFailure.InvalidContent);
        var fileId = Guid.CreateVersion7(command.Now);
        var leaf = Path.GetFileName(fileName.Replace('\\', '/'));
        var safeName = string.Concat(leaf.Where(c => !char.IsControl(c)));
        var uploaded = await uploads.CreateAsync(fileId,
            $"competitions/{command.CompetitionId:N}/challenge-writeups/{command.CompetitionChallengeId:N}/{fileId:N}.pdf",
            safeName[..Math.Min(safeName.Length, 256)],
            TeamWriteUpRules.ContentType, content, command.Now, ct);
        var attached = false;
        try
        {
            var result = await store.SaveDraftAsync(command with { Format = WriteUpFormat.Pdf, Markdown = null, FileId = uploaded.FileId }, ct);
            attached = result.Failure is null;
            return result;
        }
        finally { if (!attached) await uploads.AbandonAsync(uploaded.FileId); }
    }

    public Task<WriteUpMutationResult> SubmitAsync(SubmitWriteUp command, CancellationToken ct) => store.SubmitAsync(command, ct);
    public Task<WriteUpMutationResult> ReviewAsync(ReviewWriteUp command, CancellationToken ct) =>
        !Enum.IsDefined(command.Action) || command.Action == WriteUpReviewAction.Reject && (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 4000)
            ? Task.FromResult(new WriteUpMutationResult(Failure: ChallengeWriteUpFailure.InvalidContent))
            : store.ReviewAsync(command, ct);
    public Task<WriteUpContentView> ReadContentAsync(Guid competitionId, Guid challengeId, Guid versionId,
        Guid actorId, bool staff, DateTimeOffset now, CancellationToken ct, Guid? executionScopeId = null) =>
        store.ReadContentAsync(competitionId, challengeId, versionId, actorId, staff, now, ct, executionScopeId);

    public async Task<WriteUpUnlockResult> UnlockAsync(UnlockWriteUp command, CancellationToken ct)
    {
        var candidate = await store.ReadUnlockCandidateAsync(command, ct);
        if (candidate.Failure is { } failure) return new(false, 0, failure);
        if (candidate.Format == WriteUpFormat.Pdf)
        {
            try
            {
                if (candidate.File is null || !await objects.ObjectExists(candidate.File.ObjectKey, ct))
                    return new(false, 0, ChallengeWriteUpFailure.ContentUnavailable);
                await using var content = await objects.OpenRead(candidate.File.ObjectKey, ct);
                if (content is null) return new(false, 0, ChallengeWriteUpFailure.ContentUnavailable);
            }
            catch (IOException) { return new(false, 0, ChallengeWriteUpFailure.ContentUnavailable); }
        }
        return await store.UnlockAsync(command, ct);
    }
    public Task<WriteUpSettingsResult> GetSettingsAsync(Guid competitionId, Guid? challengeId, Guid actorId, DateTimeOffset now, CancellationToken ct) =>
        store.GetSettingsAsync(competitionId, challengeId, actorId, now, ct);
    public Task<WriteUpSettingsResult> UpdateSettingsAsync(UpdateWriteUpSettings command, CancellationToken ct) =>
        command.DeductionPercent is < 0 or > 100 || command.DeadlineHours is < 0 or > ChallengeWriteUpPolicy.MaximumDeadlineHours
            ? Task.FromResult(new WriteUpSettingsResult(null, null, ChallengeWriteUpFailure.InvalidContent))
            : store.UpdateSettingsAsync(command, ct);
    public async Task<WriteUpPdfContent?> OpenPdfAsync(Guid competitionId, Guid challengeId, Guid versionId, Guid actorId,
        bool staff, DateTimeOffset now, CancellationToken ct, Guid? executionScopeId = null)
    {
        var content = await ReadContentAsync(competitionId, challengeId, versionId, actorId, staff, now, ct, executionScopeId);
        if (content.Failure is not null || content.Format != WriteUpFormat.Pdf || content.File is null) return null;
        var stream = await objects.OpenRead(content.File.ObjectKey, ct);
        if (stream is not null && executionScopeId is not null)
        {
            try {
                var rechecked = await ReadContentAsync(competitionId, challengeId, versionId, actorId, staff, now, ct, executionScopeId);
                if (rechecked.Failure is not null || rechecked.File?.FileId != content.File.FileId) { await stream.DisposeAsync(); return null; }
            }
            catch { await stream.DisposeAsync(); throw; }
        }
        return stream is null ? null : new(content.File.FileName, stream, content.File.FileId);
    }
    public async Task<ChallengeWriteUpQuote?> QuoteAsync(Guid competitionId, Guid challengeId, Guid versionId, Guid actorId,
        DateTimeOffset now, CancellationToken ct)
    {
        var list = await store.ListAsync(competitionId, challengeId, actorId, false, now, ct);
        var root = list?.Items.SingleOrDefault(x => x.PublishedVersionId == versionId);
        if (list is null || root is null || snapshots is null) return null;
        var projection = await snapshots.CreateScoreboardAsync(competitionId, now, ct);
        if (projection is null) return null;
        var gross = projection.Snapshot.Teams.SingleOrDefault(x => x.TeamId == list.Access.TeamId)?.ChallengeBenefits
            .SingleOrDefault(x => x.CompetitionChallengeId == challengeId)?.GrossPoints ?? 0;
        var free = list.Access.IsFree || root.TeamId == list.Access.TeamId;
        return new(versionId, list.Access.Settings.PolicyStamp, free, list.Access.IsUnlocked,
            list.Access.CanUnlock && !free, list.Access.DeductionPercent, list.Access.CanShowCurrentScore ? gross : null,
            !list.Access.CanShowCurrentScore ? null : free || list.Access.IsUnlocked ? 0 : ChallengeWriteUpPolicy.Deduction(gross, list.Access.DeductionPercent));
    }
    public Task<WriteUpReviewPage?> ListReviewAsync(WriteUpReviewQuery query, CancellationToken ct) => store.ListReviewAsync(query, ct);
    public Task<IReadOnlyList<ChallengeWriteUpView>?> ListMineAsync(Guid competitionId, Guid actorId, DateTimeOffset now, CancellationToken ct) =>
        store.ListMineAsync(competitionId, actorId, now, ct);
}
