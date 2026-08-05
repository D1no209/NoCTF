using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Appeals;
using NoCTF.Domain.Teams;

namespace NoCTF.API.Endpoints.Teams;

public sealed class GetMyTeamBanCaseRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed record MyTeamBanAppealResponse(
    Guid Id,
    Guid SubmittedByUserId,
    string SubmittedByUserName,
    string Statement,
    DateTimeOffset SubmittedAt,
    TeamBanAppealStatus Status,
    Guid? ResolvedByUserId,
    string? ResolvedByUserName,
    string? ResolutionReason,
    DateTimeOffset? ResolvedAt);

public sealed record MyTeamBanCaseResponse(
    Guid BanEventId,
    Guid CompetitionId,
    Guid TeamId,
    string TeamName,
    TeamBanSource Source,
    DateTimeOffset BannedAt,
    bool IsCurrentlyBanned,
    bool CanAppeal,
    MyTeamBanAppealResponse? Appeal);

public sealed class GetMyTeamBanCaseEndpoint(
    GetMyTeamBanCase get,
    IUserContext user)
    : Endpoint<GetMyTeamBanCaseRequest,
        Results<Ok<MyTeamBanCaseResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/team-ban-case");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("GetMyTeamBanCase"));
        Summary(summary =>
        {
            summary.Summary = "Gets the signed-in member's latest team ban case.";
            summary.Description =
                "Returns the source category and private appeal state without exposing staff evidence.";
        });
    }

    public override async Task<Results<Ok<MyTeamBanCaseResponse>, NotFound>> ExecuteAsync(
        GetMyTeamBanCaseRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var banCase = await get.ExecuteAsync(
            request.CompetitionId,
            user.UserId,
            cancellationToken);
        return banCase is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(Map(banCase));
    }

    internal static MyTeamBanCaseResponse Map(TeamBanCaseView banCase) =>
        new(
            banCase.BanEventId,
            banCase.CompetitionId,
            banCase.TeamId,
            banCase.TeamName,
            banCase.Source,
            banCase.BannedAt,
            banCase.IsCurrentlyBanned,
            banCase.CanAppeal,
            banCase.Appeal is null
                ? null
                : new MyTeamBanAppealResponse(
                    banCase.Appeal.Id,
                    banCase.Appeal.SubmittedByUserId,
                    banCase.Appeal.SubmittedByUserName,
                    banCase.Appeal.Statement,
                    banCase.Appeal.SubmittedAt,
                    banCase.Appeal.Status,
                    banCase.Appeal.ResolvedByUserId,
                    banCase.Appeal.ResolvedByUserName,
                    banCase.Appeal.ResolutionReason,
                    banCase.Appeal.ResolvedAt));
}
