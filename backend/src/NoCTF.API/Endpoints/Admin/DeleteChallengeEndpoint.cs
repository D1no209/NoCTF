using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class DeleteChallengeRequest
{
    public Guid Id { get; set; }
}

public class DeleteChallengeEndpoint(ApplicationDbContext db, IStorageProvider storageProvider) : Endpoint<DeleteChallengeRequest>, IAuditableEndpoint
{
    public override void Configure()
    {
        Delete("/api/admin/challenges/{id}");
        Roles("Admin");
    }

    public override async Task HandleAsync(DeleteChallengeRequest req, CancellationToken ct)
    {
        var challenge = await db.ChallengeTemplates
            .FirstOrDefaultAsync(c => c.Id == req.Id, ct);

        if (challenge is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var inUse = await db.Challenges
            .IgnoreQueryFilters()
            .AnyAsync(c => c.TemplateId == req.Id, ct);
        if (inUse)
        {
            await SendStringAsync("challenge_template_in_use", 409, cancellation: ct);
            return;
        }

        var storageKeys = new[] { challenge.AttachmentStorageKey, challenge.PatchTemplateStorageKey };
        db.ChallengeTemplates.Remove(challenge);
        await db.SaveChangesAsync(ct);
        await StorageObjectCleanup.DeleteUnreferencedAsync(db, storageProvider, storageKeys, ct);
        await SendNoContentAsync(ct);
    }
}
