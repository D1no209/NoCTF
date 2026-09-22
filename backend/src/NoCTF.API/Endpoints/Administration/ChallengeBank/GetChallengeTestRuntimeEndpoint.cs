using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Domain.Runtime;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeTestFlagStateProtocol>))]
public enum RuntimeTestFlagStateProtocol
{
    NotRequired,
    Pending,
    Succeeded,
    Failed,
    Canceled
}

public sealed record ChallengeTestRuntimeResponse(
    Guid Id,
    Guid ChallengeId,
    RuntimeKindProtocol RuntimeKind,
    RuntimeProviderProtocol Provider,
    RuntimeStateProtocol State,
    RuntimeFailureCodeProtocol? FailureCode,
    RuntimeTestFlagStateProtocol FlagState,
    string? TestFlag,
    IReadOnlyList<RuntimeAccessResponse> Accesses,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RunningAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? StoppedAt);

internal static class ChallengeTestRuntimeMapping
{
    public static ChallengeTestRuntimeResponse ToResponse(
        ChallengeTestRuntimeView view,
        HttpRequest request) =>
        new(
            view.Id,
            view.ChallengeId,
            RuntimeProtocolMapper.ToProtocol(view.RuntimeKind),
            RuntimeProtocolMapper.ToProtocol(view.Provider),
            RuntimeProtocolMapper.ToProtocol(view.State),
            view.FailureCode is null
                ? null
                : RuntimeProtocolMapper.ToProtocol(view.FailureCode.Value),
            view.FlagState switch
            {
                RuntimeTestFlagState.NotRequired => RuntimeTestFlagStateProtocol.NotRequired,
                RuntimeTestFlagState.Pending => RuntimeTestFlagStateProtocol.Pending,
                RuntimeTestFlagState.Succeeded => RuntimeTestFlagStateProtocol.Succeeded,
                RuntimeTestFlagState.Failed => RuntimeTestFlagStateProtocol.Failed,
                RuntimeTestFlagState.Canceled => RuntimeTestFlagStateProtocol.Canceled,
                _ => throw new ArgumentOutOfRangeException(nameof(view), view.FlagState, null)
            },
            view.TestFlag,
            view.State == RuntimeState.Running
                ? RuntimeAccessMapping.ToResponse(
                    view.Id,
                    view.AccessMode,
                    view.AccessEndpoints ?? [],
                    request)
                : [],
            view.CreatedAt,
            view.RunningAt,
            view.ExpiresAt,
            view.StoppedAt);
}

public sealed class GetChallengeTestRuntimeEndpoint(
    GetChallengeTestRuntime get,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<ChallengeTestRuntimeResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/challenges/{challengeId}/test-runtimes/current");
        AuthSchemes("Bearer");
        Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankGetTestRuntime"));
        Summary(summary =>
        {
            summary.Summary = "Gets the latest challenge-template test Runtime.";
            summary.Description =
                "Returns protected test Flag and access displays only to template managers and platform administrators.";
        });
    }

    public override async Task<Results<Ok<ChallengeTestRuntimeResponse>, NotFound>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        HttpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            NoStore = true,
            NoCache = true
        };
        var runtime = await get.ExecuteAsync(
            Route<Guid>("challengeId"),
            user.UserId,
            user.IsAdministrator,
            cancellationToken);
        if (runtime is null) return TypedResults.NotFound();
        return TypedResults.Ok(ChallengeTestRuntimeMapping.ToResponse(
            runtime,
            HttpContext.Request));
    }
}
