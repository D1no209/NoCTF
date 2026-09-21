using Microsoft.AspNetCore.Http;
using NoCTF.API.Endpoints.Administration.ChallengeBank;

namespace NoCTF.Tests.Unit.Api;

public sealed class ChallengeAttachmentUploadContractTests
{
    [Test]
    public async Task Omitted_attachment_ids_keep_server_generated_id_behavior()
    {
        var request = Request([File("first.txt")]);

        var result = await new UploadChallengeAttachmentsValidator()
            .ValidateAsync(request);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task One_attachment_id_per_file_is_valid()
    {
        var request = Request(
            [File("first.txt"), File("second.txt")],
            [Guid.NewGuid(), Guid.NewGuid()]);

        var result = await new UploadChallengeAttachmentsValidator()
            .ValidateAsync(request);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Attachment_id_count_must_match_file_count()
    {
        var request = Request(
            [File("first.txt"), File("second.txt")],
            [Guid.NewGuid()]);

        var result = await new UploadChallengeAttachmentsValidator()
            .ValidateAsync(request);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error => error.PropertyName.StartsWith(
            nameof(UploadChallengeAttachmentsRequest.AttachmentIds),
            StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Attachment_ids_must_be_non_empty_and_distinct(bool useEmptyId)
    {
        var id = useEmptyId ? Guid.Empty : Guid.NewGuid();
        var request = Request(
            [File("first.txt"), File("second.txt")],
            [id, id]);

        var result = await new UploadChallengeAttachmentsValidator()
            .ValidateAsync(request);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error => error.PropertyName.StartsWith(
            nameof(UploadChallengeAttachmentsRequest.AttachmentIds),
            StringComparison.Ordinal))).IsTrue();
    }

    private static UploadChallengeAttachmentsRequest Request(
        IReadOnlyList<IFormFile> files,
        IReadOnlyList<Guid>? attachmentIds = null) =>
        new()
        {
            DeliveryPolicy = AttachmentDeliveryPolicyProtocol.All,
            AttachmentIds = attachmentIds,
            Files = files
        };

    private static IFormFile File(string name) =>
        new FormFile(new MemoryStream([1]), 0, 1, "Files", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };
}
