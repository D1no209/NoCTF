using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.WriteUps;

namespace NoCTF.API.Endpoints.Teams.WriteUps;

public sealed record TeamWriteUpResponse(
    Guid TeamId,
    string TeamName,
    Guid FileId,
    string FileName,
    string ContentType,
    long ByteLength,
    string Sha256,
    Guid SubmittedByUserId,
    string? SubmittedByDisplayName,
    DateTimeOffset SubmittedAt);

internal static class TeamWriteUpProtocol
{
    internal static TeamWriteUpResponse ToResponse(TeamWriteUpReference value) =>
        new(
            value.TeamId,
            value.TeamName,
            value.FileId,
            value.FileName,
            value.ContentType,
            value.ByteLength,
            value.Sha256,
            value.SubmittedByUserId,
            value.SubmittedByDisplayName,
            value.SubmittedAt);

    internal static string SafeFileName(string value, Guid teamId)
    {
        var leaf = Path.GetFileName(value.Replace('\\', '/'));
        var safe = string.Concat(leaf.Where(character => !char.IsControl(character))).Trim();
        return string.IsNullOrWhiteSpace(safe) || safe is "." or ".."
            ? $"writeup-{teamId:N}.pdf"
            : safe;
    }

    internal static void SetPrivatePdfHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
    }

    internal static void SetPrivateInlinePdfHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}

public sealed class GetMyTeamWriteUpEndpoint(
    ManageTeamWriteUps writeUps,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<TeamWriteUpResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/teams/me/writeup");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("GetMyTeamWriteUp"));
        Summary(summary => summary.Summary = "Gets the current team's submitted WriteUp metadata.");
    }

    public override async Task<Results<Ok<TeamWriteUpResponse>, NotFound>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var writeUp = await writeUps.FindMineAsync(
            Route<Guid>("competitionId"),
            user.UserId,
            cancellationToken);
        return writeUp is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(TeamWriteUpProtocol.ToResponse(writeUp));
    }
}
