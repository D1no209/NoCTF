using System.Text;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Storage;

namespace NoCTF.Application.LiveSolo.Templates;

public sealed record UploadLiveSoloPoolAttachmentsCommand(Guid CompetitionId, Guid CompetitionChallengeId, Guid ActorId,
    Guid ExpectedStamp, Guid ExpectedMaterialStamp, AttachmentDeliveryPolicy DeliveryPolicy, string? DownloadFileName,
    IReadOnlyList<ChallengeAttachmentUploadItem> Files, DateTimeOffset Now);

public sealed class UploadLiveSoloPoolAttachments(ILiveSoloQuestionPoolStore pool, ManagedFileUploads uploads)
{
    public async Task<LiveSoloPoolResult> ExecuteAsync(UploadLiveSoloPoolAttachmentsCommand command, CancellationToken ct)
    {
        if (command.Files.Count is < 1 or > 128 || !Enum.IsDefined(command.DeliveryPolicy)) return new(null, LiveSoloPoolFailure.InvalidContent);
        var random = command.DeliveryPolicy == AttachmentDeliveryPolicy.RandomOnePerTeam;
        static bool FileName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 260
            && value == Path.GetFileName(value) && !value.Any(char.IsControl) && !value.Contains('/') && !value.Contains('\\');
        if (random && (!FileName(command.DownloadFileName) || command.Files.Select(x => x.FileName).Distinct().Count() != command.Files.Count)
            || command.Files.Any(x => random
                ? Encoding.UTF8.GetByteCount(x.FileName) is < 1 or > 4096 || x.FileName.Any(char.IsControl) || x.FileName.Contains('/') || x.FileName.Contains('\\') || x.FileName is "." or ".."
                : !FileName(x.FileName))) return new(null, LiveSoloPoolFailure.InvalidContent);
        var failure = await pool.CheckEditAsync(command.CompetitionId, command.CompetitionChallengeId, command.ActorId, command.ExpectedStamp, command.ExpectedMaterialStamp, ct);
        if (failure is not null) return new(null, failure);
        var current = (await pool.ListAsync(command.CompetitionId, command.ActorId, ct))?.SingleOrDefault(x => x.Id == command.CompetitionChallengeId);
        if (current is null) return new(null, LiveSoloPoolFailure.NotFound);
        var staged = new List<LiveSoloPoolUploadedAttachment>(); var committed = false;
        try
        {
            foreach (var file in command.Files)
            {
                var id = Guid.CreateVersion7();
                var upload = await uploads.CreateAsync(id, $"attachments/{id:N}", random ? command.DownloadFileName!.Trim() : file.FileName.Trim(),
                    file.ContentType, file.Content, command.Now, ct);
                staged.Add(new(upload.FileId, random ? file.FileName : null));
            }
            var result = await pool.EditAsync(new(command.CompetitionId, command.CompetitionChallengeId, command.ActorId,
                command.ExpectedStamp, command.ExpectedMaterialStamp, current.Title, current.Description, current.Direction, current.Definition, command.Now,
                AddedAttachments: staged), ct);
            committed = result.Failure is null;
            return result;
        }
        finally { if (!committed) foreach (var file in staged) await uploads.AbandonAsync(file.FileId); }
    }
}
