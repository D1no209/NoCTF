using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Media;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ManageLiveSoloViewerRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid? ExpectedLeaseId { get; set; }
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloViewerAction>))] public LiveSoloViewerAction Action { get; set; }
}
public sealed class ManageLiveSoloViewerValidator : Validator<ManageLiveSoloViewerRequest>
{
    public ManageLiveSoloViewerValidator() { RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty(); RuleFor(x => x.Action).IsInEnum();
        RuleFor(x => x.ExpectedLeaseId).NotEmpty().When(x => x.Action == LiveSoloViewerAction.Leave); }
}
public sealed record LiveSoloViewerResponse(Guid LeaseId, DateTimeOffset ExpiresAt);
public sealed class ManageLiveSoloViewerEndpoint(ILiveSoloViewerStore viewers, LiveSoloViewerBrowserAccess browser, IUserContext user, IOptions<RefreshHttpOptions> origins)
    : Endpoint<ManageLiveSoloViewerRequest, Results<Ok<LiveSoloViewerResponse>, NoContent, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/program/viewer"); AllowAnonymous();
        Description(x => x.WithName("ManageLiveSoloViewer").WithMetadata(new LiveSoloViewerBrowserAccessMetadata()));
        Summary(x => x.Summary = "Enters, renews or leaves a browser-bound delayed-programme viewer lease.");
    }
    public override async Task<Results<Ok<LiveSoloViewerResponse>, NoContent, ProblemHttpResult>> ExecuteAsync(ManageLiveSoloViewerRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, origins.Value)) return TypedResults.Problem(statusCode: 403, title: "InvalidOrigin");
        var leaseId = browser.LeaseId(HttpContext, req.CompetitionId, req.MatchId);
        if (req.Action == LiveSoloViewerAction.Leave)
        {
            if (leaseId != req.ExpectedLeaseId) return TypedResults.NoContent();
            await viewers.LeaveAsync(req.CompetitionId, req.MatchId, user.UserId, leaseId, ct);
            browser.Clear(HttpContext, req.CompetitionId, req.MatchId); return TypedResults.NoContent();
        }
        var result = req.Action == LiveSoloViewerAction.Enter
            ? await viewers.EnterAsync(req.CompetitionId, req.MatchId, user.UserId, leaseId, ct)
            : await viewers.RenewAsync(req.CompetitionId, req.MatchId, user.UserId, leaseId, ct);
        if (result.Admission is not { } admission)
        {
            browser.Clear(HttpContext, req.CompetitionId, req.MatchId);
            var code = $"LiveSoloViewer{result.Failure ?? LiveSoloViewerFailure.Unavailable}";
            return TypedResults.Problem(statusCode: result.Failure == LiveSoloViewerFailure.CapacityReached ? 429 : result.Failure == LiveSoloViewerFailure.Conflict ? 409 : 403,
                title: code, extensions: new Dictionary<string, object?> { ["code"] = code });
        }
        browser.Write(HttpContext, req.CompetitionId, req.MatchId, admission.Id);
        return TypedResults.Ok(new LiveSoloViewerResponse(admission.Id, admission.ExpiresAt));
    }
}
