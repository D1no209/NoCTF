using System.Text;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions.Credentials;

public sealed class DownloadCompetitionCredentialTemplateEndpoint(ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : EndpointWithoutRequest<Results<FileContentHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/credential-batches/import-template.csv");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDownloadCompetitionCredentialTemplate"));
    }

    public override async Task<Results<FileContentHttpResult, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        if (!await authorizer.CanJudgeAsync(user.UserId, Route<Guid>("competitionId"), ct))
            return TypedResults.Forbid();
        const string template = "\uFEFFusername,email,teamName,captain,trackKey,fullName,studentNumber,school\r\n";
        return TypedResults.File(Encoding.UTF8.GetBytes(template), "text/csv; charset=utf-8", "noctf-credential-import.csv");
    }
}
