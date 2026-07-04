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
    public IChallengeSubmissionHandler? FindHandler(string typeId)
        => handlers.FirstOrDefault(handler =>
            string.Equals(handler.TypeId, typeId, StringComparison.OrdinalIgnoreCase));
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
    public IChallengeFeatureProvider? FindProvider(string typeId)
        => providers.FirstOrDefault(provider =>
            string.Equals(provider.TypeId, typeId, StringComparison.OrdinalIgnoreCase));
}

public class ChallengeAdminFeatureRegistry(IEnumerable<IChallengeAdminFeatureProvider> providers)
    : IChallengeAdminFeatureRegistry
{
    public IChallengeAdminFeatureProvider? FindProvider(string typeId)
        => providers.FirstOrDefault(provider =>
            string.Equals(provider.TypeId, typeId, StringComparison.OrdinalIgnoreCase));
}
