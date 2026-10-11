using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Questions;

namespace NoCTF.API.Endpoints.Theory.Questions;

public sealed class GetTheoryQuestionImageRequest { public Guid QuestionId { get; set; } public Guid FileId { get; set; } }
public sealed class GetTheoryQuestionImageEndpoint(ManageTheoryImages images, IUserContext user, TimeProvider clock)
    : Endpoint<GetTheoryQuestionImageRequest, Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/theory/questions/{questionId}/images/{fileId}"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
        Description(builder => builder.WithName("GetTheoryQuestionImage").Produces(200, typeof(byte[]), "application/octet-stream"));
    }
    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(GetTheoryQuestionImageRequest request, CancellationToken ct)
    {
        var file = await images.ReadAsync(user.UserId, request.QuestionId, null, request.FileId, TheoryImageAudience.Editor, clock.GetUtcNow(), ct);
        return file is null ? TypedResults.NotFound() : TypedResults.File(file.Content, file.ContentType);
    }
}
