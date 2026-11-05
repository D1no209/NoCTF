using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Questionnaires.Images;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class GetQuestionnaireImageRequest
{
    public Guid ChallengeId { get; set; }
    public Guid FileId { get; set; }
}

public sealed class GetQuestionnaireImageEndpoint(ManageQuestionnaireImages images, IUserContext user, TimeProvider clock)
    : Endpoint<GetQuestionnaireImageRequest, Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/challenges/{challengeId}/questionnaire/images/{fileId}");
        AuthSchemes("Bearer"); Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankGetQuestionnaireImage"));
        Summary(summary =>
        {
            summary.Summary = "Read a questionnaire image for a question-bank editor.";
            summary.Description = "Verifies management access to the challenge and serves its referenced immutable image.";
        });
    }

    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(GetQuestionnaireImageRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var file = await images.ReadAsync(user.UserId, request.ChallengeId, null, request.FileId, QuestionnaireImageAudience.Editor, clock.GetUtcNow(), ct);
        return file is null ? TypedResults.NotFound() : TypedResults.File(file.Content, file.ContentType);
    }
}
