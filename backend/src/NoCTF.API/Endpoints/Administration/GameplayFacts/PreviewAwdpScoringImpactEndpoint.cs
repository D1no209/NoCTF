using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Scoring.Awdp;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Administration.GameplayFacts;

public sealed class PreviewAwdpScoringImpactRequest
{
    [QueryParam]
    public int Limit { get; set; } = 50;
}

public sealed class PreviewAwdpScoringImpactValidator
    : Validator<PreviewAwdpScoringImpactRequest>
{
    public PreviewAwdpScoringImpactValidator() =>
        RuleFor(request => request.Limit).InclusiveBetween(1, 100);
}

public sealed record AwdpScoringImpactTeamResponse(
    Guid TeamId,
    string TeamName,
    long CurrentScore,
    long ContinuousScore,
    long Delta);

public sealed record AwdpScoringImpactCompetitionResponse(
    Guid CompetitionId,
    string CompetitionTitle,
    int CurrentSchemaVersion,
    IReadOnlyList<AwdpScoringImpactTeamResponse> Teams);

public sealed record AwdpScoringImpactPreviewResponse(
    DateTimeOffset ProjectedAt,
    IReadOnlyList<AwdpScoringImpactCompetitionResponse> Competitions);

public sealed class PreviewAwdpScoringImpactEndpoint(
    PreviewAwdpScoringImpact preview,
    TimeProvider timeProvider)
    : Endpoint<PreviewAwdpScoringImpactRequest, Ok<AwdpScoringImpactPreviewResponse>>
{
    public override void Configure()
    {
        Get("/admin/awdp/scoring-impact-preview");
        AuthSchemes("Bearer");
        Roles(nameof(UserRole.Administrator));
        Description(builder => builder.WithName("AdminPreviewAwdpScoringImpact"));
        Summary(summary =>
        {
            summary.Summary = "Previews legacy AWDP scoring differences.";
            summary.Description =
                "Read-only comparison of current legacy scores and continuous-round scores. It never changes gameplay facts, events or cached leaderboards.";
        });
    }

    public override async Task<Ok<AwdpScoringImpactPreviewResponse>> ExecuteAsync(
        PreviewAwdpScoringImpactRequest request,
        CancellationToken cancellationToken)
    {
        var projectedAt = timeProvider.GetUtcNow();
        var competitions = await preview.ExecuteAsync(
            request.Limit,
            projectedAt,
            cancellationToken);
        return TypedResults.Ok(new AwdpScoringImpactPreviewResponse(
            projectedAt,
            competitions.Select(competition => new AwdpScoringImpactCompetitionResponse(
                competition.CompetitionId,
                competition.CompetitionTitle,
                competition.CurrentSchemaVersion,
                competition.Teams.Select(team => new AwdpScoringImpactTeamResponse(
                    team.TeamId,
                    team.TeamName,
                    team.CurrentScore,
                    team.ContinuousScore,
                    team.Delta)).ToArray())).ToArray()));
    }
}
