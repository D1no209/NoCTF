using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Challenges;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Competitions.Progression;

namespace NoCTF.Infrastructure.GameplayFacts.Intake;

internal static class GameplayFactAdmissionPersistence
{
    public static Task<GameplayFactAdmissionSnapshot?> LoadAsync(
        NoCtfDbContext db,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken) =>
        LoadAsync(
            db,
            competitionId,
            competitionChallengeId,
            userId,
            TimeProvider.System.GetUtcNow(),
            new ChallengeRuntimeTemplateCatalog(),
            cancellationToken);

    public static async Task<GameplayFactAdmissionSnapshot?> LoadAsync(
        NoCtfDbContext db,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        DateTimeOffset observedAt,
        IChallengeRuntimeTemplateCatalog runtimeTemplates,
        CancellationToken cancellationToken)
    {
        var scope = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Join(
                db.CompetitionChallenges.AsNoTracking()
                    .Where(challenge => challenge.Id == competitionChallengeId),
                competition => competition.Id,
                challenge => challenge.CompetitionId,
                (competition, challenge) => new { Competition = competition, CompetitionChallenge = challenge })
            .Join(
                db.Challenges.AsNoTracking(),
                item => item.CompetitionChallenge.ChallengeId,
                challenge => challenge.Id,
                (item, challenge) => new { item.Competition, item.CompetitionChallenge, Challenge = challenge })
            .Join(
                db.Teams.AsNoTracking().Where(team => team.Members.Any(member => member.UserId == userId)),
                item => item.Competition.Id,
                team => team.CompetitionId,
                (item, team) => new
                {
                    item.Competition.Id,
                    item.Competition.Mode,
                    item.Competition.Status,
                    item.Competition.StartAt,
                    item.Competition.EndAt,
                    item.Competition.PracticeModeEnabled,
                    CompetitionDeleted = item.Competition.DeletedAt != null,
                    CompetitionConfiguration = item.Competition.ModeConfiguration,
                    CompetitionChallengeId = item.CompetitionChallenge.Id,
                    ChallengeRules = item.CompetitionChallenge.Rules,
                    ChallengePublished = item.CompetitionChallenge.IsPublished,
                    ChallengeDeleted = item.Challenge.DeletedAt != null
                        || item.CompetitionChallenge.DeletedAt != null,
                    ChallengeDefinition = item.Challenge.Definition,
                    TeamId = team.Id,
                    TeamDeleted = team.DeletedAt != null,
                    TeamBanned = team.IsBanned,
                    TeamStatus = team.RegistrationStatus
                })
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);
        if (scope is null)
            return null;
        if (scope.Mode == GameMode.Ctf
            && !await new ProgressionChallengeAccess(db).IsActiveAsync(
                competitionId, competitionChallengeId, scope.TeamId,
                cancellationToken))
            return null;

        var officialWindow = scope.Mode == GameMode.Ctf
            ? await CompetitionOfficialWindowReader.ReadAsync(
                db,
                competitionId,
                scope.StartAt,
                scope.EndAt,
                cancellationToken)
            : CompetitionOfficialWindow.Resolve(
                scope.StartAt,
                scope.EndAt);
        var practicePhase = scope.Mode == GameMode.Ctf
            && scope.Status == CompetitionStatus.Finished;
        var attemptQuery = db.GameplayFacts.AsNoTracking()
            .Where(submission =>
                submission.CompetitionId == competitionId
                && submission.TeamId == scope.TeamId
                && submission.CompetitionChallengeId == competitionChallengeId
                && submission.State != GameplayFactState.PlatformFailed);
        if (scope.Mode == GameMode.Ctf)
        {
            attemptQuery = practicePhase
                ? attemptQuery.Where(submission => submission.OccurredAt >= officialWindow.EndAt)
                : attemptQuery.Where(submission => submission.OccurredAt >= officialWindow.StartAt
                    && submission.OccurredAt < officialWindow.EndAt);
        }
        var attempts = await attemptQuery
            .GroupBy(submission => submission.Kind)
            .Select(group => new
            {
                Kind = group.Key,
                Count = group.Count(),
                HasCorrect = group.Any(fact => fact.Result == GameplayFactResult.Correct)
            })
            .ToDictionaryAsync(item => item.Kind, cancellationToken);
        var flagAttempts = attempts.GetValueOrDefault(GameplayFactKind.FlagAttempt);
        var breakAttempts = attempts.GetValueOrDefault(GameplayFactKind.BreakAttempt);
        var fixAttempts = attempts.GetValueOrDefault(GameplayFactKind.FixAttempt);
        var practiceRuntimeState = PracticeRuntimeAdmissionState.NotRequired;
        if (practicePhase && scope.PracticeModeEnabled)
        {
            var template = runtimeTemplates.Get(scope.ChallengeDefinition);
            if (template is not null)
            {
                if (template.RuntimeKind is not (RuntimeKind.Container))
                {
                    practiceRuntimeState = PracticeRuntimeAdmissionState.Unsupported;
                }
                else
                {
                    var running = await db.RuntimeInstances.AsNoTracking().AnyAsync(instance =>
                        instance.CompetitionId == competitionId
                        && instance.CompetitionChallengeId == competitionChallengeId
                        && instance.TeamId == scope.TeamId
                        && instance.Purpose == RuntimePurpose.Practice
                        && instance.State == RuntimeState.Running
                        && instance.ExpiresAt > observedAt,
                        cancellationToken);
                    practiceRuntimeState = running
                        ? PracticeRuntimeAdmissionState.Running
                        : PracticeRuntimeAdmissionState.NotRunning;
                }
            }
        }

        return new(
            competitionId,
            scope.TeamId,
            competitionChallengeId,
            scope.Mode,
            scope.CompetitionConfiguration
                ?? throw new InvalidOperationException("Competition configuration is required."),
            scope.ChallengeRules
                ?? throw new InvalidOperationException("Challenge rules are required."),
            (flagAttempts?.Count ?? 0) + (breakAttempts?.Count ?? 0),
            fixAttempts?.Count ?? 0,
            scope.Status,
            scope.StartAt,
            scope.EndAt,
            scope.CompetitionDeleted,
            scope.ChallengeDeleted,
            scope.ChallengePublished,
            scope.TeamDeleted,
            scope.TeamBanned,
            scope.TeamStatus == TeamRegistrationStatus.Approved,
            true,
            breakAttempts?.HasCorrect == true,
            fixAttempts?.HasCorrect == true,
            flagAttempts?.HasCorrect == true,
            officialWindow.EndAt,
            scope.PracticeModeEnabled,
            practiceRuntimeState,
            scope.ChallengeDefinition);
    }

    public static bool Matches(
        GameplayFactAdmissionSnapshot expected,
        GameplayFactAdmissionSnapshot? current) =>
        current is not null
        && current.CompetitionId == expected.CompetitionId
        && current.TeamId == expected.TeamId
        && current.CompetitionChallengeId == expected.CompetitionChallengeId
        && current.Mode == expected.Mode
        && current.CompetitionStatus == expected.CompetitionStatus
        && current.StartAt == expected.StartAt
        && current.EndAt == expected.EndAt
        && current.OfficialEndAt == expected.OfficialEndAt
        && current.PracticeModeEnabled == expected.PracticeModeEnabled
        && current.PracticeRuntimeState == expected.PracticeRuntimeState
        && ChallengeDefinitionStructuralComparer.Equals(
            current.ChallengeDefinition,
            expected.ChallengeDefinition)
        && current.CompetitionDeleted == expected.CompetitionDeleted
        && current.ChallengeDeleted == expected.ChallengeDeleted
        && current.ChallengePublished == expected.ChallengePublished
        && current.TeamDeleted == expected.TeamDeleted
        && current.TeamBanned == expected.TeamBanned
        && current.TeamApproved == expected.TeamApproved
        && current.UserBelongsToTeam == expected.UserBelongsToTeam
        && current.HasCorrectBreak == expected.HasCorrectBreak
        && current.HasCorrectFix == expected.HasCorrectFix;
}
