using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using System.Text.Json.Serialization;
using NoCTF.Application.Runtime.Flags;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class ListRuntimeFlagsRequest : PaginationRequest
{
    public Guid RuntimeInstanceId { get; set; }
    [QueryParam] public bool IncludeHistory { get; set; }
}

public sealed class ListRuntimeFlagsValidator : Validator<ListRuntimeFlagsRequest>
{
    public ListRuntimeFlagsValidator()
    {
        PaginationRules.Add(this);
        RuleFor(request => request.RuntimeInstanceId).NotEmpty();
    }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeFlagSourceProtocol>))]
public enum RuntimeFlagSourceProtocol { Instance, Team, Static, AwdRound }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeFlagStateProtocol>))]
public enum RuntimeFlagStateProtocol { Active, Scheduled, Expired, Deleted }

public sealed record RuntimeFlagResponse(ChallengeFlagResponse Flag, RuntimeFlagSourceProtocol Source, RuntimeFlagStateProtocol State);
public sealed record RuntimeFlagListResponse(IReadOnlyList<RuntimeFlagResponse> Items, int Total);

public sealed class ListRuntimeFlagsEndpoint(QueryRuntimeFlags flags, ICompetitionModerationAuthorizer authorizer,
    IUserContext user, TimeProvider timeProvider)
    : Endpoint<ListRuntimeFlagsRequest, Results<Ok<RuntimeFlagListResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/runtimes/{runtimeInstanceId}/flags");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListRuntimeFlags"));
        Summary(summary =>
        {
            summary.Summary = "Queries current and historical flags applicable to one runtime.";
            summary.Description = "Returns paginated protected flag records and their typed scope and validity to authorized administrators or template managers. Responses must not be cached.";
        });
    }

    public override async Task<Results<Ok<RuntimeFlagListResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        ListRuntimeFlagsRequest request, CancellationToken ct)
    {
        HttpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue { NoStore = true, NoCache = true };
        var scope = await flags.FindScopeAsync(request.RuntimeInstanceId, user.UserId, user.IsAdministrator, ct);
        if (scope is null) return TypedResults.NotFound();
        if (scope.CompetitionId is Guid competitionId)
        {
            if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct)) return TypedResults.Forbid();
        }
        else if (!scope.CanManageTemplate) return TypedResults.Forbid();
        var page = await flags.ExecuteAsync(new(request.RuntimeInstanceId, request.IncludeHistory,
            request.Offset, request.Limit, timeProvider.GetUtcNow(), request.Desc), ct);
        return TypedResults.Ok(new RuntimeFlagListResponse(page.Items.Select(item => new RuntimeFlagResponse(
            ChallengeFlagMapping.ToResponse(item.Flag),
            item.Source switch
            {
                RuntimeFlagSource.Instance => RuntimeFlagSourceProtocol.Instance,
                RuntimeFlagSource.Team => RuntimeFlagSourceProtocol.Team,
                RuntimeFlagSource.Static => RuntimeFlagSourceProtocol.Static,
                RuntimeFlagSource.AwdRound => RuntimeFlagSourceProtocol.AwdRound,
                _ => throw new ArgumentOutOfRangeException(nameof(item.Source))
            },
            item.State switch
            {
                RuntimeFlagState.Active => RuntimeFlagStateProtocol.Active,
                RuntimeFlagState.Scheduled => RuntimeFlagStateProtocol.Scheduled,
                RuntimeFlagState.Expired => RuntimeFlagStateProtocol.Expired,
                RuntimeFlagState.Deleted => RuntimeFlagStateProtocol.Deleted,
                _ => throw new ArgumentOutOfRangeException(nameof(item.State))
            })).ToArray(), page.Total));
    }
}
