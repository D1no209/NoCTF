using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class CreateChallengeRequest
{
    public Guid? Id { get; set; }
    public Guid ChallengeId { get; set; }
    public string? CustomTitle { get; set; }
    public int Order { get; set; }
    public IReadOnlyList<string>? Tags { get; set; }
}

public sealed class CreateChallengeValidator : Validator<CreateChallengeRequest>
{
    public CreateChallengeValidator()
    {
        RuleFor(request => request.Id)
            .Must(id => id is null || id != Guid.Empty)
            .WithMessage("Id cannot be empty when supplied.");
        RuleFor(request => request.ChallengeId).NotEmpty();
        RuleFor(request => request.CustomTitle).MaximumLength(160);
        RuleFor(request => request.Order).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Tags).Must(tags => NoCTF.Domain.Challenges.CompetitionChallengeTags.TryNormalize(tags, out _))
            .WithMessage("Use at most 20 nonblank tags, each up to 40 characters.");
    }
}

public sealed class CreateChallengeEndpoint(
    CreateChallenge create,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<CreateChallengeRequest,
        Results<
            Created<ChallengeResponse>,
            NotFound,
            ForbidHttpResult,
            Conflict<CompetitionChallengeConflictResponse>,
            ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/challenges");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCreateCompetitionChallenge"));
        Summary(summary =>
        {
            summary.Summary = "Links a global challenge template to a competition.";
            summary.Description = "Creates a CompetitionChallenge without copying or mutating the global template.";
        });
    }

    public override async Task<
        Results<
            Created<ChallengeResponse>,
            NotFound,
            ForbidHttpResult,
            Conflict<CompetitionChallengeConflictResponse>,
            ProblemHttpResult>> ExecuteAsync(
        CreateChallengeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var result = await create.ExecuteAsync(new CreateCompetitionChallengeCommand(
            request.Id,
            competitionId,
            request.ChallengeId,
            request.Order,
            timeProvider.GetUtcNow(),
            request.CustomTitle, request.Tags), ct);
        if (result.Challenge is not null)
        {
            var response = ChallengeMapper.ToResponse(result.Challenge);
            return TypedResults.Created(
                $"/api/v1/admin/competitions/{competitionId}/challenges/{response.Id}",
                response);
        }

        return result.Failure switch
        {
            ChallengeMutationFailure.CompetitionNotFound
                or ChallengeMutationFailure.TemplateNotFound =>
                TypedResults.NotFound(),
            ChallengeMutationFailure.ResourceIdConflict
                or ChallengeMutationFailure.ChallengeOrderConflict
                or ChallengeMutationFailure.ChallengeTemplateConflict
                or ChallengeMutationFailure.ExperimentalFeatureDisabled =>
                TypedResults.Conflict(
                    CompetitionChallengeConflictMapper.ToResponse(result.Failure.Value)),
            ChallengeMutationFailure.InvalidChallengeId
                or ChallengeMutationFailure.InvalidTitle
                or ChallengeMutationFailure.InvalidTags
                or ChallengeMutationFailure.InvalidOrder
                or ChallengeMutationFailure.TemplateModeMismatch =>
                TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Challenge was not created.",
                    detail: "Competition challenge values are invalid."),
            _ => throw new InvalidOperationException(
                $"Unsupported competition challenge create failure: {result.Failure}.")
        };
    }
}
