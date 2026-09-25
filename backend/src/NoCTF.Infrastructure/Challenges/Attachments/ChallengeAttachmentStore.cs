using NoCTF.Infrastructure.Persistence;
using System.Security.Cryptography;
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
    FileReferenceLock fileLock,
    TimeProvider? clock = null) : IChallengeAttachmentStore
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;

    public ChallengeAttachmentStore(NoCtfDbContext db)
        : this(db, new FileReferenceLock()) { }

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

    public async Task<ChallengeAttachmentSet?> ListAdminAsync(
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
        if (challenge is null)
            return null;
        var flags = await (includeDeleted
                ? db.ChallengeFlags.IgnoreQueryFilters()
                : db.ChallengeFlags)
            .AsNoTracking()
            .Where(flag => flag.ChallengeId == challengeId
                && flag.SpecificationKind == SpecificationKind.Attachment
                && flag.SpecificationId != null
                && flag.MatchKind == ChallengeFlagMatchKind.Exact)
            .OrderBy(flag => flag.CreatedAt)
            .ThenBy(flag => flag.Id)
            .ToArrayAsync(ct);
        var flagsByAttachment = flags
            .GroupBy(flag => flag.SpecificationId!.Value)
            .ToDictionary(group => group.Key, group => group.First().Flag);
        var activeRandom = flags.Any(flag => flag.DeletedAt == null);
        return new(
            activeRandom
                ? AttachmentDeliveryPolicy.RandomOnePerTeam
                : AttachmentDeliveryPolicy.All,
            challenge.Attachments.Where(item => includeDeleted || item.DeletedAt == null)
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .Select(item => Map(
                    item,
                    item.File,
                    flagsByAttachment.GetValueOrDefault(item.Id)))
                .ToArray());
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
        var challenge = await LockWritableChallengeAsync(
            challengeId, actorId, isAdministrator, ct);
        if (challenge is null)
            return AddChallengeAttachmentState.ChallengeNotFound;
        if (await HasRandomCandidatesAsync(challengeId, ct))
            return AddChallengeAttachmentState.DeliveryModeConflict;
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

    public async Task<AddChallengeAttachmentState> AddBatchAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        IReadOnlyList<ChallengeAttachmentBatchEntry> entries,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var challenge = await LockWritableChallengeAsync(
            challengeId, actorId, isAdministrator, ct);
        if (challenge is null)
            return AddChallengeAttachmentState.ChallengeNotFound;
        if (await HasRandomCandidatesAsync(challengeId, ct))
            return AddChallengeAttachmentState.DeliveryModeConflict;
        if (entries.Count == 0
            || entries.Any(entry => entry.AttachmentId == Guid.Empty || entry.FileId == Guid.Empty)
            || entries.Select(entry => entry.AttachmentId).Distinct().Count() != entries.Count
            || entries.Select(entry => entry.FileId).Distinct().Count() != entries.Count
            || await db.Set<ChallengeAttachment>().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(attachment => entries.Select(entry => entry.AttachmentId)
                    .Contains(attachment.Id), ct))
            return AddChallengeAttachmentState.ResourceIdConflict;
        foreach (var entry in entries)
        {
            if (!await fileLock.AcquireAsync(db, entry.FileId, ct))
                return AddChallengeAttachmentState.ResourceIdConflict;
        }
        db.Set<ChallengeAttachment>().AddRange(entries.Select(entry => new ChallengeAttachment
        {
            Id = entry.AttachmentId,
            ChallengeId = challengeId,
            FileId = entry.FileId,
            CreatedAt = entry.CreatedAt
        }));
        challenge.UpdatedAt = entries.Max(entry => entry.CreatedAt);
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

    public async Task<AddChallengeAttachmentState> AddRandomBatchAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        string downloadFileName,
        IReadOnlyList<RandomAttachmentBatchEntry> entries,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var challenge = await LockWritableChallengeAsync(
            challengeId, actorId, isAdministrator, ct);
        if (challenge is null)
            return AddChallengeAttachmentState.ChallengeNotFound;

        var activeAttachments = await db.Set<ChallengeAttachment>().AsNoTracking()
            .Where(attachment => attachment.ChallengeId == challengeId
                && attachment.DeletedAt == null)
            .Join(
                db.Files.AsNoTracking(),
                attachment => attachment.FileId,
                file => file.Id,
                (attachment, file) => new { attachment.Id, file.FileName })
            .ToArrayAsync(ct);
        var activeFlags = await db.ChallengeFlags.AsNoTracking()
            .Where(flag => flag.ChallengeId == challengeId)
            .ToArrayAsync(ct);
        var candidateFlags = activeFlags.Where(flag =>
                flag.SpecificationKind == SpecificationKind.Attachment
                && flag.SpecificationId != null
                && flag.MatchKind == ChallengeFlagMatchKind.Exact)
            .ToArray();
        if (activeFlags.Length != candidateFlags.Length
            || activeAttachments.Any(attachment =>
                candidateFlags.Count(flag => flag.SpecificationId == attachment.Id) != 1)
            || activeAttachments.Any(attachment =>
                !string.Equals(attachment.FileName, downloadFileName, StringComparison.Ordinal)))
        {
            return AddChallengeAttachmentState.DeliveryModeConflict;
        }

        var existingFlags = candidateFlags.Select(flag => flag.Flag)
            .ToHashSet(StringComparer.Ordinal);
        if (entries.Select(entry => entry.ExactFlag).Any(flag => !existingFlags.Add(flag)))
            return AddChallengeAttachmentState.DuplicateFlag;
        if (entries.Any(entry => entry.AttachmentId == Guid.Empty || entry.FileId == Guid.Empty)
            || entries.Select(entry => entry.AttachmentId).Distinct().Count() != entries.Count
            || entries.Select(entry => entry.FileId).Distinct().Count() != entries.Count
            || await db.Set<ChallengeAttachment>().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(attachment => entries.Select(entry => entry.AttachmentId)
                    .Contains(attachment.Id), ct))
        {
            return AddChallengeAttachmentState.ResourceIdConflict;
        }
        foreach (var entry in entries)
        {
            if (!await fileLock.AcquireAsync(db, entry.FileId, ct))
                return AddChallengeAttachmentState.ResourceIdConflict;
        }

        db.Set<ChallengeAttachment>().AddRange(entries.Select(entry => new ChallengeAttachment
        {
            Id = entry.AttachmentId,
            ChallengeId = challengeId,
            FileId = entry.FileId,
            CreatedAt = entry.CreatedAt
        }));
        db.ChallengeFlags.AddRange(entries.Select(entry => new TemplateChallengeFlag
        {
            Id = Guid.CreateVersion7(entry.CreatedAt),
            ChallengeId = challengeId,
            Flag = entry.ExactFlag,
            FlagSha256 = NoCTF.Application.Challenges.Flags.ManageChallengeFlags.Hash(entry.ExactFlag),
            MatchKind = ChallengeFlagMatchKind.Exact,
            SpecificationKind = SpecificationKind.Attachment,
            SpecificationId = entry.AttachmentId,
            CreatedAt = entry.CreatedAt
        }));
        challenge.UpdatedAt = entries.Max(entry => entry.CreatedAt);
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
        var challenge = await LockWritableChallengeAsync(challengeId, actorId, isAdministrator, ct);
        if (challenge is null)
            return false;
        var attachment = await db.Set<ChallengeAttachment>().SingleOrDefaultAsync(item =>
            item.ChallengeId == challengeId
            && item.Id == attachmentId, ct);
        if (attachment is null)
            return false;
        attachment.DeletedAt = now;
        var linkedCandidates = await db.ChallengeFlags.Where(flag =>
                flag.ChallengeId == challengeId
                && flag.TeamId == null
                && flag.SpecificationKind == SpecificationKind.Attachment
                && flag.SpecificationId == attachmentId)
            .ToArrayAsync(ct);
        foreach (var candidate in linkedCandidates)
            candidate.DeletedAt = now;
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
        var challenge = await LockWritableChallengeAsync(challengeId, actorId, isAdministrator, ct);
        if (challenge is null)
            return false;
        var attachment = await db.Set<ChallengeAttachment>().IgnoreQueryFilters()
            .SingleOrDefaultAsync(item =>
                item.ChallengeId == challengeId
                && item.Id == attachmentId
                && item.DeletedAt != null, ct);
        if (attachment is null)
            return false;
        attachment.DeletedAt = null;
        var linkedCandidates = await db.ChallengeFlags.IgnoreQueryFilters().Where(flag =>
                flag.ChallengeId == challengeId
                && flag.TeamId == null
                && flag.SpecificationKind == SpecificationKind.Attachment
                && flag.SpecificationId == attachmentId
                && flag.DeletedAt != null)
            .ToArrayAsync(ct);
        foreach (var candidate in linkedCandidates)
            candidate.DeletedAt = null;
        challenge.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<ChallengeAttachmentSet?> ListPlayerAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken ct)
    {
        var scope = await ResolvePlayerScopeAsync(competitionId, competitionChallengeId, userId, ct);
        if (scope is null)
            return null;
        if (await HasRandomCandidatesAsync(scope.ChallengeId, ct))
            return new(AttachmentDeliveryPolicy.RandomOnePerTeam, []);
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
        return new(
            AttachmentDeliveryPolicy.All,
            attachments.Select(item => Map(item, files[item.FileId])).ToArray());
    }

    public async Task<ChallengeAttachmentContent?> GetPlayerAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid? attachmentId,
        Guid userId,
        Func<string, CancellationToken, Task<bool>> objectExistsAsync,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var scope = await ResolvePlayerScopeAsync(competitionId, competitionChallengeId, userId, ct);
        if (scope is null)
            return null;
        var policy = await HasRandomCandidatesAsync(scope.ChallengeId, ct)
            ? AttachmentDeliveryPolicy.RandomOnePerTeam
            : AttachmentDeliveryPolicy.All;
        if ((policy == AttachmentDeliveryPolicy.All) != attachmentId.HasValue)
            return null;

        Guid selectedId;
        ChallengeFlag? pendingAssignment = null;
        if (attachmentId is Guid requested)
        {
            selectedId = requested;
        }
        else
        {
            await AcquireCompetitionChallengeAssignmentLockAsync(competitionChallengeId, ct);
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
                        flag.SpecificationId != null &&
                        flag.MatchKind == ChallengeFlagMatchKind.Exact &&
                        db.Set<ChallengeAttachment>().Any(attachment =>
                            attachment.Id == flag.SpecificationId &&
                            attachment.ChallengeId == scope.ChallengeId &&
                            attachment.DeletedAt == null))
                    .OrderBy(flag => flag.Id)
                    .ToListAsync(ct);
                if (candidates.Count == 0)
                    return null;
                var assignedIds = await db.ChallengeFlags.AsNoTracking()
                    .Where(flag => flag.CompetitionChallengeId == competitionChallengeId
                        && flag.TeamId != null
                        && flag.SpecificationKind == SpecificationKind.Attachment
                        && flag.SpecificationId != null)
                    .Select(flag => flag.SpecificationId!.Value)
                    .Distinct()
                    .ToArrayAsync(ct);
                var unused = candidates.Where(candidate =>
                        !assignedIds.Contains(candidate.SpecificationId!.Value))
                    .ToArray();
                var selectionPool = unused.Length > 0 ? unused : candidates.ToArray();
                var selected = selectionPool[RandomNumberGenerator.GetInt32(selectionPool.Length)];
                selectedId = selected.SpecificationId!.Value;
                pendingAssignment = new TeamChallengeFlag
                {
                    Id = Guid.CreateVersion7(),
                    CompetitionChallengeId = competitionChallengeId,
                    TeamId = scope.TeamId,
                    Flag = selected.Flag,
                    FlagSha256 = selected.FlagSha256,
                    MatchKind = selected.MatchKind,
                    SpecificationKind = SpecificationKind.Attachment,
                    SpecificationId = selectedId,
                    CreatedAt = timeProvider.GetUtcNow()
                };
            }
        }

        var attachment = await db.Set<ChallengeAttachment>().IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.File)
            .SingleOrDefaultAsync(item =>
                item.Id == selectedId &&
                item.ChallengeId == scope.ChallengeId &&
                (policy == AttachmentDeliveryPolicy.RandomOnePerTeam || item.DeletedAt == null), ct);
        if (attachment is null)
            return null;
        if (!await objectExistsAsync(attachment.File.ObjectKey, ct))
            return null;
        if (pendingAssignment is not null)
        {
            db.ChallengeFlags.Add(pendingAssignment);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                db.Entry(pendingAssignment).State = EntityState.Detached;
                await transaction.RollbackAsync(ct);
                var winningAttachmentId = await db.ChallengeFlags.AsNoTracking()
                    .Where(flag =>
                        flag.CompetitionChallengeId == competitionChallengeId
                        && flag.TeamId == scope.TeamId
                        && flag.SpecificationKind == SpecificationKind.Attachment
                        && flag.SpecificationId != null)
                    .Select(flag => flag.SpecificationId!.Value)
                    .SingleOrDefaultAsync(ct);
                if (winningAttachmentId == Guid.Empty)
                    throw;
                var winningAttachment = await db.Set<ChallengeAttachment>()
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(item => item.File)
                    .SingleOrDefaultAsync(item =>
                        item.Id == winningAttachmentId
                        && item.ChallengeId == scope.ChallengeId, ct);
                if (winningAttachment is null
                    || !await objectExistsAsync(winningAttachment.File.ObjectKey, ct))
                    return null;
                return new(Map(winningAttachment), winningAttachment.File.ObjectKey);
            }
        }
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
                challenge.OwnerId == actorId || challenge.Managers.Any(manager => manager.UserId == actorId));
    }

    private async Task<PlayerScope?> ResolvePlayerScopeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken ct) =>
        await db.Teams.AsNoTracking()
            .Where(team =>
                team.CompetitionId == competitionId &&
                team.Members.Any(member => member.UserId == userId) &&
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
                (item.Competition.Status == CompetitionStatus.Running
                    || item.Competition.Status == CompetitionStatus.Finished
                        && item.Competition.Mode == GameMode.Ctf
                        && item.Competition.PracticeModeEnabled))
            .Select(item => new PlayerScope(
                item.Team.Id,
                item.Challenge.ChallengeId))
            .SingleOrDefaultAsync(ct);

    private Task<bool> HasRandomCandidatesAsync(Guid challengeId, CancellationToken ct) =>
        db.ChallengeFlags.AsNoTracking().AnyAsync(flag =>
            flag.ChallengeId == challengeId
            && flag.SpecificationKind == SpecificationKind.Attachment
            && flag.SpecificationId != null
            && flag.MatchKind == ChallengeFlagMatchKind.Exact,
            ct);

    private async Task<Challenge?> LockWritableChallengeAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        var challenge = await ChallengeTemplateCriticalSection.AcquireAsync(db, challengeId, ct);
        return challenge is not null
            && challenge.DeletedAt is null
            && (isAdministrator
                || challenge.OwnerId == actorId
                || challenge.Managers.Any(manager => manager.UserId == actorId))
            ? challenge
            : null;
    }

    private async Task AcquireCompetitionChallengeAssignmentLockAsync(
        Guid competitionChallengeId,
        CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var exists = await db.CompetitionChallenges
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(item => item.Id == competitionChallengeId, budget.Token);
            if (!exists)
                throw new DbUpdateConcurrencyException("The attachment assignment scope no longer exists.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new FeatureCriticalSectionTimeoutException("attachment-assignment");
        }
    }

    private static ChallengeAttachmentView Map(ChallengeAttachment attachment) =>
        Map(attachment, attachment.File, null);

    private static ChallengeAttachmentView Map(
        ChallengeAttachment attachment,
        StoredFile file,
        string? exactFlag = null) =>
        new(
            attachment.Id, attachment.ChallengeId, file.FileName,
            file.ContentType, file.ByteLength,
            Convert.ToHexString(file.Sha256), exactFlag, attachment.DeletedAt, attachment.CreatedAt);

    private sealed record PlayerScope(Guid TeamId, Guid ChallengeId);
}
