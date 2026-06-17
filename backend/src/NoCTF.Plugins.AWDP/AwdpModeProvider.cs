using NoCTF.Application.CompetitionModes;

namespace NoCTF.Plugins.AWDP;

public class AwdpModeProvider : ICompetitionModeProvider
{
    public string ModeKey => "awdp";

    public CompetitionCapabilityDescriptor GetCapabilities()
        => new(ModeKey, ["submit-flag", "submit-patch"], ["service-dashboard", "patch-submissions"], ["round", "patch-validation"]);

    public bool CanHandleAction(string actionKey)
        => false;

    public Task<CompetitionActionResult> HandleActionAsync(
        CompetitionActionContext context,
        CancellationToken ct = default)
        => Task.FromResult(new CompetitionActionResult(false, "unsupported_action"));

    public bool CanProvideView(string viewKey)
        => false;

    public Task<CompetitionViewResult> GetViewAsync(CompetitionViewContext context, CancellationToken ct = default)
        => throw new NotSupportedException($"AWDP view '{context.ViewKey}' is not implemented by the mode provider yet.");
}
