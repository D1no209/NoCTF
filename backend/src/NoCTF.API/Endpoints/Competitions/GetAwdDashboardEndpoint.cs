using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Competitions;

public class GetAwdDashboardRequest
{
    public Guid Id { get; set; }
}

public class ServiceStatusDto
{
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public Guid ChallengeId { get; set; }
    public string ChallengeName { get; set; } = string.Empty;
    public string Status { get; set; } = "unknown"; // "healthy" | "down" | "unknown"
}

public class AwdDashboardDto
{
    public Guid CompetitionId { get; set; }
    public int CurrentRound { get; set; }
    public int RoundDurationSeconds { get; set; }
    public int RemainingSeconds { get; set; }
    public List<ServiceStatusDto> Services { get; set; } = [];
}

/// <summary>
/// GET /api/competitions/{id}/awd-dashboard — returns current AWD round state and service statuses.
/// </summary>
public class GetAwdDashboardEndpoint(ApplicationDbContext dbContext)
    : Endpoint<GetAwdDashboardRequest, AwdDashboardDto>
{
    public override void Configure()
    {
        Get("/api/competitions/{id}/awd-dashboard");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetAwdDashboardRequest req, CancellationToken ct)
    {
        var competition = await dbContext.Competitions
            .IgnoreQueryFilters()
            .Where(c => c.Id == req.Id)
            .FirstOrDefaultAsync(ct);

        if (competition is null || !PublicCompetitionGuard.IsPublic(competition.Status))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        int roundDuration = competition.RoundDurationSeconds ?? 300;
        if (DateTime.UtcNow < competition.StartTime && competition.Status != CompetitionStatus.Finished)
        {
            await SendAsync(new AwdDashboardDto
            {
                CompetitionId = req.Id,
                CurrentRound = 0,
                RoundDurationSeconds = roundDuration,
                RemainingSeconds = 0,
                Services = []
            }, cancellation: ct);
            return;
        }

        // Current running round
        var currentRound = await dbContext.AwdRounds
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == req.Id && r.Status == AwdRoundStatus.Running)
            .OrderByDescending(r => r.RoundNumber)
            .FirstOrDefaultAsync(ct);

        int roundNumber = currentRound?.RoundNumber ?? 0;
        int remainingSeconds = 0;

        if (currentRound is not null)
        {
            var elapsed = (int)(DateTime.UtcNow - currentRound.StartTime).TotalSeconds;
            remainingSeconds = Math.Max(0, roundDuration - elapsed);
        }

        // Service statuses: for each GameBox, find latest CheckResult for current round
        var gameBoxes = await dbContext.AwdGameBoxes
            .IgnoreQueryFilters()
            .Where(gb => gb.CompetitionId == req.Id)
            .ToListAsync(ct);

        var teamIds = gameBoxes.Select(gb => gb.TeamId).Distinct().ToList();
        var challengeIds = gameBoxes.Select(gb => gb.ChallengeId).Distinct().ToList();

        var teams = await dbContext.Teams
            .IgnoreQueryFilters()
            .Where(t =>
                teamIds.Contains(t.Id) &&
                t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !t.IsBanned)
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var challenges = await dbContext.Challenges
            .IgnoreQueryFilters()
            .Where(c => challengeIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Title, ct);

        // Latest check results for current round (or most recent round if none running)
        var checkResults = roundNumber > 0
            ? await dbContext.AwdCheckResults
                .IgnoreQueryFilters()
                .Where(cr => cr.CompetitionId == req.Id && cr.RoundNumber == roundNumber)
                .ToListAsync(ct)
            : [];

        var checkLookup = checkResults
            .GroupBy(cr => (cr.TeamId, cr.ChallengeId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(cr => cr.CheckedAt).First());

        var services = gameBoxes.Select(gb =>
        {
            checkLookup.TryGetValue((gb.TeamId, gb.ChallengeId), out var result);
            string status = result?.Status switch
            {
                AwdCheckStatus.Healthy => "healthy",
                AwdCheckStatus.Down => "down",
                AwdCheckStatus.Error => "down",
                _ => "unknown"
            };

            return new ServiceStatusDto
            {
                TeamId = gb.TeamId,
                TeamName = teams.GetValueOrDefault(gb.TeamId, gb.TeamId.ToString()),
                ChallengeId = gb.ChallengeId,
                ChallengeName = challenges.GetValueOrDefault(gb.ChallengeId, gb.ChallengeId.ToString()),
                Status = status
            };
        }).ToList();

        await SendAsync(new AwdDashboardDto
        {
            CompetitionId = req.Id,
            CurrentRound = roundNumber,
            RoundDurationSeconds = roundDuration,
            RemainingSeconds = remainingSeconds,
            Services = services
        }, cancellation: ct);
    }
}
