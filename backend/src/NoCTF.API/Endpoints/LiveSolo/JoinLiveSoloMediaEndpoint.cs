using System.Security.Claims;
using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Infrastructure.Authentication.Mfa;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class JoinLiveSoloMediaRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid Generation { get; set; }
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloMediaRole>))] public required LiveSoloMediaRole Role { get; set; }
}
public sealed class JoinLiveSoloMediaValidator : Validator<JoinLiveSoloMediaRequest>
{
    public JoinLiveSoloMediaValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty(); RuleFor(x => x.Generation).NotEmpty(); RuleFor(x => x.Role).IsInEnum();
    }
}
public sealed record JoinLiveSoloMediaResponse(LiveSoloMediaResponse Session, string ServerUrl, string Token, DateTimeOffset ExpiresAt);
public sealed class JoinLiveSoloMediaEndpoint(ManageLiveSoloMedia media, IUserContext user)
    : Endpoint<JoinLiveSoloMediaRequest, Results<Ok<JoinLiveSoloMediaResponse>, ForbidHttpResult, Conflict<LiveSoloFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/media/token"); AuthSchemes("Bearer");
        Description(x => x.WithName("JoinLiveSoloMedia")); Summary(x => x.Summary = "Issues a short private screen-publisher or authorized staff token for the current room generation.");
    }
    public override async Task<Results<Ok<JoinLiveSoloMediaResponse>, ForbidHttpResult, Conflict<LiveSoloFailureResponse>>> ExecuteAsync(JoinLiveSoloMediaRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        if (!int.TryParse(HttpContext.User.FindFirstValue("token_version"), out var version)) return TypedResults.Forbid();
        var result = await media.JoinAsync(new(req.CompetitionId, req.MatchId, user.UserId, req.Generation, req.Role,
            version, AuthenticationContextClaims.Read(HttpContext.User)), ct);
        return result.Session is not null && result.Token is not null && result.Failure is null
            ? TypedResults.Ok(new JoinLiveSoloMediaResponse(LiveSoloMediaResponse.From(result.Session), result.Token.ServerUrl, result.Token.Token, result.Token.ExpiresAt))
            : result.Failure == LiveSoloMediaFailure.Unauthorized ? TypedResults.Forbid()
            : TypedResults.Conflict(new LiveSoloFailureResponse(result.Failure == LiveSoloMediaFailure.InvalidGeneration ? LiveSoloFailure.Conflict : LiveSoloFailure.MediaUnavailable));
    }
}
