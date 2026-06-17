using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Competitions;

public class SubmitFlagRequest
{
    public Guid Id { get; set; }
    public Guid ChallengeId { get; set; }
    public string Flag { get; set; } = string.Empty;
}

public class SubmitFlagResponse
{
    public bool Correct { get; set; }
    public bool AlreadySolved { get; set; }
    public string Result { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/competitions/{id}/challenges/{challengeId}/submit
/// Delegates to the CTF IGameMode plugin.
/// </summary>
public class SubmitFlagEndpoint(ApplicationDbContext dbContext, IEnumerable<IGameMode> gameModes)
    : Endpoint<SubmitFlagRequest, SubmitFlagResponse>
{
    public override void Configure()
    {
        Post("/api/competitions/{id}/challenges/{challengeId}/submit");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(SubmitFlagRequest req, CancellationToken ct)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var ctfGameMode = gameModes.FirstOrDefault(g => g.Type == GameModeType.Ctf);
        if (ctfGameMode is null)
        {
            await SendAsync(new SubmitFlagResponse { Correct = false, Result = "mode_unavailable" }, 503, ct);
            return;
        }

        // Find the user's team in this competition
        var teamMember = await dbContext.TeamMembers
            .AsNoTracking()
            .Join(dbContext.Teams.IgnoreQueryFilters().Where(t => t.CompetitionId == req.Id),
                  tm => tm.TeamId,
                  t => t.Id,
                  (tm, t) => new { tm.UserId, TeamId = t.Id })
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

        if (teamMember is null)
        {
            await SendAsync(new SubmitFlagResponse
            {
                Correct = false,
                AlreadySolved = false,
                Result = "no_team"
            }, 400, ct);
            return;
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var context = new SubmissionContext(
            CompetitionId: req.Id,
            TeamId: teamMember.TeamId,
            ChallengeId: req.ChallengeId,
            UserId: userId,
            FlagContent: req.Flag,
            IpAddress: ipAddress
        );

        var result = await ctfGameMode.ProcessSubmissionAsync(context, ct);

        var response = result switch
        {
            SubmissionResult.Accepted => new SubmitFlagResponse { Correct = true, AlreadySolved = false, Result = "accepted" },
            SubmissionResult.AlreadySolved => new SubmitFlagResponse { Correct = true, AlreadySolved = true, Result = "already_solved" },
            SubmissionResult.WrongFlag => new SubmitFlagResponse { Correct = false, AlreadySolved = false, Result = "wrong_flag" },
            _ => new SubmitFlagResponse { Correct = false, AlreadySolved = false, Result = result.ToString().ToLowerInvariant() }
        };

        await SendAsync(response, cancellation: ct);
    }
}
