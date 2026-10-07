using NoCTF.Application.Storage;
using NoCTF.Application.Teams.WriteUps;
using NoCTF.Domain.Challenges.WriteUps;

namespace NoCTF.Application.Challenges.WriteUps;

public enum ChallengeWriteUpFailure : short
{
    NotFound, Forbidden, Disabled, DeadlinePassed, InvalidContent, Conflict,
    NotPublished, ConfirmationChanged, CompetitionNotRunning, ContentUnavailable
}

public sealed record WriteUpVersionView(Guid Id, int Number, WriteUpFormat Format,
    WriteUpVersionState State, Guid ConcurrencyStamp, Guid ActorUserId,
    DateTimeOffset UpdatedAt, DateTimeOffset? SubmittedAt, string? ReviewReason,
    string? Markdown = null, Guid? FileId = null, string? FileName = null);
public sealed record ChallengeWriteUpView(Guid Id, Guid CompetitionChallengeId,
    string ChallengeTitle, WriteUpSource Source, Guid? TeamId, string AuthorName,
    Guid ConcurrencyStamp, Guid? PublishedVersionId, DateTimeOffset UpdatedAt,
    WriteUpVersionView? Draft, WriteUpVersionView? Submitted, WriteUpVersionView? Published,
    IReadOnlyList<WriteUpVersionView> Versions);
public sealed record WriteUpSettingsView(bool Enabled, int DeductionPercent, int DeadlineHours,
    DateTimeOffset DeadlineAt, int? ChallengeDeductionPercent, Guid PolicyStamp);
public sealed record WriteUpAccessView(bool CanManage, bool CanJudge, bool CanSubmit,
    bool IsFree, bool IsUnlocked, int DeductionPercent, DateTimeOffset? UnlockedAt,
    bool CanUnlock, WriteUpSettingsView Settings, Guid? TeamId);
public sealed record WriteUpListView(WriteUpAccessView Access, IReadOnlyList<ChallengeWriteUpView> Items);
public sealed record WriteUpMutationResult(ChallengeWriteUpView? WriteUp = null,
    ChallengeWriteUpFailure? Failure = null);
public sealed record SaveWriteUpDraft(Guid CompetitionId, Guid CompetitionChallengeId, Guid ActorId,
    bool Official, WriteUpFormat Format, string? Markdown, Guid? FileId,
    Guid? ExpectedStamp, DateTimeOffset Now);
public sealed record SubmitWriteUp(Guid CompetitionId, Guid CompetitionChallengeId, Guid ActorId,
    bool Official, Guid ExpectedStamp, DateTimeOffset Now);
public sealed record ReviewWriteUp(Guid CompetitionId, Guid CompetitionChallengeId, Guid WriteUpId,
    Guid VersionId, Guid ActorId, Guid ExpectedStamp, WriteUpReviewAction Action,
    string? Reason, DateTimeOffset Now);
public sealed record WriteUpFileReference(Guid VersionId, string ObjectKey, string FileName, string ContentType);
public sealed record WriteUpContentView(Guid VersionId, WriteUpFormat Format, string? Markdown,
    WriteUpFileReference? File, ChallengeWriteUpFailure? Failure = null);

public interface IChallengeWriteUpStore
{
    Task<WriteUpListView?> ListAsync(Guid competitionId, Guid challengeId, Guid actorId, bool staff, DateTimeOffset now, CancellationToken ct);
    Task<WriteUpMutationResult> SaveDraftAsync(SaveWriteUpDraft command, CancellationToken ct);
    Task<WriteUpMutationResult> SubmitAsync(SubmitWriteUp command, CancellationToken ct);
    Task<WriteUpMutationResult> ReviewAsync(ReviewWriteUp command, CancellationToken ct);
    Task<WriteUpContentView> ReadContentAsync(Guid competitionId, Guid challengeId, Guid versionId, Guid actorId, bool staff, DateTimeOffset now, CancellationToken ct);
}

public sealed class ManageChallengeWriteUps(IChallengeWriteUpStore store, ManagedFileUploads uploads)
{
    public Task<WriteUpListView?> ListAsync(Guid competitionId, Guid challengeId, Guid actorId,
        bool staff, DateTimeOffset now, CancellationToken ct) => store.ListAsync(competitionId, challengeId, actorId, staff, now, ct);

    public Task<WriteUpMutationResult> SaveMarkdownAsync(SaveWriteUpDraft command, CancellationToken ct) =>
        ChallengeWriteUpPolicy.ValidContent(command.Format, command.Markdown, command.FileId)
            ? store.SaveDraftAsync(command, ct)
            : Task.FromResult(new WriteUpMutationResult(Failure: ChallengeWriteUpFailure.InvalidContent));

    public async Task<WriteUpMutationResult> SavePdfAsync(SaveWriteUpDraft command, string fileName,
        string contentType, long byteLength, Stream content, CancellationToken ct)
    {
        var access = await store.ListAsync(command.CompetitionId, command.CompetitionChallengeId,
            command.ActorId, command.Official, command.Now, ct);
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
        command.Action == WriteUpReviewAction.Reject && (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 4000)
            ? Task.FromResult(new WriteUpMutationResult(Failure: ChallengeWriteUpFailure.InvalidContent))
            : store.ReviewAsync(command, ct);
    public Task<WriteUpContentView> ReadContentAsync(Guid competitionId, Guid challengeId, Guid versionId,
        Guid actorId, bool staff, DateTimeOffset now, CancellationToken ct) =>
        store.ReadContentAsync(competitionId, challengeId, versionId, actorId, staff, now, ct);
}
