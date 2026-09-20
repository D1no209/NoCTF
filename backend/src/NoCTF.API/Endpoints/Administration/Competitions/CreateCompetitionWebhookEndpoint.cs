using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class CreateCompetitionWebhookRequest
{
    public Guid CompetitionId { get; set; }
    public string? Name { get; set; }
    public string? EndpointUrl { get; set; }
    public bool Enabled { get; set; }
}

public sealed class CreateCompetitionWebhookValidator
    : Validator<CreateCompetitionWebhookRequest>
{
    public CreateCompetitionWebhookValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(100);
        RuleFor(request => request.EndpointUrl).NotEmpty().MaximumLength(2048)
            .Must(CompetitionWebhookEndpointValidation.IsValid)
            .WithMessage("EndpointUrl must be an absolute HTTP or HTTPS URL without credentials or a fragment.");
    }
}

public sealed record CompetitionWebhookCreatedResponse(
    CompetitionWebhookTargetResponse Target,
    string SigningSecret);

public sealed record CompetitionWebhookFailureResponse(CompetitionWebhookProblemCode Code);

public sealed class CreateCompetitionWebhookEndpoint(
    CreateCompetitionWebhookTarget create,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<CreateCompetitionWebhookRequest,
        Results<Created<CompetitionWebhookCreatedResponse>, NotFound, ForbidHttpResult,
            Conflict<CompetitionWebhookFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/webhooks");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCreateCompetitionWebhook"));
        Summary(summary => summary.Summary = "Creates an outbound competition webhook target.");
    }

    public override async Task<Results<Created<CompetitionWebhookCreatedResponse>, NotFound, ForbidHttpResult,
        Conflict<CompetitionWebhookFailureResponse>, ProblemHttpResult>> ExecuteAsync(
        CreateCompetitionWebhookRequest request,
        CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        var result = await create.ExecuteAsync(new(
            request.CompetitionId,
            request.Name!,
            request.EndpointUrl!,
            request.Enabled,
            timeProvider.GetUtcNow()), ct);
        if (result.Failure == CompetitionWebhookMutationFailure.CompetitionNotFound)
            return TypedResults.NotFound();
        if (result.Failure == CompetitionWebhookMutationFailure.DuplicateEndpoint)
        {
            return TypedResults.Conflict(new CompetitionWebhookFailureResponse(
                CompetitionWebhookProblemCode.DuplicateEndpoint));
        }
        if (!result.Succeeded || result.Target is null || result.SigningSecret is null)
            return CompetitionWebhookEndpointValidation.Failure(result.Failure);
        return TypedResults.Created(
            $"/api/v1/admin/competitions/{request.CompetitionId}/webhooks/{result.Target.Id}",
            new CompetitionWebhookCreatedResponse(
                CompetitionWebhookProtocol.ToResponse(result.Target, true),
                result.SigningSecret));
    }
}

internal static class CompetitionWebhookEndpointValidation
{
    public static bool IsValid(string? value) =>
        value is { Length: <= 2048 }
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && !string.IsNullOrWhiteSpace(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment);

    public static ProblemHttpResult Failure(CompetitionWebhookMutationFailure? failure) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid webhook target.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = failure is CompetitionWebhookMutationFailure value
                    ? CompetitionWebhookProtocol.ToProblemCode(value)
                    : CompetitionWebhookProblemCode.InvalidEndpoint
            });
}
