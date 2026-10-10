using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Templates;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class CopyLiveSoloTemplateRequest
{
    public Guid CompetitionId { get; set; }
    public Guid SourceChallengeId { get; set; }
    public bool CopyAttachments { get; set; } = true;
    public bool CopyFlags { get; set; }
    public IReadOnlyList<string>? Tags { get; set; }
}
public sealed class CopyLiveSoloTemplateValidator : Validator<CopyLiveSoloTemplateRequest>
{
    public CopyLiveSoloTemplateValidator()
    { RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.SourceChallengeId).NotEmpty(); }
}
public sealed record LiveSoloTemplateCopyResponse(Guid ChallengeId, Guid CompetitionChallengeId, Guid CanonicalChallengeId,
    string Title, int AttachmentCount, int FlagCount, bool AllocationChanged);
public sealed record LiveSoloTemplateCopyFailureResponse(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloTemplateCopyFailure>))] LiveSoloTemplateCopyFailure Code)
{
    public string Detail => ApiMessages.Text(ApiMessages.For(Code).Id);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}
public sealed class CopyLiveSoloTemplateEndpoint(CopyLiveSoloTemplate templates, IUserContext user, TimeProvider clock)
    : Endpoint<CopyLiveSoloTemplateRequest, Results<Ok<LiveSoloTemplateCopyResponse>, NotFound, ForbidHttpResult,
        Conflict<LiveSoloTemplateCopyFailureResponse>, UnprocessableEntity<LiveSoloTemplateCopyFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/templates/copies"); AuthSchemes("Bearer");
        Description(x => x.WithName("CopyLiveSoloTemplate"));
        Summary(x => x.Summary = "Copies FlagSubmission-compatible material into independent LiveSolo template and competition instances with canonical provenance.");
    }
    public override async Task<Results<Ok<LiveSoloTemplateCopyResponse>, NotFound, ForbidHttpResult,
        Conflict<LiveSoloTemplateCopyFailureResponse>, UnprocessableEntity<LiveSoloTemplateCopyFailureResponse>>>
        ExecuteAsync(CopyLiveSoloTemplateRequest req, CancellationToken ct)
    {
        var result = await templates.ExecuteAsync(new(req.CompetitionId, req.SourceChallengeId, user.UserId,
            req.CopyAttachments, req.CopyFlags, req.Tags, clock.GetUtcNow()), ct);
        return result.Failure switch
        {
            null when result.Copy is { } copy => TypedResults.Ok(new LiveSoloTemplateCopyResponse(copy.ChallengeId, copy.CompetitionChallengeId,
                copy.CanonicalChallengeId, copy.Title, copy.AttachmentCount, copy.FlagCount, copy.AllocationChanged)),
            LiveSoloTemplateCopyFailure.NotFound => TypedResults.NotFound(), LiveSoloTemplateCopyFailure.Forbidden => TypedResults.Forbid(),
            LiveSoloTemplateCopyFailure.Conflict => TypedResults.Conflict(new LiveSoloTemplateCopyFailureResponse(result.Failure.Value)),
            _ => TypedResults.UnprocessableEntity(new LiveSoloTemplateCopyFailureResponse(result.Failure ?? LiveSoloTemplateCopyFailure.UnsupportedSource))
        };
    }
}
