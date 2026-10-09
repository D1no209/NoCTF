using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Admission;

namespace NoCTF.API.Endpoints.LiveSolo;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloRuntimeActionProtocol>))]
public enum LiveSoloRuntimeActionProtocol { Start, Reset, Stop }
public sealed class MutateLiveSoloRuntimeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
    public Guid QuestionId { get; set; }
    public required LiveSoloRuntimeActionProtocol Action { get; set; }
    public Guid? ExpectedRuntimeInstanceId { get; set; }
}
public sealed class MutateLiveSoloRuntimeValidator : Validator<MutateLiveSoloRuntimeRequest>
{
    public MutateLiveSoloRuntimeValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.RoundId).NotEmpty(); RuleFor(x => x.QuestionId).NotEmpty(); RuleFor(x => x.Action).IsInEnum();
        RuleFor(x => x.ExpectedRuntimeInstanceId).NotEmpty().When(x => x.Action is LiveSoloRuntimeActionProtocol.Reset or LiveSoloRuntimeActionProtocol.Stop);
    }
}
public sealed class MutateLiveSoloRuntimeEndpoint(ManageLiveSoloRuntimes runtimes, IUserContext user, TimeProvider clock)
    : Endpoint<MutateLiveSoloRuntimeRequest, Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<LiveSoloFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/runtime");
        AuthSchemes("Bearer"); Options(x => x.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.RuntimeCommand))
            .WithMetadata(new HumanVerificationMetadata(HumanVerificationAction.Runtime)));
        Description(x => x.WithName("MutateLiveSoloRuntime"));
        Summary(x => x.Summary = "Starts, replaces or stops the current team's execution environment with revision and request replay checks.");
    }
    public override async Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<LiveSoloFailureResponse>, ProblemHttpResult>>
        ExecuteAsync(MutateLiveSoloRuntimeRequest req, CancellationToken ct)
    {
        var result = await runtimes.ExecuteAsync(new(new(req.CompetitionId, req.MatchId, req.RoundId, req.QuestionId, user.UserId, clock.GetUtcNow()),
            req.Action switch { LiveSoloRuntimeActionProtocol.Start => RuntimeAction.Start, LiveSoloRuntimeActionProtocol.Reset => RuntimeAction.Reset,
                LiveSoloRuntimeActionProtocol.Stop => RuntimeAction.Stop, _ => throw new InvalidOperationException("Invalid Runtime protocol action.") },
            req.ExpectedRuntimeInstanceId), ct);
        if (result.Runtime is { } runtime && result.Failure is null)
        {
            var status = $"/api/v1/competitions/{req.CompetitionId:D}/live-solo/matches/{req.MatchId:D}/rounds/{req.RoundId:D}/questions/{req.QuestionId:D}/runtime";
            return TypedResults.Accepted(status, new RuntimeAcceptedResponse(runtime.Id, status));
        }
        return result.Failure == RuntimeMutationFailure.NotFound ? TypedResults.NotFound()
            : TypedResults.Conflict(new LiveSoloFailureResponse(result.Failure switch
            {
                RuntimeMutationFailure.Unsupported => LiveSoloFailure.IsolationUnavailable,
                RuntimeMutationFailure.Conflict => LiveSoloFailure.Conflict,
                RuntimeMutationFailure.CapacityExceeded => LiveSoloFailure.NotReady,
                _ => LiveSoloFailure.NotReady
            }));
    }
}
