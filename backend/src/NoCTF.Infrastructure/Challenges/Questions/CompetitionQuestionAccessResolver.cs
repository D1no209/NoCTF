using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Questions;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using static NoCTF.Infrastructure.Challenges.Questions.CompetitionQuestionStore;

namespace NoCTF.Infrastructure.Challenges.Questions;

public sealed class CompetitionQuestionAccessResolver(NoCtfDbContext db)
{
    internal async Task<ActorContext?> ResolveActorAsync(
        Guid competitionId,
        Guid actorUserId,
        CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(candidate => candidate.Id == actorUserId
                && candidate.AccountStatus == UserAccountStatus.Active)
            .Select(candidate => new { candidate.Role })
            .SingleOrDefaultAsync(ct);
        var competition = await db.Competitions.AsNoTracking()
            .Where(candidate => candidate.Id == competitionId)
            .Select(candidate => new
            {
                candidate.OwnerId,
                IsManager = candidate.Collaborators.Any(collaborator =>
                    collaborator.Role == CompetitionCollaboratorRole.Manager
                    && collaborator.UserId == actorUserId),
                IsJudge = candidate.Collaborators.Any(collaborator =>
                    collaborator.Role == CompetitionCollaboratorRole.Judge
                    && collaborator.UserId == actorUserId),
                IsObserver = candidate.Collaborators.Any(collaborator =>
                    collaborator.Role == CompetitionCollaboratorRole.Observer
                    && collaborator.UserId == actorUserId),
                candidate.MaxActiveQuestionsPerTeam,
                candidate.MaxParticipantMessagesBeforeHandlerReply,
                candidate.AllowChallengeOwnersToHandleQuestions
            })
            .SingleOrDefaultAsync(ct);
        if (user is null || competition is null)
            return null;
        var teamId = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                && team.Members.Any(member => member.UserId == actorUserId)
                && team.DeletedAt == null
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && !team.IsBanned)
            .Select(team => (Guid?)team.Id)
            .SingleOrDefaultAsync(ct);
        return new(
            user.Role == UserRole.Administrator,
            competition.OwnerId == actorUserId || competition.IsManager,
            competition.IsJudge,
            competition.IsObserver,
            teamId,
            competition.MaxActiveQuestionsPerTeam,
            competition.MaxParticipantMessagesBeforeHandlerReply,
            competition.AllowChallengeOwnersToHandleQuestions);
    }

    internal async Task<Guid[]> ResolveOwnedChallengeIdsAsync(
        Guid competitionId,
        Guid actorUserId,
        ActorContext actor,
        CancellationToken ct)
    {
        if (!actor.AllowChallengeOwnersToHandleQuestions
            || actor.IsPlatformAdministrator
            || actor.IsCompetitionManager
            || actor.IsJudge
            || actor.IsObserver)
            return [];
        return await db.CompetitionChallenges.AsNoTracking()
            .Where(binding => binding.CompetitionId == competitionId)
            .Join(
                db.Challenges.AsNoTracking(),
                binding => binding.ChallengeId,
                challenge => challenge.Id,
                (binding, challenge) => new { binding.Id, Challenge = challenge })
            .Where(item => item.Challenge.OwnerId == actorUserId
                || item.Challenge.Managers.Any(manager => manager.UserId == actorUserId))
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync(ct);
    }

    internal static AccessResolution? Resolve(
        QuestionAggregate question,
        ActorContext actor,
        IReadOnlyCollection<Guid> ownedChallengeIds)
    {
        if (actor.IsPlatformAdministrator)
            return new(CompetitionQuestionAccess.Handler,
                CompetitionQuestionParticipantRole.PlatformAdministrator);
        if (actor.IsCompetitionManager)
            return new(CompetitionQuestionAccess.Handler,
                CompetitionQuestionParticipantRole.CompetitionManager);
        if (actor.IsJudge)
            return new(CompetitionQuestionAccess.Handler,
                CompetitionQuestionParticipantRole.Judge);
        if (actor.IsObserver)
            return new(CompetitionQuestionAccess.Observer,
                CompetitionQuestionParticipantRole.Handler);
        if (actor.TeamId == question.Root.TeamId)
            return new(CompetitionQuestionAccess.Asker,
                CompetitionQuestionParticipantRole.Participant);
        if (actor.AllowChallengeOwnersToHandleQuestions
            && question.Root.CompetitionChallengeId is { } challengeId
            && ownedChallengeIds.Contains(challengeId))
            return new(CompetitionQuestionAccess.Handler,
                CompetitionQuestionParticipantRole.ChallengeOwner);
        return null;
    }

    internal async Task<AccessResolution?> ResolveAsync(
        QuestionAggregate question,
        Guid actorUserId,
        ActorContext actor,
        CancellationToken ct)
    {
        var direct = Resolve(question, actor, []);
        if (direct is not null || !actor.AllowChallengeOwnersToHandleQuestions
            || question.Root.CompetitionChallengeId is not { } challengeId)
            return direct;
        return await db.CompetitionChallenges.AsNoTracking().AnyAsync(binding =>
                binding.Id == challengeId
                && binding.CompetitionId == question.RootNotification.TargetId
                && db.Challenges.Any(challenge => challenge.Id == binding.ChallengeId
                    && (challenge.OwnerId == actorUserId
                        || challenge.Managers.Any(manager => manager.UserId == actorUserId))), ct)
            ? new(CompetitionQuestionAccess.Handler,
                CompetitionQuestionParticipantRole.ChallengeOwner)
            : null;
    }
}
