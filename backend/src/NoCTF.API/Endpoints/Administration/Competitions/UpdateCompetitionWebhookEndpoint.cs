using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class UpdateCompetitionWebhookRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TargetId { get; set; }
    public string? Name { get; set; }
    public string? EndpointUrl { get; set; }
    public bool Enabled { get; set; }
}

public sealed class UpdateCompetitionWebhookValidator
    : Validator<UpdateCompetitionWebhookRequest>
{
    public UpdateCompetitionWebhookValidator()
    {
        RuleFor(request => request.TargetId).NotEmpty();
        RuleFor(request => request.Name).NotEmpty().MaximumLength(100);
        RuleFor(request => request.EndpointUrl).NotEmpty().MaximumLength(2048)
            .Must(CompetitionWebhookEndpointValidation.IsValid);
    }
}

public sealed class UpdateCompetitionWebhookEndpoint(
    UpdateCompetitionWebhookTarget update,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<UpdateCompetitionWebhookRequest,
        Results<Ok<CompetitionWebhookTargetResponse>, NotFound, ForbidHttpResult,
            Conflict<CompetitionWebhookFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Summary(summary =>
        {
            summary.Summary = "Updates the destination and event subscriptions of a competition webhook.";
            summary.Description = summary.Summary;
        });

        Put("/admin/competitions/{competitionId}/webhooks/{targetId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminUpdateCompetitionWebhook"));
    }

    public override async Task<Results<Ok<CompetitionWebhookTargetResponse>, NotFound, ForbidHttpResult,
        Conflict<CompetitionWebhookFailureResponse>, ProblemHttpResult>> ExecuteAsync(
        UpdateCompetitionWebhookRequest request,
        CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        var result = await update.ExecuteAsync(new(
            request.CompetitionId,
            request.TargetId,
            request.Name!,
            request.EndpointUrl!,
            request.Enabled,
            timeProvider.GetUtcNow()), ct);
        if (result.Failure is CompetitionWebhookMutationFailure.CompetitionNotFound
            or CompetitionWebhookMutationFailure.TargetNotFound)
            return TypedResults.NotFound();
        if (result.Failure == CompetitionWebhookMutationFailure.DuplicateEndpoint)
        {
            return TypedResults.Conflict(new CompetitionWebhookFailureResponse(
                CompetitionWebhookProblemCode.DuplicateEndpoint,
                "A webhook target with this endpoint URL already exists."));
        }
        if (!result.Succeeded || result.Target is null)
            return CompetitionWebhookEndpointValidation.Failure(result.Failure);
        return TypedResults.Ok(CompetitionWebhookProtocol.ToResponse(result.Target, true));
    }
}
