using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions.Progression;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class SaveCompetitionProgressionRequest
{
    public Guid CompetitionId { get; set; }
    public Guid? ExpectedConcurrencyStamp { get; set; }
    public bool Enabled { get; set; }
    public bool ShowPlayerMap { get; set; }
    public ProgressionNodeContract[] Nodes { get; set; } = [];
    public ProgressionEdgeContract[] Edges { get; set; } = [];
}

public sealed class SaveCompetitionProgressionValidator
    : Validator<SaveCompetitionProgressionRequest>
{
    public SaveCompetitionProgressionValidator()
    {
        RuleFor(request => request.CompetitionId).NotEmpty();
        RuleFor(request => request.Nodes)
            .Must(nodes => nodes is not null
                && nodes.Length <= ProgressionGraphRules.MaximumNodes);
        RuleFor(request => request.Edges)
            .Must(edges => edges is not null
                && edges.Length <= ProgressionGraphRules.MaximumEdges);
        RuleForEach(request => request.Nodes).Must(node =>
            node is not null && node.Id != Guid.Empty
            && Enum.IsDefined(node.Kind)
            && (node.Kind == ProgressionNodeKind.Challenge
                ? node.Challenge is not null
                    && node.Challenge.CompetitionChallengeId != Guid.Empty
                    && node.Badge is null
                : node.Badge is not null
                    && node.Badge.CompetitionBadgeId != Guid.Empty
                    && node.Challenge is null));
        RuleForEach(request => request.Edges).Must(edge =>
            edge is not null && edge.Id != Guid.Empty && Enum.IsDefined(edge.Condition));
    }
}

public sealed record ProgressionSaveProblem(string Code, string? Detail);

public sealed class SaveCompetitionProgressionEndpoint(
    SaveCompetitionProgression save,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider clock)
    : Endpoint<SaveCompetitionProgressionRequest,
        Results<Ok<CompetitionProgressionContract>, NotFound, ForbidHttpResult,
            Conflict<ProgressionSaveProblem>, ValidationProblem>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/progression");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminSaveCompetitionProgression"));
    }

    public override async Task<Results<Ok<CompetitionProgressionContract>, NotFound, ForbidHttpResult,
        Conflict<ProgressionSaveProblem>, ValidationProblem>>
        ExecuteAsync(SaveCompetitionProgressionRequest request, CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        var result = await save.ExecuteAsync(new(
            request.CompetitionId, request.ExpectedConcurrencyStamp,
            request.Enabled, request.ShowPlayerMap,
            request.Nodes.Select(node => new ProgressionNodeDraft(
                node.Id, node.Kind,
                node.Kind == ProgressionNodeKind.Challenge
                    ? node.Challenge!.CompetitionChallengeId
                    : node.Badge!.CompetitionBadgeId,
                node.RequiresPrerequisites)).ToArray(),
            request.Edges.Select(edge => new ProgressionEdgeDraft(
                edge.Id, edge.SourceNodeId, edge.TargetNodeId, edge.Condition)).ToArray(),
            clock.GetUtcNow()), ct);
        return result.Failure switch
        {
            null => TypedResults.Ok(ProgressionProtocol.ToContract(result.Progression!)),
            CompetitionProgressionSaveFailure.CompetitionNotFound => TypedResults.NotFound(),
            CompetitionProgressionSaveFailure.ConcurrencyConflict => TypedResults.Conflict(
                new ProgressionSaveProblem("ConcurrencyConflict", null)),
            _ => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["graph"] = [result.GraphFailure?.ToString()
                    ?? result.Failure.Value.ToString()]
            })
        };
    }
}
