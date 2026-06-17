using NoCTF.Application.CompetitionModes;

namespace NoCTF.Plugins.KoH;

public class KohModeProvider : ICompetitionModeProvider
{
    public string ModeKey => "koh";

    public CompetitionCapabilityDescriptor GetCapabilities()
        => new(ModeKey, [], ["control-dashboard"], ["agent-poll"]);

    public bool CanHandleAction(string actionKey)
        => false;

    public Task<CompetitionActionResult> HandleActionAsync(
        CompetitionActionContext context,
        CancellationToken ct = default)
        => Task.FromResult(new CompetitionActionResult(false, "unsupported_action"));

    public bool CanProvideView(string viewKey)
        => false;

    public Task<CompetitionViewResult> GetViewAsync(CompetitionViewContext context, CancellationToken ct = default)
        => throw new NotSupportedException($"KoH view '{context.ViewKey}' is not implemented by the mode provider yet.");
}
