using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;

namespace NoCTF.API.Endpoints.Runtime;

public sealed record RuntimeResponse(
    Guid Id,
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid? TeamId,
    int Generation,
    RuntimeKind RuntimeKind,
    RuntimeProvider Provider,
    RuntimeState State,
    RuntimeFailureCode? FailureCode,
    long ProcessingVersion,
    IReadOnlyList<string> Urls,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RunningAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? StoppedAt);

public sealed record RuntimeAcceptedResponse(
    Guid RuntimeInstanceId,
    string StatusUrl);

internal static class RuntimeEndpointMapping
{
    public static RuntimeResponse ToResponse(RuntimeInstanceView view) =>
        new(
            view.Id,
            view.CompetitionId,
            view.CompetitionChallengeId,
            view.TeamId,
            view.Generation,
            view.RuntimeKind,
            view.Provider,
            view.State,
            view.FailureCode,
            view.ProcessingVersion,
            view.State == RuntimeState.Running ? view.Urls : [],
            view.CreatedAt,
            view.RunningAt,
            view.ExpiresAt,
            view.StoppedAt);

    public static RuntimeAcceptedResponse ToAccepted(RuntimeInstanceView view) =>
        new(
            view.Id,
            $"/api/v1/competitions/{view.CompetitionId}/challenges/{view.CompetitionChallengeId}/runtime");
}

public sealed class GetRuntimeEndpoint(
    GetPlayerRuntime get,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<RuntimeResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Gets the current team runtime.";
            summary.Description = "Only Running instances expose expanded public URLs.";
        });
    }

    public override async Task<Results<Ok<RuntimeResponse>, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        var view = await get.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            user.UserId,
            ct);
        return view is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(RuntimeEndpointMapping.ToResponse(view));
    }
}
