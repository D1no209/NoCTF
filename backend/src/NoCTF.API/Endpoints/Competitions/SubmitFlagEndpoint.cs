using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API;
using NoCTF.Core;
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
/// Delegates flag-like submissions to the competition's registered game mode.
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

        var competition = await dbContext.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.Id, ct);

        if (competition is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var gameMode = gameModes.FirstOrDefault(g => g.Type == competition.GameModeType);
        if (gameMode is null)
        {
            AuditLogWriter.Add(
                dbContext,
                HttpContext,
                "flag.submitted",
                "Challenge",
                req.ChallengeId.ToString(),
                new
                {
                    competitionId = req.Id,
                    challengeId = req.ChallengeId,
                    correct = false,
                    result = "mode_unavailable",
                    flagLength = req.Flag?.Length ?? 0
                },
                exception: "mode_unavailable");
            await dbContext.SaveChangesAsync(ct);
            await SendAsync(new SubmitFlagResponse { Correct = false, Result = "mode_unavailable" }, 503, ct);
            return;
        }

        // Find the user's team in this competition
        var teamMember = await dbContext.TeamMembers
            .AsNoTracking()
            .Join(dbContext.Teams.IgnoreQueryFilters().Where(t => t.CompetitionId == req.Id),
                  tm => tm.TeamId,
                  t => t.Id,
                  (tm, t) => new { tm.UserId, TeamId = t.Id, t.RegistrationStatus, t.IsBanned })
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

        if (teamMember is null)
        {
            AuditLogWriter.Add(
                dbContext,
                HttpContext,
                "flag.submitted",
                "Challenge",
                req.ChallengeId.ToString(),
                new
                {
                    competitionId = req.Id,
                    challengeId = req.ChallengeId,
                    correct = false,
                    result = "no_team",
                    flagLength = req.Flag?.Length ?? 0
                },
                exception: "no_team");
            await dbContext.SaveChangesAsync(ct);
            await SendAsync(new SubmitFlagResponse
            {
                Correct = false,
                AlreadySolved = false,
                Result = "no_team"
            }, 400, ct);
            return;
        }

        if (teamMember.RegistrationStatus != TeamRegistrationStatus.Approved)
        {
            AuditLogWriter.Add(
                dbContext,
                HttpContext,
                "flag.submitted",
                "Challenge",
                req.ChallengeId.ToString(),
                new
                {
                    competitionId = req.Id,
                    challengeId = req.ChallengeId,
                    teamId = teamMember.TeamId,
                    correct = false,
                    result = "team_not_approved",
                    flagLength = req.Flag?.Length ?? 0
                },
                exception: "team_not_approved");
            await dbContext.SaveChangesAsync(ct);
            await SendAsync(new SubmitFlagResponse
            {
                Correct = false,
                AlreadySolved = false,
                Result = "team_not_approved"
            }, 403, ct);
            return;
        }

        if (teamMember.IsBanned)
        {
            CompetitionLogWriter.Add(
                dbContext,
                req.Id,
                "flag.blocked_banned_team",
                "A banned team attempted to submit a flag.",
                "warning",
                teamMember.TeamId,
                userId,
                req.ChallengeId);
            AuditLogWriter.Add(
                dbContext,
                HttpContext,
                "flag.submitted",
                "Challenge",
                req.ChallengeId.ToString(),
                new
                {
                    competitionId = req.Id,
                    challengeId = req.ChallengeId,
                    teamId = teamMember.TeamId,
                    correct = false,
                    result = "team_banned",
                    flagLength = req.Flag?.Length ?? 0
                },
                exception: "team_banned");
            await dbContext.SaveChangesAsync(ct);
            await SendAsync(new SubmitFlagResponse
            {
                Correct = false,
                AlreadySolved = false,
                Result = "team_banned"
            }, 403, ct);
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

        var result = await gameMode.ProcessSubmissionAsync(context, ct);

        var response = result switch
        {
            SubmissionResult.Accepted => new SubmitFlagResponse { Correct = true, AlreadySolved = false, Result = "accepted" },
            SubmissionResult.AlreadySolved => new SubmitFlagResponse { Correct = true, AlreadySolved = true, Result = "already_solved" },
            SubmissionResult.WrongFlag => new SubmitFlagResponse { Correct = false, AlreadySolved = false, Result = "wrong_flag" },
            SubmissionResult.InstanceRequired => new SubmitFlagResponse { Correct = false, AlreadySolved = false, Result = "instance_required" },
            SubmissionResult.InstanceExpired => new SubmitFlagResponse { Correct = false, AlreadySolved = false, Result = "instance_expired" },
            SubmissionResult.AttemptsExhausted => new SubmitFlagResponse { Correct = false, AlreadySolved = false, Result = "attempts_exhausted" },
            SubmissionResult.FlagRateLimited => new SubmitFlagResponse { Correct = false, AlreadySolved = false, Result = "flag_rate_limited" },
            SubmissionResult.CompetitionNotStarted => new SubmitFlagResponse { Correct = false, AlreadySolved = false, Result = "competition_not_started" },
            SubmissionResult.CompetitionEnded => new SubmitFlagResponse { Correct = false, AlreadySolved = false, Result = "competition_ended" },
            _ => new SubmitFlagResponse { Correct = false, AlreadySolved = false, Result = result.ToString().ToLowerInvariant() }
        };

        AuditLogWriter.Add(
            dbContext,
            HttpContext,
            "flag.submitted",
            "Challenge",
            req.ChallengeId.ToString(),
            new
            {
                competitionId = req.Id,
                challengeId = req.ChallengeId,
                teamId = teamMember.TeamId,
                correct = response.Correct,
                alreadySolved = response.AlreadySolved,
                result = response.Result,
                flagLength = req.Flag?.Length ?? 0
            },
            exception: response.Correct ? null : response.Result);
        await dbContext.SaveChangesAsync(ct);

        await SendAsync(response, cancellation: ct);
    }
}
