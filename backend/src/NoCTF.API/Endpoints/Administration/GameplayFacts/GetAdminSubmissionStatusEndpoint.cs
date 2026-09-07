using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Application.Teams.Moderation;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Domain.Gameplay;

namespace NoCTF.API.Endpoints.Administration.GameplayFacts;

public sealed class GetAdminGameplayFactStatusRequest
{
    public Guid CompetitionId { get; set; }
    public Guid GameplayFactId { get; set; }
}

public sealed class GetAdminGameplayFactStatusEndpoint(
    IAdminGameplayFactStatusReader reader,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    AccessAdminPatch patches)
    : Endpoint<GetAdminGameplayFactStatusRequest, Results<Ok<AdminGameplayFactStatusResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/gameplay-facts/{gameplayFactId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetGameplayFact"));
        Summary(summary =>
        {
            summary.Summary = "Gets an administrative gameplay-fact view.";
            summary.Description = "Returns protected gameplay-fact content, current evaluation, and management diagnostics.";
        });
    }

    public override async Task<Results<Ok<AdminGameplayFactStatusResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        GetAdminGameplayFactStatusRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        request.GameplayFactId = Route<Guid>("gameplayFactId");
        if (!await authorizer.CanObserveAsync(user.UserId, request.CompetitionId, cancellationToken))
            return TypedResults.Forbid();
        var view = await reader.FindAsync(request.CompetitionId, request.GameplayFactId, cancellationToken);
        if (view is null) return TypedResults.NotFound();
        var response = GameplayFactMapper.ToAdminStatusResponse(view);
        if (view.Kind == GameplayFactKind.FixAttempt)
        {
            var patch = await patches.DescribeAsync(request.CompetitionId, request.GameplayFactId, user.UserId, cancellationToken);
            response = response with
            {
                Patch = patch.Metadata is null ? null : AdminPatchMapping.ToResponse(patch.Metadata),
                PatchFailure = patch.Failure is null or AdminPatchFailure.Forbidden ? null : AdminPatchMapping.ToProtocol(patch.Failure.Value),
                CanDownloadPatch = patch.Failure != AdminPatchFailure.Forbidden
            };
        }
        return TypedResults.Ok(response);
    }
}
