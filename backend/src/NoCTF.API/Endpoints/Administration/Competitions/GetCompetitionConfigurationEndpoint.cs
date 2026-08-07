using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed record CompetitionConfigurationResponse(
    Guid CompetitionId,
    GameModeProtocol Mode,
    string Json,
    int Revision,
    CompetitionStatusProtocol CompetitionStatus,
    DateTimeOffset UpdatedAt);

internal static class CompetitionConfigurationMapping
{
    public static CompetitionConfigurationResponse ToResponse(CompetitionConfigurationView view) =>
        new(
            view.CompetitionId,
            CompetitionProtocolMapper.ToProtocol(view.Mode),
            view.Json,
            view.Revision,
            CompetitionProtocolMapper.ToProtocol(view.CompetitionStatus),
            view.UpdatedAt);
}

public sealed class GetCompetitionConfigurationEndpoint(
    GetCompetitionConfiguration get,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<Ok<CompetitionConfigurationResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/configuration");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCompetitionConfiguration_Get"));
        Summary(summary =>
        {
            summary.Summary = "Gets a competition's game-mode configuration.";
            summary.Description = "Returns the versioned configuration JSON visible to competition administrators.";
        });
    }

    public override async Task<
        Results<Ok<CompetitionConfigurationResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var view = await get.ExecuteAsync(competitionId, ct);
        return view is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(CompetitionConfigurationMapping.ToResponse(view));
    }
}
