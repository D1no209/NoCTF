using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Submissions;

namespace NoCTF.API.Endpoints.Competitions.Events;

public sealed class AccessCompetitionSubmissionFlagRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class AccessCompetitionSubmissionFlagValidator
    : Validator<AccessCompetitionSubmissionFlagRequest>
{
    public AccessCompetitionSubmissionFlagValidator() =>
        RuleFor(request => request.Reason)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(512);
}

public sealed record AccessCompetitionSubmissionFlagResponse(
    Guid SubmissionId,
    SubmissionKind SubmissionKind,
    string SubmittedFlag,
    DateTimeOffset AccessedAt);

public sealed class AccessCompetitionSubmissionFlagEndpoint(
    AccessSubmissionFlag access,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<AccessCompetitionSubmissionFlagRequest,
        Results<Ok<AccessCompetitionSubmissionFlagResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/submissions/{submissionId}/flag-access");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminAccessCompetitionSubmissionFlag"));
        Summary(summary =>
        {
            summary.Summary = "Explicitly reads one protected submitted Flag.";
            summary.Description =
                "Administrator, owner, manager, and judge only. Every successful access appends an immutable audit event.";
        });
    }

    public override async Task<
        Results<Ok<AccessCompetitionSubmissionFlagResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
        ExecuteAsync(
            AccessCompetitionSubmissionFlagRequest request,
            CancellationToken cancellationToken)
    {
        var result = await access.ExecuteAsync(new SubmissionFlagAccessCommand(
            Route<Guid>("competitionId"),
            Route<Guid>("submissionId"),
            user.UserId,
            request.Reason,
            timeProvider.GetUtcNow()), cancellationToken);
        if (result.State == CompetitionEventReadState.Forbidden)
            return TypedResults.Forbid();
        if (result.State == CompetitionEventReadState.CompetitionNotFound)
            return TypedResults.NotFound();
        if (result.State != CompetitionEventReadState.Available
            || result.View is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Flag access request.");
        }
        return TypedResults.Ok(new AccessCompetitionSubmissionFlagResponse(
            result.View.SubmissionId,
            result.View.SubmissionKind,
            result.View.SubmittedFlag,
            result.View.AccessedAt));
    }
}
