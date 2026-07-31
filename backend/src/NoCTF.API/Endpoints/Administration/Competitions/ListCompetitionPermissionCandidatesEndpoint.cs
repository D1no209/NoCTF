using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Permissions;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed record CompetitionPermissionCandidateResponse(
    Guid Id,
    string UserName,
    UserKind Kind,
    UserRole Role,
    bool EmailVerified);

public sealed record CompetitionPermissionCandidateListResponse(
    IReadOnlyList<CompetitionPermissionCandidateResponse> Items);

internal static class CompetitionPermissionCandidateMapper
{
    public static CompetitionPermissionCandidateResponse ToResponse(
        CompetitionPermissionCandidate candidate) =>
        new(
            candidate.Id,
            candidate.UserName,
            candidate.Kind,
            candidate.Role,
            candidate.EmailVerified);
}

public sealed class ListCompetitionPermissionCandidatesEndpoint(
    ListCompetitionPermissionCandidates list,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<
            Ok<CompetitionPermissionCandidateListResponse>,
            NotFound,
            ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/permission-candidates");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListCompetitionPermissionCandidates"));
        Summary(summary =>
        {
            summary.Summary = "Lists eligible competition permission candidates.";
            summary.Description =
                "Returns a resource-scoped, minimal user projection only to the competition owner or a platform administrator.";
        });
    }

    public override async Task<
        Results<
            Ok<CompetitionPermissionCandidateListResponse>,
            NotFound,
            ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var result = await list.ExecuteAsync(
            Route<Guid>("competitionId"),
            user.UserId,
            user.IsAdministrator,
            ct);
        return result.State switch
        {
            CompetitionPermissionCandidateListState.Listed =>
                TypedResults.Ok(new CompetitionPermissionCandidateListResponse(
                    result.Candidates!
                        .Select(CompetitionPermissionCandidateMapper.ToResponse)
                        .ToArray())),
            CompetitionPermissionCandidateListState.NotFound => TypedResults.NotFound(),
            CompetitionPermissionCandidateListState.Forbidden => TypedResults.Forbid(),
            _ => throw new InvalidOperationException(
                $"Unsupported competition permission candidate state: {result.State}.")
        };
    }
}
