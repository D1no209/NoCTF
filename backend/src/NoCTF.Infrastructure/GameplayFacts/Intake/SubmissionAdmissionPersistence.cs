using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.GameModes.Registration;

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
                db.Teams.AsNoTracking().Where(team => team.MemberIds.Contains(userId)),
                item => item.Competition.Id,
                team => team.CompetitionId,
                (item, team) => new { item.Competition, item.CompetitionChallenge, item.Challenge, Team = team })
            .SingleOrDefaultAsync(cancellationToken);
        if (scope is null)
            return null;

        var officialWindow = scope.Competition.Mode == GameMode.Ctf
            ? await CompetitionOfficialWindowReader.ReadAsync(
                db,
                competitionId,
                scope.Competition.StartAt,
                scope.Competition.EndAt,
                cancellationToken)
            : CompetitionOfficialWindow.Resolve(
                scope.Competition.StartAt,
                scope.Competition.EndAt);
        var practicePhase = scope.Competition.Mode == GameMode.Ctf
            && scope.Competition.Status == CompetitionStatus.Finished;
        var attemptQuery = db.GameplayFacts.AsNoTracking()
            .Where(submission =>
                submission.CompetitionId == competitionId
                && submission.TeamId == scope.Team.Id
                && submission.CompetitionChallengeId == competitionChallengeId
                && submission.State != GameplayFactState.PlatformFailed);
        if (scope.Competition.Mode == GameMode.Ctf)
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
        if (practicePhase && scope.Competition.PracticeModeEnabled)
        {
            var template = runtimeTemplates.Get(scope.Competition.Mode, scope.Challenge.DefinitionJson);
            if (template is not null)
            {
                if (template.RuntimeKind is not (RuntimeKind.Container or RuntimeKind.Compose))
                {
                    practiceRuntimeState = PracticeRuntimeAdmissionState.Unsupported;
                }
                else
                {
                    var running = await db.RuntimeInstances.AsNoTracking().AnyAsync(instance =>
                        instance.CompetitionId == competitionId
                        && instance.CompetitionChallengeId == competitionChallengeId
                        && instance.TeamId == scope.Team.Id
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
            scope.Team.Id,
            competitionChallengeId,
            scope.Competition.Mode,
            scope.Competition.ConfigurationJson,
            scope.CompetitionChallenge.RulesJson,
            (flagAttempts?.Count ?? 0) + (breakAttempts?.Count ?? 0),
            fixAttempts?.Count ?? 0,
            scope.Competition.Status,
            scope.Competition.StartAt,
            scope.Competition.EndAt,
            scope.Competition.DeletedAt is not null,
            scope.Challenge.DeletedAt is not null || scope.CompetitionChallenge.DeletedAt is not null,
            scope.CompetitionChallenge.IsPublished,
            scope.Team.DeletedAt is not null,
            scope.Team.IsBanned,
            scope.Team.RegistrationStatus == TeamRegistrationStatus.Approved,
            true,
            breakAttempts?.HasCorrect == true,
            fixAttempts?.HasCorrect == true,
            flagAttempts?.HasCorrect == true,
            officialWindow.EndAt,
            scope.Competition.PracticeModeEnabled,
            practiceRuntimeState);
    }

    public static bool Matches(
        GameplayFactAdmissionSnapshot expected,
        GameplayFactAdmissionSnapshot? current) =>
        current is not null
        && current.CompetitionId == expected.CompetitionId
        && current.TeamId == expected.TeamId
        && current.CompetitionChallengeId == expected.CompetitionChallengeId
        && current.CompetitionStatus == expected.CompetitionStatus
        && current.OfficialEndAt == expected.OfficialEndAt
        && current.PracticeModeEnabled == expected.PracticeModeEnabled
        && current.PracticeRuntimeState == expected.PracticeRuntimeState
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
