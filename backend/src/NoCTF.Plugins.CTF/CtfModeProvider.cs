using System.Text.Json;
using NoCTF.Application.CompetitionModes;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.CTF;

public class CtfModeProvider(CtfGameMode gameMode) : ICompetitionModeProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ModeKey => "ctf";

    public CompetitionCapabilityDescriptor GetCapabilities()
        => new(ModeKey, ["submit-flag"], ["challenge-list", "submissions"], []);

    public bool CanHandleAction(string actionKey)
        => string.Equals(actionKey, "submit-flag", StringComparison.OrdinalIgnoreCase);

    public async Task<CompetitionActionResult> HandleActionAsync(
        CompetitionActionContext context,
        CancellationToken ct = default)
    {
        if (context.TeamId is null)
            return new CompetitionActionResult(false, "no_team");

        var payload = JsonSerializer.Deserialize<SubmitFlagActionPayload>(context.PayloadJson, JsonOptions);
        if (payload is null || payload.ChallengeId == Guid.Empty || string.IsNullOrWhiteSpace(payload.Flag))
            return new CompetitionActionResult(false, "invalid_payload");

        var result = await gameMode.ProcessSubmissionAsync(new SubmissionContext(
            context.CompetitionId,
            context.TeamId.Value,
            payload.ChallengeId,
            context.UserId,
            payload.Flag,
            context.IpAddress), ct);

        return result switch
        {
            SubmissionResult.Accepted => new CompetitionActionResult(true, "accepted", new { correct = true }),
            SubmissionResult.AlreadySolved => new CompetitionActionResult(true, "already_solved", new { correct = true, alreadySolved = true }),
            SubmissionResult.WrongFlag => new CompetitionActionResult(false, "wrong_flag", new { correct = false }),
            SubmissionResult.CompetitionNotStarted => new CompetitionActionResult(false, "competition_not_started"),
            SubmissionResult.CompetitionEnded => new CompetitionActionResult(false, "competition_ended"),
            SubmissionResult.CompetitionPaused => new CompetitionActionResult(false, "competition_paused"),
            _ => new CompetitionActionResult(false, result.ToString().ToLowerInvariant())
        };
    }

    public bool CanProvideView(string viewKey)
        => false;

    public Task<CompetitionViewResult> GetViewAsync(CompetitionViewContext context, CancellationToken ct = default)
        => throw new NotSupportedException($"CTF view '{context.ViewKey}' is not implemented by the mode provider yet.");

    private sealed record SubmitFlagActionPayload(Guid ChallengeId, string Flag);
}
