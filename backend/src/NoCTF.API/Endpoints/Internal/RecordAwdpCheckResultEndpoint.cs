using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Submissions.Processing;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Internal;

public sealed class RecordAwdpCheckResultRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public int ExitCode { get; set; }
    public bool TimedOut { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string SourceKey { get; set; } = string.Empty;
}

[Mapper]
internal static partial class AwdpCheckResultMapper
{
    public static partial RecordAwdpCheckResultCommand ToCommand(RecordAwdpCheckResultRequest request);
}

public sealed class RecordAwdpCheckResultEndpoint(RecordAwdpCheckResult record)
    : Endpoint<RecordAwdpCheckResultRequest,
        Results<Created<SystemScoringEventResponse>, Ok<SystemScoringEventResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/internal/competitions/{competitionId}/awdp-check-results");
        AuthSchemes("RunnerScoringBearer");
        Policies("ScoringInput");
        Description(builder => builder.ProducesProblemFE(StatusCodes.Status400BadRequest)
            .ProducesProblemFE(StatusCodes.Status404NotFound)
            .ProducesProblemFE(StatusCodes.Status409Conflict)
            .ProducesProblemFE(StatusCodes.Status503ServiceUnavailable));
        Summary(summary => summary.Summary = "Map an AWDP checker exit code to an idempotent score-free fact.");
    }

    public override async Task<Results<Created<SystemScoringEventResponse>, Ok<SystemScoringEventResponse>, ProblemHttpResult>> ExecuteAsync(
        RecordAwdpCheckResultRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        if (request.TeamId == Guid.Empty || request.ChallengeId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.SourceKey) || request.SourceKey.Length > 256)
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid AWDP check result.");
        try
        {
            var result = await record.ExecuteAsync(AwdpCheckResultMapper.ToCommand(request), cancellationToken);
            if (result.Failure == SystemScoringEventRecordFailure.CompetitionNotFound)
                return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Competition was not found.");
            if (result.Failure == SystemScoringEventRecordFailure.CompetitionFinished)
                return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Finished competitions are read-only.");
            if (result.Failure == SystemScoringEventRecordFailure.CompetitionModeMismatch)
                return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Competition is not in AWDP mode.");
            if (result.Failure == SystemScoringEventRecordFailure.SourceConflict)
                return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Source key belongs to a different AWDP check fact.");
            var response = new SystemScoringEventResponse(result.ScoringEventId, result.Created);
            return result.Created
                ? TypedResults.Created($"/internal/competitions/{request.CompetitionId}/scoring-events/{result.ScoringEventId}", response)
                : TypedResults.Ok(response);
        }
        catch (BackgroundWorkUnavailableException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "System event processing is unavailable.");
        }
    }
}
