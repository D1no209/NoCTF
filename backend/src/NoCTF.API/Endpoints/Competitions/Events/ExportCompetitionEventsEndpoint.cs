using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.API.Endpoints.Competitions.Events;

public sealed class ExportCompetitionEventsRequest
{
    [QueryParam] public CompetitionEventKind? Kind { get; set; }
    [QueryParam] public CompetitionEventLevel? MinimumLevel { get; set; }
    [QueryParam] public Guid? TeamId { get; set; }
    [QueryParam] public Guid? UserId { get; set; }
    [QueryParam] public Guid? CompetitionChallengeId { get; set; }
    [QueryParam] public Guid? RuntimeInstanceId { get; set; }
    [QueryParam] public DateTimeOffset From { get; set; }
    [QueryParam] public DateTimeOffset To { get; set; }
}

public sealed class ExportCompetitionEventsValidator
    : Validator<ExportCompetitionEventsRequest>
{
    public ExportCompetitionEventsValidator()
    {
        RuleFor(request => request.From).NotEmpty();
        RuleFor(request => request.To).NotEmpty();
        RuleFor(request => request).Must(request =>
                request.From <= request.To
                && request.To - request.From <= TimeSpan.FromDays(31))
            .WithMessage("The export range must be between zero and 31 days.");
    }
}

public sealed class ExportCompetitionEventsEndpoint(
    ExportCompetitionEvents export,
    IUserContext user)
    : Endpoint<ExportCompetitionEventsRequest,
        Results<FileStreamHttpResult, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/events/export");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminExportCompetitionEvents"));
        Summary(summary =>
        {
            summary.Summary = "Exports a bounded competition event range as JSON Lines.";
            summary.Description =
                "Administrator, competition owner, and manager only; at most 31 days and 50,000 events.";
        });
    }

    public override async Task<
        Results<FileStreamHttpResult, NotFound, ForbidHttpResult, ProblemHttpResult>>
        ExecuteAsync(
            ExportCompetitionEventsRequest request,
            CancellationToken cancellationToken)
    {
        var competitionId = Route<Guid>("competitionId");
        var result = await export.ExecuteAsync(new CompetitionEventQuery(
            competitionId,
            user.UserId,
            request.Kind,
            request.MinimumLevel,
            request.TeamId,
            request.UserId,
            request.CompetitionChallengeId,
            request.RuntimeInstanceId,
            request.From,
            request.To,
            null,
            null,
            50_000), cancellationToken);
        if (result.State == CompetitionEventReadState.Forbidden)
            return TypedResults.Forbid();
        if (result.State == CompetitionEventReadState.CompetitionNotFound)
            return TypedResults.NotFound();
        if (result.State != CompetitionEventReadState.Available
            || result.Export is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid competition event export.");
        }
        return TypedResults.Stream(
            result.Export.Content,
            "application/x-ndjson",
            result.Export.FileName);
    }
}
