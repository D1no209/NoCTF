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
    }

    public override async Task HandleAsync(GetKohDashboardRequest req, CancellationToken ct)
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
            .Where(c => c.CompetitionId == req.Id)
            .ToListAsync(ct);

        var teamIds = await dbContext.Teams
            .IgnoreQueryFilters()
            .Where(t =>
                t.CompetitionId == req.Id &&
                t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !t.IsBanned)
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var controlRecords = await dbContext.KohControlRecords
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == req.Id && teamIds.Keys.Contains(r.TeamId))
            .OrderBy(r => r.StartTime)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        var challengeStatuses = challenges.Select(challenge =>
        {
            var records = controlRecords
                .Where(r => r.ChallengeId == challenge.Id)
                .ToList();

            var activeRecord = records.FirstOrDefault(r => r.EndTime == null);

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
