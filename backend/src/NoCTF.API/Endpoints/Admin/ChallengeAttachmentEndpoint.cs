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
    IConfiguration configuration)
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
        var challenge = await db.ChallengeTemplates.FirstOrDefaultAsync(c => c.Id == challengeId, ct);
        if (challenge is null)
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

        var safeName = Path.GetFileName(file.FileName);
        var storagePath = $"challenge-attachments/{challenge.Id:N}/{DateTime.UtcNow:yyyyMMddHHmmss}-{safeName}";
        await using var stream = file.OpenReadStream();
        var key = await storageProvider.UploadAsync(storagePath, stream, file.ContentType, ct);
        var url = await storageProvider.GetUrlAsync(key, ct);

        challenge.AttachmentUrl = url;
        challenge.DeploymentType = challenge.DeploymentType == NoCTF.Core.ChallengeDeploymentType.NoAttachment
            ? NoCTF.Core.ChallengeDeploymentType.StaticAttachment
            : challenge.DeploymentType;
        challenge.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await SendAsync(new ChallengeAttachmentResponse { AttachmentUrl = url }, cancellation: ct);
    }
}

public class UploadChallengePatchTemplateEndpoint(
    ApplicationDbContext db,
    IStorageProvider storageProvider,
    IConfiguration configuration)
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
        var challenge = await db.ChallengeTemplates.FirstOrDefaultAsync(c => c.Id == challengeId, ct);
        if (challenge is null)
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

        var safeName = Path.GetFileName(file.FileName);
        var storagePath = $"challenge-patch-templates/{challenge.Id:N}/{DateTime.UtcNow:yyyyMMddHHmmss}-{safeName}";
        await using var stream = file.OpenReadStream();
        var key = await storageProvider.UploadAsync(storagePath, stream, file.ContentType, ct);
        var url = await storageProvider.GetUrlAsync(key, ct);

        challenge.PatchTemplateUrl = url;
        challenge.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await SendAsync(new ChallengePatchTemplateResponse { PatchTemplateUrl = url }, cancellation: ct);
    }
}
