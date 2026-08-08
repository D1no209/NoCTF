using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using System.Text.Json;

namespace NoCTF.Infrastructure.Challenges.Hints;

public sealed class ChallengeHintStore(
    NoCtfDbContext db,
    ILeaderboardProjectionEngine projection,
    ITransactionalMessageOutbox outbox,
    TeamChallengeCriticalSection criticalSection,
    ICompetitionEventRecorder? eventRecorder = null) : IChallengeHintStore
{
    public ChallengeHintStore(
        NoCtfDbContext db,
        ILeaderboardProjectionEngine projection,
        ITransactionalMessageOutbox outbox,
        ICompetitionEventRecorder? eventRecorder = null)
        : this(
            db,
            projection,
            outbox,
            new TeamChallengeCriticalSection(new LocalCriticalSectionRegistry()),
            eventRecorder) { }

    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<IReadOnlyList<ChallengeHintView>?> ListAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        bool includeDeleted,
        CancellationToken ct)
    {
        if (!await ScopeExistsAsync(competitionId, competitionChallengeId, ct))
            return null;
        var entity = await db.CompetitionChallenges
            .SingleAsync(challenge => challenge.Id == competitionChallengeId, ct);
        return entity.Hints.Where(hint => includeDeleted || hint.HiddenAt == null)
            .Select(hint => Map(hint, entity.Id, entity.UpdatedAt)).ToArray();
    }

    public async Task<ChallengeHintView?> FindAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid hintId,
        bool includeDeleted,
        CancellationToken ct)
    {
        if (!await ScopeExistsAsync(competitionId, competitionChallengeId, ct))
            return null;
        var entity = await db.CompetitionChallenges
            .SingleAsync(challenge => challenge.Id == competitionChallengeId, ct);
        var hint = entity.Hints.SingleOrDefault(item =>
            item.Id == hintId && (includeDeleted || item.HiddenAt == null));
        return hint is null ? null : Map(hint, entity.Id, entity.UpdatedAt);
    }

    public async Task<ChallengeHintSaveResult> SaveAsync(
        SaveChallengeHintCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await CompetitionStateReader.ReadAsync(db, command.CompetitionId, ct) is null)
            return new(null, ChallengeHintSaveFailure.ScopeNotFound);

        var challenge = await db.CompetitionChallenges
            .SingleOrDefaultAsync(item =>
                item.Id == command.CompetitionChallengeId &&
                item.CompetitionId == command.CompetitionId, ct);
        if (challenge is null)
            return new(null, ChallengeHintSaveFailure.ScopeNotFound);
        CompetitionChallengeHint hint;
        if (!command.IsCreate && command.HintId is Guid hintId)
        {
            hint = challenge.Hints.SingleOrDefault(item => item.Id == hintId && item.HiddenAt == null)
                ?? null!;
            if (hint is null)
                return new(null, ChallengeHintSaveFailure.HintNotFound);
        }
        else
        {
            var requestedId = command.HintId ?? Guid.CreateVersion7(command.Now);
            if (challenge.Hints.Any(item => item.Id == requestedId))
                return new(null, ChallengeHintSaveFailure.ResourceIdConflict);
            hint = new CompetitionChallengeHint
            {
                Id = requestedId
            };
            challenge.Hints.Add(hint);
        }
        var wasPublished = hint.PublishedAt is { } previousPublishedAt
            && previousPublishedAt <= command.Now;
        hint.Content = command.Content;
        hint.Cost = command.Cost;
        hint.PublishedAt = command.PublishedAt;
        challenge.Revision = checked(challenge.Revision + 1);
        challenge.UpdatedAt = command.Now;
        try
        {
            await db.SaveChangesAsync(ct);
            await LeaderboardRevision.IncrementAsync(db, command.CompetitionId, ct);
            await outbox.PublishAsync(new InvalidateLeaderboard(command.CompetitionId));
            await QueueHintPublicationAsync(
                command.CompetitionId,
                challenge,
                hint,
                command.Now,
                wasPublished,
                ct);
            await transaction.CommitAsync(ct);
            await outbox.FlushOutgoingMessagesAsync();
            return new(Map(hint, challenge.Id, challenge.UpdatedAt));
        }
        catch (DbUpdateException) when (command.IsCreate)
        {
            return new(null, ChallengeHintSaveFailure.ResourceIdConflict);
        }
    }

    public async Task<bool> DeleteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid hintId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await CompetitionStateReader.ReadAsync(db, competitionId, ct) is null)
            return false;

        var challenge = await db.CompetitionChallenges
            .SingleOrDefaultAsync(item =>
                item.Id == competitionChallengeId &&
                item.CompetitionId == competitionId, ct);
        var hint = challenge?.Hints.SingleOrDefault(item => item.Id == hintId && item.HiddenAt == null);
        if (hint is null)
            return false;
        hint.HiddenAt = now;
        challenge!.Revision = checked(challenge.Revision + 1);
        challenge.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await LeaderboardRevision.IncrementAsync(db, competitionId, ct);
        await outbox.PublishAsync(new InvalidateLeaderboard(competitionId));
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid hintId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await CompetitionStateReader.ReadAsync(db, competitionId, ct) is null)
            return false;

        var challenge = await db.CompetitionChallenges
            .SingleOrDefaultAsync(item =>
                item.Id == competitionChallengeId &&
                item.CompetitionId == competitionId, ct);
        var hint = challenge?.Hints.SingleOrDefault(item =>
            item.Id == hintId && item.HiddenAt != null);
        if (hint is null)
            return false;
        hint.HiddenAt = null;
        challenge!.Revision = checked(challenge.Revision + 1);
        challenge.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await LeaderboardRevision.IncrementAsync(db, competitionId, ct);
        await outbox.PublishAsync(new InvalidateLeaderboard(competitionId));
        await QueueHintPublicationAsync(
            competitionId,
            challenge,
            hint,
            now,
            wasPublished: false,
            ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return true;
    }

    public async Task<HintUnlockAttempt> UnlockAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid hintId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var teamId = await db.Teams.AsNoTracking()
            .Where(team =>
                team.CompetitionId == competitionId &&
                team.MemberIds.Contains(userId) &&
                team.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !team.IsBanned)
            .Select(team => (Guid?)team.Id)
            .SingleOrDefaultAsync(ct);
        if (teamId is null)
            return HintUnlockAttempt.Failed(HintUnlockFailure.NotFound);
        await using var unlockLease = await criticalSection.AcquireAsync(
            db,
            teamId.Value,
            competitionChallengeId,
            ct);
        var challenge = await db.CompetitionChallenges
            .SingleOrDefaultAsync(item =>
                item.Id == competitionChallengeId &&
                item.CompetitionId == competitionId &&
                item.IsPublished, ct);
        var hint = challenge?.Hints.SingleOrDefault(item =>
            item.Id == hintId &&
            item.HiddenAt == null &&
            item.PublishedAt != null &&
            item.PublishedAt <= now);
        if (hint is null)
            return HintUnlockAttempt.Failed(HintUnlockFailure.NotFound);
        var existing = await db.Submissions.AsNoTracking().Where(item =>
            item.CompetitionId == competitionId &&
            item.TeamId == teamId &&
            item.Kind == SubmissionKind.HintUnlock &&
            item.SubmittedFlag == hintId.ToString("D"))
            .OrderByDescending(item => item.ReceivedAt)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(ct);
        if (existing is Guid existingId)
            return HintUnlockAttempt.Success(new(existingId, false));

        var submissionId = Guid.CreateVersion7(now);
        db.Submissions.Add(new Submission
        {
            Id = submissionId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId.Value,
            SubmittedByUserId = userId,
            Kind = SubmissionKind.HintUnlock,
            SubmittedFlag = hintId.ToString("D"),
            ReceivedAt = now,
            EvaluationState = SubmissionEvaluationState.Queued,
            EvaluationUpdatedAt = now,
            ProcessingVersion = 0
        });
        await outbox.PublishAsync(new EvaluateSubmission(submissionId, 0));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return HintUnlockAttempt.Success(new(submissionId, true));
    }

    private async Task<long> AuthoritativeScoreAsync(
        Guid competitionId,
        Guid teamId,
        DateTimeOffset projectedAt,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking().SingleAsync(item => item.Id == competitionId, ct);
        var teams = await db.Teams.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CompetitionId == competitionId)
            .Select(item => new LeaderboardTeamFact(
                item.Id,
                item.Name,
                item.IsBanned,
                item.DeletedAt != null,
                item.RegisteredAt))
            .ToListAsync(ct);
        var challenges = await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CompetitionId == competitionId)
            .Join(db.Challenges.IgnoreQueryFilters().AsNoTracking(), item => item.ChallengeId, template => template.Id,
                (item, template) => new LeaderboardChallengeFact(
                    item.Id, template.Direction, template.Title, item.DeletedAt != null || template.DeletedAt != null,
                    item.RulesJson))
            .ToListAsync(ct);
        var submissions = await db.Submissions.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId && item.CurrentScoringEventId != null)
            .Join(db.ScoringEvents.AsNoTracking(), item => item.CurrentScoringEventId, fact => (Guid?)fact.Id,
                (item, fact) => new { Submission = item, ScoringEvent = fact })
            .Join(db.Users.AsNoTracking(), item => item.Submission.SubmittedByUserId, user => user.Id,
                (item, user) => new LeaderboardSubmissionFact(
                    item.Submission.Id, item.Submission.TeamId, item.Submission.CompetitionChallengeId,
                    item.Submission.Kind, item.Submission.ReceivedAt, item.ScoringEvent,
                    item.ScoringEvent.VictimTeamId, user.UserName,
                    item.Submission.SubmittedFlag))
            .ToListAsync(ct);
        var hintCosts = (await db.CompetitionChallenges.AsNoTracking()
                .Where(item => item.CompetitionId == competitionId)
                .ToListAsync(ct))
            .SelectMany(item => item.Hints)
            .ToDictionary(item => item.Id, item => item.Cost);
        var systemEvents = await db.ScoringEvents.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId && item.SubmissionId == null)
            .ToListAsync(ct);
        var system = systemEvents.Select(item => new LeaderboardSystemFact(
            item,
            item.Kind == ScoringEventKind.HintUnlock && item.SpecificationId is { } hintId
                ? hintCosts.GetValueOrDefault(hintId)
                : 0)).ToList();
        var lifecycleEvents = await db.CompetitionEvents.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId
                && item.Kind == CompetitionEventKind.CompetitionLifecycleChanged)
            .OrderBy(item => item.OccurredAt)
            .ToListAsync(ct);
        var lifecycleAudits = lifecycleEvents.Select(item =>
        {
            using var payload = JsonDocument.Parse(item.PayloadJson);
            return new CompetitionLifecycleTransition
            {
                Id = item.Id,
                CompetitionId = item.CompetitionId,
                From = Enum.Parse<CompetitionStatus>(payload.RootElement.GetProperty("from").GetString()!, true),
                To = Enum.Parse<CompetitionStatus>(payload.RootElement.GetProperty("to").GetString()!, true),
                ActorId = item.ActorUserId,
                OccurredAt = item.OccurredAt
            };
        }).ToList();
        IReadOnlyList<LeaderboardAwdRoundFact> awdRounds = [];
        if (competition.Mode == GameMode.Awd)
        {
            awdRounds = await db.ChallengeFlags.AsNoTracking()
                .Where(flag => flag.TeamId != null
                    && flag.CompetitionChallengeId != null
                    && flag.SpecificationKind == SpecificationKind.AwdRound
                    && flag.SpecificationId != null
                    && flag.ValidStart != null
                    && flag.ValidUntil != null
                    && flag.DeletedAt == null)
                .Join(
                    db.CompetitionChallenges.AsNoTracking()
                        .Where(item => item.CompetitionId == competitionId),
                    flag => flag.CompetitionChallengeId,
                    item => (Guid?)item.Id,
                    (flag, _) => new LeaderboardAwdRoundFact(
                        flag.CompetitionChallengeId!.Value,
                        flag.TeamId!.Value,
                        flag.SpecificationId!.Value,
                        flag.ValidStart!.Value,
                        flag.ValidUntil!.Value))
                .ToListAsync(ct);
        }
        var result = projection.Project(new(
            competitionId, competition.Mode, teams, submissions, system, challenges,
            competition.ConfigurationJson, competition.StartAt, lifecycleAudits,
            awdRounds, projectedAt));
        return result.Entries.SingleOrDefault(item => item.TeamId == teamId)?.Score ?? 0;
    }

    private async Task QueueHintPublicationAsync(
        Guid competitionId,
        CompetitionChallenge challenge,
        CompetitionChallengeHint hint,
        DateTimeOffset now,
        bool wasPublished,
        CancellationToken ct)
    {
        if (hint.PublishedAt is not { } publishedAt)
            return;
        if (publishedAt <= now && wasPublished)
            return;

        var challengeTitle = await db.Challenges.AsNoTracking()
            .Where(template => template.Id == challenge.ChallengeId)
            .Select(template => template.Title)
            .SingleAsync(ct);
        var message = new PublishHintNotification(
            competitionId,
            challenge.Id,
            hint.Id,
            challengeTitle,
            hint.Cost,
            publishedAt,
            challenge.Revision);
        if (publishedAt <= now)
            await outbox.PublishAsync(message);
        else
            await outbox.ScheduleAsync(message, publishedAt);
    }

    private Task<bool> ScopeExistsAsync(Guid competitionId, Guid competitionChallengeId, CancellationToken ct) =>
        db.CompetitionChallenges.AnyAsync(
            item => item.Id == competitionChallengeId && item.CompetitionId == competitionId, ct);

    private static ChallengeHintView Map(
        CompetitionChallengeHint hint,
        Guid competitionChallengeId,
        DateTimeOffset updatedAt) =>
        new(
            hint.Id, competitionChallengeId, hint.Content, hint.Cost,
            hint.PublishedAt, hint.HiddenAt, updatedAt, updatedAt);
}
