using NoCTF.Infrastructure.Persistence;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Storage;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Challenges;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Infrastructure.Challenges.Attachments;

public sealed class ChallengeAttachmentStore(
    NoCtfDbContext db,
    TeamChallengeCriticalSection criticalSection,
    FileReferenceLock fileLock) : IChallengeAttachmentStore
{
    public ChallengeAttachmentStore(NoCtfDbContext db)
        : this(
            db,
            new TeamChallengeCriticalSection(new LocalCriticalSectionRegistry()),
            new FileReferenceLock()) { }

    public Task<bool> CanWriteAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct) =>
        WriteAuthorized(actorId, isAdministrator)
            .AsNoTracking()
            .AnyAsync(item => item.Id == challengeId, ct);

    public Task<bool> AttachmentIdExistsAsync(
        Guid attachmentId,
        CancellationToken ct) =>
        db.Set<ChallengeAttachment>().IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(item => item.Id == attachmentId, ct);

    public async Task<IReadOnlyList<ChallengeAttachmentView>?> ListAdminAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken ct)
    {
        var challenge = await WriteAuthorized(actorId, isAdministrator, includeDeleted)
            .Include(item => item.Attachments)
            .ThenInclude(item => item.File)
            .SingleOrDefaultAsync(item => item.Id == challengeId, ct);
        return challenge is null
            ? null
            : challenge.Attachments.Where(item => includeDeleted || item.DeletedAt == null)
                .OrderBy(item => item.CreatedAt).ThenBy(item => item.Id).Select(Map).ToArray();
    }

    public async Task<AddChallengeAttachmentState> AddAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        Guid attachmentId,
        Guid fileId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var challenge = await WriteAuthorized(actorId, isAdministrator)
            .SingleOrDefaultAsync(item => item.Id == challengeId, ct);
        if (challenge is null)
            return AddChallengeAttachmentState.ChallengeNotFound;
        if (await db.Set<ChallengeAttachment>().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(item => item.Id == attachmentId, ct))
            return AddChallengeAttachmentState.ResourceIdConflict;
        if (!await fileLock.AcquireAsync(db, fileId, ct))
            return AddChallengeAttachmentState.ResourceIdConflict;
        db.Set<ChallengeAttachment>().Add(new ChallengeAttachment
        {
            Id = attachmentId,
            ChallengeId = challengeId,
            FileId = fileId,
            CreatedAt = now
        });
        challenge.Revision = checked(challenge.Revision + 1);
        challenge.UpdatedAt = now;
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return AddChallengeAttachmentState.Added;
        }
        catch (DbUpdateException)
        {
            return AddChallengeAttachmentState.ResourceIdConflict;
        }
    }

    public async Task<bool> DeleteAsync(
        Guid challengeId,
        Guid attachmentId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var challenge = await WriteAuthorized(actorId, isAdministrator)
            .Include(item => item.Attachments)
            .ThenInclude(item => item.File)
            .SingleOrDefaultAsync(item => item.Id == challengeId, ct);
        var attachment = challenge?.Attachments.SingleOrDefault(item =>
            item.Id == attachmentId && item.DeletedAt == null);
        if (attachment is null)
            return false;
        attachment.DeletedAt = now;
        challenge!.Revision = checked(challenge.Revision + 1);
        challenge.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> RestoreAsync(
        Guid challengeId,
        Guid attachmentId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var challenge = await WriteAuthorized(actorId, isAdministrator, includeDeleted: true)
            .Include(item => item.Attachments)
            .ThenInclude(item => item.File)
            .SingleOrDefaultAsync(item => item.Id == challengeId && item.DeletedAt == null, ct);
        var attachment = challenge?.Attachments.SingleOrDefault(item =>
            item.Id == attachmentId && item.DeletedAt != null);
        if (attachment is null)
            return false;
        attachment.DeletedAt = null;
        challenge!.Revision = checked(challenge.Revision + 1);
        challenge.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<ChallengeAttachmentView>?> ListPlayerAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken ct)
    {
        var scope = await ResolvePlayerScopeAsync(competitionId, competitionChallengeId, userId, ct);
        if (scope is null || ReadPolicy(scope.ConfigurationJson) != AttachmentDeliveryPolicy.All)
            return null;
        var attachments = await db.Challenges.AsNoTracking()
            .Where(challenge => challenge.Id == scope.ChallengeId)
            .SelectMany(challenge => challenge.Attachments)
            .Where(attachment => attachment.DeletedAt == null)
            .OrderBy(attachment => attachment.CreatedAt)
            .ThenBy(attachment => attachment.Id)
            .ToListAsync(ct);
        var fileIds = attachments.Select(item => item.FileId).ToArray();
        var files = await db.Files.AsNoTracking().Where(file => fileIds.Contains(file.Id))
            .ToDictionaryAsync(file => file.Id, ct);
        return attachments.Select(item => Map(item, files[item.FileId])).ToArray();
    }

    public async Task<ChallengeAttachmentContent?> GetPlayerAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid? attachmentId,
        Guid userId,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var scope = await ResolvePlayerScopeAsync(competitionId, competitionChallengeId, userId, ct);
        if (scope is null)
            return null;
        var policy = ReadPolicy(scope.ConfigurationJson);
        if ((policy == AttachmentDeliveryPolicy.All) != attachmentId.HasValue)
            return null;

        Guid selectedId;
        if (attachmentId is Guid requested)
        {
            selectedId = requested;
        }
        else
        {
            await using var assignmentLease = await criticalSection.AcquireAsync(
                db,
                scope.TeamId,
                competitionChallengeId,
                ct);
            var existing = await db.ChallengeFlags.SingleOrDefaultAsync(flag =>
                flag.CompetitionChallengeId == competitionChallengeId &&
                flag.TeamId == scope.TeamId &&
                flag.SpecificationKind == SpecificationKind.Attachment, ct);
            if (existing is not null && existing.SpecificationId is Guid existingId)
            {
                selectedId = existingId;
            }
            else
            {
                var candidates = await db.ChallengeFlags.AsNoTracking()
                    .Where(flag =>
                        flag.ChallengeId == scope.ChallengeId &&
                        flag.SpecificationKind == SpecificationKind.Attachment &&
                        flag.SpecificationId != null)
                    .OrderBy(flag => flag.Id)
                    .ToListAsync(ct);
                if (candidates.Count == 0)
                    return null;
                var selected = candidates[RandomNumberGenerator.GetInt32(candidates.Count)];
                selectedId = selected.SpecificationId!.Value;
                db.ChallengeFlags.Add(new ChallengeFlag
                {
                    Id = Guid.CreateVersion7(),
                    CompetitionChallengeId = competitionChallengeId,
                    TeamId = scope.TeamId,
                    Flag = selected.Flag,
                    FlagSha256 = selected.FlagSha256,
                    SpecificationKind = SpecificationKind.Attachment,
                    SpecificationId = selectedId,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync(ct);
            }
        }

        var attachment = await db.Set<ChallengeAttachment>().AsNoTracking()
            .Include(item => item.File)
            .SingleOrDefaultAsync(item => item.Id == selectedId && item.DeletedAt == null, ct);
        if (attachment is null)
            return null;
        await transaction.CommitAsync(ct);
        return new(Map(attachment), attachment.File.ObjectKey);
    }

    private IQueryable<Challenge> WriteAuthorized(
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted = false)
    {
        var source = includeDeleted ? db.Challenges.IgnoreQueryFilters() : db.Challenges;
        return
        isAdministrator
            ? source
            : source.Where(challenge =>
                challenge.OwnerId == actorId || challenge.ManagerIds.Contains(actorId));
    }

    private async Task<PlayerScope?> ResolvePlayerScopeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken ct) =>
        await db.Teams.AsNoTracking()
            .Where(team =>
                team.CompetitionId == competitionId &&
                team.MemberIds.Contains(userId) &&
                !team.IsBanned &&
                team.RegistrationStatus == TeamRegistrationStatus.Approved)
            .Join(
                db.CompetitionChallenges.AsNoTracking(),
                team => team.CompetitionId,
                challenge => challenge.CompetitionId,
                (team, challenge) => new { Team = team, Challenge = challenge })
            .Join(
                db.Competitions.AsNoTracking(),
                pair => pair.Team.CompetitionId,
                competition => competition.Id,
                (pair, competition) => new { pair.Team, pair.Challenge, Competition = competition })
            .Where(item =>
                item.Challenge.Id == competitionChallengeId &&
                item.Challenge.IsPublished &&
                item.Competition.Status == CompetitionStatus.Running)
            .Select(item => new PlayerScope(
                item.Team.Id,
                item.Challenge.ChallengeId,
                item.Challenge.RulesJson))
            .SingleOrDefaultAsync(ct);

    private static AttachmentDeliveryPolicy ReadPolicy(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("attachmentPolicy", out var value) &&
            !document.RootElement.TryGetProperty("AttachmentPolicy", out value))
            return AttachmentDeliveryPolicy.All;
        return Enum.TryParse<AttachmentDeliveryPolicy>(value.GetString(), true, out var policy)
            ? policy
            : AttachmentDeliveryPolicy.All;
    }

    private static ChallengeAttachmentView Map(ChallengeAttachment attachment) => Map(attachment, attachment.File);

    private static ChallengeAttachmentView Map(
        ChallengeAttachment attachment,
        StoredFile file) =>
        new(
            attachment.Id, attachment.ChallengeId, file.FileName,
            file.ContentType, file.ByteLength,
            Convert.ToHexString(file.Sha256), attachment.DeletedAt, attachment.CreatedAt);

    private sealed record PlayerScope(Guid TeamId, Guid ChallengeId, string ConfigurationJson);
}
