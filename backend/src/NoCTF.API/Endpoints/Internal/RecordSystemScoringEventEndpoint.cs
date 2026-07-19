using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Internal;

public sealed class RecordSystemScoringEventRequest
{
    public Guid CompetitionId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? ChallengeId { get; set; }
    public ScoringEventKind Kind { get; set; }
    public ScoringResult Result { get; set; }
    public ScoringFailureCode? FailureCode { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string EvaluatorVersion { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
}

public sealed record SystemScoringEventResponse(Guid ScoringEventId, bool Created);

[Mapper]
internal static partial class SystemScoringEventMapper
{
    public static partial RecordSystemScoringEventCommand ToCommand(RecordSystemScoringEventRequest request);
    public static partial SystemScoringEventResponse ToResponse(RecordSystemScoringEventResult result);
}

public sealed class RecordSystemScoringEventEndpoint(RecordSystemScoringEvent record)
    : Endpoint<RecordSystemScoringEventRequest,
        Results<Created<SystemScoringEventResponse>, Ok<SystemScoringEventResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/internal/competitions/{competitionId}/scoring-events");
        AuthSchemes("RunnerScoringBearer");
        Policies("ScoringInput");
    }

    public override async Task<Results<Created<SystemScoringEventResponse>, Ok<SystemScoringEventResponse>, ProblemHttpResult>> ExecuteAsync(
        RecordSystemScoringEventRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        if (string.IsNullOrWhiteSpace(request.SourceKey) || request.SourceKey.Length > 256)
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid source key.");

        try
        {
            var result = await record.ExecuteAsync(SystemScoringEventMapper.ToCommand(request), cancellationToken);
            var response = SystemScoringEventMapper.ToResponse(result);
            return result.Created
                ? TypedResults.Created($"/internal/competitions/{request.CompetitionId}/scoring-events/{result.ScoringEventId}", response)
                : TypedResults.Ok(response);
        }
        catch (ArgumentException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "System event was rejected.", detail: exception.Message);
        }
    }
}
