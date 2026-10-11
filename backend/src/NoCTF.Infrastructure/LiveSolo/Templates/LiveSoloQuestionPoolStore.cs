using System.Data;
using System.Text;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Challenges.Flags;
using NoCTF.GameModes.Registration;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.LiveSolo.Templates;
using NoCTF.Application.Messaging;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Templates;

public sealed class LiveSoloQuestionPoolStore(NoCtfDbContext db, ICompetitionModerationAuthorizer authorizer,
    IChallengeManagementStore challenges, IChallengeConfigurationCatalog configurations,
    LiveSoloMaterialCloner cloner, IPostCommitMessagePublisher messages) : ILiveSoloQuestionPoolStore
{
    private async Task<T> TransactionAsync<T>(Func<Task<T>> work, Func<T> conflict, CancellationToken ct) where T : ILiveSoloPoolOperation
    {
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var result = await work();
                if (result.Failure is not null)
                {
                    await transaction.RollbackAsync(ct); messages.DiscardPendingMessages(); db.ChangeTracker.Clear(); return result;
                }
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); committed = true;
                await messages.FlushCommittedMessagesAsync(); return result;
            }
            catch (Exception ex) when (!committed && attempt < 2 && TransactionFailureClassifier.IsRetryable(ex))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); }
            catch (Exception ex) when (!committed && TransactionFailureClassifier.IsRetryable(ex))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); return conflict(); }
            catch when (!committed) { messages.DiscardPendingMessages(); throw; }
        }
    }

    private Task<bool> ActiveAsync(Guid actor, CancellationToken ct) => db.Users.AnyAsync(x => x.Id == actor && x.AccountStatus == UserAccountStatus.Active, ct);
    private Task<bool> AvailableAsync(Guid competition, CancellationToken ct) => db.Competitions.AnyAsync(x => x.Id == competition && x.Mode == GameMode.LiveSolo, ct);
    private bool Valid(string title, string direction, LiveSoloChallengeDefinition definition) =>
        ChallengeTemplateValidation.Validate(GameMode.LiveSolo, title, direction, definition).Succeeded
        && configurations.ValidateDefinition(GameMode.LiveSolo, definition).Count == 0;
    private Task<bool> ActiveRoundAsync(Guid entry, CancellationToken ct) => db.LiveSoloRoundQuestions.Where(x => x.CompetitionChallengeId == entry)
        .Join(db.LiveSoloRounds, x => x.RoundId, x => x.Id, (_, round) => round)
        .AnyAsync(x => x.State == LiveSoloRoundState.Preparing || x.State == LiveSoloRoundState.Countdown
            || x.State == LiveSoloRoundState.Running || x.State == LiveSoloRoundState.ConfirmingResult, ct);

    public async Task<IReadOnlyList<LiveSoloPoolQuestion>?> ListAsync(Guid competitionId, Guid actorId, CancellationToken ct)
    {
        if (!await ActiveAsync(actorId, ct) || !await AvailableAsync(competitionId, ct)
            || !await authorizer.CanObserveAsync(actorId, competitionId, ct)) return null;
        var entries = await db.CompetitionChallenges.AsNoTracking().Where(x => x.CompetitionId == competitionId).OrderBy(x => x.Order).ToArrayAsync(ct);
        var ids = entries.Select(x => x.ChallengeId).ToArray();
        var templates = await db.Challenges.AsNoTracking().Include(x => x.Attachments).ThenInclude(x => x.File).Include(x => x.Managers).Where(x => ids.Contains(x.Id)).AsSplitQuery().ToDictionaryAsync(x => x.Id, ct);
        var owners = await db.Set<LiveSoloQuestionMaterial>().AsNoTracking().Where(x => x.CompetitionId == competitionId).ToDictionaryAsync(x => x.CompetitionChallengeId, ct);
        var groups = await db.Set<LiveSoloQuestionGroupItem>().AsNoTracking().Where(x => entries.Select(e => e.Id).Contains(x.CompetitionChallengeId))
            .ToArrayAsync(ct);
        var flags = await db.ChallengeFlags.AsNoTracking().Where(x => ids.Contains(x.ChallengeId!.Value) && x.TeamId == null).ToArrayAsync(ct);
        var actor = await db.Users.AsNoTracking().SingleAsync(x => x.Id == actorId, ct);
        bool CanEdit(CompetitionChallenge entry) => owners.ContainsKey(entry.Id) || actor.Role == UserRole.Administrator
            || templates[entry.ChallengeId].OwnerId == actorId || templates[entry.ChallengeId].Managers.Any(x => x.UserId == actorId)
            || !templates[entry.ChallengeId].Attachments.Any() && !flags.Any(x => x.ChallengeId == entry.ChallengeId);
        return entries.Where(x => templates.ContainsKey(x.ChallengeId)).Select(entry => Project(entry, templates[entry.ChallengeId],
            owners.GetValueOrDefault(entry.Id), groups.Where(x => x.CompetitionChallengeId == entry.Id).Select(x => x.QuestionGroupId).Distinct().ToArray()) with { CanEditContent = CanEdit(entry), Attachments = MapAttachments(templates[entry.ChallengeId]), Flags = CanEdit(entry) ? flags.Where(x => x.ChallengeId == entry.ChallengeId).Select(MapFlag).ToArray() : [] }).ToArray();
    }

    private static LiveSoloPoolQuestion Project(CompetitionChallenge entry, Challenge template, LiveSoloQuestionMaterial? owner, IReadOnlyList<Guid> groups) =>
        new(entry.Id, template.Id, entry.CustomTitle ?? template.Title, template.Description, template.Direction, entry.ConcurrencyStamp,
            template.ConcurrencyStamp, owner is null ? LiveSoloMaterialOwnership.FollowingQuestionBank : LiveSoloMaterialOwnership.CompetitionOwned,
            owner?.SourceTemplateId ?? (owner is null ? template.Id : null), (LiveSoloChallengeDefinition)template.Definition!, groups);
    private static IReadOnlyList<ChallengeAttachmentView> MapAttachments(Challenge template) => template.Attachments.Where(x => x.DeletedAt == null).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
        .Select(x => new ChallengeAttachmentView(x.Id, template.Id, x.File.FileName, x.File.ContentType, x.File.ByteLength, Convert.ToHexString(x.File.Sha256), null, x.DeletedAt, x.CreatedAt)).ToArray();
    private static ChallengeFlagView MapFlag(ChallengeFlag x) => new(x.Id, x.ChallengeId, x.CompetitionChallengeId, x.TeamId, x.Flag, x.MatchKind, x.SpecificationKind, x.SpecificationId, x.ValidStart, x.ValidUntil, x.DeletedAt, x.CreatedAt);
    private async Task<LiveSoloPoolQuestion> ProjectAsync(CompetitionChallenge entry, Challenge template, Guid actorId, CancellationToken ct)
    {
        await db.Entry(template).Collection(x => x.Attachments).Query().Include(x => x.File).LoadAsync(ct);
        await db.Entry(template).Collection(x => x.Managers).LoadAsync(ct);
        var ownership = await db.Set<LiveSoloQuestionMaterial>().SingleOrDefaultAsync(x => x.CompetitionChallengeId == entry.Id, ct);
        var flags = (await db.ChallengeFlags.Where(x => x.ChallengeId == template.Id && x.TeamId == null).ToArrayAsync(ct)).Select(MapFlag).ToArray();
        var role = await db.Users.Where(x => x.Id == actorId).Select(x => x.Role).SingleAsync(ct);
        var canEdit = ownership is not null || role == UserRole.Administrator || template.OwnerId == actorId
            || template.Managers.Any(x => x.UserId == actorId) || !template.Attachments.Any() && flags.Length == 0;
        return Project(entry, template, ownership,
            await db.Set<LiveSoloQuestionGroupItem>().Where(x => x.CompetitionChallengeId == entry.Id).Select(x => x.QuestionGroupId).Distinct().ToArrayAsync(ct))
            with { CanEditContent = canEdit, Attachments = MapAttachments(template), Flags = canEdit ? flags : [] };
    }
    public async Task<LiveSoloPoolFailure?> CheckEditAsync(Guid competitionId, Guid entryId, Guid actorId, Guid expectedStamp, Guid materialStamp, CancellationToken ct)
    {
        if (!await ActiveAsync(actorId, ct) || !await authorizer.CanModerateAsync(actorId, competitionId, ct)) return LiveSoloPoolFailure.Forbidden;
        var entry = await db.CompetitionChallenges.AsNoTracking().SingleOrDefaultAsync(x => x.Id == entryId && x.CompetitionId == competitionId && x.Mode == GameMode.LiveSolo, ct);
        if (entry is null) return LiveSoloPoolFailure.NotFound;
        var template = await db.Challenges.AsNoTracking().SingleOrDefaultAsync(x => x.Id == entry.ChallengeId, ct);
        if (template is null) return LiveSoloPoolFailure.NotFound;
        if (entry.ConcurrencyStamp != expectedStamp || template.ConcurrencyStamp != materialStamp) return LiveSoloPoolFailure.Conflict;
        return await ActiveRoundAsync(entryId, ct) ? LiveSoloPoolFailure.ActiveRound : null;
    }
    private async Task<int> NextOrderAsync(Guid competition, CancellationToken ct) =>
        (await db.CompetitionChallenges.IgnoreQueryFilters().Where(x => x.CompetitionId == competition).Select(x => (int?)x.Order).MaxAsync(ct) ?? -1) + 1;

    public Task<LiveSoloPoolResult> CreateAsync(CreateLiveSoloPoolQuestion command, CancellationToken ct) => TransactionAsync(async () =>
    {
        if (!await ActiveAsync(command.ActorId, ct) || !await authorizer.CanModerateAsync(command.ActorId, command.CompetitionId, ct)) return new LiveSoloPoolResult(null, LiveSoloPoolFailure.Forbidden);
        if (!await AvailableAsync(command.CompetitionId, ct)) return new(null, LiveSoloPoolFailure.NotFound);
        if (!Valid(command.Title, command.Direction, command.Definition)) return new(null, LiveSoloPoolFailure.UnsupportedDefinition);
        var owner = await db.Competitions.Where(x => x.Id == command.CompetitionId).Select(x => x.OwnerId).SingleAsync(ct);
        var template = new LiveSoloChallenge { Id = Guid.CreateVersion7(command.Now), OwnerId = owner, Visibility = ChallengeVisibility.Private, CatalogScope = ChallengeCatalogScope.CompetitionOwned,
            Title = command.Title.Trim(), Description = command.Description, Direction = command.Direction.Trim(), Definition = command.Definition,
            CreatedAt = command.Now, UpdatedAt = command.Now };
        ChallengeDefinitionGraph.AssignChallengeId(template.Definition, template.Id); db.Challenges.Add(template); await db.SaveChangesAsync(ct);
        var added = await challenges.CreateAsync(new(null, command.CompetitionId, template.Id, await NextOrderAsync(command.CompetitionId, ct), command.Now, MaterialScope: ChallengeCatalogScope.CompetitionOwned), new LiveSoloCompetitionChallengeRules(), ct);
        if (added.Challenge is null) throw new InvalidOperationException("Validated local material could not be added to its competition.");
        var entry = await db.CompetitionChallenges.SingleAsync(x => x.Id == added.Challenge.Id, ct);
        db.Add(new LiveSoloQuestionMaterial { CompetitionChallengeId = entry.Id, CompetitionId = entry.CompetitionId, ChallengeId = template.Id,
            CreatedByUserId = command.ActorId, CreatedAt = command.Now }); await db.SaveChangesAsync(ct);
        return new(await ProjectAsync(entry, template, command.ActorId, ct));
    }, () => new(null, LiveSoloPoolFailure.Conflict), ct);

    public Task<LiveSoloPoolResult> DeployAsync(DeployLiveSoloPoolQuestion command, CancellationToken ct) => TransactionAsync(async () =>
    {
        if (!await ActiveAsync(command.ActorId, ct) || !await authorizer.CanModerateAsync(command.ActorId, command.CompetitionId, ct)) return new LiveSoloPoolResult(null, LiveSoloPoolFailure.Forbidden);
        if (!await AvailableAsync(command.CompetitionId, ct)) return new(null, LiveSoloPoolFailure.NotFound);
        var source = await db.Challenges.Include(x => x.Managers).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == command.SourceTemplateId, ct);
        if (source is null || source.CatalogScope != ChallengeCatalogScope.QuestionBank) return new(null, LiveSoloPoolFailure.NotFound);
        var actor = await db.Users.SingleAsync(x => x.Id == command.ActorId, ct);
        if (source.Visibility != ChallengeVisibility.Shared && actor.Role != UserRole.Administrator
            && source.OwnerId != actor.Id && !source.Managers.Any(x => x.UserId == actor.Id)) return new(null, LiveSoloPoolFailure.Forbidden);
        if (source.Mode != GameMode.LiveSolo || source.Definition is not LiveSoloChallengeDefinition definition
            || !Valid(source.Title, source.Direction, definition)) return new(null, LiveSoloPoolFailure.UnsupportedDefinition);
        var existing = await db.CompetitionChallenges.SingleOrDefaultAsync(x => x.CompetitionId == command.CompetitionId
            && (x.ChallengeId == source.Id || db.Set<LiveSoloQuestionMaterial>().Any(m => m.CompetitionChallengeId == x.Id && m.SourceTemplateId == source.Id)), ct);
        if (existing is not null)
        {
            var material = existing.ChallengeId == source.Id ? source : await db.Challenges.SingleAsync(x => x.Id == existing.ChallengeId, ct);
            return new(await ProjectAsync(existing, material, command.ActorId, ct));
        }
        if (source.ConcurrencyStamp != command.ExpectedSourceStamp) return new(null, LiveSoloPoolFailure.SourceChanged);
        var added = await challenges.CreateAsync(new(null, command.CompetitionId, source.Id, await NextOrderAsync(command.CompetitionId, ct), command.Now), new LiveSoloCompetitionChallengeRules(), ct);
        if (added.Challenge is null) return new(null, LiveSoloPoolFailure.Conflict);
        return new(await ProjectAsync(await db.CompetitionChallenges.SingleAsync(x => x.Id == added.Challenge.Id, ct), source, command.ActorId, ct));
    }, () => new(null, LiveSoloPoolFailure.Conflict), ct);

    public Task<LiveSoloPoolResult> EditAsync(EditLiveSoloPoolQuestion command, CancellationToken ct) => TransactionAsync(async () =>
    {
        if (!await ActiveAsync(command.ActorId, ct) || !await authorizer.CanModerateAsync(command.ActorId, command.CompetitionId, ct)) return new LiveSoloPoolResult(null, LiveSoloPoolFailure.Forbidden);
        var entry = await db.CompetitionChallenges.SingleOrDefaultAsync(x => x.Id == command.CompetitionChallengeId && x.CompetitionId == command.CompetitionId && x.Mode == GameMode.LiveSolo, ct);
        if (entry is null) return new(null, LiveSoloPoolFailure.NotFound);
        var source = await db.Challenges.Include(x => x.Managers).Include(x => x.Attachments).AsSplitQuery().SingleAsync(x => x.Id == entry.ChallengeId, ct);
        if (entry.ConcurrencyStamp != command.ExpectedStamp || source.ConcurrencyStamp != command.ExpectedMaterialStamp) return new(null, LiveSoloPoolFailure.Conflict);
        if (!Valid(command.Title, command.Direction, command.Definition)) return new(null, LiveSoloPoolFailure.UnsupportedDefinition);
        var sourceFlags = await db.ChallengeFlags.Where(x => x.ChallengeId == source.Id && x.TeamId == null).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArrayAsync(ct);
        if (command.Flags is not null && (command.Flags.Count > 0 && !StaticFlagConfiguration.SupportsManualFlags(command.Definition, new ChallengeRuntimeTemplateCatalog())
            || sourceFlags.Any(x => x.SpecificationKind != null) || command.Flags.Count > 128
            || command.Flags.Any(x => !Enum.IsDefined(x.MatchKind) || Encoding.UTF8.GetByteCount(x.Flag) is < 1 or > 4096 || x.Flag.Contains('\0')
                || x.MatchKind == ChallengeFlagMatchKind.RegularExpression && !ChallengeFlagMatcher.IsValidRegularExpression(x.Flag)))) return new(null, LiveSoloPoolFailure.InvalidContent);
        var flagsChanged = command.Flags is not null && !sourceFlags.Select(x => new LiveSoloPoolFlagInput(x.Flag, x.MatchKind)).SequenceEqual(command.Flags);
        var removed = command.RemovedAttachmentIds?.Distinct().ToArray() ?? [];
        if (removed.Any(id => !source.Attachments.Any(x => x.Id == id && x.DeletedAt == null))) return new(null, LiveSoloPoolFailure.InvalidContent);
        var added = command.AddedAttachments ?? [];
        if (added.Any(x => x.FileId == Guid.Empty) || added.Select(x => x.FileId).Distinct().Count() != added.Count) return new(null, LiveSoloPoolFailure.InvalidContent);
        var existingRandom = sourceFlags.Any(x => x.SpecificationKind == SpecificationKind.Attachment);
        if (added.Any() && (added.Any(x => (x.ExactFlag != null) != (added[0].ExactFlag != null))
            || added[0].ExactFlag != null && (source.Attachments.Any(x => !removed.Contains(x.Id)) || sourceFlags.Any(x => x.SpecificationKind == null))
            || added[0].ExactFlag == null && existingRandom)) return new(null, LiveSoloPoolFailure.InvalidContent);
        if (!flagsChanged && removed.Length == 0 && added.Count == 0 && (entry.CustomTitle ?? source.Title) == command.Title.Trim() && source.Description == command.Description && source.Direction == command.Direction.Trim()
            && ChallengeDefinitionStructuralComparer.Equals(source.Definition, command.Definition)) return new(await ProjectAsync(entry, source, command.ActorId, ct));
        if (await ActiveRoundAsync(entry.Id, ct)) return new(null, LiveSoloPoolFailure.ActiveRound);
        IReadOnlyDictionary<Guid, Guid>? copiedAttachmentIds = null;
        var ownership = await db.Set<LiveSoloQuestionMaterial>().SingleOrDefaultAsync(x => x.CompetitionChallengeId == entry.Id, ct);
        if (ownership is null)
        {
            var actor = await db.Users.SingleAsync(x => x.Id == command.ActorId, ct);
            var writable = actor.Role == UserRole.Administrator || source.OwnerId == actor.Id || source.Managers.Any(x => x.UserId == actor.Id);
            if (!writable && (source.Attachments.Any(x => x.DeletedAt == null) || await db.ChallengeFlags.AnyAsync(x => x.ChallengeId == source.Id && x.DeletedAt == null, ct)))
                return new(null, LiveSoloPoolFailure.ProtectedMaterialsForbidden);
            var cloned = await cloner.CloneAsync(source, await db.Competitions.Where(x => x.Id == entry.CompetitionId).Select(x => x.OwnerId).SingleAsync(ct), true, true, command.Now, ct);
            if (cloned.Challenge is null) return new(null, cloned.Failure);
            copiedAttachmentIds = cloned.AttachmentIds;
            cloned.Challenge.CatalogScope = ChallengeCatalogScope.CompetitionOwned;
            ownership = new() { CompetitionChallengeId = entry.Id, CompetitionId = entry.CompetitionId, ChallengeId = cloned.Challenge.Id,
                SourceTemplateId = source.Id, SourceTemplateStamp = source.ConcurrencyStamp, CreatedByUserId = actor.Id, CreatedAt = command.Now };
            db.Add(ownership); source = cloned.Challenge; entry.ChallengeId = source.Id;
            await db.SaveChangesAsync(ct);
        }
        var flags = await db.ChallengeFlags.Where(x => x.ChallengeId == source.Id && x.TeamId == null).ToArrayAsync(ct);
        if (flagsChanged)
        {
            foreach (var flag in flags) flag.DeletedAt = command.Now;
            db.ChallengeFlags.AddRange(command.Flags!.Select(x => new TemplateChallengeFlag { Id = Guid.CreateVersion7(), ChallengeId = source.Id, Flag = x.Flag,
                FlagSha256 = ManageChallengeFlags.Hash(x.Flag), MatchKind = x.MatchKind, CreatedAt = command.Now }));
        }
        foreach (var originalId in removed)
        {
            var id = copiedAttachmentIds?.GetValueOrDefault(originalId) ?? originalId;
            var attachment = source.Attachments.Single(x => x.Id == id); attachment.DeletedAt = command.Now;
            foreach (var flag in flags.Where(x => x.SpecificationKind == SpecificationKind.Attachment && x.SpecificationId == id)) flag.DeletedAt = command.Now;
            await messages.PublishAsync(new NoCTF.Application.Messaging.CleanupFile(attachment.FileId));
        }
        foreach (var upload in added)
        {
            if (!await new NoCTF.Infrastructure.Storage.FileReferenceLock().AcquireAsync(db, upload.FileId, ct)) return new(null, LiveSoloPoolFailure.InvalidContent);
            var attachment = new ChallengeAttachment { Id = Guid.CreateVersion7(), ChallengeId = source.Id, FileId = upload.FileId, CreatedAt = command.Now };
            source.Attachments.Add(attachment); db.Set<ChallengeAttachment>().Add(attachment);
            if (upload.ExactFlag is not null) db.ChallengeFlags.Add(new TemplateChallengeFlag { Id = Guid.CreateVersion7(), ChallengeId = source.Id, Flag = upload.ExactFlag,
                FlagSha256 = ManageChallengeFlags.Hash(upload.ExactFlag), MatchKind = ChallengeFlagMatchKind.Exact,
                SpecificationKind = SpecificationKind.Attachment, SpecificationId = attachment.Id, CreatedAt = command.Now });
        }
        source.Title = command.Title.Trim(); source.Description = command.Description; source.Direction = command.Direction.Trim(); source.UpdatedAt = command.Now;
        if (!ChallengeDefinitionStructuralComparer.Equals(source.Definition, command.Definition))
        {
            if (source.Definition is not null) { db.Remove(source.Definition); await db.SaveChangesAsync(ct); }
            ChallengeDefinitionGraph.AssignChallengeId(command.Definition, source.Id); source.Definition = command.Definition; db.Add(command.Definition);
        }
        entry.CustomTitle = null; entry.UpdatedAt = command.Now;
        await db.SaveChangesAsync(ct); return new(await ProjectAsync(entry, source, command.ActorId, ct));
    }, () => new(null, LiveSoloPoolFailure.Conflict), ct);

    public Task<LiveSoloPoolExport> ExportAsync(ExportLiveSoloPoolQuestion command, CancellationToken ct) => TransactionAsync(async () =>
    {
        var actor = await db.Users.SingleOrDefaultAsync(x => x.Id == command.ActorId && x.AccountStatus == UserAccountStatus.Active, ct);
        if (actor is null || !actor.Role.CanManageResources() || !await authorizer.CanModerateAsync(command.ActorId, command.CompetitionId, ct)) return new LiveSoloPoolExport(null, LiveSoloPoolFailure.Forbidden);
        var entry = await db.CompetitionChallenges.SingleOrDefaultAsync(x => x.Id == command.CompetitionChallengeId && x.CompetitionId == command.CompetitionId && x.Mode == GameMode.LiveSolo, ct);
        if (entry is null) return new(null, LiveSoloPoolFailure.NotFound);
        if (!await db.Set<LiveSoloQuestionMaterial>().AnyAsync(x => x.CompetitionChallengeId == entry.Id, ct)) return new(null, LiveSoloPoolFailure.AlreadyInQuestionBank);
        var source = await db.Challenges.Include(x => x.Attachments).AsSplitQuery().SingleAsync(x => x.Id == entry.ChallengeId, ct);
        if (entry.ConcurrencyStamp != command.ExpectedStamp || source.ConcurrencyStamp != command.ExpectedMaterialStamp) return new(null, LiveSoloPoolFailure.Conflict);
        var cloned = await cloner.CloneAsync(source, actor.Id, true, true, command.Now, ct);
        return cloned.Challenge is null ? new(null, cloned.Failure) : new(cloned.Challenge.Id);
    }, () => new(null, LiveSoloPoolFailure.Conflict), ct);
}
