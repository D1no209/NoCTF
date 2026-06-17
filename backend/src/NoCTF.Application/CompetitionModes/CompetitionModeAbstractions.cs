namespace NoCTF.Application.CompetitionModes;

public sealed record CompetitionCapabilityDescriptor(
    string ModeKey,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Views,
    IReadOnlyList<string> Jobs);

public sealed record CompetitionActionContext(
    Guid CompetitionId,
    Guid? TeamId,
    Guid UserId,
    string ActionKey,
    string PayloadJson,
    string IpAddress);

public sealed record CompetitionActionResult(
    bool Success,
    string Code,
    object? Data = null);

public sealed record CompetitionViewContext(
    Guid CompetitionId,
    Guid? TeamId,
    Guid? UserId,
    string ViewKey);

public sealed record CompetitionViewResult(
    string ViewKey,
    object Data);

public interface ICompetitionModeProvider
{
    string ModeKey { get; }
    CompetitionCapabilityDescriptor GetCapabilities();
    bool CanHandleAction(string actionKey);
    Task<CompetitionActionResult> HandleActionAsync(CompetitionActionContext context, CancellationToken ct = default);
    bool CanProvideView(string viewKey);
    Task<CompetitionViewResult> GetViewAsync(CompetitionViewContext context, CancellationToken ct = default);
}

public interface ICompetitionModeRegistry
{
    ICompetitionModeProvider GetRequiredProvider(string modeKey);
}

public class CompetitionModeRegistry(IEnumerable<ICompetitionModeProvider> providers) : ICompetitionModeRegistry
{
    public ICompetitionModeProvider GetRequiredProvider(string modeKey)
    {
        var provider = providers.FirstOrDefault(p =>
            string.Equals(p.ModeKey, modeKey, StringComparison.OrdinalIgnoreCase));

        return provider ?? throw new NotSupportedException($"Unknown competition mode '{modeKey}'.");
    }
}
