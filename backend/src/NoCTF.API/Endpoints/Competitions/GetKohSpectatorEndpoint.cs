using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Competitions;

public sealed class GetKohSpectatorRequest
{
    public Guid CompetitionId { get; set; }
    [QueryParam] public int Minutes { get; set; } = 10;
    [QueryParam] public int Limit { get; set; } = 256;
    [QueryParam] public string? Cursor { get; set; }
}
public sealed class GetKohSpectatorValidator : Validator<GetKohSpectatorRequest>
{
    public GetKohSpectatorValidator()
    {
        RuleFor(x => x.Minutes).Must(x => x is 5 or 10 or 30);
        RuleFor(x => x.Limit).InclusiveBetween(1, 2000);
        RuleFor(x => x.Cursor).MaximumLength(100);
    }
}
public sealed class GetKohSpectatorEndpoint(GetSpectatorScreen query)
    : Endpoint<GetKohSpectatorRequest, Results<Ok<SpectatorScreenResponse>, Accepted, NotFound, Conflict>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/koh-spectator");
        AllowAnonymous();
        Summary(s => s.Summary = "Read a public-only KoH control field and bounded confirmation history.");
    }
    public override async Task<Results<Ok<SpectatorScreenResponse>, Accepted, NotFound, Conflict>> ExecuteAsync(
        GetKohSpectatorRequest request, CancellationToken ct) => SpectatorProtocol.Map(await query.ExecuteAsync(
            Route<Guid>("competitionId"), GameMode.Koh, request.Minutes, request.Cursor, ct, request.Limit));
}
