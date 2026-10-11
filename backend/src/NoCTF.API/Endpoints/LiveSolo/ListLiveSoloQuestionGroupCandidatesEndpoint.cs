using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed record LiveSoloQuestionCandidateRiskResponse(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloSelectionRiskKind>))] LiveSoloSelectionRiskKind Kind,
    Guid? CompetitionChallengeId, Guid? CanonicalChallengeId);
[JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloQuestionExclusionProtocol>))]
public enum LiveSoloQuestionExclusionProtocol { Empty, MissingQuestion, DeletedQuestion, DuplicateQuestion, InvalidSchedule, UnsupportedDefinition, ActiveRoundConflict }
public sealed record LiveSoloQuestionUseResponse(Guid MatchId, Guid RoundId, int Number, int Replay,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloRoundState>))] LiveSoloRoundState State,
    DateTimeOffset PreparedAt, DateTimeOffset? OpenedAt);
public sealed record LiveSoloQuestionCandidateResponse(Guid Id, string Name, bool Reserve, Guid ConcurrencyStamp, Guid SelectionRevision,
    bool Available, bool AutomaticEligible,
    IReadOnlyList<LiveSoloQuestionExclusionProtocol> Exclusions, IReadOnlyList<LiveSoloQuestionCandidateRiskResponse> Risks,
    IReadOnlyList<LiveSoloQuestionUseResponse> Uses);
public sealed record LiveSoloQuestionCandidateListResponse(IReadOnlyList<LiveSoloQuestionCandidateResponse> Items);
public sealed class ListLiveSoloQuestionGroupCandidatesRequest { public Guid CompetitionId { get; set; } public Guid MatchId { get; set; } }
public sealed class ListLiveSoloQuestionGroupCandidatesEndpoint(ILiveSoloMatchStore matches, IUserContext user, TimeProvider clock)
    : Endpoint<ListLiveSoloQuestionGroupCandidatesRequest, Results<Ok<LiveSoloQuestionCandidateListResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/question-groups"); AuthSchemes("Bearer");
        Description(x => x.WithName("ListLiveSoloQuestionGroupCandidates"));
    }
    public override async Task<Results<Ok<LiveSoloQuestionCandidateListResponse>, NotFound>> ExecuteAsync(ListLiveSoloQuestionGroupCandidatesRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await matches.CandidatesAsync(req.CompetitionId, req.MatchId, user.UserId, clock.GetUtcNow(), ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(new LiveSoloQuestionCandidateListResponse(result.Select(x => new LiveSoloQuestionCandidateResponse(
            x.Id, x.Name, x.Reserve, x.ConcurrencyStamp, x.SelectionRevision, x.Available, x.AutomaticEligible, x.Exclusions.Select(e => (LiveSoloQuestionExclusionProtocol)e).ToArray(),
            x.Risks.Select(r => new LiveSoloQuestionCandidateRiskResponse(r.Kind, r.CompetitionChallengeId, r.CanonicalChallengeId)).ToArray(),
            x.Uses.Select(u => new LiveSoloQuestionUseResponse(u.MatchId, u.RoundId, u.Number, u.Replay, u.State, u.PreparedAt, u.OpenedAt)).ToArray())).ToArray()));
    }
}
