using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Competitions.Lifecycle;

public sealed class CompetitionStartGateStore(NoCtfDbContext db)
    : ICompetitionStartGateStore
{
    public async Task<CompetitionStartGateSnapshot?> LoadAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == competitionId && item.DeletedAt == null, ct);
        if (competition?.ModeConfiguration is null)
            return null;
        var challengeRows = await db.CompetitionChallenges.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId
                && item.DeletedAt == null)
            .Join(
                db.Challenges.AsNoTracking(),
                item => item.ChallengeId,
                template => template.Id,
                (item, template) => new { Instance = item, Template = template })
            .AsSplitQuery()
            .ToArrayAsync(ct);
        var challenges = challengeRows.Select(row => new StartGateChallenge(
            row.Instance.Id,
            row.Template.Mode,
            row.Instance.Rules ?? throw new InvalidOperationException(
                $"Competition challenge {row.Instance.Id} has no rules."),
            row.Template.Definition ?? throw new InvalidOperationException(
                $"Challenge {row.Template.Id} has no definition."),
            row.Instance.IsPublished,
            row.Instance.Hints.Select(hint => hint.Cost).ToArray())).ToArray();
        var teamTracks = await db.Teams.AsNoTracking()
            .Where(
            team => team.CompetitionId == competitionId
                && team.DeletedAt == null
                && !team.IsBanned
                && team.RegistrationStatus == TeamRegistrationStatus.Approved)
            .Select(team => team.TrackKey)
            .ToArrayAsync(ct);
        return new(
            competition.Id,
            competition.Mode,
            competition.Status,
            competition.ModeConfiguration,
            challenges,
            teamTracks.Length,
            competition.MaxConcurrentRuntimeInstancesPerTeam,
            competition.Tracks,
            teamTracks,
            competition.TracksEnabled);
    }
}
