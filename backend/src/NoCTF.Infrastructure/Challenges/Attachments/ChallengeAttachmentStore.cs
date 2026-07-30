using NoCTF.Infrastructure.Persistence;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Storage;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Challenges.Attachments;

public sealed class ChallengeAttachmentStore(NoCtfDbContext db) : IChallengeAttachmentStore
{
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
        StoredObject storedObject,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var challenge = await WriteAuthorized(actorId, isAdministrator)
            .SingleOrDefaultAsync(item => item.Id == challengeId, ct);
        if (challenge is null)
            return AddChallengeAttachmentState.ChallengeNotFound;
        if (await db.Set<ChallengeAttachment>().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(item => item.Id == attachmentId, ct))
            return AddChallengeAttachmentState.ResourceIdConflict;
        db.Set<ChallengeAttachment>().Add(new ChallengeAttachment
        {
            Id = attachmentId,
            ChallengeId = challengeId,
            ObjectKey = storedObject.ObjectKey,
            FileName = storedObject.FileName,
            ContentType = storedObject.ContentType,
            Length = storedObject.Length,
            Sha256Bytes = Convert.FromHexString(storedObject.Sha256),
            CreatedAt = now
        });
        challenge.Revision = checked(challenge.Revision + 1);
        challenge.UpdatedAt = now;
        try
        {
            await db.SaveChangesAsync(ct);
            return AddChallengeAttachmentState.Added;
        }
        catch (DbUpdateException)
        {
            return AddChallengeAttachmentState.ResourceIdConflict;
        }
    }

    public async Task<ChallengeAttachmentView?> UpdateAsync(
        Guid challengeId,
        Guid attachmentId,
        Guid actorId,
        bool isAdministrator,
        string fileName,
        string contentType,
        CancellationToken ct)
    {
        var challenge = await WriteAuthorized(actorId, isAdministrator)
            .Include(item => item.Attachments)
            .SingleOrDefaultAsync(item => item.Id == challengeId, ct);
        var attachment = challenge?.Attachments.SingleOrDefault(item =>
            item.Id == attachmentId && item.DeletedAt == null);
        if (attachment is null)
            return null;
        attachment.FileName = fileName;
        attachment.ContentType = contentType;
        challenge!.Revision = checked(challenge.Revision + 1);
        challenge.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Map(attachment);
    }

    public async Task<bool> DeleteAsync(
        Guid challengeId,
        Guid attachmentId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var challenge = await WriteAuthorized(actorId, isAdministrator)
            .Include(item => item.Attachments)
            .SingleOrDefaultAsync(item => item.Id == challengeId, ct);
        var attachment = challenge?.Attachments.SingleOrDefault(item =>
            item.Id == attachmentId && item.DeletedAt == null);
        if (attachment is null)
            return false;
        attachment.DeletedAt = now;
        challenge!.Revision = checked(challenge.Revision + 1);
        challenge.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
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
        var challenge = await WriteAuthorized(actorId, isAdministrator, includeDeleted: true)
            .Include(item => item.Attachments)
            .SingleOrDefaultAsync(item => item.Id == challengeId && item.DeletedAt == null, ct);
        var attachment = challenge?.Attachments.SingleOrDefault(item =>
            item.Id == attachmentId && item.DeletedAt != null);
        if (attachment is null)
            return false;
        attachment.DeletedAt = null;
        challenge!.Revision = checked(challenge.Revision + 1);
        challenge.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
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
        return attachments.Select(Map).ToArray();
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
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({scope.TeamId.ToString() + ":" + competitionChallengeId.ToString()}, 0))",
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

        var attachment = await db.Challenges.AsNoTracking()
            .Where(challenge => challenge.Id == scope.ChallengeId)
            .SelectMany(challenge => challenge.Attachments)
            .SingleOrDefaultAsync(item => item.Id == selectedId && item.DeletedAt == null, ct);
        if (attachment is null)
            return null;
        await transaction.CommitAsync(ct);
        return new(Map(attachment), attachment.ObjectKey);
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

    private static ChallengeAttachmentView Map(ChallengeAttachment attachment) =>
        new(
            attachment.Id, attachment.ChallengeId, attachment.FileName,
            attachment.ContentType, attachment.Length,
            Convert.ToHexString(attachment.Sha256Bytes), attachment.DeletedAt, attachment.CreatedAt);

    private sealed record PlayerScope(Guid TeamId, Guid ChallengeId, string ConfigurationJson);
}
