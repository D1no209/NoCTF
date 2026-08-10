using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed record AdminCompetitionListResponse(IReadOnlyList<CompetitionResponse> Items);

public sealed class ListAdminCompetitionsRequest
{
    public bool IncludeDeleted { get; set; }
}

public sealed class ListAdminCompetitionsEndpoint(
    ListAdminCompetitions list,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<ListAdminCompetitionsRequest, Ok<AdminCompetitionListResponse>>
{
    public override void Configure()
    {
        Get("/admin/competitions");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListCompetitions"));
        Summary(summary =>
        {
            summary.Summary = "Lists competitions visible to the current administrator.";
            summary.Description = "Resource permissions, not the /admin path alone, determine visibility.";
        });
    }

    public override async Task<Ok<AdminCompetitionListResponse>> ExecuteAsync(
        ListAdminCompetitionsRequest request,
        CancellationToken ct)
    {
        var items = await list.ExecuteAsync(
            user.UserId,
            user.IsAdministrator,
            request.IncludeDeleted,
            ct);
        var responses = new List<CompetitionResponse>(items.Count);
        foreach (var item in items)
        {
            var role = user.IsAdministrator || item.OwnerId == user.UserId
                ? CompetitionAdministrationRoleProtocol.Owner
                : await authorizer.CanModerateAsync(user.UserId, item.Id, ct)
                    ? CompetitionAdministrationRoleProtocol.Manager
                    : await authorizer.CanJudgeAsync(user.UserId, item.Id, ct)
                        ? CompetitionAdministrationRoleProtocol.Judge
                        : CompetitionAdministrationRoleProtocol.Observer;
            responses.Add(CompetitionMapper.ToResponse(item) with
            {
                AdministrationRole = role
            });
        }
        return TypedResults.Ok(new AdminCompetitionListResponse(responses));
    }
}
