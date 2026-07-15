using System.IO;

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

public sealed record CompetitionFileActionContext(
    Guid CompetitionId,
    Guid TeamId,
    Guid UserId,
    Guid ChallengeId,
    string ActionKey,
    Stream File,
    string FileName,
    string ContentType,
    string IpAddress);

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

public interface ICompetitionFileActionProvider
{
    string ModeKey { get; }
    bool CanHandleFileAction(string actionKey);
    Task<CompetitionActionResult> HandleFileActionAsync(CompetitionFileActionContext context, CancellationToken ct = default);
}

public interface ICompetitionFileActionRegistry
{
    ICompetitionFileActionProvider? FindProvider(string modeKey, string actionKey);
}

public interface ICompetitionModeRegistry
{
    ICompetitionModeProvider GetRequiredProvider(string modeKey);
}

public class CompetitionModeRegistry(IEnumerable<ICompetitionModeProvider> providers) : ICompetitionModeRegistry
{
    private readonly IReadOnlyDictionary<string, ICompetitionModeProvider> _providers =
        ProviderRegistry.BuildUnique(providers, provider => provider.ModeKey, "competition mode");

    public ICompetitionModeProvider GetRequiredProvider(string modeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modeKey);
        return _providers.TryGetValue(modeKey.Trim(), out var provider)
            ? provider
            : throw new NotSupportedException($"Unknown competition mode '{modeKey}'.");
    }
}

public sealed class CompetitionFileActionRegistry(
    IEnumerable<ICompetitionFileActionProvider> providers) : ICompetitionFileActionRegistry
{
    private readonly ICompetitionFileActionProvider[] _providers = providers.ToArray();

    public ICompetitionFileActionProvider? FindProvider(string modeKey, string actionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionKey);
        var matches = _providers
            .Where(provider =>
                string.Equals(provider.ModeKey, modeKey.Trim(), StringComparison.OrdinalIgnoreCase) &&
                provider.CanHandleFileAction(actionKey))
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Multiple file-action providers handle mode '{modeKey}' and action '{actionKey}'.")
        };
    }
}
