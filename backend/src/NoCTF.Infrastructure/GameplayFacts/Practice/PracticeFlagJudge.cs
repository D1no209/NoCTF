using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Practice;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.GameplayFacts.Practice;

public sealed class PracticeFlagJudge(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog runtimeTemplates) : IPracticeFlagJudge
{
    public PracticeFlagJudge(NoCtfDbContext db)
        : this(db, new NoCTF.GameModes.Registration.ChallengeRuntimeTemplateCatalog()) { }

    public async Task<PracticeFlagResult> JudgeAsync(
        JudgePracticeFlagCommand command,
        CancellationToken ct)
    {
        var scope = await db.CompetitionChallenges.AsNoTracking()
            .Where(item => item.Id == command.CompetitionChallengeId
                && item.CompetitionId == command.CompetitionId
                && item.IsPublished
                && item.DeletedAt == null)
            .Join(
                db.Competitions.AsNoTracking().Where(item =>
                    item.Status == CompetitionStatus.Finished
                    && item.Mode == GameMode.Ctf
                    && item.PracticeModeEnabled
                    && item.DeletedAt == null),
                item => item.CompetitionId,
                competition => competition.Id,
                (item, competition) => new { Instance = item, Competition = competition })
            .Join(
                db.Challenges.AsNoTracking().Where(item => item.DeletedAt == null),
                item => item.Instance.ChallengeId,
                template => template.Id,
                (item, template) => new { item.Instance, Template = template })
            .Select(item => new { item.Instance.ChallengeId, item.Template.DefinitionJson })
            .SingleOrDefaultAsync(ct);
        if (scope is null)
            return new(FailureCode: PracticeFlagFailureCode.PracticeUnavailable);

        var team = await db.Teams.AsNoTracking()
            .Where(item => item.CompetitionId == command.CompetitionId
                && item.MemberIds.Contains(command.UserId)
                && item.DeletedAt == null
                && !item.IsBanned
                && item.RegistrationStatus == TeamRegistrationStatus.Approved)
            .Select(item => new { item.Id })
            .SingleOrDefaultAsync(ct);
        if (team is null)
            return new(FailureCode: PracticeFlagFailureCode.TeamNotEligible);

        var running = await db.RuntimeInstances.AsNoTracking().AnyAsync(item =>
            item.CompetitionId == command.CompetitionId
            && item.CompetitionChallengeId == command.CompetitionChallengeId
            && item.TeamId == team.Id
            && item.Purpose == RuntimePurpose.Practice
            && item.State == RuntimeState.Running
            && item.ExpiresAt > command.SubmittedAt, ct);
        if (!running)
            return new(FailureCode: PracticeFlagFailureCode.RuntimeNotRunning);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(command.Flag));
        var supportsRegularExpression = runtimeTemplates
            .Get(GameMode.Ctf, scope.DefinitionJson)?.FlagSource
                is null or RuntimeFlagSource.Static;
        var candidates = await db.ChallengeFlags.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(flag => flag.DeletedAt == null
                && (flag.ChallengeId == scope.ChallengeId
                    || flag.CompetitionChallengeId == command.CompetitionChallengeId)
                && (flag.TeamId == null || flag.TeamId == team.Id)
                && (flag.ValidStart == null || flag.ValidStart <= command.SubmittedAt)
                && (flag.ValidUntil == null || command.SubmittedAt < flag.ValidUntil)
                && (flag.FlagSha256 == hash
                    || supportsRegularExpression
                    && flag.MatchKind == NoCTF.Domain.Challenges.ChallengeFlagMatchKind.RegularExpression))
            .ToArrayAsync(ct);
        var correct = candidates.Any(candidate =>
            ChallengeFlagMatcher.IsMatch(command.Flag, candidate));
        return new(correct ? PracticeFlagJudgement.Correct : PracticeFlagJudgement.Wrong);
    }
}
