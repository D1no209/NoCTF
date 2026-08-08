using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Teams;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Appeals;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Teams;

namespace NoCTF.API.Endpoints.Administration.Teams;

public sealed class ListTeamBanAppealsRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed record AdminTeamBanAppealResponse(
    Guid Id,
    Guid SubmittedByUserId,
    string SubmittedByUserName,
    string Statement,
    DateTimeOffset SubmittedAt,
    TeamBanAppealStatusProtocol Status,
    Guid? ResolvedByUserId,
    string? ResolvedByUserName,
    string? ResolutionReason,
    DateTimeOffset? ResolvedAt);

public sealed record AdminTeamBanCaseResponse(
    Guid BanEventId,
    Guid CompetitionId,
    Guid TeamId,
    string TeamName,
    TeamBanSourceProtocol Source,
    DateTimeOffset BannedAt,
    bool IsCurrentlyBanned,
    bool CanResolve,
    AdminTeamBanAppealResponse Appeal);

public sealed record AdminTeamBanAppealListResponse(
    IReadOnlyList<AdminTeamBanCaseResponse> Items);

public sealed class ListTeamBanAppealsEndpoint(
    ListTeamBanAppeals list,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<ListTeamBanAppealsRequest,
        Results<Ok<AdminTeamBanAppealListResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/team-ban-appeals");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListTeamBanAppeals"));
        Summary(summary =>
        {
            summary.Summary = "Lists private team ban appeals.";
            summary.Description =
                "Observers and judges may read; only administrators, owners, and managers may resolve.";
        });
    }

    public override async Task<
        Results<Ok<AdminTeamBanAppealListResponse>, NotFound, ForbidHttpResult>>
        ExecuteAsync(
            ListTeamBanAppealsRequest request,
            CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(
                user.UserId,
                request.CompetitionId,
                cancellationToken))
        {
            return TypedResults.Forbid();
        }
        var canModerate = await authorizer.CanModerateAsync(
            user.UserId,
            request.CompetitionId,
            cancellationToken);
        var appeals = await list.ExecuteAsync(request.CompetitionId, cancellationToken);
        if (appeals is null)
            return TypedResults.NotFound();
        return TypedResults.Ok(new AdminTeamBanAppealListResponse(
            appeals.Where(banCase => banCase.Appeal is not null)
                .Select(banCase => Map(banCase, canModerate))
                .ToArray()));
    }

    private static AdminTeamBanCaseResponse Map(
        TeamBanCaseView banCase,
        bool canModerate)
    {
        var appeal = banCase.Appeal!;
        return new(
            banCase.BanEventId,
            banCase.CompetitionId,
            banCase.TeamId,
            banCase.TeamName,
            TeamMapper.ToProtocol(banCase.Source),
            banCase.BannedAt,
            banCase.IsCurrentlyBanned,
            canModerate
                && banCase.IsCurrentlyBanned
                && appeal.Status == TeamBanAppealStatus.Submitted,
            new AdminTeamBanAppealResponse(
                appeal.Id,
                appeal.SubmittedByUserId,
                appeal.SubmittedByUserName,
                appeal.Statement,
                appeal.SubmittedAt,
                TeamMapper.ToProtocol(appeal.Status),
                appeal.ResolvedByUserId,
                appeal.ResolvedByUserName,
                appeal.ResolutionReason,
                appeal.ResolvedAt));
    }
}
