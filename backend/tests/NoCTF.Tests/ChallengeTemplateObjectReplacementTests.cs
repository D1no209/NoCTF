using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NoCTF.API.Endpoints.Admin;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Tests;

public class ChallengeTemplateObjectReplacementTests
{
    [Fact]
    public Task ConcurrentAttachmentReplacement_QueuesActualDisplacedObjects()
        => VerifyConcurrentReplacementAsync(ChallengeTemplateObjectSlot.Attachment);

    [Fact]
    public Task ConcurrentPatchTemplateReplacement_QueuesActualDisplacedObjects()
        => VerifyConcurrentReplacementAsync(ChallengeTemplateObjectSlot.PatchTemplate);

    private static async Task VerifyConcurrentReplacementAsync(ChallengeTemplateObjectSlot slot)
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider(databaseRoot)
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), databaseRoot)
            .Options;
        var challengeId = Guid.NewGuid();
        const string oldKey = "templates/old-object.zip";
        await using (var seedDb = new ApplicationDbContext(options, new NullTenantContext()))
        {
            seedDb.ChallengeTemplates.Add(new ChallengeTemplate
            {
                Id = challengeId,
                Title = "Concurrent replacement",
                AttachmentStorageKey = slot == ChallengeTemplateObjectSlot.Attachment ? oldKey : "attachment/unchanged.zip",
                AttachmentUrl = "/old-attachment",
                PatchTemplateStorageKey = slot == ChallengeTemplateObjectSlot.PatchTemplate ? oldKey : null,
                PatchTemplateUrl = "/old-patch",
                DeploymentType = ChallengeDeploymentType.NoAttachment,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await seedDb.SaveChangesAsync();
        }

        await using var firstDb = new ApplicationDbContext(options, new NullTenantContext());
        await using var secondDb = new ApplicationDbContext(options, new NullTenantContext());
        // Reproduce endpoint contexts that observed the same old row before
        // their uploads completed.
        _ = await firstDb.ChallengeTemplates.SingleAsync(challenge => challenge.Id == challengeId);
        _ = await secondDb.ChallengeTemplates.SingleAsync(challenge => challenge.Id == challengeId);

        const string firstKey = "templates/upload-a.zip";
        const string secondKey = "templates/upload-b.zip";
        await Task.WhenAll(
            ChallengeTemplateObjectReplacement.ReplaceAsync(
                firstDb, challengeId, slot, firstKey, "/url-a", CancellationToken.None),
            ChallengeTemplateObjectReplacement.ReplaceAsync(
                secondDb, challengeId, slot, secondKey, "/url-b", CancellationToken.None));

        await using var verifyDb = new ApplicationDbContext(options, new NullTenantContext());
        var template = await verifyDb.ChallengeTemplates.AsNoTracking().SingleAsync(challenge => challenge.Id == challengeId);
        var finalKey = slot == ChallengeTemplateObjectSlot.Attachment
            ? template.AttachmentStorageKey
            : template.PatchTemplateStorageKey;
        var finalUrl = slot == ChallengeTemplateObjectSlot.Attachment
            ? template.AttachmentUrl
            : template.PatchTemplateUrl;
        Assert.Contains(finalKey, new[] { firstKey, secondKey });
        Assert.Equal(finalKey == firstKey ? "/url-a" : "/url-b", finalUrl);

        var losingKey = finalKey == firstKey ? secondKey : firstKey;
        var queuedKeys = await verifyDb.StorageCleanupItems
            .AsNoTracking()
            .Select(item => item.StorageKey)
            .OrderBy(key => key)
            .ToArrayAsync();
        Assert.Equal(new[] { oldKey, losingKey }.OrderBy(key => key), queuedKeys);
        Assert.DoesNotContain(finalKey, queuedKeys);

        if (slot == ChallengeTemplateObjectSlot.Attachment)
            Assert.Equal(ChallengeDeploymentType.StaticAttachment, template.DeploymentType);
        else
            Assert.Equal("attachment/unchanged.zip", template.AttachmentStorageKey);
    }

    private sealed class NullTenantContext : ITenantContext
    {
        public Guid? CompetitionId => null;
        public void SetCompetitionId(Guid? id) { }
    }
}
