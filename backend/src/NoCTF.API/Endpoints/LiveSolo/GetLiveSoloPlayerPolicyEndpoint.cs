using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class GetLiveSoloPlayerPolicyRequest { public Guid CompetitionId { get; set; } }
public sealed record LiveSoloPlayerPolicyResponse(bool Enabled, bool PlatformStreamingEnabled, int RequiredWins, int MaximumRosterMembers,
    int PublicDelaySeconds, bool ParticipantsMayViewOpponents, bool RecordingEnabled);
public sealed class GetLiveSoloPlayerPolicyEndpoint(ILiveSoloPlayerPolicyReader policies, IUserContext user)
    : Endpoint<GetLiveSoloPlayerPolicyRequest, Results<Ok<LiveSoloPlayerPolicyResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/player-policy"); AuthSchemes("Bearer");
        Description(x => x.WithName("GetLiveSoloPlayerPolicy"));
        Summary(x => x.Summary = "Returns minimal preparation and sharing policy to approved participants or authorized staff.");
    }
    public override async Task<Results<Ok<LiveSoloPlayerPolicyResponse>, NotFound>> ExecuteAsync(GetLiveSoloPlayerPolicyRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var policy = await policies.ReadAsync(req.CompetitionId, user.UserId, ct);
        return policy is null ? TypedResults.NotFound() : TypedResults.Ok(new LiveSoloPlayerPolicyResponse(policy.Enabled,
            policy.PlatformStreamingEnabled, policy.RequiredWins, policy.MaximumRosterMembers, policy.PublicDelaySeconds, policy.ParticipantsMayViewOpponents, policy.RecordingEnabled));
    }
}
