using NoCTF.Core;
using NoCTF.PluginBase;

namespace NoCTF.Application.CompetitionModes;

public sealed record ChallengeSubmissionResult(
    SubmissionResult Result,
    object? Data = null);

public interface IChallengeSubmissionHandler
{
    string TypeId { get; }
    Task<ChallengeSubmissionResult> ProcessSubmissionAsync(
        SubmissionContext context,
        Challenge challenge,
        CancellationToken ct = default);
}

public interface IChallengeSubmissionHandlerRegistry
{
    IChallengeSubmissionHandler? FindHandler(string typeId);
}

public class ChallengeSubmissionHandlerRegistry(IEnumerable<IChallengeSubmissionHandler> handlers)
    : IChallengeSubmissionHandlerRegistry
{
    private readonly IReadOnlyDictionary<string, IChallengeSubmissionHandler> _handlers =
        ProviderRegistry.BuildUnique(handlers, handler => handler.TypeId, "challenge submission");

    public IChallengeSubmissionHandler? FindHandler(string typeId)
        => string.IsNullOrWhiteSpace(typeId)
            ? null
            : _handlers.GetValueOrDefault(typeId.Trim());
}

public sealed record ChallengeFeatureContext(
    Guid CompetitionId,
    Guid ChallengeId,
    Guid? TeamId,
    Guid UserId,
    string TypeId,
    string FeatureKey,
    string PayloadJson,
    string IpAddress);

public sealed record ChallengeFeatureResult(
    bool Success,
    string Code,
    object? Data = null,
    int StatusCode = 200);

public interface IChallengeFeatureProvider
{
    string TypeId { get; }
    bool CanHandle(string featureKey);
    Task<ChallengeFeatureResult> HandleAsync(ChallengeFeatureContext context, CancellationToken ct = default);
}

public interface IChallengeAdminFeatureProvider
{
    string TypeId { get; }
    bool CanHandle(string featureKey);
    Task<ChallengeFeatureResult> HandleAsync(ChallengeFeatureContext context, CancellationToken ct = default);
}

public interface IChallengeFeatureRegistry
{
    IChallengeFeatureProvider? FindProvider(string typeId);
}

public interface IChallengeAdminFeatureRegistry
{
    IChallengeAdminFeatureProvider? FindProvider(string typeId);
}

public class ChallengeFeatureRegistry(IEnumerable<IChallengeFeatureProvider> providers) : IChallengeFeatureRegistry
{
    private readonly IReadOnlyDictionary<string, IChallengeFeatureProvider> _providers =
        ProviderRegistry.BuildUnique(providers, provider => provider.TypeId, "challenge feature");

    public IChallengeFeatureProvider? FindProvider(string typeId)
        => string.IsNullOrWhiteSpace(typeId)
            ? null
            : _providers.GetValueOrDefault(typeId.Trim());
}

public class ChallengeAdminFeatureRegistry(IEnumerable<IChallengeAdminFeatureProvider> providers)
    : IChallengeAdminFeatureRegistry
{
    private readonly IReadOnlyDictionary<string, IChallengeAdminFeatureProvider> _providers =
        ProviderRegistry.BuildUnique(providers, provider => provider.TypeId, "challenge admin feature");

    public IChallengeAdminFeatureProvider? FindProvider(string typeId)
        => string.IsNullOrWhiteSpace(typeId)
            ? null
            : _providers.GetValueOrDefault(typeId.Trim());
}

internal static class ProviderRegistry
{
    public static IReadOnlyDictionary<string, TProvider> BuildUnique<TProvider>(
        IEnumerable<TProvider> providers,
        Func<TProvider, string> keySelector,
        string providerKind)
    {
        var result = new Dictionary<string, TProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            var key = keySelector(provider)?.Trim();
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException($"A {providerKind} provider has an empty key.");
            if (!result.TryAdd(key, provider))
                throw new InvalidOperationException($"Duplicate {providerKind} provider key '{key}'.");
        }

        return result;
    }
}
