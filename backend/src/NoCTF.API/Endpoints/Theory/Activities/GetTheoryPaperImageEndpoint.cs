using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Questions;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class GetTheoryPaperImageRequest
{
    public Guid ActivityId { get; set; } public Guid PaperId { get; set; } public Guid FileId { get; set; }
    [QueryParam] public bool Staff { get; set; }
}
public sealed class GetTheoryPaperImageEndpoint(ManageTheoryImages images, IUserContext user, TimeProvider clock)
    : Endpoint<GetTheoryPaperImageRequest, Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/theory/activities/{activityId}/papers/{paperId}/images/{fileId}"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
        Description(builder => builder.WithName("GetTheoryPaperImage").Produces(200, typeof(byte[]), "application/octet-stream"));
    }
    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(GetTheoryPaperImageRequest request, CancellationToken ct)
    {
        var file = await images.ReadAsync(user.UserId, request.ActivityId, request.PaperId, request.FileId,
            request.Staff ? TheoryImageAudience.Reviewer : TheoryImageAudience.Participant, clock.GetUtcNow(), ct);
        return file is null ? TypedResults.NotFound() : TypedResults.File(file.Content, file.ContentType);
    }
}
