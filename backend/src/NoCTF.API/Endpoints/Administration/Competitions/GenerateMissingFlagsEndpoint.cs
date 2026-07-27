using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed record GenerateMissingFlagsResponse(
    IReadOnlyList<MissingFlagGenerationFailure> Failures);

public sealed class GenerateMissingFlagsEndpoint(
    GenerateMissingFlags generate,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<GenerateMissingFlagsResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/flags/generate-missing");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGenerateMissingFlags"));
        Summary(summary =>
        {
            summary.Summary = "Synchronously generates missing CTF PerTeam and KoH flags.";
            summary.Description = "The response contains only stable failure metadata and never exposes flags or counts.";
        });
    }

    public override async Task<Results<Ok<GenerateMissingFlagsResponse>, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var failures = await generate.ExecuteAsync(
            competitionId, DateTimeOffset.UtcNow, ct);
        return TypedResults.Ok(new GenerateMissingFlagsResponse(failures));
    }
}
