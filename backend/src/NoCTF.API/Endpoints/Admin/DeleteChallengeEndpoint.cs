using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class DeleteChallengeRequest
{
    public Guid Id { get; set; }
}

public class DeleteChallengeEndpoint(ApplicationDbContext db) : Endpoint<DeleteChallengeRequest>, IAuditableEndpoint
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
        await using var cleanupTransaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        db.ChallengeTemplates.Remove(challenge);
        await StorageObjectCleanup.EnqueueAsync(db, storageKeys, ct);
        await db.SaveChangesAsync(ct);
        if (cleanupTransaction is not null)
            await cleanupTransaction.CommitAsync(ct);
        await SendNoContentAsync(ct);
    }
}
