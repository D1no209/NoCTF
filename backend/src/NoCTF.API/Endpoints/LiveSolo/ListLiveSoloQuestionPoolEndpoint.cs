using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Templates;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed record LiveSoloPoolQuestionResponse(Guid Id, Guid ChallengeId, string Title, string? Description, string Direction,
    Guid ConcurrencyStamp, Guid MaterialStamp,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloMaterialOwnership>))] LiveSoloMaterialOwnership Ownership,
    Guid? SourceTemplateId, ChallengeDefinitionContract? Definition, IReadOnlyList<Guid> QuestionGroupIds, IReadOnlyList<ChallengeAttachmentResponse> Attachments, IReadOnlyList<ChallengeFlagResponse> Flags, bool CanEditContent);
public sealed record LiveSoloQuestionPoolResponse(IReadOnlyList<LiveSoloPoolQuestionResponse> Items, bool CanManage);
public sealed record LiveSoloPoolFailureResponse(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloPoolFailure>))] LiveSoloPoolFailure Code)
{
    public string MessageKey => ApiMessages.For(Code).Key;
    public string Detail => ApiMessages.Text(ApiMessages.For(Code).Id);
}
internal static class LiveSoloPoolProtocol
{
    public static LiveSoloPoolQuestionResponse Question(LiveSoloPoolQuestion value, bool configuration = true) => new(value.Id,
        value.ChallengeId, value.Title, value.Description, value.Direction, value.ConcurrencyStamp, value.MaterialStamp,
        value.Ownership, value.SourceTemplateId, configuration ? ChallengeDefinitionContractMapper.FromDomain(value.Definition) : null, value.QuestionGroupIds, value.Attachments.Select(x => ChallengeAttachmentMapping.ToResponse(x)).ToArray(), configuration ? value.Flags.Select(ChallengeFlagMapping.ToResponse).ToArray() : [], configuration && value.CanEditContent);
    public static Results<Ok<LiveSoloPoolQuestionResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloPoolFailureResponse>, UnprocessableEntity<LiveSoloPoolFailureResponse>, ProblemHttpResult> Mutation(LiveSoloPoolResult value) => value.Failure switch
    {
        null when value.Question is not null => TypedResults.Ok(Question(value.Question)),
        LiveSoloPoolFailure.NotFound => TypedResults.NotFound(),
        LiveSoloPoolFailure.Forbidden => TypedResults.Forbid(),
        LiveSoloPoolFailure.ProtectedMaterialsForbidden => ApiProblems.Problem(statusCode: StatusCodes.Status403Forbidden,
            detail: ApiMessages.For(LiveSoloPoolFailure.ProtectedMaterialsForbidden), extensions: new Dictionary<string, object?> { ["code"] = LiveSoloPoolFailure.ProtectedMaterialsForbidden.ToString() }),
        LiveSoloPoolFailure.Conflict or LiveSoloPoolFailure.SourceChanged or LiveSoloPoolFailure.ActiveRound => TypedResults.Conflict(new LiveSoloPoolFailureResponse(value.Failure.Value)),
        _ => TypedResults.UnprocessableEntity(new LiveSoloPoolFailureResponse(value.Failure ?? LiveSoloPoolFailure.InvalidContent))
    };
}
public sealed class ListLiveSoloQuestionPoolRequest { public Guid CompetitionId { get; set; } }
public sealed class ListLiveSoloQuestionPoolEndpoint(ManageLiveSoloQuestionPool pool, ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : Endpoint<ListLiveSoloQuestionPoolRequest, Results<Ok<LiveSoloQuestionPoolResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/question-pool"); AuthSchemes("Bearer");
        Description(x => x.WithName("ListLiveSoloQuestionPool"));
        Summary(x => x.Summary = "Reads the authorized competition question pool, ownership and question-list references.");
    }
    public override async Task<Results<Ok<LiveSoloQuestionPoolResponse>, NotFound>> ExecuteAsync(ListLiveSoloQuestionPoolRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await pool.ListAsync(req.CompetitionId, user.UserId, ct);
        if (result is null) return TypedResults.NotFound();
        var canManage = await authorizer.CanModerateAsync(user.UserId, req.CompetitionId, ct);
        return TypedResults.Ok(new LiveSoloQuestionPoolResponse(result.Select(x => LiveSoloPoolProtocol.Question(x, canManage)).ToArray(), canManage));
    }
}
