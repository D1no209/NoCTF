using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class ChallengeAttachmentResponse
{
    public string AttachmentUrl { get; set; } = string.Empty;
}

public class ChallengePatchTemplateResponse
{
    public string PatchTemplateUrl { get; set; } = string.Empty;
}

public class UploadChallengeAttachmentEndpoint(
    ApplicationDbContext db,
    IStorageProvider storageProvider,
    IConfiguration configuration,
    ILogger<UploadChallengeAttachmentEndpoint> logger)
    : EndpointWithoutRequest<ChallengeAttachmentResponse>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/admin/challenges/{id}/attachment");
        Roles("Admin");
        AllowFileUploads();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var challengeId = Route<Guid>("id");
        if (!await db.ChallengeTemplates.AsNoTracking().AnyAsync(c => c.Id == challengeId, ct))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var file = Files.FirstOrDefault();
        if (file is null)
        {
            await SendStringAsync("no_file", 400, cancellation: ct);
            return;
        }

        var maxBytes = configuration.GetValue<long>("ChallengeAttachmentUpload:MaxBytes", 100 * 1024 * 1024);
        if (file.Length <= 0 || file.Length > maxBytes)
        {
            await SendStringAsync("invalid_size", 400, cancellation: ct);
            return;
        }

        var extension = SafeExtension(file.FileName);
        var storagePath = $"challenge-attachments/{challengeId:N}/{Guid.NewGuid():N}{extension}";
        await using var stream = file.OpenReadStream();
        var key = await storageProvider.UploadAsync(storagePath, stream, file.ContentType, ct);
        string url;
        try
        {
            url = await UploadedObjectPersistence.CompleteAsync(
                db,
                key,
                async cancellationToken =>
                {
                    var uploadedUrl = await storageProvider.GetUrlAsync(key, cancellationToken);
                    await ChallengeTemplateObjectReplacement.ReplaceAsync(
                        db,
                        challengeId,
                        ChallengeTemplateObjectSlot.Attachment,
                        key,
                        uploadedUrl,
                        cancellationToken);
                    return uploadedUrl;
                },
                logger,
                ct);
        }
        catch (ChallengeTemplateNotFoundException)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendAsync(new ChallengeAttachmentResponse { AttachmentUrl = url }, cancellation: ct);
    }

    internal static string SafeExtension(string fileName)
    {
        var extension = Path.GetExtension(Path.GetFileName(fileName));
        return extension.Length is > 0 and <= 20 &&
               extension.All(character => char.IsAsciiLetterOrDigit(character) || character == '.')
            ? extension.ToLowerInvariant()
            : string.Empty;
    }
}

public class UploadChallengePatchTemplateEndpoint(
    ApplicationDbContext db,
    IStorageProvider storageProvider,
    IConfiguration configuration,
    ILogger<UploadChallengePatchTemplateEndpoint> logger)
    : EndpointWithoutRequest<ChallengePatchTemplateResponse>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/admin/challenges/{id}/patch-template");
        Roles("Admin");
        AllowFileUploads();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var challengeId = Route<Guid>("id");
        if (!await db.ChallengeTemplates.AsNoTracking().AnyAsync(c => c.Id == challengeId, ct))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var file = Files.FirstOrDefault();
        if (file is null)
        {
            await SendStringAsync("no_file", 400, cancellation: ct);
            return;
        }

        var maxBytes = configuration.GetValue<long>("ChallengePatchTemplateUpload:MaxBytes", 25 * 1024 * 1024);
        if (file.Length <= 0 || file.Length > maxBytes)
        {
            await SendStringAsync("invalid_size", 400, cancellation: ct);
            return;
        }

        var extension = UploadChallengeAttachmentEndpoint.SafeExtension(file.FileName);
        var storagePath = $"challenge-patch-templates/{challengeId:N}/{Guid.NewGuid():N}{extension}";
        await using var stream = file.OpenReadStream();
        var key = await storageProvider.UploadAsync(storagePath, stream, file.ContentType, ct);
        string url;
        try
        {
            url = await UploadedObjectPersistence.CompleteAsync(
                db,
                key,
                async cancellationToken =>
                {
                    var uploadedUrl = await storageProvider.GetUrlAsync(key, cancellationToken);
                    await ChallengeTemplateObjectReplacement.ReplaceAsync(
                        db,
                        challengeId,
                        ChallengeTemplateObjectSlot.PatchTemplate,
                        key,
                        uploadedUrl,
                        cancellationToken);
                    return uploadedUrl;
                },
                logger,
                ct);
        }
        catch (ChallengeTemplateNotFoundException)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendAsync(new ChallengePatchTemplateResponse { PatchTemplateUrl = url }, cancellation: ct);
    }
}
