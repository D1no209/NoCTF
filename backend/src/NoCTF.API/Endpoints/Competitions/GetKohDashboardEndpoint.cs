using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Competitions;

public class GetKohDashboardRequest
{
    public Guid Id { get; set; }
}

public class KohControlHistoryDto
{
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
}

public class KohChallengeStatusDto
{
    public Guid ChallengeId { get; set; }
    public string ChallengeName { get; set; } = string.Empty;
    public Guid? CurrentControllerTeamId { get; set; }
    public string? CurrentControllerTeamName { get; set; }
    public double ControlDurationSeconds { get; set; }
    public List<KohControlHistoryDto> History { get; set; } = [];
}

public class KohDashboardDto
{
    public Guid CompetitionId { get; set; }
    public List<KohChallengeStatusDto> Challenges { get; set; } = [];
}

/// <summary>
/// GET /api/competitions/{id}/koh-dashboard — returns current KoH controllers and control history.
/// </summary>
public class GetKohDashboardEndpoint(ApplicationDbContext dbContext)
    : Endpoint<GetKohDashboardRequest, KohDashboardDto>
{
    public override void Configure()
    {
        Get("/api/competitions/{id}/koh-dashboard");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting("public-read"));
    }

    public override async Task HandleAsync(GetKohDashboardRequest req, CancellationToken ct)
    {
        var competition = await dbContext.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == req.Id)
            .Select(c => new { c.Status, c.StartTime })
            .FirstOrDefaultAsync(ct);

        if (competition is null || !PublicCompetitionGuard.IsPublic(competition.Status))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (DateTime.UtcNow < competition.StartTime && competition.Status != CompetitionStatus.Finished)
        {
            await SendAsync(new KohDashboardDto
            {
                CompetitionId = req.Id,
                Challenges = []
            }, cancellation: ct);
            return;
        }

        var challenges = await dbContext.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.CompetitionId == req.Id && !c.IsDeleting)
            .Select(c => new { c.Id, c.Title })
            .ToListAsync(ct);

        var teamIds = await dbContext.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t =>
                t.CompetitionId == req.Id &&
                t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !t.IsBanned)
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var activeChallengeIds = challenges.Select(challenge => challenge.Id).ToArray();
        var historyCapacity = Math.Clamp(challenges.Count * 100, 100, 5_000);
        var eligibleControlRecords = dbContext.KohControlRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r =>
                r.CompetitionId == req.Id &&
                activeChallengeIds.Contains(r.ChallengeId) &&
                teamIds.Keys.Contains(r.TeamId));
        var activeControlRecords = await eligibleControlRecords
            .Where(record => record.EndTime == null)
            .ToListAsync(ct);
        var recentControlHistory = await eligibleControlRecords
            .Where(record => record.EndTime != null)
            .OrderByDescending(r => r.StartTime)
            .Take(historyCapacity)
            .ToListAsync(ct);
        var controlRecords = activeControlRecords
            .Concat(recentControlHistory)
            .DistinctBy(record => record.Id)
            .ToList();

        var now = DateTime.UtcNow;
        var recordsByChallenge = controlRecords.ToLookup(record => record.ChallengeId);

        var challengeStatuses = challenges.Select(challenge =>
        {
            var records = recordsByChallenge[challenge.Id]
                .OrderBy(r => r.StartTime)
                .ToList();

            // A legacy/racing writer may have left more than one open record.
            // Prefer the most recently observed controller instead of reporting
            // a stale predecessor while repair closes the duplicate row.
            var activeRecord = records.LastOrDefault(r => r.EndTime == null);

            double controlDuration = 0;
            if (activeRecord is not null)
                controlDuration = (now - activeRecord.StartTime).TotalSeconds;

            var history = records.Select(r => new KohControlHistoryDto
            {
                TeamId = r.TeamId,
                TeamName = teamIds.GetValueOrDefault(r.TeamId, r.TeamId.ToString()),
                StartTime = new DateTimeOffset(r.StartTime, TimeSpan.Zero),
                EndTime = r.EndTime.HasValue ? new DateTimeOffset(r.EndTime.Value, TimeSpan.Zero) : null
            }).ToList();

            return new KohChallengeStatusDto
            {
                ChallengeId = challenge.Id,
                ChallengeName = challenge.Title,
                CurrentControllerTeamId = activeRecord?.TeamId,
                CurrentControllerTeamName = activeRecord is not null
                    ? teamIds.GetValueOrDefault(activeRecord.TeamId, activeRecord.TeamId.ToString())
                    : null,
                ControlDurationSeconds = controlDuration,
                History = history
            };
        }).ToList();

        await SendAsync(new KohDashboardDto
        {
            CompetitionId = req.Id,
            Challenges = challengeStatuses
        }, cancellation: ct);
    }
}
