using FluentStorage.Storage;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Storage;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Teams.WriteUps;

public static class TeamWriteUpRules
{
    public const long MaximumFileBytes = 64L * 1024 * 1024;
    public const string ContentType = "application/pdf";

    private static ReadOnlySpan<byte> PdfHeader => "%PDF-"u8;

    public static bool HasValidFileName(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase);

    public static bool HasValidContentType(string contentType) =>
        string.Equals(contentType, ContentType, StringComparison.OrdinalIgnoreCase);

    public static async Task<bool> HasValidHeaderAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        if (!content.CanSeek)
            return false;
        var originalPosition = content.Position;
        var header = new byte[PdfHeader.Length];
        var read = await content.ReadAsync(header, cancellationToken);
        content.Position = originalPosition;
        return read == header.Length && header.AsSpan().SequenceEqual(PdfHeader);
    }
}

public enum TeamWriteUpSubmissionState : short
{
    Updated,
    NotFound,
    Forbidden,
    InvalidPdf,
    SubmissionDeadlinePassed
}

public sealed record TeamWriteUpSubmissionContext(
    Guid TeamId,
    DateTimeOffset CompetitionEndAt,
    int DeadlineHours);

public sealed record TeamWriteUpReference(
    Guid TeamId,
    string TeamName,
    Guid FileId,
    string ObjectKey,
    string FileName,
    string ContentType,
    long ByteLength,
    string Sha256,
    Guid SubmittedByUserId,
    string? SubmittedByDisplayName,
    DateTimeOffset SubmittedAt);

public sealed record TeamWriteUpSubmissionResult(
    TeamWriteUpSubmissionState State,
    TeamWriteUpReference? WriteUp = null);

public sealed record TeamWriteUpChallengeScore(
    Guid CompetitionChallengeId,
    string Title,
    string Direction,
    long NetPoints);

public sealed record TeamWriteUpReviewItem(
    TeamWriteUpReference WriteUp,
    long? TotalScore,
    IReadOnlyList<TeamWriteUpChallengeScore> ChallengeScores);

public sealed record TeamWriteUpReview(
    bool ScoreboardAvailable,
    IReadOnlyList<TeamWriteUpReviewItem> Items);

public sealed record TeamWriteUpContent(
    TeamWriteUpReference Metadata,
    Stream Content);

public interface ITeamWriteUpStore
{
    Task<TeamWriteUpSubmissionContext?> FindSubmissionContextAsync(
        Guid competitionId,
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task<TeamWriteUpSubmissionResult> ReplaceAsync(
        Guid competitionId,
        Guid teamId,
        Guid actorUserId,
        Guid fileId,
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken);

    Task<TeamWriteUpReference?> FindMineAsync(
        Guid competitionId,
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task<TeamWriteUpReference?> FindAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TeamWriteUpReference>> ListAsync(
        Guid competitionId,
        CancellationToken cancellationToken);
}

public sealed class ManageTeamWriteUps(
    ITeamWriteUpStore store,
    ManagedFileUploads uploads,
    IStore objects,
    ILeaderboardCache leaderboards)
{
    public async Task<TeamWriteUpSubmissionResult> ReplaceMineAsync(
        Guid competitionId,
        Guid actorUserId,
        string fileName,
        string contentType,
        long byteLength,
        Stream content,
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken = default)
    {
        if (byteLength is <= 0 or > TeamWriteUpRules.MaximumFileBytes
            || !TeamWriteUpRules.HasValidFileName(fileName)
            || !TeamWriteUpRules.HasValidContentType(contentType)
            || !await TeamWriteUpRules.HasValidHeaderAsync(content, cancellationToken))
        {
            return new(TeamWriteUpSubmissionState.InvalidPdf);
        }

        var submission = await store.FindSubmissionContextAsync(
            competitionId,
            actorUserId,
            cancellationToken);
        if (submission is null)
            return new(TeamWriteUpSubmissionState.Forbidden);
        if (!CompetitionWriteUpPolicy.CanSubmit(
                submission.CompetitionEndAt,
                submission.DeadlineHours,
                submittedAt))
        {
            return new(TeamWriteUpSubmissionState.SubmissionDeadlinePassed);
        }

        var fileId = Guid.CreateVersion7(submittedAt);
        var uploaded = await uploads.CreateAsync(
            fileId,
            $"competitions/{competitionId:N}/writeups/{submission.TeamId:N}/{fileId:N}.pdf",
            NormalizeFileName(fileName),
            TeamWriteUpRules.ContentType,
            content,
            submittedAt,
            cancellationToken);
        var attached = false;
        try
        {
            var result = await store.ReplaceAsync(
                competitionId,
                submission.TeamId,
                actorUserId,
                uploaded.FileId,
                submittedAt,
                cancellationToken);
            attached = result.State == TeamWriteUpSubmissionState.Updated;
            return result;
        }
        finally
        {
            if (!attached)
                await uploads.AbandonAsync(uploaded.FileId);
        }
    }

    public Task<TeamWriteUpReference?> FindMineAsync(
        Guid competitionId,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        store.FindMineAsync(competitionId, actorUserId, cancellationToken);

    public async Task<TeamWriteUpContent?> OpenMineAsync(
        Guid competitionId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var reference = await store.FindMineAsync(
            competitionId,
            actorUserId,
            cancellationToken);
        return await OpenAsync(reference, cancellationToken);
    }

    public async Task<TeamWriteUpContent?> OpenAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken cancellationToken = default)
    {
        var reference = await store.FindAsync(
            competitionId,
            teamId,
            cancellationToken);
        return await OpenAsync(reference, cancellationToken);
    }

    public Task<TeamWriteUpReference?> FindAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken cancellationToken = default) =>
        store.FindAsync(competitionId, teamId, cancellationToken);

    public async Task<TeamWriteUpReview> ReviewAsync(
        Guid competitionId,
        CancellationToken cancellationToken = default)
    {
        var writeUps = await store.ListAsync(competitionId, cancellationToken);
        if (writeUps.Count == 0)
            return new(false, []);

        var projection = await leaderboards.GetScoreboardAsync(
            competitionId,
            cancellationToken);
        if (projection is null)
        {
            return new(false, writeUps.Select(writeUp =>
                new TeamWriteUpReviewItem(writeUp, null, [])).ToArray());
        }

        var columns = projection.Schema.Columns.ToDictionary(column => column.Index);
        var teams = projection.Snapshot.Teams.ToDictionary(team => team.TeamId);
        var items = writeUps.Select(writeUp =>
        {
            teams.TryGetValue(writeUp.TeamId, out var team);
            var scores = projection.ChallengeCatalog.Challenges
                .OrderBy(challenge => challenge.Order)
                .ThenBy(challenge => challenge.CompetitionChallengeId)
                .Select(challenge => new TeamWriteUpChallengeScore(
                    challenge.CompetitionChallengeId,
                    challenge.Title,
                    challenge.Direction,
                    (team?.Slots ?? [])
                        .Where(slot => columns.TryGetValue(slot.ColumnIndex, out var column)
                            && column.CompetitionChallengeId == challenge.CompetitionChallengeId)
                        .Sum(slot => slot.NetPoints ?? 0)))
                .ToArray();
            return new TeamWriteUpReviewItem(writeUp, team?.TotalScore ?? 0, scores);
        }).ToArray();
        return new(true, items);
    }

    private async Task<TeamWriteUpContent?> OpenAsync(
        TeamWriteUpReference? reference,
        CancellationToken cancellationToken)
    {
        if (reference is null)
            return null;
        var content = await objects.OpenRead(reference.ObjectKey, cancellationToken);
        return content is null ? null : new(reference, content);
    }

    private static string NormalizeFileName(string value)
    {
        var leaf = Path.GetFileName(value.Replace('\\', '/'));
        var safe = string.Concat(leaf.Where(character => !char.IsControl(character))).Trim();
        if (string.IsNullOrWhiteSpace(safe) || safe is "." or "..")
            return "writeup.pdf";
        var stem = Path.GetFileNameWithoutExtension(safe).Trim();
        if (string.IsNullOrWhiteSpace(stem))
            return "writeup.pdf";
        return $"{stem[..Math.Min(stem.Length, 256)]}.pdf";
    }
}
