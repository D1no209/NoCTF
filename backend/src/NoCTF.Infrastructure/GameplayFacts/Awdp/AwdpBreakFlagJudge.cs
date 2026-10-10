using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.GameplayFacts.Awdp;

public sealed class AwdpBreakFlagJudge(NoCtfDbContext db) : IAwdpBreakFlagJudge
{
    public async Task<AwdpBreakFlagJudgementResult> JudgeAsync(
        JudgeAwdpBreakFlagCommand command,
        CancellationToken ct)
    {
        var challengeExists = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == command.CompetitionChallengeId
                && challenge.CompetitionId == command.CompetitionId
                && challenge.IsPublished
                && challenge.DeletedAt == null)
            .Join(
                db.Competitions.AsNoTracking().Where(competition =>
                    competition.Mode == GameMode.Awdp
                    && competition.DeletedAt == null),
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (_, _) => true)
            .AnyAsync(ct);
        if (!challengeExists)
        {
            return new(FailureCode:
                AwdpBreakFlagJudgementFailureCode.JudgementUnavailable);
        }

        var teamId = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == command.CompetitionId
                && team.Members.Any(member => member.UserId == command.UserId)
                && team.DeletedAt == null
                && !team.IsBanned
                && team.RegistrationStatus == TeamRegistrationStatus.Approved)
            .Select(team => (Guid?)team.Id)
            .SingleOrDefaultAsync(ct);
        if (teamId is null)
        {
            return new(FailureCode:
                AwdpBreakFlagJudgementFailureCode.TeamNotEligible);
        }

        var expectedHash = await db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == command.CompetitionId
                && fact.CompetitionChallengeId == command.CompetitionChallengeId
                && fact.TeamId == teamId
                && fact.Kind == GameplayFactKind.BreakAttempt
                && fact.State == GameplayFactState.Completed
                && fact.TimeEligibility == NoCTF.Domain.Challenges.GameplayFactTimeEligibility.Valid
                && (fact.Result == GameplayFactResult.Correct || fact.Result == GameplayFactResult.RightButDue)
                && fact.ValueSha256 != null)
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.Id)
            .Select(fact => fact.ValueSha256)
            .FirstOrDefaultAsync(ct);
        if (expectedHash is null)
        {
            return new(FailureCode:
                AwdpBreakFlagJudgementFailureCode.AchievementNotSucceeded);
        }

        var submittedHash = SHA256.HashData(Encoding.UTF8.GetBytes(command.Flag));
        return new(CryptographicOperations.FixedTimeEquals(expectedHash, submittedHash)
            ? AwdpBreakFlagJudgement.Correct
            : AwdpBreakFlagJudgement.Wrong);
    }
}
