using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Questionnaires.Images;

namespace NoCTF.API.Endpoints.Questionnaires.Sessions;

public sealed class GetQuestionnaireSessionImageRequest
{
    public Guid ActivityId { get; set; } public Guid SessionId { get; set; } public Guid FileId { get; set; }
    [QueryParam] public bool Staff { get; set; }
}
public sealed class GetQuestionnaireSessionImageValidator : Validator<GetQuestionnaireSessionImageRequest>
{
    public GetQuestionnaireSessionImageValidator()
    { RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.SessionId).NotEmpty(); RuleFor(request => request.FileId).NotEmpty(); }
}
public sealed class GetQuestionnaireSessionImageEndpoint(ManageQuestionnaireImages images, IUserContext user, TimeProvider clock)
    : Endpoint<GetQuestionnaireSessionImageRequest, Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/questionnaires/activities/{activityId}/sessions/{sessionId}/images/{fileId}"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse()));
        Description(builder => builder.WithName("GetQuestionnaireSessionImage").Produces(200, typeof(byte[]), "application/octet-stream"));
    }
    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(GetQuestionnaireSessionImageRequest request, CancellationToken ct)
    {
        var file = await images.ReadAsync(user.UserId, request.ActivityId, request.SessionId, request.FileId,
            request.Staff ? QuestionnaireImageAudience.Reviewer : QuestionnaireImageAudience.Participant, clock.GetUtcNow(), ct);
        return file is null ? TypedResults.NotFound() : TypedResults.File(file.Content, file.ContentType);
    }
}
