using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Challenges.Bank;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class CopyChallengeTemplateRequest
{
    public Guid SourceChallengeId { get; set; }
    public bool CopyAttachments { get; set; } = true;
    public bool CopyFlags { get; set; }
}
public sealed class CopyChallengeTemplateValidator : Validator<CopyChallengeTemplateRequest>
{
    public CopyChallengeTemplateValidator()
    { RuleFor(x => x.SourceChallengeId).NotEmpty(); }
}
public sealed record ChallengeTemplateCopyResponse(Guid ChallengeId, Guid CanonicalChallengeId,
    string Title, int AttachmentCount, int FlagCount);
public sealed record ChallengeTemplateCopyFailureResponse(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeTemplateCopyFailure>))] ChallengeTemplateCopyFailure Code)
{
    public string Detail => ApiMessages.Text(ApiMessages.For(Code).Id);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}
public sealed class CopyChallengeTemplateEndpoint(CopyChallengeTemplate templates, IUserContext user, TimeProvider clock)
    : Endpoint<CopyChallengeTemplateRequest, Results<Ok<ChallengeTemplateCopyResponse>, NotFound, ForbidHttpResult,
        Conflict<ChallengeTemplateCopyFailureResponse>, UnprocessableEntity<ChallengeTemplateCopyFailureResponse>>>
{
    public override void Configure()
    {
        Post("/admin/challenges/copies"); AuthSchemes("Bearer");
        Description(x => x.WithName("AdminChallengeBankCopyTemplate"));
        Summary(x => x.Summary = "Copies reusable capability definitions, authorized attachments and Flags into an independent question-bank template with canonical provenance.");
    }
    public override async Task<Results<Ok<ChallengeTemplateCopyResponse>, NotFound, ForbidHttpResult,
        Conflict<ChallengeTemplateCopyFailureResponse>, UnprocessableEntity<ChallengeTemplateCopyFailureResponse>>>
        ExecuteAsync(CopyChallengeTemplateRequest req, CancellationToken ct)
    {
        var result = await templates.ExecuteAsync(new(req.SourceChallengeId, user.UserId,
            req.CopyAttachments, req.CopyFlags, clock.GetUtcNow()), ct);
        return result.Failure switch
        {
            null when result.Copy is { } copy => TypedResults.Ok(new ChallengeTemplateCopyResponse(copy.ChallengeId,
                copy.CanonicalChallengeId, copy.Title, copy.AttachmentCount, copy.FlagCount)),
            ChallengeTemplateCopyFailure.NotFound => TypedResults.NotFound(), ChallengeTemplateCopyFailure.Forbidden => TypedResults.Forbid(),
            ChallengeTemplateCopyFailure.Conflict => TypedResults.Conflict(new ChallengeTemplateCopyFailureResponse(result.Failure.Value)),
            _ => TypedResults.UnprocessableEntity(new ChallengeTemplateCopyFailureResponse(result.Failure ?? ChallengeTemplateCopyFailure.UnsupportedSource))
        };
    }
}
